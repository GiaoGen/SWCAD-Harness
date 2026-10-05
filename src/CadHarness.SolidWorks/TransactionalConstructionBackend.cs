using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public sealed record ConstructionPreparation(RelationPlan After, CadProgram Addition, ChangeSet ChangeSet)
    : MutationPreparation(ChangeSet, FullValidationReason.NewModelFinalization);
public sealed class ConstructionRollback
{
    internal NativeConstructionCheckpoint Checkpoint { get; }
    internal ConstructionRollback(NativeConstructionCheckpoint checkpoint) => Checkpoint = checkpoint;
}

public sealed class TransactionalConstructionBackend : IRequestMutationBackend<CadProgram, ConstructionPreparation, ConstructionRollback>
{
    private readonly SolidWorksExecutionContext context;
    private readonly List<OperationExecutionResult> executed = new();
    public IReadOnlyList<OperationExecutionResult> Operations => executed.ToArray();
    public bool RecoveryAllowed => false;
    public bool Recover(ConstructionPreparation prepared) => false;
    public TransactionalConstructionBackend(SolidWorksExecutionContext context) => this.context = context;
    public ConstructionPreparation ResolveInputs(CadState state, CadProgram request)
    {
        context.CheckThread(); executed.Clear();
        if (!context.RelationContextUsable || context.MutationInProgress) throw new StateException("TRANSACTION_BUSY", "Construction session is unavailable or busy.");
        var plan = ConstructionPrograms.Plan(request, context.RelationProgram);
        var existing = state.Features.Select(f => f.SemanticId).ToHashSet(StringComparer.Ordinal);
        var addition = plan.Program with { Operations = plan.Program.Operations.Where(o => !existing.Contains(o.SemanticId!)).ToArray(), Relations = Array.Empty<DesignRelation>() };
        var created = addition.Operations.Select(o => o.SemanticId!).ToArray();
        var inputs = addition.Operations.SelectMany(o => o.Inputs).SelectMany(i => i.References).Select(r => r.SemanticId).ToHashSet(StringComparer.Ordinal);
        var affected = state.Entities.Where(e => inputs.Contains(e.SemanticId) || e.Type == SemanticType.BodyRef).ToArray();
        var changes = new ChangeSet(created.Concat(affected.Select(e => e.OwnerFeatureSemanticId)).Distinct().ToArray(), Array.Empty<string>(), affected.Select(e => e.SemanticId).ToArray())
        {
            CreatedFeatures = created,
            CreatedEntities = addition.Operations.SelectMany(o => ProfileOutputs.For(o).Select(output => o.SemanticId + output.Suffix)).ToArray()
        };
        return new(plan, addition, changes);
    }
    public void Preflight(CadState state, ConstructionPreparation prepared)
    {
        var doc = context.Document;
        if (doc.GetType() != (int)swDocumentTypes_e.swDocPART || doc.GetActiveSketch2() is not null || doc.Extension.NeedsRebuild2 != 0)
            throw new StateException(FailureCodes.PreconditionFailed, "Construction requires a rebuilt Part outside sketch editing.");
        if (context.RelationProgram is null && (NativeGeometry.SolidBodies(doc).Count != 0 || context.ConstructedFeatureIds.Count != 0))
            throw new StateException(FailureCodes.OperationUnsupported, "Initial construction requires a pristine Part; extensions require a managed session.");
        if (context.RelationProgram is not null) RelationNativeReadback.Verify(context, context.RelationProgram);
        VerifyState(state, context.CaptureConstructionState());
        foreach (var operation in prepared.Addition.Operations)
            foreach (var input in operation.Inputs)
                foreach (var reference in input.References.Where(r => state.Entities.Any(e => e.SemanticId == r.SemanticId)))
                {
                    var slot = OperationRegistry.Default.Get(operation.Kind).Inputs.Single(i => i.Name == input.Name);
                    var bound = new SemanticEntityBinder().Bind(state, slot, new(reference.SemanticId, reference.Type));
                    if (!bound.Succeeded || PersistentReferenceAdapter.Resolve<object>(context, bound.Entity!.NativeReference).Health != ReferenceHealth.Healthy)
                        throw new StateException(bound.FailureCode ?? "STALE_REFERENCE", "Existing construction input is unavailable: " + reference.SemanticId);
                }
        if (prepared.Addition.Operations.Count == 0) throw new StateException(FailureCodes.PreconditionFailed, "Construction must introduce at least one operation.");
    }
    public ConstructionRollback CaptureRollback(CadState state, ConstructionPreparation prepared) => new(NativeConstructionCheckpoint.Capture(context));
    public ChangeSet Execute(ConstructionPreparation prepared)
    {
        context.MutationInProgress = true;
        DocumentIdentityAdapter.EnsurePersistentIds(context);
        context.RelationProgram = prepared.After.Program; context.RelationDependencies = prepared.After.Dependencies;
        var execution = new CompositionBackend().ExecutePrepared(context, prepared.Addition);
        executed.AddRange(execution.Operations);
        if (!execution.Succeeded) throw new StateException(execution.FailureCode!, execution.Message);
        return prepared.Changes;
    }
    public bool Rebuild() => context.Document.ForceRebuild3(false) && context.Document.Extension.NeedsRebuild2 == 0;
    public void ValidatePostconditions(ConstructionPreparation prepared)
    {
        RelationNativeReadback.Verify(context, prepared.After.Program);
        NativeHoleInstanceVerifier.Verify(context, prepared.After.Program, prepared.After.Program.Operations
            .Where(o => o.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole).Select(o => o.SemanticId!));
    }
    public CadState ValidateFinal(CadState state, ConstructionPreparation prepared, ValidationScope scope)
    {
        var observed = context.CaptureState(null, prepared.After.Program, null, checked(state.Revision + 1));
        // Edge treatments can intentionally consume their host's linear edges.
        // Preserve those outputs with their actual health, so Binder/Planner
        // cannot reuse them. Feature/body/frame/host references must stay usable.
        if (observed.Features.Any(f => f.ReferenceHealth != ReferenceHealth.Healthy) || observed.Entities.Any(e => e.ReferenceHealth != ReferenceHealth.Healthy &&
            !ConstructionReferenceHealth.IntentionalConsumption(prepared.After.Program, e)))
            throw new StateException("STALE_REFERENCE", "Final construction state has unhealthy native references.");
        return observed;
    }
    public void StageState(ConstructionPreparation prepared, CadState validated)
    {
        context.UpdateConstructedOperations(prepared.After.Program); context.RelationDependencies = prepared.After.Dependencies;
        context.RelationRevision = validated.Revision; context.MutationInProgress = false;
    }
    public void Rollback(ConstructionRollback rollback) => rollback.Checkpoint.Restore(context);
    public void ValidateRestored(CadState state, ConstructionRollback rollback, ValidationScope scope)
    {
        rollback.Checkpoint.Verify(context);
        if (context.RelationProgram is not null) RelationNativeReadback.Verify(context, context.RelationProgram);
        VerifyState(state, context.CaptureConstructionState());
    }
    public void Invalidate() { context.RelationContextUsable = false; context.MutationInProgress = false; }

    private static void VerifyState(CadState expected, CadState actual)
    {
        // State capture is deterministic within the owning native session.
        // Geometry/parameters receive the same tolerances as native validation;
        // all identities, references, graph, bindings and revisions are exact.
        if (!expected.Document.Matches(actual.Document) || expected.Revision != actual.Revision ||
            !expected.Features.SequenceEqual(actual.Features) || !expected.Bindings.SequenceEqual(actual.Bindings) ||
            !StateRelationData.Relations(expected).SequenceEqual(StateRelationData.Relations(actual)) ||
            !StateRelationData.Dependencies(expected).SequenceEqual(StateRelationData.Dependencies(actual)) ||
            expected.Parameters.Count != actual.Parameters.Count || expected.Entities.Count != actual.Entities.Count)
            throw new StateException("STATE_DRIFT_DETECTED", "Construction baseline identity/metadata differs from native state.");
        foreach (var before in expected.Parameters)
        {
            var after = actual.Parameters.SingleOrDefault(p => p.SemanticId == before.SemanticId);
            if (after is null || after.Kind != before.Kind) throw new StateException("STATE_DRIFT_DETECTED", "Construction parameter binding differs.");
            RelationNativeReadback.Near(after.Value, before.Value, "Construction baseline parameter differs.");
        }
        foreach (var before in expected.Entities)
        {
            var after = actual.Entities.SingleOrDefault(e => e.SemanticId == before.SemanticId);
            if (after is null || before with { Geometry = null } != after with { Geometry = null })
                throw new StateException("STATE_DRIFT_DETECTED", "Construction semantic reference differs.");
            if (before.Geometry == after.Geometry) continue;
            if (before.Geometry is null || after.Geometry is null) throw new StateException("STATE_DRIFT_DETECTED", "Construction geometry missing.");
            var a = before.Geometry; var b = after.Geometry;
            if (!GeometryMath.Near(a.OriginMm, b.OriginMm) || a.Direction != b.Direction || a.Frame != b.Frame ||
                a.RadiusMm.HasValue != b.RadiusMm.HasValue || (a.RadiusMm.HasValue && Math.Abs(a.RadiusMm.Value - b.RadiusMm!.Value) > GeometryMath.ToleranceMm))
                throw new StateException("STATE_DRIFT_DETECTED", "Construction semantic geometry differs.");
        }
    }
}

// The legacy no-file entry point also uses the shared transaction. Its commit
// publishes the validated live session; callers can supply AtomicStateStore
// when durable JSON commit is required.
internal sealed class ConstructionMemoryStore : ICadStateStore
{
    private CadState state;
    internal ConstructionMemoryStore(CadState state) => this.state = state;
    public CadState Load() => state;
    public void Commit(CadState next) => state = next;
}
