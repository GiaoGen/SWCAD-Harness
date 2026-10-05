using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.CircularGeometry.Tests;

internal static class PureTests
{
    internal static int Run(string root)
    {
        var program = TestData.Fixture(root); var runtime = SolidWorksPlanningRuntime.ForConstruction();
        var tests = new List<(string Name, Action Run)>(); void Add(string name, Action run) => tests.Add((name, run));
        void Valid(CadProgram p) { var check = runtime.Preflight(p); TestData.Check(check.IsValid, check.Issues.FirstOrDefault()?.Message ?? "Rejected valid program."); }
        void Invalid(CadProgram p) => TestData.Check(!runtime.Preflight(p).IsValid, "Accepted invalid geometry/capability.");
        Add("G4 composes through generic handlers", () => Valid(program));
        Add("native registry contains circular pattern", () => TestData.Check(new FeatureBackendRegistry().SupportedKinds.Contains(OperationKind.CreateCircularPattern), "Missing handler."));
        Add("catalog advertises both implemented profiles", () => TestData.Check(runtime.Capabilities.Profiles.SequenceEqual(new[] { ProfileKind.CenteredRectangle, ProfileKind.Circle }), "Profile projection differs."));
        Add("circle outputs exclude fictitious linear topology", () => TestData.Check(ProfileOutputs.For(ProfileKind.Circle).All(o => o.Type != SemanticType.LinearEdge) && ProfileOutputs.For(ProfileKind.Circle).Any(o => o.Suffix == ".rotational_reference" && o.Type == SemanticType.CylindricalFace), "Wrong profile outputs."));
        Add("rectangle excludes cylindrical axis output", () => TestData.Check(ProfileOutputs.For(ProfileKind.CenteredRectangle).All(o => o.Suffix != ".rotational_reference"), "Phantom rectangle cylinder."));
        Add("catalog exposes structured per-profile output map", () => TestData.Check(runtime.Capabilities.ProfileOutputs.Count == 2 && runtime.Capabilities.ToPromptJson().Contains("outputsByProfile"), "No profile-specific projection."));
        Add("circular axis type is actual native cylinder only", () => TestData.Check(runtime.Capabilities.Registry.Get(OperationKind.CreateCircularPattern).Inputs.Single(i => i.Name == "axis").AcceptedTypes.SequenceEqual(new[] { SemanticType.CylindricalFace }), "Axis types overpromise."));
        Add("logical axis cannot drive native circular pattern", () => Invalid(TestData.Input(program, 3, "axis", new("disk.axis_x", SemanticType.ReferenceAxis))));
        Add("hole wall cannot replace the host rotation axis", () => Invalid(TestData.Input(program, 3, "axis", new("center_bore.wall_face", SemanticType.CylindricalFace))));
        Add("rectangle cannot claim circle outputs", () => Invalid(TestData.Change(program, 0, "profile", new ProfileParameter(new CenteredRectangleProfile(100, 100)))));
        Add("circle cannot claim rectangle direction", () =>
        {
            var linear = program.Operations[3] with { Kind = OperationKind.CreateLinearPattern,
                Inputs = new[] { program.Operations[3].Input("seed")!, new OperationInput("direction", new[] { new SemanticReference("disk.direction_x", SemanticType.LinearEdge) }) },
                Parameters = new Dictionary<string, OperationParameter> { ["count"] = new CountParameter(2), ["spacingMm"] = new LengthParameter(10) } };
            Invalid(program with { Operations = program.Operations.Take(3).Append(linear).ToArray() });
        });
        Add("full circle default span excludes duplicate endpoint", () =>
        {
            var positions = PatternGeometry.Positions(program, program.Operations[3]); TestData.Check(positions.Count == 6, "Wrong count.");
            TestData.Near(positions[1].XMm, 17.5); TestData.Near(positions[1].YMm, 35 * Math.Sqrt(3) / 2);
            TestData.Near(positions[5].YMm, -35 * Math.Sqrt(3) / 2);
        });
        Add("explicit full span matches omitted default", () => TestData.Check(PatternGeometry.Positions(program, program.Operations[3]).SequenceEqual(
            PatternGeometry.Positions(program, TestData.Change(program, 3, "angleDeg", new AngleParameter(360)).Operations[3])), "Default span differs."));
        Add("partial span includes final endpoint", () =>
        {
            var partial = TestData.Change(TestData.Change(program, 3, "count", new CountParameter(4)), 3, "angleDeg", new AngleParameter(210)); Valid(partial);
            var positions = PatternGeometry.Positions(partial, partial.Operations[3]); TestData.Near(positions[3].XMm, 35 * Math.Cos(210 * Math.PI / 180)); TestData.Near(positions[3].YMm, -17.5);
        });
        Add("rotated offset seed uses generic coordinates", () =>
        {
            var p = TestData.Change(program, 2, "placement", new PlacementParameter(new(0, 35))); Valid(p);
            var positions = PatternGeometry.Positions(p, p.Operations[3]); TestData.Near(positions[1].XMm, -35 * Math.Sqrt(3) / 2); TestData.Near(positions[1].YMm, 17.5);
        });
        Add("unseen dimensions and count have no preset dependency", () =>
        {
            var p = TestData.Change(TestData.Change(TestData.Change(program, 0, "profile", new ProfileParameter(new CircleProfile(140))), 0, "depthMm", new LengthParameter(9)), 3, "count", new CountParameter(9)); Valid(p);
        });
        foreach (var (field, parameter) in new (string, OperationParameter)[] { ("count", new CountParameter(1)), ("count", new CountParameter(1025)), ("angleDeg", new AngleParameter(0)), ("angleDeg", new AngleParameter(361)), ("angleDeg", new AngleParameter(double.NaN)) })
        { var f = field; var v = parameter; Add("reject invalid circular " + f + " " + v, () => Invalid(TestData.Change(program, 3, f, v))); }
        Add("circle host checks radial boundary", () => Invalid(TestData.Change(program, 2, "placement", new PlacementParameter(new(40, 30)))));
        Add("hole cannot touch outer boundary", () => Invalid(TestData.Change(program, 2, "placement", new PlacementParameter(new(46, 0)))));
        Add("circular instances cannot overlap", () => Invalid(TestData.Change(program, 3, "count", new CountParameter(30))));
        Add("partial arc collision rejected", () => Invalid(TestData.Change(program, 3, "angleDeg", new AngleParameter(10))));
        Add("bolt holes cannot intersect center bore", () => Invalid(TestData.Change(program, 2, "placement", new PlacementParameter(new(12, 0)))));
        Add("circular centering relation remains unimplemented", () => Invalid(program with { Relations = program.Relations.Append(new(RelationKind.CenteredAbout, "bolt_ring", "disk.local_frame")).ToArray() }));
        Add("read-only circular scalar expectations include defaults", () =>
        {
            var registry = ParameterMutationRegistry.Default; TestData.Near(registry.Expected(program.Operations[0], EditableParameter.ProfileDiameter), 100);
            TestData.Near(registry.Expected(program.Operations[3], EditableParameter.PatternCount), 6); TestData.Near(registry.Expected(program.Operations[3], EditableParameter.PatternAngle), 360);
        });
        Add("M9A hole edit is executable with circular dependencies", () =>
        {
            var state = TestData.Sample(program); var edit = SolidWorksPlanningRuntime.ForEditSnapshot(program, state);
            TestData.Check(edit.Capabilities.ParameterEdits.Single().Parameter == EditableParameter.HoleDiameter && edit.Preflight(new("0.2", new[] { TestData.Edit(9) }, Array.Empty<DesignRelation>())).IsValid, "Hole mutation lost.");
            TestData.Check(edit.Capabilities.ParameterEdits.All(e => e.OwnerKind != OperationKind.CreateCircularPattern && e.Parameter != EditableParameter.ExtrusionDepth), "Unverified circle mutations advertised.");
        });
        Add("DirtySet carries circular dependency", () =>
        {
            var state = TestData.Sample(program); var dirty = DirtySet.Expand(state, new(new[] { "bolt_seed" }, new[] { "bolt_seed.hole_diameter" }, Array.Empty<string>()));
            var scope = ValidationScope.Select(state, dirty, FullValidationReason.None);
            TestData.Check(dirty.Features.Contains("bolt_ring") && !scope.FullModel && scope.Parameters.SequenceEqual(new[] { "bolt_seed.hole_diameter" }), "Missing incremental dependency.");
        });
        Add("unhealthy rotation input hides dependent mutation", () =>
        {
            var state = TestData.Sample(program);
            var stale = state with { Entities = state.Entities.Select(e => e.SemanticId == "disk.rotational_reference" ? e with { ReferenceHealth = ReferenceHealth.Stale } : e).ToArray() };
            TestData.Check(SolidWorksPlanningRuntime.ForEditSnapshot(program, stale).Capabilities.ParameterEdits.Count == 0, "Stale axis exposed a dependent mutation.");
        });
        Add("strict planner accepts generic G4 composition", () =>
        {
            var envelope = JsonSerializer.Serialize(new { outcome = "planned", program = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(program)), reason = "" });
            var source = new FixturePlanSource(new Dictionary<string, string> { ["disk"] = envelope });
            var result = new CadPlanner(runtime, source).PlanAsync("disk").GetAwaiter().GetResult(); TestData.Check(result.Succeeded && result.ModelCalls == 0, "Planner rejected circle composition.");
        });
        var results = new List<object>(); var failed = 0;
        foreach (var test in tests) try { test.Run(); results.Add(new { test.Name, Passed = true }); Console.WriteLine("PASS " + test.Name); }
            catch (Exception e) { failed++; results.Add(new { test.Name, Passed = false, Error = e.Message }); Console.WriteLine("FAIL " + test.Name + ": " + e.Message); }
        var output = Path.Combine(root, "artifacts/milestone9b"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "pure-result.json"), JsonSerializer.Serialize(new { Total = tests.Count, Passed = tests.Count - failed, Failed = failed, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "construction-capabilities.json"), runtime.Capabilities.ToPromptJson());
        Console.WriteLine($"M9B PURE: {tests.Count - failed}/{tests.Count} passed; zero Parts."); return failed == 0 ? 0 : 1;
    }
}
