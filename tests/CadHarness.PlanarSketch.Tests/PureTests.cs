using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;

internal static class PureTests
{
    internal static int Run(string root)
    {
        var tests = new List<(string, Action)>();
        void Add(string name, Action run) => tests.Add((name, run));
        void Accept(ClosedSketch sketch) => _ = PlanarSketchPreflight.Prepare(sketch);
        void Reject(ClosedSketch sketch, string? code = null)
        {
            try { Accept(sketch); } catch (ContractException e) { Program.Check(code is null || e.Code == code, e.Code); return; }
            throw new Exception("Unsafe sketch accepted.");
        }
        Add("outer and two inner loops; exact area and winding", () => {
            var p = PlanarSketchPreflight.Prepare(SketchCases.Composite);
            Program.Check(p.Primitives.Count == 9 && Math.Abs(p.Loops.Sum(l => l.SignedAreaMm2) - (2400 - Math.PI * 16 - (72 + Math.PI * 9))) < 1e-8, "Area/slot lowering mismatch."); });
        Add("signed coordinates and circle radius", () => Accept(SketchCases.Circle()));
        Add("closed arc with declared length/radius", () => Accept(SketchCases.Arcs));
        Add("horizontal vertical parallel perpendicular equal distance", () => Accept(SketchCases.Rectangle));
        Add("concentric and coincident with nested circular loops", () => Accept(SketchCases.Rings));
        Add("open chain rejects", () => Reject(SketchCases.Arcs with { Entities = SketchCases.Arcs.Entities.Select(e => e.Id == "line" ? e with { Points = new[] { "b", "a" } } : e).ToArray() }));
        Add("bowtie self intersection rejects", () => Reject(SketchCases.Composite with { Points = SketchCases.Composite.Points.Select(p => p.Id == "b" ? SketchCases.Point("b", 30, 20) : p.Id == "c" ? SketchCases.Point("c", 30, -20) : p).ToArray() }));
        Add("zero area rejects", () => Reject(SketchCases.Rectangle with { Points = SketchCases.Rectangle.Points.Select(p => p with { Y = new(0) }).ToArray() }));
        Add("wrong winding rejects", () => Reject(SketchCases.Composite with { Entities = SketchCases.Composite.Entities.Select(e => e.Id == "polygon" ? e with { Points = e.Points.Reverse().ToArray() } : e).ToArray() }));
        Add("inner outside outer rejects", () => Reject(SketchCases.Composite with { Points = SketchCases.Composite.Points.Select(p => p.Id == "hole" ? SketchCases.Point("hole", 40, 3) : p).ToArray() }));
        Add("touching inner boundary rejects", () => Reject(SketchCases.Composite with { Points = SketchCases.Composite.Points.Select(p => p.Id == "hole" ? SketchCases.Point("hole", -26, 3) : p).ToArray() }));
        Add("circle and slot intersection rejects", () => Reject(SketchCases.Composite with { Points = SketchCases.Composite.Points.Select(p => p.Id == "hole" ? SketchCases.Point("hole", 10, -5) : p).ToArray() }));
        Add("contradictory dimension rejects", () => Reject(SketchCases.Circle() with { Constraints = new[] { new SketchConstraint("radial", ConstraintKind.Radius, new[] { "circle" }, new(4)) } }, V03FailureCodes.OverConstrainedSketch));
        Add("unresolved radial driving degree rejects", () => Reject(SketchCases.Circle() with { Constraints = Array.Empty<SketchConstraint>() }, V03FailureCodes.UnderConstrainedSketch));
        Add("declared under constrained rejects", () => Reject(SketchCases.Circle() with { Status = ConstraintStatus.UnderConstrained }, V03FailureCodes.UnderConstrainedSketch));
        Add("declared over constrained rejects", () => Reject(SketchCases.Circle() with { Status = ConstraintStatus.OverConstrained }, V03FailureCodes.OverConstrainedSketch));
        Add("duplicate constraint rejects", () => Reject(SketchCases.Rectangle with { Constraints = SketchCases.Rectangle.Constraints.Concat(new[] { SketchCases.Rectangle.Constraints[0] with { Id = "extra" } }).ToArray() }, V03FailureCodes.OverConstrainedSketch));
        Add("arc sweep endpoint disagreement rejects", () => Reject(SketchCases.Arcs with { Entities = SketchCases.Arcs.Entities.Select(e => e.Id == "arc" ? e with { SweepDegrees = 90 } : e).ToArray() }));
        Add("slot length/center inconsistency rejects", () => Reject(SketchCases.Composite with { Entities = SketchCases.Composite.Entities.Select(e => e.Id == "slot" ? e with { Length = new(19) } : e).ToArray() }));
        Add("lowered primitive budget rejects", () => {
            var points = Enumerable.Range(0, 66).Select(i => SketchCases.Point("p_" + i, (i < 33 ? -100 : 100) + 10 * Math.Cos((i % 33) * 2 * Math.PI / 33), 10 * Math.Sin((i % 33) * 2 * Math.PI / 33))).ToArray();
            Reject(new(points, new[] {
                new SketchEntity("first", SketchEntityKind.Polyline, points.Take(33).Select(p => p.Id).ToArray(), null, null, null, null),
                new SketchEntity("second", SketchEntityKind.Polyline, points.Skip(33).Select(p => p.Id).ToArray(), null, null, null, null) },
                new[] { new SketchLoop("outer_a", false, new[] { "first" }), new SketchLoop("outer_b", false, new[] { "second" }) }, Array.Empty<SketchConstraint>(), ConstraintStatus.FullyConstrained)); });
        Add("frame mirrored rejects", () => {
            try { ContractValidation.Frame(SketchCases.XY with { ZAxis = new(0, 0, -1) }); } catch (ContractException) { return; } throw new Exception("Mirrored accepted."); });
        Add("rotated frame signed world placement", () => {
            var p = SketchCases.Placement with { Frame = new(new(5, -4, 10), new(0, 1, 0), new(-1, 0, 0), new(0, 0, 1)), Offset = new(-2) };
            var w = SketchFrames.World(p, new(-3, 7)); Program.Check(w == new Vector3(-2, -7, 8), "Signed transform mismatch."); });
        Add("native normal reversed rejects", () => {
            try { SketchFrames.VerifyPlane(SketchCases.Placement, new(0, 0, 0), new(0, 0, -1), null); } catch (ContractException e) when(e.Code == "SPATIAL_FRAME_MISMATCH") { return; } throw new Exception("Reverse accepted."); });
        Add("face direction required", () => {
            try { ContractValidation.Placement(SketchCases.Placement with { Plane = new("host.top_face", CadHarness.Ir.SemanticType.PlanarFace) }); } catch (ContractException) { return; } throw new Exception("Directionless face accepted."); });
        Add("circle ray containment is analytic", () => { var p = PlanarSketchPreflight.Prepare(SketchCases.Circle(0, 0, 5)); Program.Check(PlanarSketchPreflight.Contains(p.Loops[0], new(0,0)) && !PlanarSketchPreflight.Contains(p.Loops[0], new(6,0)), "Containment mismatch."); });
        var results = tests.Select(test => { string? error = null; try { test.Item2(); } catch (Exception e) { error = e.ToString(); } Console.WriteLine((error is null ? "PASS " : "FAIL ") + test.Item1 + (error is null ? "" : error)); return new { name = test.Item1, passed = error is null, error }; }).ToArray();
        var dir = Path.Combine(root, "artifacts/milestone15/pure", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")); Directory.CreateDirectory(dir);
        Program.Write(Path.Combine(dir, "result.json"), new { passed = results.Count(r => r.passed), failed = results.Count(r => !r.passed), nativeCalls = 0, results });
        return results.All(r => r.passed) ? 0 : 1;
    }
}
