using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed record NativeEditPreparation(CadProgram Before, RelationPlan After, string Target,
    EditableParameter Parameter, double ExpectedValue, ChangeSet ChangeSet, FullValidationReason Reasons)
    : MutationPreparation(ChangeSet, Reasons);

public sealed record NativeEditRollback(CadState State, CadProgram Program, DependencyGraph Dependencies,
    OperationNode Pattern, IReadOnlyDictionary<string, Point2D> SeedPositions, object SessionSnapshot);

// Finite native adapter for the existing M5 count/spacing vocabulary. The
// generic coordinator supports other adapters without knowing these operations.
public sealed class TransactionalParameterBackend : IMutationBackend<NativeEditPreparation, NativeEditRollback>
{
    private readonly SolidWorksExecutionContext context;
    private readonly List<ValidationReadSet> reads = new();
    public IReadOnlyList<ValidationReadSet> ValidationReads => reads.AsReadOnly();
    public bool RecoveryAllowed => false;
    public bool Recover(NativeEditPreparation prepared) => false;
    public TransactionalParameterBackend(SolidWorksExecutionContext context) => this.context = context;

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
        var proposed = RelationParameterEditor.Apply(context.RelationProgram, edit);
        var target = edit.Input("target")!.References[0];
        var bound = new SemanticEntityBinder().Bind(state, OperationRegistry.Default.Get(OperationKind.EditParameter).Inputs[0], new(target.SemanticId, target.Type));
        if (!bound.Succeeded) throw new StateException(bound.FailureCode!, bound.Message);
        var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, bound.Entity!.NativeReference).NativeObject;
        if (feature is null || PersistentReferenceAdapter.Capture(context, feature) != PersistentReferenceAdapter.Capture(context, context.DirectFeature(target.SemanticId)))
            throw new StateException("STALE_REFERENCE", "Bound edit target differs from the native session owner.");
        var parameter = edit.Parameter<ParameterNameParameter>("parameter").Value;
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
        foreach (var operation in plan.Program.Operations.Where(o => affected.Contains(o.SemanticId!) && o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern))
        {
            var d = LinearPatternHandler.Dimensions(operation);
            entities.Add(LinearPatternHandler.Direction(context, operation, d.Swap ? 1 : 0).SemanticId);
            if (d.Y > 1) entities.Add(LinearPatternHandler.Direction(context, operation, 1).SemanticId);
        }
        var changes = new ChangeSet(DirtySetIds(changed), new[] { binding[0].ParameterSemanticId }, DirtySetIds(entities));
        var value = edit.Parameter<EditValueParameter>("value").Value switch
        { LengthParameter length => length.Millimeters, CountParameter count => count.Value, _ => throw new StateException(FailureCodes.OperationUnsupported, "Unsupported scalar edit.") };
        var risk = parameter is EditableParameter.PatternCount or EditableParameter.PatternCountX or EditableParameter.PatternCountY ? FullValidationReason.HighRiskTopology : FullValidationReason.None;
        return new(context.RelationProgram, plan, target.SemanticId, parameter, value, changes, risk);
    }

    public void Preflight(CadState state, NativeEditPreparation prepared)
    {
        var check = new CompositionBackend().Preflight(prepared.After.Program with { Relations = Array.Empty<DesignRelation>() });
        if (!check.IsValid) throw new StateException(check.FailureCode!, check.Message);
        var before = LinearPatternHandler.Dimensions(prepared.Before.Operations.Single(o => o.SemanticId == prepared.Target));
        var after = LinearPatternHandler.Dimensions(prepared.After.Program.Operations.Single(o => o.SemanticId == prepared.Target));
        if (before.Swap != after.Swap || (before.Y > 1) != (after.Y > 1))
            throw new StateException(FailureCodes.OperationUnsupported, "Scalar edits preserve active native pattern directions.");
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
        var seeds = prepared.Changes.ChangedFeatures.Where(id => IsHole(context.Operation(id))).ToDictionary(id => id, id =>
        { var actual = NativeHoleProfile.Read(context, id); return new Point2D(actual.CenterMm.X, actual.CenterMm.Y); });
        var original = prepared.Before.Operations.Single(o => o.SemanticId == prepared.Target);
        var data = (ILinearPatternFeatureData)context.DirectFeature(prepared.Target).GetDefinition();
        var d = LinearPatternHandler.Dimensions(original);
        var parameters = new Dictionary<string, OperationParameter>(original.Parameters);
        if (original.Kind == OperationKind.CreateLinearPattern)
        { parameters["count"] = new CountParameter(data.D1TotalInstances); parameters["spacingMm"] = new LengthParameter(data.D1Spacing * 1000); }
        else
        {
            parameters["countX"] = new CountParameter(d.Swap ? 1 : data.D1TotalInstances);
            parameters["countY"] = new CountParameter(d.Swap ? data.D1TotalInstances : data.D2TotalInstances);
            if (!d.Swap) parameters["spacingXMm"] = new LengthParameter(data.D1Spacing * 1000);
            if (d.Y > 1 || d.Swap) parameters["spacingYMm"] = new LengthParameter((d.Swap ? data.D1Spacing : data.D2Spacing) * 1000);
        }
        return new(state, prepared.Before, context.RelationDependencies!, original with { Parameters = parameters }, seeds, context.SnapshotOutputs());
    }

    public ChangeSet Execute(NativeEditPreparation prepared)
    {
        context.MutationInProgress = true;
        foreach (var operation in prepared.After.Program.Operations.Where(IsHole).Where(o => prepared.Changes.ChangedFeatures.Contains(o.SemanticId!)))
            NativeHoleProfile.SetPlacement(context, operation.SemanticId!, Placement(operation));
        NativePatternEditor.Apply(context, prepared.After.Program.Operations.Single(o => o.SemanticId == prepared.Target), prepared.Parameter);
        return prepared.Changes;
    }
    public bool Rebuild() => context.Document.ForceRebuild3(false);
    public void ValidatePostconditions(NativeEditPreparation prepared)
    {
        CheckIntegrity();
        // API bools are checked by SetPlacement/ModifyDefinition. Rebuild and
        // transaction ownership are independent mandatory Level 0 checks.
        if (!context.MutationInProgress) throw new StateException("TRANSACTION_INTEGRITY_FAILED", "Native mutation ownership was lost.");
    }
    public CadState ValidateFinal(CadState state, NativeEditPreparation prepared, ValidationScope scope)
    {
        foreach (var id in prepared.Changes.ChangedFeatures.Where(id => IsHole(context.Operation(id)))) context.RefreshHoleWall(id);
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
        NativePatternEditor.Apply(context, rollback.Pattern);
        context.UpdateConstructedOperations(rollback.Program); context.RelationDependencies = rollback.Dependencies;
        context.RelationRevision = rollback.State.Revision;
        context.RestoreOutputs(rollback.SessionSnapshot); context.MutationInProgress = false;
    }
    public void ValidateRestored(CadState state, NativeEditRollback rollback, ValidationScope scope)
    {
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
            observed.Entities.Where(e => scope.Entities.Contains(e.SemanticId)).Any(e => e.ReferenceHealth != ReferenceHealth.Healthy))
            throw new StateException("STALE_REFERENCE", "Validated native references must remain healthy.");
        foreach (var id in scope.Parameters)
        {
            var binding = state.Bindings.Single(b => b.ParameterSemanticId == id);
            var op = expected.Operations.Single(o => o.SemanticId == binding.OwnerFeatureSemanticId);
            var desired = ExpectedParameter(op, binding.Parameter);
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
    private static double ExpectedParameter(OperationNode op, EditableParameter parameter) => parameter switch
    {
        EditableParameter.ExtrusionDepth or EditableParameter.BlindHoleDepth => op.Parameter<LengthParameter>("depthMm").Millimeters,
        EditableParameter.HoleDiameter => op.Parameter<LengthParameter>("diameterMm").Millimeters,
        EditableParameter.PatternSpacing => op.Parameter<LengthParameter>("spacingMm").Millimeters,
        EditableParameter.PatternSpacingX => op.Parameter<LengthParameter>("spacingXMm").Millimeters,
        EditableParameter.PatternSpacingY => op.Parameter<LengthParameter>("spacingYMm").Millimeters,
        EditableParameter.PatternCount => op.Parameter<CountParameter>("count").Value,
        EditableParameter.PatternCountX => op.Parameter<CountParameter>("countX").Value,
        EditableParameter.PatternCountY => op.Parameter<CountParameter>("countY").Value,
        EditableParameter.FilletRadius => op.Parameter<LengthParameter>("radiusMm").Millimeters,
        EditableParameter.ChamferDistance => op.Parameter<LengthParameter>("distanceMm").Millimeters,
        _ => throw new StateException(FailureCodes.OperationUnsupported, "No native accessor for parameter.")
    };
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
