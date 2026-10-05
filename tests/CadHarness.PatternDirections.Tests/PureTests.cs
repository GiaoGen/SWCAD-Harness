using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.PatternDirections.Tests;
internal static class PureTests
{
    internal static CadProgram Stable(CadProgram program) => program with { Operations = program.Operations.Select(o =>
        o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern ? o with { Inputs = o.Inputs.Select(input =>
            input.Name == "seed" ? input : input with { References = input.References.Select(r => r with { Type = SemanticType.ReferenceAxis }).ToArray() }).ToArray() } : o).ToArray() };
    internal static int Run(string root)
    {
        var results = new List<object>(); var failed = 0;
        void Test(string name, Action run) { try { run(); results.Add(new { Name = name, Passed = true }); } catch (Exception e) { failed++; results.Add(new { Name = name, Passed = false, Message = e.Message }); } }
        var runtime = SolidWorksPlanningRuntime.ForConstruction(); var linear = Stable(CaseData.All().Single(c => c.Name == "G1").Program!);
        var rectangular = Stable(CaseData.All().Single(c => c.Name == "G5").Program!);
        Test("profile direction outputs are ReferenceAxis", () => Program.Check(ProfileOutputs.For(ProfileKind.CenteredRectangle).Where(o => o.Suffix.StartsWith(".direction_", StringComparison.Ordinal)).All(o => o.Type == SemanticType.ReferenceAxis), "BREP direction remains."));
        Test("circle does not claim new linear directions", () => Program.Check(!ProfileOutputs.For(ProfileKind.Circle).Any(o => o.Suffix.StartsWith(".direction_", StringComparison.Ordinal)), "Circle capability expanded."));
        Test("runtime only accepts datum pattern directions", () => Program.Check(runtime.Capabilities.Registry.Contracts.SelectMany(c => c.Inputs).Where(i => i.Role == SemanticRole.PatternDirection).All(i => i.AcceptedTypes.SequenceEqual(new[] { SemanticType.ReferenceAxis })), "PatternDirection type diverged."));
        Test("linear plus fillet strict roundtrip", () => Program.Check(new CadProgramJson(runtime.Capabilities.Registry).Parse(new CadProgramJson().Serialize(linear)).IsValid && runtime.Preflight(linear).IsValid, "Linear plan failed."));
        Test("rectangular plus chamfer strict preflight", () => Program.Check(runtime.Preflight(rectangular).IsValid, "Rectangular plan failed."));
        Test("explicit old BREP directions reject", () => Program.Check(!runtime.Preflight(CaseData.All().Single(c => c.Name == "G1").Program!).IsValid, "Old direction accepted as datum."));
        Test("omitted linear direction uses stable local X", () =>
        {
            var implicitDirection = linear with { Operations = linear.Operations.Select(o => o.Kind == OperationKind.CreateLinearPattern ? o with { Inputs = o.Inputs.Where(i => i.Name != "direction").ToArray() } : o).ToArray() };
            Program.Check(runtime.Preflight(implicitDirection).IsValid, "Implicit direction failed.");
        });
        Test("shorthand direction inherits ReferenceAxis", () =>
        {
            var serialized = new CadProgramJson().Serialize(linear);
            var parsed = new CadProgramJson(runtime.Capabilities.Registry).Parse(serialized); Program.Check(parsed.IsValid, "Datum parser failed.");
            Program.Check(OperationRegistry.Default.Get(OperationKind.CreateLinearPattern).Inputs.Single(i => i.Name == "direction").AcceptedTypes[0] == SemanticType.ReferenceAxis, "Shorthand type wrong.");
        });
        Test("relations preserve centered counts and spacing", () =>
        {
            var solved = new DesignRelationEngine().Solve(rectangular).Program;
            var p = solved.Operations.Single(o => o.SemanticId == "seed").Parameter<PlacementParameter>("placement").Value;
            Program.Check(p == new Point2D(-25, -20), "Centered layout changed.");
        });
        Test("planner schema projects datum references", () => Program.Check(PlannerResponseSchema.Create(runtime.Capabilities).Contains("reference_axis", StringComparison.Ordinal), "Schema lacks datum input."));
        Program.VerifyEvidence(root);
        Program.Write(Path.Combine(root, "artifacts/milestone9e/pure-result.json"), new { Total = results.Count, Passed = results.Count - failed, Failed = failed, Results = results });
        Console.WriteLine($"M9E pure: {results.Count - failed}/{results.Count} passed."); return failed == 0 ? 0 : 1;
    }
}
