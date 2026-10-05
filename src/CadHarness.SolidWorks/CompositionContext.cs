using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

// M4 uses direct construction outputs. M5 enables CADState candidate binding via
// RelationProgram; neither path uses display names or guesses stale topology.
public sealed partial class SolidWorksExecutionContext
{
    private sealed record NativeOutput(SemanticType Type, string Owner, NativePersistentReference Reference, SemanticGeometry? LogicalGeometry = null);
    private readonly Dictionary<string, NativeOutput> outputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OperationNode> operations = new(StringComparer.Ordinal);
    internal string? ConstructionRoot { get; private set; }
    internal CadProgram? RelationProgram { get; set; }
    internal long RelationRevision { get; set; }
    internal bool RelationContextUsable { get; set; } = true;
    private readonly Dictionary<string, NativePersistentReference> holeProfiles = new(StringComparer.Ordinal);

    internal void RegisterFeature(OperationNode operation, IFeature feature)
    {
        var id = operation.SemanticId!;
        if (operations.ContainsKey(id)) throw new NativeOperationException(FailureCodes.PreconditionFailed, "Semantic feature ID is already registered.");
        operations.Add(id, operation);
        Register(id, SemanticType.FeatureRef, id, feature);
    }
    private void Register(string id, SemanticType type, string owner, object native, SemanticGeometry? logical = null) =>
        outputs.Add(id, new(type, owner, PersistentReferenceAdapter.Capture(this, native), logical));

    internal T Resolve<T>(SemanticReference reference) where T : class
    {
        CheckThread();
        if (RelationProgram is not null)
        {
            var bound = new SemanticEntityBinder().Bind(CaptureBindingState(), new InputContract("native", null, new[] { reference.Type }),
                new BindingQuery(reference.SemanticId, reference.Type));
            if (!bound.Succeeded) throw new NativeOperationException(bound.FailureCode!, bound.Message);
            var nativeResult = PersistentReferenceAdapter.Resolve<T>(this, bound.Entity!.NativeReference);
            if (nativeResult.NativeObject is null) throw new NativeOperationException("STALE_REFERENCE", "Bound entity has no healthy native object: " + reference.SemanticId);
            return nativeResult.NativeObject;
        }
        if (!outputs.TryGetValue(reference.SemanticId, out var output) || output.Type != reference.Type)
            throw new NativeOperationException("BINDING_UNRESOLVED", "No construction output matches " + reference.SemanticId + ".");
        var resolution = PersistentReferenceAdapter.Resolve<T>(this, output.Reference);
        if (resolution.NativeObject is null)
            throw new NativeOperationException("STALE_REFERENCE", "Construction output is no longer healthy: " + reference.SemanticId + ".");
        return resolution.NativeObject;
    }
    public IReadOnlyList<string> ConstructedFeatureIds => operations.Keys.ToArray();
    public object NativeFeature(string semanticId) => Resolve<IFeature>(new(semanticId, SemanticType.FeatureRef));
    internal string Owner(string id) => outputs.TryGetValue(id, out var output) ? output.Owner :
        throw new NativeOperationException("BINDING_UNRESOLVED", "Unknown construction output: " + id);
    internal OperationNode Operation(string id) => operations.TryGetValue(id, out var operation) ? operation :
        throw new NativeOperationException("BINDING_UNRESOLVED", "Unknown construction feature: " + id);
    internal bool ContainsFeature(string id) => operations.ContainsKey(id);

    internal void RegisterExtrude(OperationNode operation, IFeature feature)
    {
        RegisterFeature(operation, feature);
        ConstructionRoot = operation.SemanticId!;
        var id = ConstructionRoot;
        var bodies = NativeGeometry.SolidBodies(Document);
        if (bodies.Count != 1) throw new NativeOperationException("GEOMETRY_INVALID", "Construction extrusion must create one solid body.");
        var body = bodies[0];
        Register(id + ".body", SemanticType.BodyRef, id, body);
        var origin = new Point3(0, 0, 0);
        var xAxis = new Vector3(1, 0, 0); var yAxis = new Vector3(0, 1, 0); var zAxis = new Vector3(0, 0, 1);
        Register(id + ".local_frame", SemanticType.LocalFrame, id, feature, new(origin, Frame: new(origin, xAxis, yAxis, zAxis)));
        var profile = operation.Parameter<ProfileParameter>("profile").Value;
        var depth = Millimeters.ToMeters(operation.Parameter<LengthParameter>("depthMm").Millimeters);
        var faces = NativeTopology.Faces(body);
        var top = NativeTopology.Unique(faces.Where(f => NativeTopology.IsZPlane(f, depth)), "extrude top face");
        var bottom = NativeTopology.Unique(faces.Where(f => NativeTopology.IsZPlane(f, 0)), "extrude bottom face");
        Register(id + ".top_face", SemanticType.PlanarFace, id, top);
        Register(id + ".bottom_face", SemanticType.PlanarFace, id, bottom);
        if (profile is CircleProfile circle)
        {
            Register(id + ".axis_x", SemanticType.ReferenceAxis, id, feature, new(origin, xAxis));
            Register(id + ".axis_y", SemanticType.ReferenceAxis, id, feature, new(origin, yAxis));
            Register(id + ".rotational_reference", SemanticType.CylindricalFace, id,
                NativeTopology.Unique(faces.Where(f => f.GetSurface() is ISurface s && s.IsCylinder() &&
                    NativeTopology.Near(NativeGeometry.Doubles(s.CylinderParams)[6], circle.DiameterMm / 2000)), "extrude outer cylindrical face"));
            return;
        }
        var rectangle = (CenteredRectangleProfile)profile;
        var edges = NativeTopology.Edges(body);
        var w = Millimeters.ToMeters(rectangle.WidthMm) / 2;
        var h = Millimeters.ToMeters(rectangle.HeightMm) / 2;
        // Fixed local-frame meaning: corners ordered (-x,-y),(-x,+y),(+x,-y),(+x,+y).
        var index = 1;
        foreach (var x in new[] { -w, w })
            foreach (var y in new[] { -h, h })
                Register(id + ".outer_edge_" + index++, SemanticType.LinearEdge, id,
                    NativeTopology.Unique(edges.Where(e => NativeTopology.IsVerticalAt(e, x, y)), "extrude corner edge"));
        var datumX = NativePatternDirection.Create(Document, 0);
        var datumY = NativePatternDirection.Create(Document, 1);
        Register(id + ".direction_x", SemanticType.ReferenceAxis, id, datumX);
        Register(id + ".direction_y", SemanticType.ReferenceAxis, id, datumY);
        Register(id + ".axis_x", SemanticType.ReferenceAxis, id, datumX);
        Register(id + ".axis_y", SemanticType.ReferenceAxis, id, datumY);
    }
    internal void RegisterHole(OperationNode operation, IFeature feature)
    {
        RegisterFeature(operation, feature);
        var diameter = Millimeters.ToMeters(operation.Parameter<LengthParameter>("diameterMm").Millimeters);
        var walls = NativeTopology.FeatureFaces(feature).Where(f => f.GetSurface() is ISurface s && s.IsCylinder() &&
            NativeTopology.Near(NativeGeometry.Doubles(s.CylinderParams)[6], diameter / 2));
        Register(operation.SemanticId! + ".wall_face", SemanticType.CylindricalFace, operation.SemanticId!,
            NativeTopology.Unique(walls, "hole cylindrical wall"));
    }
    internal void RegisterHoleProfile(string owner, IFeature profile)
    {
        holeProfiles[owner] = PersistentReferenceAdapter.Capture(this, profile);
        Register(owner + ".profile", SemanticType.SketchProfile, owner, profile);
    }
    internal IFeature HoleProfile(string owner)
    {
        if (!holeProfiles.TryGetValue(owner, out var reference)) throw new NativeOperationException("BINDING_UNRESOLVED", "No bound circle profile for " + owner);
        return PersistentReferenceAdapter.Resolve<IFeature>(this, reference).NativeObject ?? throw new NativeOperationException("STALE_REFERENCE", "Bound hole profile is stale.");
    }
    internal IFeature DirectFeature(string id) => PersistentReferenceAdapter.Resolve<IFeature>(this, outputs[id].Reference).NativeObject ??
        throw new NativeOperationException("STALE_REFERENCE", "Managed feature reference is stale: " + id);
    internal void UpdateConstructedOperations(CadProgram program)
    {
        foreach (var operation in program.Operations) if (operation.SemanticId is not null && operations.ContainsKey(operation.SemanticId)) operations[operation.SemanticId] = operation;
        RelationProgram = program;
    }
}

internal static class NativeTopology
{
    internal static T Unique<T>(IEnumerable<T> candidates, string meaning)
    {
        var items = candidates.Take(2).ToArray();
        if (items.Length != 1) throw new NativeOperationException(items.Length == 0 ? "BINDING_UNRESOLVED" : "BINDING_AMBIGUOUS",
            "Construction output must have exactly one native " + meaning + ".");
        return items[0];
    }
    internal static IReadOnlyList<IFace2> Faces(IBody2 body) => Objects<IFace2>(body.GetFaces());
    internal static IReadOnlyList<IFace2> FeatureFaces(IFeature feature) => Objects<IFace2>(feature.GetFaces());
    internal static IReadOnlyList<IEdge> Edges(IBody2 body) => Objects<IEdge>(body.GetEdges());
    internal static IReadOnlyList<T> Objects<T>(object native) => native is Array array ? array.Cast<object>().Cast<T>().ToArray() : Array.Empty<T>();
    internal static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-8;
    internal static bool IsZPlane(IFace2 face, double z)
    {
        if (face.GetSurface() is not ISurface surface || !surface.IsPlane()) return false;
        var p = NativeGeometry.Doubles(surface.PlaneParams);
        return p.Length >= 6 && Near(Math.Abs(p[2]), 1) && Near(p[5], z);
    }
    internal static double[][]? Endpoints(IEdge edge)
    {
        if (edge.GetStartVertex() is not IVertex a || edge.GetEndVertex() is not IVertex b) return null;
        return new[] { NativeGeometry.Doubles(a.GetPoint()), NativeGeometry.Doubles(b.GetPoint()) };
    }
    internal static bool IsVerticalAt(IEdge edge, double x, double y) => edge.GetCurve() is ICurve c && c.IsLine() &&
        Endpoints(edge) is { } p && p.All(v => Near(v[0], x) && Near(v[1], y));
    internal static bool IsDirectionEdge(IEdge edge, int axis, double other, double z) => edge.GetCurve() is ICurve c && c.IsLine() &&
        Endpoints(edge) is { } p && p.All(v => Near(v[1 - axis], other) && Near(v[2], z));
    internal static void Select(IModelDoc2 doc, object native, bool append, int mark)
    {
        var data = ((ISelectionMgr)doc.SelectionManager).CreateSelectData();
        data.Mark = mark;
        var ok = native is IFeature feature ? feature.Select2(append, mark) : native is IEntity entity && entity.Select4(append, data);
        if (!ok) throw new NativeOperationException(FailureCodes.PreconditionFailed, "Native construction input could not be selected.");
    }
}
