using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;

namespace CadHarness.Playground;

public sealed record ModelParameter(string Id, ParameterKey Key, ScalarUnit Unit, double Value, bool Editable);
public sealed record ModelFeature(string Id, string Name, string Kind, string Health, string Support, string Reason,
    string? NativeReference, IReadOnlyList<ModelParameter> Parameters, object? Geometry, string DependencyStatus = "unknown");
public sealed record ModelEdge(string From, string To, string Kind, string Provenance);
public sealed record ModelView(string Origin, string Phase, bool Owned, bool Editable, long? Revision,
    string? SourcePath, string? WorkingPath, string Configuration, bool InventoryComplete,
    IReadOnlyList<ModelFeature> Features, IReadOnlyList<ModelEdge> Edges, IReadOnlyList<RequirementRecord>? Requirements = null);
public sealed record LocalRelations(ModelFeature Focus, IReadOnlyList<ModelFeature> Parents,
    IReadOnlyList<ModelFeature> Children, IReadOnlyList<ModelEdge> Edges);

// A bounded projection of recorded facts, never a target resolver or a geometric inference engine.
public static class ModelViews
{
    public static ModelView External(ObservedModel observed, bool owned, bool editable, ExternalPlateContract? geometry = null)
    {
        ObservedStateValidation.Validate(observed);
        var edges = observed.Dependencies.Select(e => new ModelEdge(e.Prerequisite, e.Dependent, e.Kind.ToString(), "observed_native")).ToList();
        if (geometry is not null)
        {
            foreach (var target in geometry.Holes.Select(h => h.Target).Concat(geometry.Patterns.Select(p => p.Target)))
                edges.Add(new(geometry.Root, target, "Host", "qualified_native_geometry"));
            foreach (var p in geometry.Patterns) edges.Add(new(p.Seed, p.Target, "PatternSeed", "qualified_native_definition"));
        }
        return new("external", owned ? "committed" : "observed_read_only", owned, editable, observed.Selection.ExpectedRevision,
            observed.Selection.Source.Path, observed.Selection.WorkingCopy.Path, observed.Selection.ConfigurationName, observed.InventoryComplete,
            observed.Features.Select(f => new ModelFeature(f.SemanticId, f.DisplayName, f.NativeType + " / " + f.Subtype,
                f.Health.ToString(), editable && f.EditSupport == EditSupport.Editable && f.Parameters.Any(p => Qualified(observed, f, p.Key)) ? "editable" : "read_only", f.SupportReason,
                f.NativeReference?.Base64, f.Parameters.Select(p => new ModelParameter(p.SemanticId, p.Key, p.Unit, p.Value,
                    editable && f.EditSupport == EditSupport.Editable && Qualified(observed, f, p.Key))).ToArray(), f.Geometry, f.DependencyCompleteness.ToString())).ToArray(),
            edges.Distinct().ToArray());
    }
    private static bool Qualified(ObservedModel model, ObservedFeature f, ParameterKey key)
    {
        try { NativeQualificationCandidates.RequireExecutable(model, f.SemanticId, key); return true; }
        catch (ContractException) { return false; }
    }
    public static ModelView Generic(GenericConstructionState state, string path, bool editable, IReadOnlyList<RequirementRecord>? requirements = null) => new("managed", "committed", true,
        editable, state.Revision, null, path, state.Document.ConfigurationName, true,
        state.Features.Select(f => new ModelFeature(f.SemanticId, f.SemanticId, f.Kind + " / " + f.NativeType, "Healthy",
            editable && state.Parameters.Any(p => p.Owner == f.SemanticId && p.Qualified && GenericFeatureCapabilities.IsQualified(f.Kind, p.Key)) ? "editable" : "read_only",
            "Committed Generic state; only explicitly qualified native scalars are editable.", f.Reference.Base64,
            state.Parameters.Where(p => p.Owner == f.SemanticId).Select(p => new ModelParameter(p.SemanticId, p.Key, ScalarUnit.Millimeter, p.Value,
                editable && p.Qualified && GenericFeatureCapabilities.IsQualified(f.Kind, p.Key))).ToArray(), state.Program.Operations.Single(o => o.SemanticId == f.SemanticId), "tracked")).ToArray(),
        state.Dependencies.Select(e => new ModelEdge(e.Prerequisite, e.Dependent, GenericEdgeKind(state, e),
            e.Kind == DependencyKind.NativeInput ? "tracked_native" : "declared_design")).ToArray(), requirements);
    private static string GenericEdgeKind(GenericConstructionState state, DependencyEdge edge)
    {
        if (edge.Kind != DependencyKind.NativeInput) return edge.Kind.ToString();
        var dependent = state.Program.Operations.Single(o => o.SemanticId == edge.Dependent);
        if (dependent.HostBody?.SemanticId == edge.Prerequisite + ".body") return "Host";
        if (dependent.Profile?.SemanticId == edge.Prerequisite + ".profile") return "SketchToFeature";
        return edge.Kind.ToString();
    }
    public static LocalRelations Relations(ModelView model, string id)
    {
        var focus = model.Features.SingleOrDefault(f => f.Id == id) ?? throw new StateException("RELATION_TARGET_NOT_FOUND", "Select an exact recorded feature ID.");
        var edges = model.Edges.Where(e => e.From == id || e.To == id).ToArray();
        return new(focus, model.Features.Where(f => edges.Any(e => e.To == id && e.From == f.Id)).ToArray(),
            model.Features.Where(f => edges.Any(e => e.From == id && e.To == f.Id)).ToArray(), edges);
    }
}

public sealed record ModelOpenRequest(string Mode, string Path, string? Root, string? Configuration, bool Confirmed);
public sealed record BatchPreviewRequest(long Revision, IReadOnlyList<QueryParameterChange> Changes);
public sealed record ScalarPreviewRequest(long Revision, string Target, ParameterKey Parameter, ScalarUnit Unit, double ExpectedOldValue, double Value);
public sealed record ModernConfirmation(string Id, string Sha256, long? Revision, IReadOnlyList<string> Targets, bool Confirmed);
public sealed record ModernPreview(string Id, string Kind, string Sha256, long? Revision, string Stage, bool Executable, object Data);
public sealed record ModernOutcome(MutationResult Transaction, object Details);

// Methods run only on the existing STA dispatcher. Implementations reuse production sessions.
public interface IModernNativeSession
{
    ModelView? Model { get; }
    ModelView OpenModel(ModelOpenRequest request);
    IConstructionPlanningRuntime ConstructionRuntime();
    ModernOutcome ExecuteConstruction(ConstructionPlanPreview preview, string originalResponse);
    QueryPlanningResult PreviewBatch(BatchPreviewRequest request);
    ModernOutcome ExecuteBatch(ExternalQueryPreview preview, ModernConfirmation confirmation);
    object PreviewScalar(ScalarPreviewRequest request);
    ModernOutcome ExecuteScalar(ScalarPreviewRequest request);
}
