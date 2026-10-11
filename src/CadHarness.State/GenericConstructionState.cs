using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;

namespace CadHarness.State;

public sealed record GenericNativeFeature(string SemanticId, ConstructionKind Kind, string NativeType, NativePersistentReference Reference,
    IReadOnlyList<NativePersistentReference> AuxiliaryReferences);
public sealed record GenericNativeParameter(string SemanticId, string Owner, ParameterKey Key, double Value, bool Qualified);
public sealed record GenericNativeSketch(string SemanticId, NativePersistentReference Profile, NativePersistentReference Plane,
    SpatialPlacement Placement, ClosedSketch Specification, IReadOnlyList<NativePersistentReference> Primitives);
public sealed record GenericSolidObservation(NativePersistentReference Reference, double VolumeMm3, IReadOnlyList<double> MassProperties,
    IReadOnlyList<CadHarness.Ir.V03.Vector3> SupportPointsMm);
public sealed record GenericConstructionState(DocumentIdentity Document, long Revision, ConstructionProgram Program,
    IReadOnlyList<GenericNativeFeature> Features, IReadOnlyList<GenericNativeSketch> Sketches, IReadOnlyList<GenericNativeParameter> Parameters,
    IReadOnlyList<DependencyEdge> Dependencies, GenericSolidObservation? Solid);

public sealed class GenericConstructionStatePolicy : IMutationStatePolicy<GenericConstructionState>
{
    public static bool MatchesNativeSnapshot(GenericConstructionState expected, GenericConstructionState actual)
    {
        if (JsonSerializer.Serialize(expected with { Solid = null }) != JsonSerializer.Serialize(actual with { Solid = null })) return false;
        if (expected.Solid is null || actual.Solid is null) return expected.Solid is null && actual.Solid is null;
        var a = expected.Solid; var b = actual.Solid;
        // The native construction checkpoint already uses this SI-unit bound
        // for kernel mass/support roundoff. Identities and bindings stay exact.
        bool Near(double x, double y) => double.IsFinite(y) && Math.Abs(x - y) <= Math.Max(1e-12, Math.Abs(x) * 1e-8);
        return a.Reference == b.Reference && Near(a.VolumeMm3 / 1e9, b.VolumeMm3 / 1e9) &&
            a.MassProperties.Count == b.MassProperties.Count && a.MassProperties.Zip(b.MassProperties).All(p => Near(p.First, p.Second)) &&
            a.SupportPointsMm.Count == b.SupportPointsMm.Count && a.SupportPointsMm.Zip(b.SupportPointsMm).All(p =>
                Near(p.First.X / 1000, p.Second.X / 1000) && Near(p.First.Y / 1000, p.Second.Y / 1000) && Near(p.First.Z / 1000, p.Second.Z / 1000));
    }
    public void Validate(GenericConstructionState state)
    {
        ContractValidation.Require(state.Revision >= 0 && state.Document.DocumentId != Guid.Empty && state.Document.ConfigurationId != Guid.Empty,
            "Managed generic state needs identity and nonnegative revision.");
        if (state.Program.Operations.Count != 0) ContractValidation.Program(state.Program);
        ContractValidation.Unique(state.Features.Select(f => f.SemanticId), "generic feature");
        ContractValidation.Unique(state.Parameters.Select(p => p.SemanticId), "generic parameter");
        foreach (var f in state.Features) { ContractValidation.Require(Enum.IsDefined(f.Kind) && ContractValidation.Id(f.SemanticId), "Generic feature contract invalid."); StateValidation.DecodeReference(f.Reference); }
        foreach (var s in state.Sketches)
        {
            StateValidation.DecodeReference(s.Profile); StateValidation.DecodeReference(s.Plane);
            PlanarSketchPreflight.Prepare(s.Specification);
            ContractValidation.Require(state.Features.Any(f => f.SemanticId == s.SemanticId && f.Reference == s.Profile), "Sketch has no exact feature owner.");
        }
        foreach (var p in state.Parameters)
        {
            ContractValidation.Require(state.Features.Any(f => f.SemanticId == p.Owner), "Parameter has no exact feature owner.");
            ContractValidation.Scalar(p.Key, ScalarUnit.Millimeter, p.Value);
        }
        var ids = state.Features.Select(f => f.SemanticId).ToHashSet();
        ContractValidation.Require(state.Dependencies.All(e => ids.Contains(e.Prerequisite) && ids.Contains(e.Dependent)), "Invalid generic dependency endpoint.");
        new DependencyGraph(state.Dependencies).NativeOrder(ids);
        if (state.Solid is { } body)
        {
            StateValidation.DecodeReference(body.Reference);
            ContractValidation.Require(double.IsFinite(body.VolumeMm3) && body.VolumeMm3 > 0 && body.MassProperties.All(double.IsFinite), "Invalid solid measurement.");
        }
    }
    public long Revision(GenericConstructionState state) => state.Revision;
    public bool SameIdentity(GenericConstructionState a, GenericConstructionState b) => a.Document.Matches(b.Document);
    public DirtySet Expand(GenericConstructionState state, ChangeSet changes)
    {
        var ids = state.Features.Select(f => f.SemanticId).ToHashSet();
        ContractValidation.Require(changes.ChangedFeatures.All(id => ids.Contains(id) || changes.CreatedFeatures.Contains(id)), "Generic ChangeSet names an untracked feature.");
        var affected = new DependencyGraph(state.Dependencies).AffectedBy(changes.ChangedFeatures);
        return new(affected, changes.ChangedParameters, changes.PossiblyInvalidatedEntities.Concat(changes.CreatedEntities).Distinct().ToArray(), Array.Empty<DesignRelation>());
    }
    public ValidationScope Scope(GenericConstructionState state, DirtySet dirty, FullValidationReason reasons) => new(true, reasons,
        state.Parameters.Select(p => p.SemanticId).ToArray(), state.Features.Select(f => f.SemanticId).ToArray(), Array.Empty<DesignRelation>());
}
