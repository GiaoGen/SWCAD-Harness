using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed record NativeEditPreparation(CadProgram Before, RelationPlan After, string Target,
    EditableParameter Parameter, double ExpectedValue, ChangeSet ChangeSet, FullValidationReason Reasons,
    IParameterMutationHandler Handler)
    : MutationPreparation(ChangeSet, Reasons);

public sealed record NativeEditRollback(CadState State, CadProgram Program, DependencyGraph Dependencies,
    IParameterMutationHandler Handler, object NativePayload, IReadOnlyDictionary<string, Point2D> SeedPositions, object SessionSnapshot);

// Generic native parameter adapter. Parameter-specific access and rollback are
// supplied by the registry; pure MutationTransaction remains unchanged.
public sealed class TransactionalParameterBackend : IMutationBackend<NativeEditPreparation, NativeEditRollback>
{
    // Shared by native input resolution and the planner's runtime projection.
    public static bool IsExecutableParameter(OperationNode owner, EditableParameter parameter) =>
        ParameterMutationRegistry.Default.TryGet(owner, parameter, out _);
    private readonly SolidWorksExecutionContext context;
    private readonly ParameterMutationRegistry registry;
    private readonly List<ValidationReadSet> reads = new();
    public IReadOnlyList<ValidationReadSet> ValidationReads => reads.AsReadOnly();
    public bool RecoveryAllowed => false;
    public bool Recover(NativeEditPreparation prepared) => false;
    public TransactionalParameterBackend(SolidWorksExecutionContext context, ParameterMutationRegistry? registry = null)
    { this.context = context; this.registry = registry ?? ParameterMutationRegistry.Default; }

    public NativeEditPreparation ResolveInputs(CadState state, OperationNode edit)
    {
        context.CheckThread(); reads.Clear();
        if (context.MutationInProgress) throw new StateException("TRANSACTION_BUSY", "This native session already has a transaction.");
        if (!context.RelationContextUsable || context.RelationProgram is null || context.RelationDependencies is null)
            throw new StateException(FailureCodes.PreconditionFailed, "No usable relation construction session.");
        if (state.Revision != context.RelationRevision || !state.Document.Matches(DocumentIdentityAdapter.ReadForBinding(context)) ||
            !StateRelationData.Relations(state).ToHashSet().SetEquals(context.RelationProgram.Relations) ||
            !StateRelationData.Dependencies(state).ToHashSet().SetEquals(context.RelationDependencies.Edges))
            throw new StateException("STALE_REFERENCE", "Loaded state differs from the live document/configuration/revision/relations.");
        var proposed = registry.ApplyProgram(context.RelationProgram, edit);
        var target = edit.Input("target")!.References[0];
        var bound = new SemanticEntityBinder().Bind(state, OperationRegistry.Default.Get(OperationKind.EditParameter).Inputs[0], new(target.SemanticId, target.Type));
        if (!bound.Succeeded) throw new StateException(bound.FailureCode!, bound.Message);
        var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, bound.Entity!.NativeReference).NativeObject;
        if (feature is null || PersistentReferenceAdapter.Capture(context, feature) != PersistentReferenceAdapter.Capture(context, context.DirectFeature(target.SemanticId)))
            throw new StateException("STALE_REFERENCE", "Bound edit target differs from the native session owner.");
        var parameter = edit.Parameter<ParameterNameParameter>("parameter").Value;
        var handler = registry.Get(context.Operation(target.SemanticId), parameter);
        var binding = state.Bindings.Where(b => b.OwnerFeatureSemanticId == target.SemanticId && b.Parameter == parameter).ToArray();
        if (binding.Length != 1) throw new StateException("BINDING_UNRESOLVED", "Parameter has no unique managed binding.");
        var plan = new DesignRelationEngine().Solve(proposed);
        var changedSeeds = plan.Program.Operations.Where(IsHole).Where(o => Placement(o) != Placement(context.Operation(o.SemanticId!))).Select(o => o.SemanticId!).ToArray();
        var changed = new[] { target.SemanticId }.Concat(changedSeeds).ToArray();
        var entities = state.Entities.Where(e => changed.Contains(e.OwnerFeatureSemanticId)).Select(e => e.SemanticId).ToList();
        // The modified body's identity and the host/direction faces are dirty
        // native references even though their dimensions have not changed.
        entities.AddRange(state.Entities.Where(e => e.Type == SemanticType.BodyRef).Select(e => e.SemanticId));
        var affected = plan.Dependencies.AffectedBy(changed);
        foreach (var operation in plan.Program.Operations.Where(o => affected.Contains(o.SemanticId!) && o.Kind == OperationKind.CreateCircularPattern))
            entities.Add(operation.Input("axis")!.References[0].SemanticId);
        foreach (var operation in plan.Program.Operations.Where(o => affected.Contains(o.SemanticId!) && o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern))
        {
            var d = LinearPatternHandler.Dimensions(operation);
            entities.Add(LinearPatternHandler.Direction(context, operation, d.Swap ? 1 : 0).SemanticId);
            if (d.Y > 1) entities.Add(LinearPatternHandler.Direction(context, operation, 1).SemanticId);
        }
        var changes = new ChangeSet(DirtySetIds(changed), new[] { binding[0].ParameterSemanticId }, DirtySetIds(entities));
        var value = edit.Parameter<EditValueParameter>("value").Value switch
        { LengthParameter length => length.Millimeters, CountParameter count => count.Value, _ => throw new StateException(FailureCodes.OperationUnsupported, "Unsupported scalar edit.") };
        var risk = handler.ValidationReasons(context.Operation(target.SemanticId), parameter);
        return new(context.RelationProgram, plan, target.SemanticId, parameter, value, changes, risk, handler);
    }

    public void Preflight(CadState state, NativeEditPreparation prepared)
    {
        var check = new CompositionBackend().Preflight(prepared.After.Program with { Relations = Array.Empty<DesignRelation>() });
        if (!check.IsValid) throw new StateException(check.FailureCode!, check.Message);
        prepared.Handler.ValidateTransition(prepared.Before.Operations.Single(o => o.SemanticId == prepared.Target),
            prepared.After.Program.Operations.Single(o => o.SemanticId == prepared.Target), prepared.Parameter);
        foreach (var hole in prepared.After.Program.Operations.Where(o => o.Kind == OperationKind.CreateBlindHole))
        {
            var root = prepared.After.Program.Operations.Single(o => hole.Input("host")!.References[0].SemanticId == o.SemanticId + ".top_face");
            if (hole.Parameter<LengthParameter>("depthMm").Millimeters >= root.Parameter<LengthParameter>("depthMm").Millimeters)
                throw new StateException(FailureCodes.PreconditionFailed, "Blind-hole depth must remain below host thickness.");
        }
        var dirty = DirtySet.Expand(state, prepared.Changes);
        var scope = ValidationScope.Select(state, dirty, prepared.ValidationReasons);
        try
        {
            Verify(state, prepared.Before, scope, "preflight", state.Revision);
        }
        catch (Exception error) when (error is NativeOperationException or StateException)
        {
            // Drift suspicion automatically forces a full read before rejecting;
            // no mutation or replacement state is allowed for a drifted snapshot.
            try { Verify(state, prepared.Before, ValidationScope.Select(state, dirty, FullValidationReason.StateDriftSuspicion), "drift check", state.Revision); }
            catch (Exception) { /* Preserve the original failure; trace records the full escalation. */ }
            throw new StateException("STATE_DRIFT_DETECTED", error.Message, error);
        }
    }

    public NativeEditRollback CaptureRollback(CadState state, NativeEditPreparation prepared)
    {
        var seeds = prepared.Changes.ChangedFeatures.Where(id => IsHole(context.Operation(id)) &&
            Placement(context.Operation(id)) != Placement(prepared.After.Program.Operations.Single(o => o.SemanticId == id))).ToDictionary(id => id, id =>
        { var actual = NativeHoleProfile.Read(context, id); return new Point2D(actual.CenterMm.X, actual.CenterMm.Y); });
        var original = prepared.Before.Operations.Single(o => o.SemanticId == prepared.Target);
        return new(state, prepared.Before, context.RelationDependencies!, prepared.Handler,
            prepared.Handler.Capture(context, original, prepared.Parameter), seeds, context.SnapshotOutputs());
    }

    public ChangeSet Execute(NativeEditPreparation prepared)
    {
        context.MutationInProgress = true;
        foreach (var operation in prepared.After.Program.Operations.Where(IsHole).Where(o => prepared.Changes.ChangedFeatures.Contains(o.SemanticId!) &&
            Placement(o) != Placement(context.Operation(o.SemanticId!))))
            NativeHoleProfile.SetPlacement(context, operation.SemanticId!, Placement(operation));
        prepared.Handler.Apply(context, prepared.After.Program.Operations.Single(o => o.SemanticId == prepared.Target), prepared.Parameter);
        return prepared.Changes;
    }
    public bool Rebuild() => ExecutionTelemetry.Rebuild(() => context.Document.ForceRebuild3(false));
    public void ValidatePostconditions(NativeEditPreparation prepared)
    {
        CheckIntegrity();
        // API bools are checked by SetPlacement/ModifyDefinition. Rebuild and
        // transaction ownership are independent mandatory Level 0 checks.
        if (!context.MutationInProgress) throw new StateException("TRANSACTION_INTEGRITY_FAILED", "Native mutation ownership was lost.");
        prepared.Handler.ValidateNative(context, prepared.After.Program, prepared.Target, prepared.Parameter);
    }
    public CadState ValidateFinal(CadState state, NativeEditPreparation prepared, ValidationScope scope)
    {
        foreach (var id in prepared.After.Dependencies.AffectedBy(prepared.Changes.ChangedFeatures).Where(id => IsHole(context.Operation(id)))) context.RefreshHoleWall(id);
        var candidate = Verify(state, prepared.After.Program, scope, "final", checked(state.Revision + 1));
        var idParameter = prepared.Changes.ChangedParameters.Single();
        RelationNativeReadback.Near(candidate.Parameters.Single(p => p.SemanticId == idParameter).Value, prepared.ExpectedValue, "Edited parameter was not applied.");
        return candidate;
    }
    public void StageState(NativeEditPreparation prepared, CadState validated)
    {
        context.UpdateConstructedOperations(prepared.After.Program); context.RelationDependencies = prepared.After.Dependencies;
        context.RelationRevision = validated.Revision;
        context.MutationInProgress = false;
    }
    public void Rollback(NativeEditRollback rollback)
    {
        foreach (var (id, placement) in rollback.SeedPositions) NativeHoleProfile.SetPlacement(context, id, placement);
        rollback.Handler.Restore(context, rollback.NativePayload);
        context.UpdateConstructedOperations(rollback.Program); context.RelationDependencies = rollback.Dependencies;
        context.RelationRevision = rollback.State.Revision;
        context.RestoreOutputs(rollback.SessionSnapshot); context.MutationInProgress = false;
    }
    public void ValidateRestored(CadState state, NativeEditRollback rollback, ValidationScope scope)
    {
        foreach (var binding in state.Bindings.Where(b => registry.TryGet(rollback.Program.Operations.Single(o => o.SemanticId == b.OwnerFeatureSemanticId), b.Parameter, out _)))
        {
            var owner = rollback.Program.Operations.Single(o => o.SemanticId == binding.OwnerFeatureSemanticId);
            registry.Get(owner, binding.Parameter).ValidateNative(context, rollback.Program, owner.SemanticId!, binding.Parameter);
        }
        var restored = Verify(state, rollback.Program, scope, "rollback", state.Revision);
        foreach (var parameter in state.Parameters)
            RelationNativeReadback.Near(restored.Parameters.Single(p => p.SemanticId == parameter.SemanticId).Value, parameter.Value, "Rollback parameter differs from committed state.");
        foreach (var entity in state.Entities)
            CheckGeometry(restored.Entities.Single(e => e.SemanticId == entity.SemanticId).Geometry, entity.Geometry);
    }
    public void Invalidate() { context.RelationContextUsable = false; context.MutationInProgress = false; }

    private CadState Verify(CadState state, CadProgram expected, ValidationScope scope, string stage, long revision)
    {
        reads.Add(new(stage, scope, state.Features.Select(f => f.SemanticId).ToArray(), scope.Parameters, scope.Entities, scope.Relations));
        CheckIntegrity(); RelationNativeReadback.Verify(context, expected, scope);
        var observed = context.CaptureState(state, expected, scope, revision);
        if (observed.Features.Any(f => f.ReferenceHealth != ReferenceHealth.Healthy) ||
            observed.Entities.Where(e => scope.Entities.Contains(e.SemanticId)).Any(e => e.ReferenceHealth != ReferenceHealth.Healthy &&
                !ConstructionReferenceHealth.IntentionalConsumption(expected, e)))
            throw new StateException("STALE_REFERENCE", "Validated native references must remain healthy.");
        foreach (var id in scope.Parameters)
        {
            var binding = state.Bindings.Single(b => b.ParameterSemanticId == id);
            var op = expected.Operations.Single(o => o.SemanticId == binding.OwnerFeatureSemanticId);
            var desired = registry.Expected(op, binding.Parameter);
            RelationNativeReadback.Near(observed.Parameters.Single(p => p.SemanticId == id).Value, desired, "Native parameter differs from expected program.");
            if (stage == "preflight") RelationNativeReadback.Near(desired, state.Parameters.Single(p => p.SemanticId == id).Value, "Committed scalar differs from the session program.");
        }
        if (!state.Document.Matches(observed.Document)) throw new StateException("TRANSACTION_INTEGRITY_FAILED", "Document identity changed during validation.");
        return observed;
    }
    private void CheckIntegrity()
    {
        context.CheckThread();
        if (context.Document.GetActiveSketch2() is not null || context.Document.Extension.NeedsRebuild2 != 0)
            throw new StateException("FEATURE_REBUILD_FAILED", "Document needs rebuild or remains in sketch editing.");
        foreach (var id in context.ConstructedFeatureIds)
            if (context.DirectFeature(id).GetErrorCode2(out var warning) != 0 || warning)
                throw new StateException("FEATURE_REBUILD_FAILED", "Managed feature has native error/warning: " + id);
    }
    private static void CheckGeometry(SemanticGeometry? actual, SemanticGeometry? expected)
    {
        if (actual == expected) return;
        if (actual is null || expected is null) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Restored semantic geometry is missing.");
        void Near(double a, double b) => RelationNativeReadback.Near(a, b, "Restored semantic geometry differs.");
        if (actual.OriginMm is { } a && expected.OriginMm is { } b) { Near(a.X, b.X); Near(a.Y, b.Y); Near(a.Z, b.Z); }
        else if (actual.OriginMm != expected.OriginMm) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Restored origin differs.");
        if (actual.Direction is { } x && expected.Direction is { } y) { Near(x.X, y.X); Near(x.Y, y.Y); Near(x.Z, y.Z); }
        else if (actual.Direction != expected.Direction) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Restored direction differs.");
        if (actual.RadiusMm is { } ar && expected.RadiusMm is { } br) Near(ar, br);
        else if (actual.RadiusMm != expected.RadiusMm) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Restored radius differs.");
        if (actual.Frame != expected.Frame) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Restored frame differs.");
    }
    private static bool IsHole(OperationNode op) => op.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole;
    private static Point2D Placement(OperationNode op) => op.Parameter<PlacementParameter>("placement").Value;
    private static string[] DirtySetIds(IEnumerable<string> ids) => ids.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
}

public sealed partial class SolidWorksExecutionContext
{
    internal bool MutationInProgress { get; set; }
    internal object SnapshotOutputs() => new Dictionary<string, NativeOutput>(outputs, StringComparer.Ordinal);
    internal void RestoreOutputs(object snapshot)
    {
        outputs.Clear(); foreach (var pair in (Dictionary<string, NativeOutput>)snapshot) outputs.Add(pair.Key, pair.Value);
    }
}
