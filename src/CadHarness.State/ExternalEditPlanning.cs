using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;

namespace CadHarness.State.V03;

public sealed record ExternalHoleContract(string Target, double XMm, double YMm, double DiameterMm);
public sealed record ExternalPatternContract(string Target, string Seed, int Count, double SpacingMm, double DirectionX, double DirectionY);
// This is a measured geometry oracle, not an executable construction program.
public sealed record ExternalPlateContract(string Root, double MinimumX, double MinimumY, double MaximumX,
    double MaximumY, double BottomZ, double DepthMm, IReadOnlyList<ExternalHoleContract> Holes,
    IReadOnlyList<ExternalPatternContract> Patterns);
public sealed record ExternalEditState(string SchemaVersion, ObservedModel Observation, ExternalPlateContract Geometry);
public sealed record ExternalPreparedEdit(ScalarEdit Edit, ObservedFeature Feature, ObservedParameter Parameter);
public sealed record ExternalEditPreparation(ExternalEditState Before, ExternalEditState Proposed,
    IReadOnlyList<ExternalPreparedEdit> Ordered, ChangeSet ChangeSet)
    : MutationPreparation(ChangeSet, FullValidationReason.HighRiskTopology);

public static class ExternalEditPlanning
{
    public static ObservedModel ReconcileSavedObservation(ObservedModel expected,ObservedModel measured)
    {
        Require(ExternalInventoryIdentity.Matches(measured,expected),"Saved native inventory/dependencies changed.","STATE_DRIFT_DETECTED");
        return measured with{Features=measured.Features.Select(f=>{
            var intent=expected.Features.Single(e=>e.SemanticId==f.SemanticId);
            Require(f.Parameters.Count==intent.Parameters.Count,"Saved native parameter set changed.","STATE_DRIFT_DETECTED");
            return f with{EditSupport=intent.EditSupport,SupportReason=intent.SupportReason,Parameters=f.Parameters.Select(p=>{
                var canonical=intent.Parameters.SingleOrDefault(e=>e.SemanticId==p.SemanticId&&e.Key==p.Key&&e.Accessor==p.Accessor&&e.Unit==p.Unit);
                Require(canonical is not null,"Saved parameter identity/accessor changed.","STATE_DRIFT_DETECTED");Near(p.Value,canonical!.Value,p.Key);
                // Keep exact committed intent, retaining raw native floating-point measurement as evidence.
                return p with{Value=canonical.Value,Evidence=p.Evidence.Append(new ObservationEvidence(EvidenceSource.NativeDefinition,
                    "Saved native scalar readback="+p.Value.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"; canonical intent accepted within declared scalar tolerance.")).ToArray()};
            }).ToArray()};
        }).ToArray()};
    }
    public static EditableParameter LegacyParameter(ParameterKey key) => key switch
    {
        ParameterKey.ExtrusionDepth => EditableParameter.ExtrusionDepth,
        ParameterKey.HoleDiameter => EditableParameter.HoleDiameter,
        ParameterKey.PatternCount => EditableParameter.PatternCount,
        ParameterKey.PatternSpacing => EditableParameter.PatternSpacing,
        _ => throw new StateException(V03FailureCodes.CapabilityUnavailable, "No qualified external scalar handler.")
    };
    public static void Validate(ExternalEditState state)
    {
        ContractValidation.Version(state.SchemaVersion); ObservedStateValidation.Validate(state.Observation);
        var g = state.Geometry; var features = state.Observation.Features.ToDictionary(f => f.SemanticId);
        Require(state.Observation.InventoryComplete, "Incomplete observation cannot become editable.");
        Require(features.TryGetValue(g.Root, out var root) && root.Subtype == NativeSubtype.StraightBlindBossExtrude,
            "The measured host must have a recognized straight extrusion.");
        Require(new[] { g.MinimumX, g.MinimumY, g.MaximumX, g.MaximumY, g.BottomZ, g.DepthMm }.All(double.IsFinite) &&
            g.MaximumX > g.MinimumX && g.MaximumY > g.MinimumY && ContractValidation.Length(g.DepthMm), "Invalid plate bounds.");
        Require(g.Holes.Count <= 16 && g.Patterns.Count <= 8, "External geometry qualification bound exceeded.");
        ContractValidation.Unique(g.Holes.Select(h => h.Target).Concat(g.Patterns.Select(p => p.Target)).Append(g.Root), "qualified geometry target");
        foreach (var h in g.Holes)
            Require(features.TryGetValue(h.Target, out var f) && f.Subtype == NativeSubtype.SingleCircleThroughAllCut &&
                double.IsFinite(h.XMm) && double.IsFinite(h.YMm) && ContractValidation.Length(h.DiameterMm), "Invalid through-hole contract.");
        foreach (var p in g.Patterns)
            Require(features.TryGetValue(p.Target, out var f) && f.Subtype == NativeSubtype.SingleDirectionLinearPattern &&
                g.Holes.Any(h => h.Target == p.Seed) && p.Count is >= 2 and <= 256 && ContractValidation.Length(p.SpacingMm) &&
                double.IsFinite(p.DirectionX) && double.IsFinite(p.DirectionY) &&
                Math.Abs(p.DirectionX * p.DirectionX + p.DirectionY * p.DirectionY - 1) < 1e-8 &&
                g.Patterns.Count(q => q.Seed == p.Seed) == 1, "Unsupported pattern geometry or shared seed.");
        foreach (var f in state.Observation.Features.Where(f => f.EditSupport == EditSupport.Editable))
        {
            Require(f.SemanticId == g.Root || g.Holes.Any(h => h.Target == f.SemanticId) || g.Patterns.Any(p => p.Target == f.SemanticId),
                "Editable target lacks an independent geometry contract.");
            foreach (var p in f.Parameters) Near(p.Value, Value(g, f.SemanticId, p.Key), p.Key);
        }
        ValidateGeometry(g);
    }
    public static IReadOnlyList<(double X, double Y, double Radius)> Instances(ExternalPlateContract g)
    {
        var result = new List<(double, double, double)>();
        foreach (var hole in g.Holes)
        {
            var pattern = g.Patterns.SingleOrDefault(p => p.Seed == hole.Target);
            for (var i = 0; i < (pattern?.Count ?? 1); i++) result.Add((hole.XMm + i * (pattern?.SpacingMm ?? 0) * (pattern?.DirectionX ?? 0),
                hole.YMm + i * (pattern?.SpacingMm ?? 0) * (pattern?.DirectionY ?? 0), hole.DiameterMm / 2));
        }
        Require(result.Count <= 256, "Total hole instances exceed qualification bound.");
        return result;
    }
    public static double ExpectedVolume(ExternalPlateContract g) =>
        ((g.MaximumX - g.MinimumX) * (g.MaximumY - g.MinimumY) - Instances(g).Sum(i => Math.PI * i.Radius * i.Radius)) * g.DepthMm;
    public static void ValidateGeometry(ExternalPlateContract g)
    {
        var holes = Instances(g);
        for (var i = 0; i < holes.Count; i++)
        {
            var h = holes[i];
            Require(h.X - h.Radius > g.MinimumX + 0.01 && h.X + h.Radius < g.MaximumX - 0.01 &&
                h.Y - h.Radius > g.MinimumY + 0.01 && h.Y + h.Radius < g.MaximumY - 0.01, "Final hole/pattern intersects the host boundary.");
            for (var j = 0; j < i; j++)
                Require(Math.Sqrt(Math.Pow(h.X - holes[j].X, 2) + Math.Pow(h.Y - holes[j].Y, 2)) > h.Radius + holes[j].Radius + 0.01,
                    "Final hole/pattern instances overlap or touch.");
        }
        Require(ExpectedVolume(g) > 0, "Final host volume is invalid.");
    }
    public static double Value(ExternalPlateContract g, string target, ParameterKey key) => key switch
    {
        ParameterKey.ExtrusionDepth when target == g.Root => g.DepthMm,
        ParameterKey.HoleDiameter => g.Holes.Single(h => h.Target == target).DiameterMm,
        ParameterKey.PatternCount => g.Patterns.Single(p => p.Target == target).Count,
        ParameterKey.PatternSpacing => g.Patterns.Single(p => p.Target == target).SpacingMm,
        _ => throw new StateException(V03FailureCodes.CapabilityUnavailable, "Target/parameter has no geometry oracle.")
    };
    private static ExternalPlateContract Apply(ExternalPlateContract g, ScalarEdit e) => e.Parameter switch
    {
        ParameterKey.ExtrusionDepth => g with { DepthMm = e.Value },
        ParameterKey.HoleDiameter => g with { Holes = g.Holes.Select(h => h.Target == e.Target ? h with { DiameterMm = e.Value } : h).ToArray() },
        ParameterKey.PatternCount => g with { Patterns = g.Patterns.Select(p => p.Target == e.Target ? p with { Count = checked((int)e.Value) } : p).ToArray() },
        ParameterKey.PatternSpacing => g with { Patterns = g.Patterns.Select(p => p.Target == e.Target ? p with { SpacingMm = e.Value } : p).ToArray() },
        _ => throw new StateException(V03FailureCodes.CapabilityUnavailable, "Unsupported external scalar.")
    };
    public static ExternalEditPreparation Prepare(ExternalEditState current, EditSetRequest request)
    {
        try { ContractValidation.Edits(request); } catch (ContractException e) { throw new StateException(e.Code, e.Message, e); }
        Require(request.Origin == ModelOrigin.External, "External batch cannot use a managed origin.");
        return Prepare(current, request.Selection, request.Edits);
    }
    public static ExternalEditPreparation Prepare(ExternalEditState current, ScalarEditRequest request)
    {
        try { ContractValidation.Edit(request); } catch (ContractException e) { throw new StateException(e.Code, e.Message, e); }
        Require(request.Origin == ModelOrigin.External, "External scalar cannot use a managed origin.");
        return Prepare(current, request.Selection, new[] { request.Edit });
    }
    private static ExternalEditPreparation Prepare(ExternalEditState current, PartSelection selection, IReadOnlyList<ScalarEdit> edits)
    {
        Validate(current);
        Require(selection == current.Observation.Selection, "Document/configuration/revision or exact source/copy fingerprint changed.", "STALE_REFERENCE");
        var model = current.Observation; var map = model.Features.ToDictionary(f => f.SemanticId);
        var graph = new DependencyGraph(model.Dependencies.Select(e => new DependencyEdge(e.Prerequisite, e.Dependent, DependencyKind.NativeInput)));
        var resolved = new List<ExternalPreparedEdit>(); var final = current.Geometry;
        foreach (var e in edits)
        {
            Require(map.TryGetValue(e.Target, out var f), "Unresolved explicit target.", "BINDING_UNRESOLVED");
            Require(f!.Health == ObservationHealth.Healthy && f.NativeReference is not null && f.EditSupport == EditSupport.Editable &&
                f.DependencyCompleteness == EvidenceCompleteness.Known, "Target is not qualified in this configuration.", V03FailureCodes.ObservedOnlyTarget);
            var p = f.Parameters.SingleOrDefault(p => p.Key == e.Parameter);
            Require(p is not null && ObservedStateValidation.Matches(f.Subtype, e.Parameter, p.Accessor), "Unsupported subtype/accessor pair.", V03FailureCodes.UnsupportedNativeSubtype);
            Near(p!.Value, e.ExpectedOldValue, e.Parameter);
            foreach (var id in graph.AffectedBy(new[] { f.SemanticId }))
                Require((map[id].EditSupport == EditSupport.Editable || map[id].SupportReason == "M14_VERIFIED_SUPPORT_NODE") &&
                    map[id].Health == ObservationHealth.Healthy && map[id].DependencyCompleteness == EvidenceCompleteness.Known,
                    "Unknown/read-only downstream intent cannot be verified.", "UNVERIFIABLE_DEPENDENCY");
            resolved.Add(new(e, f, p)); final = Apply(final, e);
        }
        // Validate the complete proposal before searching a dependency-safe intermediate order.
        ValidateGeometry(final);
        var pending = resolved.ToList(); var ordered = new List<ExternalPreparedEdit>(); var intermediate = current.Geometry;
        while (pending.Count > 0)
        {
            ExternalPreparedEdit? next = null;
            foreach (var candidate in pending.OrderBy(e => e.Feature.TreeOrdinal).ThenBy(e => e.Edit.Parameter))
            {
                if (pending.Any(other => other != candidate && other.Edit.Target != candidate.Edit.Target &&
                    graph.AffectedBy(new[] { other.Edit.Target }).Contains(candidate.Edit.Target))) continue;
                try { ValidateGeometry(Apply(intermediate, candidate.Edit)); next = candidate; break; } catch (StateException) { }
            }
            Require(next is not null, "Final model is legal but no declared dependency-safe intermediate order exists.", "NO_SAFE_EDIT_ORDER");
            ordered.Add(next!); pending.Remove(next!); intermediate = Apply(intermediate, next!.Edit);
        }
        var changed = edits.Select(e => e.Target).Distinct().ToArray();
        var parameters = resolved.Select(e => e.Parameter.SemanticId).ToArray();
        var updated = model with { Selection = model.Selection with { ExpectedRevision = checked(selection.ExpectedRevision + 1) },
            Features = model.Features.Select(f => f with { Parameters = f.Parameters.Select(p => {
                var edit = edits.SingleOrDefault(e => e.Target == f.SemanticId && e.Parameter == p.Key);
                return edit is null ? p : p with { Value = edit.Value }; }).ToArray() }).ToArray() };
        var proposed = current with { Observation = updated, Geometry = final }; Validate(proposed);
        return new(current, proposed, ordered, new ChangeSet(changed, parameters, Array.Empty<string>()));
    }
    // Only recognized qualified nodes are adapted for the legacy coordinator. The complete
    // external inventory and downstream checks stay in the companion; no OperationNodes exist.
    public static CadState Adapter(ExternalEditState external)
    {
        Validate(external); var model = external.Observation; var selected = model.Features.Where(f => f.EditSupport == EditSupport.Editable).ToArray();
        var ids = selected.Select(f => f.SemanticId).ToHashSet();
        var state = new CadState { SchemaVersion = "0.2", Document = new(model.Selection.DocumentId, model.Selection.ConfigurationId,
            model.Selection.ConfigurationName, model.Selection.WorkingCopy.Path), Revision = model.Selection.ExpectedRevision,
            Features = selected.Select(f => new FeatureNode(f.SemanticId, f.Subtype switch {
                NativeSubtype.StraightBlindBossExtrude => OperationKind.CreateExtrude,
                NativeSubtype.SingleCircleThroughAllCut => OperationKind.CreateThroughHole,
                NativeSubtype.SingleDirectionLinearPattern => OperationKind.CreateLinearPattern,
                _ => throw new StateException(V03FailureCodes.UnsupportedNativeSubtype, "No legacy adapter for unknown native kind.") }, f.NativeReference!, ReferenceHealth.Healthy)).ToArray(),
            Parameters = selected.SelectMany(f => f.Parameters).Select(p => new ParameterNode(p.SemanticId,
                p.Unit == ScalarUnit.Count ? ParameterKind.Count : ParameterKind.Length, p.Value)).ToArray(),
            Bindings = selected.SelectMany(f => f.Parameters.Select(p => new ParameterBinding(p.SemanticId, f.SemanticId, LegacyParameter(p.Key)))).ToArray(),
            Entities = Array.Empty<SemanticEntityNode>(), Relations = Array.Empty<JsonElement>(),
            Dependencies = StateRelationData.Encode(model.Dependencies.Where(e => ids.Contains(e.Prerequisite) && ids.Contains(e.Dependent))
                .Select(e => new DependencyEdge(e.Prerequisite, e.Dependent, DependencyKind.NativeInput)).Distinct()) };
        StateValidation.Validate(state); return state;
    }
    public static void Near(double actual, double expected, ParameterKey key) => Require(double.IsFinite(actual) &&
        Math.Abs(actual - expected) <= (key == ParameterKey.PatternCount ? 0 : 0.001), "Native/state scalar differs from expected value.", "STATE_DRIFT_DETECTED");
    private static void Require(bool value, string message, string code = "EXTERNAL_PREFLIGHT_FAILED")
    { if (!value) throw new StateException(code, message); }
}
