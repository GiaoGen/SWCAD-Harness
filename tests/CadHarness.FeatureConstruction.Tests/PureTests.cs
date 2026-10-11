using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using Vector3 = CadHarness.Ir.V03.Vector3;

internal static class PureTests
{
    internal static int Run(string root)
    {
        var results = new List<object>(); var failed = 0;
        void Test(string name, Action test) { try { test(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e.Message); } }
        FeatureSolidExpectation Evaluate(ConstructionProgram program)
        {
            ContractValidation.Program(program);
            var sketches = program.Operations.Where(o => o.Sketch is not null).ToDictionary(o => o.SemanticId);
            return FeatureConstructionPreflight.Evaluate(program.Operations.Where(o => o.Profile is not null).Select(o =>
                new PreparedFeatureProfile(o, sketches[o.Profile!.SemanticId[..^8]].Placement!, PlanarSketchPreflight.Prepare(sketches[o.Profile!.SemanticId[..^8]].Sketch!))).ToArray());
        }
        void Near(double a, double b) => Program.Check(Math.Abs(a - b) < 1e-6, "Analytical expectation differs.");
        void Bad(ConstructionProgram p) { try { Evaluate(p); } catch (ContractException) { return; } throw new Exception("Unsafe profile accepted."); }
        Test("full revolve exact torus volume and radial/axial bounds", () => { var e = Evaluate(Cases.Torus); Near(e.VolumeMm3, 2 * Math.PI * Math.PI * 15 * 9); Near(e.RevolveRadiusMm!.Value, 18); Near(e.RevolveAxialSpanMm!.Value, 6); });
        Test("finite angle quarter cylinder exact volume", () => { var e = Evaluate(Cases.Quarter); Near(e.VolumeMm3, Math.PI * 100 * 20 / 4); Near(e.MaximumLocalMm.Z, 10); Near(e.MinimumLocalMm.Y, 0); });
        Test("generic initial prism volume", () => Near(Evaluate(Cases.Root).VolumeMm3, 48000));
        Test("merged additive material exact volume", () => Near(Evaluate(Cases.Boss).VolumeMm3, 48000 + Math.PI * 25 * 6));
        Test("slot blind cut plus independent through cut", () => Near(Evaluate(Cases.Composition).VolumeMm3, 48000 + Math.PI * 25 * 6 - (32 + Math.PI * 4) * 4 - Math.PI * 9 * 10));
        Test("native declarations do not imply editable polygon constraints", () => Program.Check(Cases.Root.Operations[0].Sketch!.Constraints.Count == 0, "Unexpected symbolic solver requirement."));
        Test("profile crossing revolve axis refuses", () => Bad(Cases.Torus with { Operations = new[] { Cases.Sketch("radial", Cases.Circle(0, 2, 3)), Cases.Torus.Operations[1] } }));
        Test("wrong exact revolve axis refuses", () => Bad(Cases.Torus with { Operations = new[] { Cases.Torus.Operations[0], Cases.Torus.Operations[1] with { Axis = new("world.axis_y", CadHarness.Ir.SemanticType.ReferenceAxis) } } }));
        Test("untracked host refuses", () => Bad(Cases.Boss with { Operations = Cases.Boss.Operations.Select(o => o.SemanticId == "boss" ? o with { HostBody = new("external.body", CadHarness.Ir.SemanticType.BodyRef) } : o).ToArray() }));
        Test("detached boss refuses before native work", () => Bad(Cases.Boss with { Operations = Cases.Boss.Operations.Select(o => o.SemanticId == "boss_sketch" ? o with { Placement = o.Placement! with { Frame = Cases.XY with { OriginMm = new(0, 0, 12) } } } : o).ToArray() }));
        Test("partially overlapping footprints refuse", () => Bad(Cases.Composition with { Operations = Cases.Composition.Operations.Select(o => o.SemanticId == "through_sketch" ? o with { Sketch = Cases.Circle(20, 0, 4) } : o).ToArray() }));
        Test("cut outside root material refuses", () => Bad(Cases.Composition with { Operations = Cases.Composition.Operations.Select(o => o.SemanticId == "through_sketch" ? o with { Sketch = Cases.Circle(40, 0, 3) } : o).ToArray() }));
        Test("whole body deletion refuses", () => Bad(Cases.Program(Cases.Root.Operations.Concat(new[] { Cases.Top, Cases.Sketch("cut_sketch", Cases.Rectangle(-40, -30, 40, 30), 10), Cases.Extrude("cut", "cut_sketch", ConstructionKind.CreateExtrudedCut, null, true, "base") }).ToArray())));
        Test("cut removing an entire support-bearing boss footprint refuses", () => Bad(Cases.Program(Cases.Boss.Operations.Concat(new[] {
            Cases.Sketch("cut_sketch", Cases.Circle(18, 0, 5), 10), Cases.Extrude("cut", "cut_sketch", ConstructionKind.CreateExtrudedCut, 6, false, "boss") }).ToArray())));
        Test("additive contact over an already removed region refuses", () => Bad(Cases.Program(Cases.Root.Operations.Concat(new[] {
            Cases.Top, Cases.Sketch("hole_sketch", Cases.Circle(18, 0, 5), 10), Cases.Extrude("hole", "hole_sketch", ConstructionKind.CreateExtrudedCut, null, true, "base"),
            Cases.Sketch("boss_sketch", Cases.Circle(18, 0, 4), 10), Cases.Extrude("boss", "boss_sketch", ConstructionKind.CreateAdditiveBoss, 6, false, "hole") }).ToArray())));
        Test("mixed sweep normals refuse", () => Bad(Cases.Boss with { Operations = Cases.Boss.Operations.Select(o => o.SemanticId == "boss_sketch" ? o with { Placement = o.Placement! with { Frame = new(new(0, 0, 10), new(1, 0, 0), new(0, 0, 1), new(0, -1, 0)) } } : o).ToArray() }));
        var snapshot = new GenericConstructionState(new(Guid.NewGuid(), Guid.NewGuid(), "Default", "owned"), 1, Cases.Root,
            Array.Empty<GenericNativeFeature>(), Array.Empty<GenericNativeSketch>(), Array.Empty<GenericNativeParameter>(), Array.Empty<DependencyEdge>(),
            new(new("AQ=="), 48000, new[] { 0.0, 1e-37, 0.005, 48e-6 }, new[] { new Vector3(40, 30, 10) }));
        Test("kernel zero noise uses unchanged checkpoint SI tolerance", () => Program.Check(GenericConstructionStatePolicy.MatchesNativeSnapshot(snapshot,
            snapshot with { Solid = snapshot.Solid! with { MassProperties = new[] { 0.0, 6e-37, 0.005, 48e-6 } } }), "Kernel roundoff rejected."));
        Test("actual volume/support drift remains refused", () => Program.Check(!GenericConstructionStatePolicy.MatchesNativeSnapshot(snapshot,
            snapshot with { Solid = snapshot.Solid! with { VolumeMm3 = 48001 } }) && !GenericConstructionStatePolicy.MatchesNativeSnapshot(snapshot,
            snapshot with { Solid = snapshot.Solid! with { SupportPointsMm = new[] { new Vector3(40, 30, 11) } } }), "Material drift accepted."));
        Test("native identity remains exact", () => Program.Check(!GenericConstructionStatePolicy.MatchesNativeSnapshot(snapshot,
            snapshot with { Solid = snapshot.Solid! with { Reference = new("Ag==") } }), "Different body reference accepted."));
        Test("only three actually qualified scalar pairs are open", () =>
        {
            var actual = Enum.GetValues<ConstructionKind>().SelectMany(k => Enum.GetValues<ParameterKey>().Where(p => GenericFeatureCapabilities.IsQualified(k, p)).Select(p => (k, p))).ToArray();
            Program.Check(actual.Length == 3 && actual.Contains((ConstructionKind.CreateRevolvedBoss, ParameterKey.RevolveRadius)) &&
                actual.Contains((ConstructionKind.CreateAdditiveBoss, ParameterKey.AdditiveBossDepth)) && actual.Contains((ConstructionKind.CreateExtrudedCut, ParameterKey.CutDepth)), "Qualification widened.");
        });
        var path = Path.Combine(root, "artifacts/milestone16/pure", DateTime.UtcNow.ToString("yyyyMMddTHHmmssffff"), "result.json");
        Program.Write(path, new { passed = failed == 0, results, nativeOpenCycles = 0, newParts = 0 });
        return failed == 0 ? 0 : 1;
    }
}
