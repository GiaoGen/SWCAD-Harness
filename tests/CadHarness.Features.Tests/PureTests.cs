using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir;
using CadHarness.SolidWorks;

namespace CadHarness.Features.Tests;

internal static class PureTests
{
    internal static CadProgram Fixture(string root, string name)
    {
        var parsed = new CadProgramJson().Parse(File.ReadAllText(Path.Combine(root, "tests", "CadHarness.Features.Tests", "Fixtures", name + ".json")));
        if (!parsed.IsValid) throw new InvalidOperationException(parsed.Issues[0].Code + ": " + parsed.Issues[0].Message);
        return parsed.Program!;
    }
    internal static int Run(string root)
    {
        var backend = new CompositionBackend();
        var g1 = Fixture(root, "g1"); var g2 = Fixture(root, "g2"); var aux = Fixture(root, "aux");
        var cases = new List<(string Name, Action Run)>
        {
            ("G1 generic IR passes whole-program backend preflight", () => Valid(backend.Preflight(g1))),
            ("G2 generic IR passes whole-program backend preflight", () => Valid(backend.Preflight(g2))),
            ("blind-hole/chamfer composition passes preflight", () => Valid(backend.Preflight(aux))),
            ("new parameters need no production preset", () => Valid(backend.Preflight(Change(g2, 2, g2.Operations[2] with
                { Parameters = Parameters(g2.Operations[2], ("countY", new CountParameter(3)), ("spacingYMm", new LengthParameter(17))) })))),
            ("relations reject before native mutation", () => Reject(backend, g1 with { Relations = new[] { new DesignRelation(RelationKind.CenteredAbout, "mounting_holes", "plate.local_frame") } }, FailureCodes.OperationUnsupported)),
            ("unknown external host rejects before native mutation", () => Reject(backend, Change(g1, 1, g1.Operations[1] with
                { Inputs = new[] { new OperationInput("host", new[] { new SemanticReference("other.top_face", SemanticType.PlanarFace) }) } }), "BINDING_UNRESOLVED")),
            ("missing repeated-direction spacing rejects before mutation", () =>
            {
                var p = Parameters(g2.Operations[2]); p.Remove("spacingYMm");
                Reject(backend, Change(g2, 2, g2.Operations[2] with { Parameters = p }), FailureCodes.PreconditionFailed);
            }),
            ("unrepeated rectangular direction needs no spacing", () =>
            {
                var p = Parameters(g2.Operations[2], ("countX", new CountParameter(1))); p.Remove("spacingXMm");
                Valid(backend.Preflight(Change(g2, 2, g2.Operations[2] with { Parameters = p })));
            }),
            ("oversized pattern rejects before mutation", () => Reject(backend, Change(g2, 2, g2.Operations[2] with
                { Parameters = Parameters(g2.Operations[2], ("countX", new CountParameter(33)), ("countY", new CountParameter(33))) }), FailureCodes.PreconditionFailed)),
            ("unsupported reference plane host rejects before mutation", () => Reject(backend, Change(g1, 1, g1.Operations[1] with
                { Inputs = new[] { new OperationInput("host", new[] { new SemanticReference("plane", SemanticType.ReferencePlane) }) } }), FailureCodes.PreconditionFailed)),
            ("unsupported reference axis direction rejects before mutation", () => Reject(backend, Change(g1, 2, g1.Operations[2] with
                { Inputs = new[] { g1.Operations[2].Input("seed")!, new OperationInput("direction", new[] { new SemanticReference("axis", SemanticType.ReferenceAxis) }) } }), FailureCodes.PreconditionFailed)),
            ("nonfinite edge-treatment radius rejects before mutation", () => Reject(backend, Change(g1, 3, g1.Operations[3] with
                { Parameters = Parameters(g1.Operations[3], ("radiusMm", new LengthParameter(double.NaN))) }), FailureCodes.SchemaInvalid)),
            ("empty edge set rejects before mutation", () => Reject(backend, Change(g1, 3, g1.Operations[3] with
                { Inputs = new[] { new OperationInput("edges", Array.Empty<SemanticReference>()) } }), FailureCodes.SchemaInvalid)),
            ("parameter edits remain outside M4", () =>
            {
                var edit = new OperationNode("edit", OperationKind.EditParameter, null,
                    new[] { new OperationInput("target", new[] { new SemanticReference("plate", SemanticType.FeatureRef) }) },
                    new Dictionary<string, OperationParameter> { ["parameter"] = new ParameterNameParameter(EditableParameter.ExtrusionDepth), ["value"] = new EditValueParameter(new LengthParameter(11)) });
                Reject(backend, g1 with { Operations = g1.Operations.Concat(new[] { edit }).ToArray() }, FailureCodes.OperationUnsupported);
            }),
            ("circular patterns explicitly report unsupported in M4", () =>
            {
                var node = new OperationNode("circular", OperationKind.CreateCircularPattern, "bolt_holes",
                    new[] { new OperationInput("seed", new[] { new SemanticReference("mounting_seed", SemanticType.FeatureRef) }),
                        new OperationInput("axis", new[] { new SemanticReference("mounting_seed.wall_face", SemanticType.CylindricalFace) }) },
                    new Dictionary<string, OperationParameter> { ["count"] = new CountParameter(6) });
                Reject(backend, g1 with { Operations = g1.Operations.Take(2).Concat(new[] { node }).ToArray() }, FailureCodes.OperationUnsupported);
            }),
            ("wrong handler kind rejects without COM context", () =>
            {
                var result = new CreateBlindHoleHandler().Execute(null!, g1.Operations[1]);
                Assert(!result.Succeeded && !result.MutationStarted && !result.StateCommitted && !result.RollbackAttempted, "Wrong kind did not reject before accessing COM.");
            }),
            ("semantic topology outputs survive strict IR round trip", () =>
            {
                var parser = new CadProgramJson(); var loaded = parser.Parse(parser.Serialize(g1));
                Assert(loaded.IsValid && backend.Preflight(loaded.Program!).IsValid, "Topology references did not round trip.");
            })
        };
        var passed = 0;
        foreach (var test in cases)
        {
            try { test.Run(); passed++; Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"M4 pure tests: {passed}/{cases.Count}; native Parts created=0.");
        return passed == cases.Count ? 0 : 1;
    }
    private static CadProgram Change(CadProgram program, int index, OperationNode node)
    { var operations = program.Operations.ToArray(); operations[index] = node; return program with { Operations = operations }; }
    private static Dictionary<string, OperationParameter> Parameters(OperationNode node, params (string Name, OperationParameter Value)[] changes)
    { var p = new Dictionary<string, OperationParameter>(node.Parameters); foreach (var change in changes) p[change.Name] = change.Value; return p; }
    private static void Reject(CompositionBackend backend, CadProgram program, string code)
    {
        var check = backend.Preflight(program);
        Assert(!check.IsValid && check.FailureCode == code, "Unexpected preflight result: " + check);
        var execution = backend.Execute(null!, program);
        Assert(!execution.Succeeded && execution.FailureCode == code && !execution.MutationStarted && !execution.RollbackAttempted && !execution.StateCommitted,
            "Rejected program accessed native context or reported mutation.");
    }
    private static void Valid(PreflightResult result) => Assert(result.IsValid, result.FailureCode + ": " + result.Message);
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
