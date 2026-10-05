using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.CurrentRuntimeCompatibility.Tests;
internal sealed record DirectionRead(string Slot, int Count, double SpacingMm, bool ReferenceMatches, bool ReverseMatches,
    bool ActualReverse, bool ExpectedReverse, NativePersistentReference ExpectedReference, NativePersistentReference ActualReference, double[] NativeVector, double[] AxisEndpointsMeters);
internal sealed record CylinderRead(double X, double Y, double Diameter, double[] Levels);
internal sealed record GeometryRead(ExtrudeMeasurement Extents, double Volume, CylinderRead[] Cylinders);
internal static partial class NativeTests
{
    private static void Near(double a, double b, double tolerance = 1e-6) => Program.Check(double.IsFinite(a) && Math.Abs(a-b) <= tolerance, $"Actual {a} differs from expected {b}." );
    private static void VerifyState(SolidWorksExecutionContext context, CadState state, CadProgram program, long revision)
    {
        StateValidation.Validate(state); Program.Check(state.Features.Count == program.Operations.Count && state.Revision == revision, "Managed feature/revision differs.");
        Program.Check(StateRelationData.Relations(state).ToHashSet().SetEquals(program.Relations), "Persisted relations differ.");
        Program.Check(StateRelationData.Dependencies(state).ToHashSet().SetEquals(new DesignRelationEngine().Solve(program).Dependencies.Edges), "Persisted dependencies differ.");
        foreach (var feature in state.Features) Program.Check(PersistentReferenceAdapter.Resolve<IFeature>(context, feature.NativeReference).Health == ReferenceHealth.Healthy, "Feature persistent reference failed.");
        foreach (var binding in state.Bindings) Near(state.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value,
            ParameterMutationRegistry.Default.Expected(program.Operations.Single(o => o.SemanticId == binding.OwnerFeatureSemanticId), binding.Parameter));
        var consumed = program.Operations.Where(o => o.Kind is OperationKind.ApplyFillet or OperationKind.ApplyChamfer).SelectMany(o => o.Input("edges")!.References).Select(r => r.SemanticId).ToHashSet();
        foreach (var entity in state.Entities)
        {
            var binding = new SemanticEntityBinder().Bind(state, new InputContract("verify", null, new[] { entity.Type }), new(entity.SemanticId, entity.Type, entity.OwnerFeatureSemanticId));
            if (entity.ReferenceHealth != ReferenceHealth.Healthy)
            { Program.Check(!binding.Succeeded && entity.Type == SemanticType.LinearEdge && consumed.Contains(entity.SemanticId), "Unexpected unavailable entity."); continue; }
            Program.Check(binding.Succeeded && PersistentReferenceAdapter.Resolve<object>(context, entity.NativeReference).Health == ReferenceHealth.Healthy, "Persisted binding failed: " + entity.SemanticId);
            if (entity.Type == SemanticType.ReferenceAxis)
            {
                var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, entity.NativeReference).NativeObject;
                Program.Check(feature?.GetSpecificFeature2() is IRefAxis && entity.Geometry?.Direction is not null, "Direction is not a real native datum.");
            }
        }
    }
    private static DirectionRead[] ReadDirections(SolidWorksExecutionContext context, CadState state, CadProgram program)
    {
        var op = program.Operations.Single(o => o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern);
        var data = (ILinearPatternFeatureData)((IFeature)context.NativeFeature(op.SemanticId!)).GetDefinition();
        var linear = op.Kind == OperationKind.CreateLinearPattern;
        Program.Check(data.D1TotalInstances == op.Parameter<CountParameter>(linear ? "count" : "countX").Value && data.D2TotalInstances == (linear ? 1 : op.Parameter<CountParameter>("countY").Value), "Native counts differ.");
        Near(data.D1Spacing * 1000, op.Parameter<LengthParameter>(linear ? "spacingMm" : "spacingXMm").Millimeters);
        if (!linear) Near(data.D2Spacing * 1000, op.Parameter<LengthParameter>("spacingYMm").Millimeters);
        Program.Check(!data.D2PatternSeedOnly && !data.VarySketch, "Native pattern mode differs.");
        var results = new List<DirectionRead>(); Program.Check(data.AccessSelections(context.Document, null), "Cannot access native selections.");
        try
        {
            Program.Check(data.PatternFeatureArray is Array seeds && seeds.Length == 1 && PersistentReferenceAdapter.Capture(context, seeds.GetValue(0)!) == state.Features.Single(f => f.SemanticId == "seed").NativeReference, "Native pattern seed reference differs.");
            Program.Check(data.SkippedItemArray is not Array skipped || skipped.Length == 0, "Pattern skips native instances.");
            for (var axis = 0; axis < (linear ? 1 : 2); axis++)
            {
                var expected = state.Entities.Single(e => e.SemanticId == "stock.direction_" + (axis == 0 ? "x" : "y"));
                var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, expected.NativeReference).NativeObject!;
                var actual = PersistentReferenceAdapter.Capture(context, Canonical(context, axis == 0 ? data.D1Axis : data.D2Axis));
                var p = (double[])((IRefAxis)feature.GetSpecificFeature2()).GetRefAxisParams();
                var v = new[] { p[3] - p[0], p[4] - p[1], p[5] - p[2] }; var length = Math.Sqrt(v.Sum(a => a * a)); Program.Check(length > 1e-12, "Degenerate native datum."); v = v.Select(a => a / length).ToArray();
                for (var i = 0; i < 3; i++) { Near(Math.Abs(v[i]), i == axis ? 1 : 0); if (i != axis) { Near(p[i] * 1000, 0); Near(p[i + 3] * 1000, 0); } }
                var reverse = axis == 0 ? data.D1ReverseDirection : data.D2ReverseDirection; var expectedReverse = v[axis] < 0;
                results.Add(new(axis == 0 ? "D1" : "D2", axis == 0 ? data.D1TotalInstances : data.D2TotalInstances,
                    (axis == 0 ? data.D1Spacing : data.D2Spacing) * 1000, actual == expected.NativeReference, reverse == expectedReverse, reverse, expectedReverse, expected.NativeReference, actual, v, p));
            }
        }
        finally { data.ReleaseSelectionAccess(); context.Document.ClearSelection2(true); }
        return results.ToArray();
    }
    private static IFeature Canonical(SolidWorksExecutionContext context, object native)
    {
        if (native is IFeature feature) return feature;
        var list = new List<IFeature>(); var next = (IFeature?)context.Document.FirstFeature();
        for (var i = 0; next is not null && i < 256; i++, next = (IFeature?)next.GetNextFeature())
            if (next.GetTypeName2() == "RefAxis" && Same(next.GetSpecificFeature2(), native)) list.Add(next);
        Program.Check(list.Count == 1, "Native datum owner is not unique."); return list[0];
    }
    private static bool Same(object a, object b)
    {
        var x = Marshal.GetIUnknownForObject(a); var y = Marshal.GetIUnknownForObject(b);
        try { return x == y; } finally { Marshal.Release(x); Marshal.Release(y); }
    }
    private static GeometryRead Observe(SolidWorksExecutionContext context)
    {
        Program.Check(context.Document.Extension.NeedsRebuild2 == 0, "Document still requires rebuild.");
        var body = ((Array)((IPartDoc)context.Document).GetBodies2(0, false)).Cast<object>().Cast<IBody2>().Single();
        var cylinders = ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>().Where(f => ((ISurface)f.GetSurface()).IsCylinder()).Select(f =>
        {
            var c = (double[])((ISurface)f.GetSurface()).CylinderParams; Near(Math.Abs(c[5]), 1);
            var levels = ((Array)f.GetEdges()).Cast<object>().Cast<IEdge>().Select(e => (ICurve)e.GetCurve()).Where(curve => curve.IsCircle())
                .Select(curve => ((double[])curve.CircleParams)[2] * 1000).OrderBy(z => z).ToArray();
            return new CylinderRead(c[0] * 1000, c[1] * 1000, c[6] * 2000, levels);
        }).OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Diameter).ToArray();
        return new(ExtrudeMeasurementReader.Read(context), ((double[])body.GetMassProperties(1))[3] * 1e9, cylinders);
    }
    internal static bool SameGeometry(GeometryRead a, GeometryRead b) =>
        a.Extents.SolidBodyCount == b.Extents.SolidBodyCount && Math.Abs(a.Extents.WidthMm - b.Extents.WidthMm) <= 1e-6 &&
        Math.Abs(a.Extents.HeightMm - b.Extents.HeightMm) <= 1e-6 && Math.Abs(a.Extents.DepthMm - b.Extents.DepthMm) <= 1e-6 &&
        Math.Abs(a.Volume - b.Volume) <= 0.001 && a.Cylinders.Length == b.Cylinders.Length && a.Cylinders.Zip(b.Cylinders).All(pair =>
            Math.Abs(pair.First.X - pair.Second.X) <= 1e-6 && Math.Abs(pair.First.Y - pair.Second.Y) <= 1e-6 && Math.Abs(pair.First.Diameter - pair.Second.Diameter) <= 1e-6 &&
            pair.First.Levels.Length == pair.Second.Levels.Length && pair.First.Levels.Zip(pair.Second.Levels).All(z => Math.Abs(z.First - z.Second) <= 1e-6));
    internal static void VerifyRecordedGeometry(GeometryRead geometry, ExpectedModel expected)
    {
        Near(geometry.Extents.WidthMm, expected.Width); Near(geometry.Extents.HeightMm, expected.Height); Near(geometry.Extents.DepthMm, expected.Thickness); Near(geometry.Volume, expected.Volume, 0.001);
        Program.Check(geometry.Cylinders.Length == expected.Holes.Count + (expected.Fillet > 0 ? 4 : 0), "Native cylinder count differs.");
        foreach (var h in expected.Holes)
        {
            var hole = geometry.Cylinders.Single(c => Math.Abs(c.X - h.X) < 1e-6 && Math.Abs(c.Y - h.Y) < 1e-6 && Math.Abs(c.Diameter - h.Diameter) < 1e-6);
            Program.Check(hole.Levels.Length == 2, "Through-hole boundaries missing."); Near(hole.Levels[0], h.Bottom); Near(hole.Levels[1], h.Top);
        }
    }
    private static string ModelSignature(SolidWorksExecutionContext context)
    {
        var features = new List<object>(); var next = (IFeature?)context.Document.FirstFeature();
        for (var i = 0; next is not null && i < 256; i++, next = (IFeature?)next.GetNextFeature()) features.Add(new { Type = next.GetTypeName2(), next.Name });
        var bodies = ((Array)((IPartDoc)context.Document).GetBodies2(0, false)).Cast<object>().Cast<IBody2>().ToArray();
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in new[] { "", context.Document.ConfigurationManager.ActiveConfiguration.Name })
        {
            var manager = (ICustomPropertyManager)context.Document.Extension.CustomPropertyManager[scope];
            if (manager.GetNames() is Array names) foreach (var name in names.Cast<string>())
            { manager.Get6(name, false, out var value, out _, out _, out _); properties[scope + ":" + name] = manager.GetType2(name) + ":" + value; }
        }
        return JsonSerializer.Serialize(new { FeatureCount = context.Document.GetFeatureCount(), Features = features, Bodies = bodies.Length,
            Faces = bodies.Sum(b => b.GetFaceCount()), Edges = bodies.Sum(b => b.GetEdgeCount()), Properties = properties, Rebuild = context.Document.Extension.NeedsRebuild2 });
    }
}

