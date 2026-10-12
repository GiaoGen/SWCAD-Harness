using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed partial class SolidWorksExecutionContext
{
    private ConvexFilletGeometry FilletGeometry(ConstructionOperation o, ConstructionProgram program) =>
        ConvexFilletProof.Describe(program, GenericExpectation(program)).Single(f => f.Id == o.SemanticId);
    private static double[] EdgePoint(IVertex vertex, LocalFrame frame)
    {
        var p = NativeGeometry.Doubles(vertex.GetPoint());
        return OrthogonalMaterialProof.Local(new(p[0] * 1000, p[1] * 1000, p[2] * 1000), frame);
    }
    private static bool Near(double[] a, double[] b) => a.Length == b.Length && a.Zip(b).All(p => Math.Abs(p.First - p.Second) < 1e-6);
    private bool EdgeMatches(IEdge edge, ConvexFilletGeometry geometry, LocalFrame frame, NativePersistentReference host)
    {
        if (edge.GetCurve() is not ICurve curve || !curve.IsLine() || edge.GetStartVertex() is not IVertex start || edge.GetEndVertex() is not IVertex end) return false;
        var a = EdgePoint(start, frame); var b = EdgePoint(end, frame);
        if (!(Near(a, geometry.Start) && Near(b, geometry.End) || Near(b, geometry.Start) && Near(a, geometry.End))) return false;
        var faces = NativeTopology.Objects<IFace2>(edge.GetTwoAdjacentFaces2());
        FeatureTrace(new("edge-owner-diagnostic", geometry.Id, string.Join(";", faces.Select(f => f.GetFeature() is IFeature owner ? owner.GetTypeName2() + ":" + PersistentReferenceAdapter.Capture(this, owner).Base64 : "no-owner")) + ";expected=" + host.Base64, Success: faces.Count == 2));
        var source = genericFeatures[geometry.Host]; var ancestors = new System.Collections.Generic.HashSet<NativePersistentReference> { host };
        while (source.Operation.HostBody is { } input)
        {
            source = genericFeatures[input.SemanticId[..^5]];
            if (source.Operation.Kind is ConstructionKind.CreateExtrude or ConstructionKind.CreateAdditiveBoss) ancestors.Add(source.Feature);
        }
        // Fused coplanar faces can retain the earlier positive feature's owner.
        // Require the declared host on at least one face and only its exact
        // managed positive ancestors on the other, with both outward normals.
        if (faces.Count != 2 || !faces.Any(f => f.GetFeature() is IFeature owner && PersistentReferenceAdapter.Capture(this, owner) == host) ||
            faces.Any(f => !((ISurface)f.GetSurface()).IsPlane() || f.GetFeature() is not IFeature owner || !ancestors.Contains(PersistentReferenceAdapter.Capture(this, owner)))) return false;
        var corner = geometry.Start;
        return geometry.Transverse.All(i => faces.Count(f => {
            var normal = NativeGeometry.Doubles(f.Normal); var direction = new CadHarness.Ir.V03.Vector3(normal[0], normal[1], normal[2]);
            var sign = Math.Abs(corner[i] - geometry.Low[i]) < 1e-7 ? -1 : 1;
            return Math.Abs(SketchFrames.Dot(direction, new[] { frame.XAxis, frame.YAxis, frame.ZAxis }[i]) - sign) < 1e-8;
        }) == 1);
    }
    private IEdge BindConvexEdge(ConstructionOperation o, ConstructionProgram program)
    {
        var geometry = FilletGeometry(o, program); var frame = GenericExpectation(program).Frame;
        var host = genericFeatures[o.EdgeTreatment!.HostFeature.SemanticId].Feature;
        var body = Resolve<IBody2>(o.HostBody!);
        var matches = NativeTopology.Objects<IEdge>(body.GetEdges()).Where(edge => EdgeMatches(edge, geometry, frame, host)).ToArray();
        ConvexFilletProof.RequireUniqueCandidateCount(matches.Length);
        var reference = PersistentReferenceAdapter.Capture(this, matches[0]);
        var exact = PersistentReferenceAdapter.Resolve<IEdge>(this, reference).NativeObject;
        if (exact is null || !EdgeMatches(exact, geometry, frame, host)) throw new StateException("STALE_REFERENCE", "Bound edge ownership/type/geometry changed before mutation.");
        FeatureTrace(new("edge-bound", o.SemanticId, reference.Base64, Success: true)); return exact;
    }
    internal void ProbeConvexEdge(ConstructionOperation operation, ConstructionProgram program) => _ = BindConvexEdge(operation, program);
    private void CreateGenericFillet(ConstructionOperation o, ConstructionProgram program)
    {
        var edge = BindConvexEdge(o, program);
        var image = new GenericEdgeImage(PersistentReferenceAdapter.Capture(this, edge), genericFeatures[o.EdgeTreatment!.HostFeature.SemanticId].Feature,
            NativeTopology.Objects<IFace2>(edge.GetTwoAdjacentFaces2()).Select(f => PersistentReferenceAdapter.Capture(this, f.GetFeature())).OrderBy(r => r.Base64, StringComparer.Ordinal).ToArray());
        Document.ClearSelection2(true); NativeTopology.Select(Document, edge, false, 1);
        var feature = ApplyFilletHandler.CreateSimpleUniform(Document, o.EdgeTreatment.Radius.Millimeters)
            ?? throw new StateException("GEOMETRY_INVALID", "Native uniform fillet returned null.");
        Document.ClearSelection2(true); RebuildSketch();
        var binding = new GenericFeatureBinding(o, PersistentReferenceAdapter.Capture(this, feature), "", false, null, null, null) { Edge = image };
        genericFeatures.Add(o.SemanticId, binding); Register(o.SemanticId, SemanticType.FeatureRef, o.SemanticId, feature);
        var body = NativeGeometry.SolidBodies(Document).Single(); Register(o.SemanticId + ".body", SemanticType.BodyRef, o.SemanticId, body);
        VerifyGenericFillet(binding, program);
    }
    private void VerifyGenericFillet(GenericFeatureBinding binding, ConstructionProgram program)
    {
        var feature = ResolveGenericFeature(binding);
        if (feature.GetErrorCode2(out var warning) != 0 || warning || feature.IsSuppressed() || feature.GetDefinition() is not ISimpleFilletFeatureData2 data)
            throw new StateException("FEATURE_REBUILD_FAILED", "Bound convex fillet is unhealthy or not a simple fillet.");
        var o = binding.Operation; var geometry = FilletGeometry(o, program); var frame = GenericExpectation(program).Frame;
        if (Math.Abs(data.DefaultRadius * 1000 - geometry.Radius) > 1e-6 || !data.AccessSelections(Document, null!))
            throw new StateException("STATE_DRIFT_DETECTED", "Fillet radius or native selections differ.");
        try
        {
            var edges = NativeTopology.Objects<IEdge>(data.Edges);
            if (edges.Count != 1 || PersistentReferenceAdapter.Capture(this, edges[0]) != binding.Edge!.Edge ||
                !EdgeMatches(edges[0], geometry, frame, binding.Edge.HostFeature) ||
                !NativeTopology.Objects<IFace2>(edges[0].GetTwoAdjacentFaces2()).Select(f => PersistentReferenceAdapter.Capture(this, f.GetFeature())).OrderBy(r => r.Base64, StringComparer.Ordinal).SequenceEqual(binding.Edge.FaceOwners))
                throw new StateException("BINDING_AMBIGUOUS", "Fillet no longer consumes its exact owned convex edge.");
        }
        finally { data.ReleaseSelectionAccess(); }
        var faces = NativeTopology.Objects<IFace2>(feature.GetFaces());
        if (faces.Count != 1 || faces[0].GetSurface() is not ISurface surface || !surface.IsCylinder())
            throw new StateException("GEOMETRY_INVALID", "Finite convex fillet must produce one cylindrical patch.");
        var cylinder = NativeGeometry.Doubles(surface.CylinderParams);
        var center = OrthogonalMaterialProof.Local(new(cylinder[0] * 1000, cylinder[1] * 1000, cylinder[2] * 1000), frame);
        var direction = new CadHarness.Ir.V03.Vector3(cylinder[3], cylinder[4], cylinder[5]);
        var axis = new[] { frame.XAxis, frame.YAxis, frame.ZAxis }[geometry.Axis];
        var uv = NativeGeometry.Doubles(faces[0].GetUVBounds());
        if (Math.Abs(cylinder[6] * 1000 - geometry.Radius) > 1e-6 || Math.Abs(Math.Abs(SketchFrames.Dot(direction, axis)) - 1) > 1e-8 ||
            geometry.Transverse.Any(i => Math.Abs(center[i] - geometry.Center[i]) > 1e-6) || Math.Abs(uv[1] - uv[0] - Math.PI / 2) > 1e-7 ||
            Math.Abs(faces[0].GetArea() * 1e6 - geometry.Radius * Math.PI / 2 * (geometry.End[geometry.Axis] - geometry.Start[geometry.Axis])) > 1e-4)
            throw new StateException("GEOMETRY_INVALID", "Convex patch center/axis/radius/sweep/area differs from the finite material proof.");
    }
}
