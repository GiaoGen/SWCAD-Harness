using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public sealed record RelationEditResult(bool Succeeded, string? FailureCode, string Message, bool MutationStarted,
    bool RebuildSucceeded, long Revision, IReadOnlyList<string> AffectedFeatures)
{
    public bool RollbackAttempted => false;
    public bool RollbackSucceeded => false;
    public bool StateCommitted => false;
}

// Construction and current edits share the transaction coordinator. The older
// CadState edit overload remains the original nontransactional M5 session API.
public sealed class RelationBackend
{
    // M6 entry point: load the committed snapshot and use the generic
    // transaction. The CadState overload below remains the M5 session API.
    public MutationResult Edit(SolidWorksExecutionContext context, AtomicStateStore store, OperationNode edit,
        FullValidationReason requested = FullValidationReason.None) =>
        new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, new TransactionalParameterBackend(context)).Execute(edit, requested);

    public PreflightResult Preflight(CadProgram program, CadProgram? prior = null)
    {
        try
        {
            ConstructionPrograms.Plan(program, prior);
            return PreflightResult.Success;
        }
        catch (StateException error) { return new(false, error.Code, error.Message); }
    }
    public CompositionExecutionResult Create(SolidWorksExecutionContext context, CadProgram program, ICadStateStore? store = null)
    {
        context.CheckThread();
        try
        {
            // Pure program/layout rejection precedes even provisional state
            // capture, identity-property registration or native sketch creation.
            ConstructionPrograms.Plan(program, context.RelationProgram);
            store ??= new ConstructionMemoryStore(context.CaptureConstructionState());
            var backend = new TransactionalConstructionBackend(context);
            var result = new RequestMutationTransaction<CadProgram, ConstructionPreparation, ConstructionRollback>(store, backend).Execute(program);
            return new(result.Succeeded, result.FailureCode, result.Message, backend.Operations) { Transaction = result };
        }
        catch (Exception error)
        {
            var failure = Failure(error);
            return new(false, failure.Code, failure.Message, Array.Empty<OperationExecutionResult>());
        }
    }
    public RelationEditResult Edit(SolidWorksExecutionContext context, CadState state, OperationNode edit)
    {
        context.CheckThread();
        var started = false; var rebuilt = false;
        var stage = "preflight";
        var affected = Array.Empty<string>();
        try
        {
            if (!context.RelationContextUsable || context.RelationProgram is null)
                throw new StateException(FailureCodes.PreconditionFailed, "No usable relation construction session.");
            StateValidation.Validate(state);
            if (state.Revision != context.RelationRevision || !state.Document.Matches(DocumentIdentityAdapter.ReadForBinding(context)))
                throw new StateException("STALE_REFERENCE", "Edit state revision/document/configuration does not match the live context.");
            if (!StateRelationData.Relations(state).ToHashSet().SetEquals(context.RelationProgram.Relations) ||
                !StateRelationData.Dependencies(state).ToHashSet().SetEquals(context.RelationDependencies!.Edges))
                throw new StateException("STALE_REFERENCE", "Edit state relations/dependencies differ from the live construction snapshot.");
            var proposed = RelationParameterEditor.Apply(context.RelationProgram, edit);
            var target = edit.Input("target")!.References[0];
            var bound = new SemanticEntityBinder().Bind(state, OperationRegistry.Default.Get(OperationKind.EditParameter).Inputs[0], new(target.SemanticId, target.Type));
            if (!bound.Succeeded) throw new StateException(bound.FailureCode!, bound.Message);
            var nativeBound = PersistentReferenceAdapter.Resolve<IFeature>(context, bound.Entity!.NativeReference);
            if (nativeBound.NativeObject is null || PersistentReferenceAdapter.Capture(context, nativeBound.NativeObject) != PersistentReferenceAdapter.Capture(context, context.DirectFeature(target.SemanticId)))
                throw new StateException("STALE_REFERENCE", "Bound edit target does not match the session's native owner.");
            var parameter = edit.Parameter<ParameterNameParameter>("parameter").Value;
            var bindings = state.Bindings.Where(b => b.OwnerFeatureSemanticId == target.SemanticId && b.Parameter == parameter).ToArray();
            if (bindings.Length != 1) throw new StateException("BINDING_UNRESOLVED", "The edited parameter has no unique managed binding.");
            var plan = new DesignRelationEngine().Solve(proposed);
            var check = new CompositionBackend().Preflight(plan.Program with { Relations = Array.Empty<DesignRelation>() });
            if (!check.IsValid) throw new StateException(check.FailureCode!, check.Message);
            affected = plan.Dependencies.AffectedBy(new[] { target.SemanticId }).ToArray();
            var current = context.RelationProgram;
            RelationNativeReadback.Verify(context, current);
            var previousDimensions = LinearPatternHandler.Dimensions(current.Operations.Single(o => o.SemanticId == target.SemanticId));
            var nextDimensions = LinearPatternHandler.Dimensions(plan.Program.Operations.Single(o => o.SemanticId == target.SemanticId));
            if (previousDimensions.Swap != nextDimensions.Swap || (previousDimensions.Y > 1) != (nextDimensions.Y > 1))
                throw new StateException(FailureCodes.OperationUnsupported, "M5 scalar edits preserve active native pattern directions; dimensionality transitions are unsupported.");
            started = true;
            stage = "seed sketch placement";
            foreach (var operation in plan.Program.Operations.Where(o => o.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole))
            {
                var old = current.Operations.Single(o => o.SemanticId == operation.SemanticId);
                var before = old.Parameter<PlacementParameter>("placement").Value;
                var after = operation.Parameter<PlacementParameter>("placement").Value;
                if (Math.Abs(before.XMm - after.XMm) > GeometryMath.ToleranceMm || Math.Abs(before.YMm - after.YMm) > GeometryMath.ToleranceMm)
                    NativeHoleProfile.SetPlacement(context, operation.SemanticId!, after);
            }
            stage = "pattern scalar definition";
            NativePatternEditor.Apply(context, plan.Program.Operations.Single(o => o.SemanticId == target.SemanticId));
            stage = "rebuild";
            rebuilt = context.Document.ForceRebuild3(false);
            if (!rebuilt) throw new NativeOperationException("FEATURE_REBUILD_FAILED", "Rebuild after relation edit failed.");
            stage = "native relation readback";
            RelationNativeReadback.Verify(context, plan.Program);
            context.UpdateConstructedOperations(plan.Program); context.RelationDependencies = plan.Dependencies;
            foreach (var operation in plan.Program.Operations.Where(o => o.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole))
                context.RefreshHoleWall(operation.SemanticId!);
            context.RelationRevision = checked(context.RelationRevision + 1);
            context.CaptureBindingState();
            return new(true, null, "Native pattern edited; coupled seed position and explicit relations verified.", true, true, context.RelationRevision, affected);
        }
        catch (Exception error) when (error is StateException or NativeOperationException or COMException)
        {
            if (started) context.RelationContextUsable = false;
            var failure = Failure(error);
            return new(false, failure.Code, stage + ": " + failure.Message, started, rebuilt, context.RelationRevision, affected);
        }
        finally { context.Document.ClearSelection2(true); }
    }
    private static (string Code, string Message) Failure(Exception error) => error switch
    {
        StateException state => (state.Code, state.Message), NativeOperationException native => (native.Code, native.Message),
        _ => ("GEOMETRY_INVALID", error.Message)
    };
}
