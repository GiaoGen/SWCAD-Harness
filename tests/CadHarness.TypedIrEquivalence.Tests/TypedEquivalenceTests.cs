using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal sealed record UnknownParameter(ParameterKind Declared) : OperationParameter(Declared);
internal sealed record UnknownProfile(ProfileKind Declared) : SketchProfile(Declared);
internal static class TypedEquivalenceTests
{
    private static OperationNode Node(OperationParameter value) => new("operation", OperationKind.CreateThroughHole, "hole",
        new[] { new OperationInput("host", new[] { new SemanticReference("plate.top_face", SemanticType.PlanarFace) }) },
        new Dictionary<string, OperationParameter> { ["value"] = value });
    private static void Pair(OperationParameter a, OperationParameter b, bool equal) =>
        Program.Check(OperationSemanticComparer.EqualsDefinition(Node(a), Node(b)) == equal &&
            OperationSemanticComparer.EqualsDefinition(Node(b), Node(a)) == equal, "Typed parameter comparison differs.");
    internal static IEnumerable<(string, Action)> Cases(string root)
    {
        yield return ("signed-zero placement uses exact numeric equality", () => Pair(new PlacementParameter(new(0, 0)), new PlacementParameter(new(-0.0, -0.0)), true));
        yield return ("nested profile and EditValue signed zeros compare numerically", () =>
        {
            Pair(new ProfileParameter(new CenteredRectangleProfile(0, 2)), new ProfileParameter(new CenteredRectangleProfile(-0.0, 2)), true);
            Pair(new ProfileParameter(new CircleProfile(0)), new ProfileParameter(new CircleProfile(-0.0)), true);
            Pair(new EditValueParameter(new EditValueParameter(new PlacementParameter(new(0, 0)))), new EditValueParameter(new EditValueParameter(new PlacementParameter(new(-0.0, -0.0)))), true);
        });
        yield return ("real placement movement and sub-tolerance movement remain unequal", () =>
        {
            Pair(new PlacementParameter(new(0, 0)), new PlacementParameter(new(0, 1)), false);
            Pair(new LengthParameter(1), new LengthParameter(1.0000000001), false);
            Pair(new PlacementParameter(new(1, 0)), new PlacementParameter(new(1.0000000001, 0)), false);
        });
        yield return ("diameter and extrusion depth changes remain unequal", () => { Pair(new LengthParameter(6), new LengthParameter(8), false); Pair(new LengthParameter(8), new LengthParameter(10), false); });
        yield return ("all profile dimensions and profile shapes compare strictly", () =>
        {
            Pair(new ProfileParameter(new CenteredRectangleProfile(100, 60)), new ProfileParameter(new CenteredRectangleProfile(101, 60)), false);
            Pair(new ProfileParameter(new CenteredRectangleProfile(100, 60)), new ProfileParameter(new CenteredRectangleProfile(100, 61)), false);
            Pair(new ProfileParameter(new CircleProfile(100)), new ProfileParameter(new CircleProfile(101)), false);
            Pair(new ProfileParameter(new CircleProfile(100)), new ProfileParameter(new CenteredRectangleProfile(100, 100)), false);
        });
        yield return ("count spacing angle and parameter names compare strictly", () =>
        {
            Pair(new CountParameter(2), new CountParameter(3), false); Pair(new CountParameter(2), new CountParameter(2), true);
            Pair(new LengthParameter(29), new LengthParameter(30), false); Pair(new AngleParameter(0), new AngleParameter(-0.0), true);
            Pair(new AngleParameter(90), new AngleParameter(91), false);
            Pair(new ParameterNameParameter(EditableParameter.HoleDiameter), new ParameterNameParameter(EditableParameter.HoleDiameter), true);
            Pair(new ParameterNameParameter(EditableParameter.HoleDiameter), new ParameterNameParameter(EditableParameter.ExtrusionDepth), false);
            Pair(new LengthParameter(1), new AngleParameter(1), false);
        });
        yield return ("operation ID kind and semantic ID are part of definition", () =>
        {
            var n = Node(new LengthParameter(7));
            foreach (var changed in new[] { n with { Id = "new_id" }, n with { SemanticId = "new_semantic" }, n with { Kind = OperationKind.CreateBlindHole } })
                Program.Check(!OperationSemanticComparer.EqualsDefinition(n, changed), "Operation identity change ignored.");
        });
        yield return ("input names references semantic types and reference order are protected", () =>
        {
            var n = Node(new LengthParameter(7));
            foreach (var input in new[] {
                new OperationInput("seed", n.Inputs[0].References),
                new OperationInput("host", new[] { new SemanticReference("another.top_face", SemanticType.PlanarFace) }),
                new OperationInput("host", new[] { new SemanticReference("plate.top_face", SemanticType.ReferencePlane) }),
                new OperationInput("host", Array.Empty<SemanticReference>()) })
                Program.Check(!OperationSemanticComparer.EqualsDefinition(n, n with { Inputs = new[] { input } }), "Input change ignored.");
            var refs = new[] { new SemanticReference("a", SemanticType.LinearEdge), new SemanticReference("b", SemanticType.LinearEdge) };
            Program.Check(!OperationSemanticComparer.EqualsDefinition(n with { Inputs = new[] { new OperationInput("edges", refs) } },
                n with { Inputs = new[] { new OperationInput("edges", refs.Reverse().ToArray()) } }), "Reference order ignored.");
        });
        yield return ("parameter key set and ordinal spelling are protected", () =>
        {
            var n = Node(new LengthParameter(7));
            foreach (var changed in new[] { new Dictionary<string, OperationParameter>(),
                new Dictionary<string, OperationParameter>(n.Parameters) { ["extra"] = new CountParameter(1) },
                new Dictionary<string, OperationParameter>(StringComparer.OrdinalIgnoreCase) { ["VALUE"] = new LengthParameter(7) } })
                Program.Check(!OperationSemanticComparer.EqualsDefinition(n, n with { Parameters = changed }), "Parameter keys changed without rejection.");
        });
        yield return ("input and parameter map ordering does not change definition", () =>
        {
            var n = Node(new LengthParameter(7)) with { Inputs = new[] { new OperationInput("a", Array.Empty<SemanticReference>()), new OperationInput("b", Array.Empty<SemanticReference>()) },
                Parameters = new Dictionary<string, OperationParameter> { ["a"] = new LengthParameter(1), ["b"] = new CountParameter(2) } };
            Program.Check(OperationSemanticComparer.EqualsDefinition(n, n with { Inputs = n.Inputs.Reverse().ToArray(), Parameters = n.Parameters.Reverse().ToDictionary(p => p.Key, p => p.Value) }), "Map ordering treated as meaning.");
        });
        yield return ("EditValue recurses and detects nested changes", () =>
        {
            Pair(new EditValueParameter(new EditValueParameter(new LengthParameter(0))), new EditValueParameter(new EditValueParameter(new LengthParameter(-0.0))), true);
            Pair(new EditValueParameter(new ProfileParameter(new CircleProfile(7))), new EditValueParameter(new ProfileParameter(new CircleProfile(9))), false);
        });
        yield return ("unknown parameter and profile shapes fail closed even against themselves", () =>
        {
            foreach (var p in new OperationParameter[] { new UnknownParameter(ParameterKind.Length), new UnknownParameter((ParameterKind)999),
                new ProfileParameter(new UnknownProfile(ProfileKind.Circle)), new ProfileParameter(new UnknownProfile((ProfileKind)999)), new ParameterNameParameter((EditableParameter)999) }) Pair(p, p, false);
        });
        yield return ("nonfinite fields fail closed with no reference equality shortcut", () =>
        {
            foreach (var bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                foreach (var p in new OperationParameter[] { new LengthParameter(bad), new AngleParameter(bad), new PlacementParameter(new(bad, 0)),
                    new PlacementParameter(new(0, bad)), new ProfileParameter(new CenteredRectangleProfile(bad, 1)), new ProfileParameter(new CenteredRectangleProfile(1, bad)),
                    new ProfileParameter(new CircleProfile(bad)), new EditValueParameter(new LengthParameter(bad)) }) Pair(p, p, false);
        });
        yield return ("null and unknown operation or input types fail closed", () =>
        {
            var n = Node(new LengthParameter(1));
            Program.Check(!OperationSemanticComparer.EqualsDefinition(null, null) && !OperationSemanticComparer.EqualsParameter(null, null), "Null accepted.");
            foreach (var bad in new[] { n with { Kind = (OperationKind)999 }, n with { Inputs = new[] { new OperationInput("host", new[] { new SemanticReference("a", (SemanticType)999) }) } },
                n with { Inputs = n.Inputs.Concat(n.Inputs).ToArray() } }) Program.Check(!OperationSemanticComparer.EqualsDefinition(bad, bad), "Unknown/duplicate input accepted.");
        });
        yield return ("actual M10C signed-zero fixture passes construction preflight without wire normalization", () =>
        {
            var (prior, addition) = Fixture(root); var merged = new CadProgram("0.2", prior.Operations.Concat(addition.Operations).ToArray(), prior.Relations.Concat(addition.Relations).ToArray());
            var solved = new DesignRelationEngine().Solve(merged).Program;
            var before = prior.Operations[1]; var after = solved.Operations.Single(o => o.SemanticId == before.SemanticId);
            var codec = new CadProgramJson();
            string Wire(OperationNode n) => codec.Serialize(new("0.2", new[] { n }, Array.Empty<DesignRelation>()));
            Program.Check(Wire(before) != Wire(after) && OperationSemanticComparer.EqualsDefinition(before, after), "Signed-zero fixture not reproduced.");
            Program.Check(BitConverter.DoubleToInt64Bits(before.Parameter<PlacementParameter>("placement").Value.YMm) == 0 &&
                BitConverter.DoubleToInt64Bits(after.Parameter<PlacementParameter>("placement").Value.YMm) == long.MinValue, "Serializer zero evidence changed.");
            var result = new RelationBackend().Preflight(addition, prior);
            Program.Check(result.IsValid, result.Message);
            Program.Write(Path.Combine(Program.Output(root), "signed-zero-regression.json"), new { BeforeWire = Wire(before), AfterWire = Wire(after), TypedEquivalent = true, PreflightPassed = result.IsValid });
        });
        yield return ("construction append still rejects actual committed placement movement", () =>
        {
            var (prior, addition) = Fixture(root); var hole = prior.Operations[1];
            var moved = hole with { Parameters = new Dictionary<string, OperationParameter>(hole.Parameters) { ["placement"] = new PlacementParameter(new(-29, 1)) } };
            var changed = prior with { Operations = new[] { prior.Operations[0], moved } };
            var result = new RelationBackend().Preflight(addition, changed);
            Program.Check(!result.IsValid && result.FailureCode == FailureCodes.OperationUnsupported && result.Message.Contains("cannot move or edit", StringComparison.Ordinal), "Committed placement protection bypassed.");
        });
        yield return ("G2 construction append remains valid and prior typed definitions are unchanged", () =>
        {
            var oracle = new VerifiedObservation(root, "G2").Oracle; var prior = StateContractTests.Prefix(oracle, 2); var addition = StateContractTests.Addition(oracle, 2);
            Program.Check(new RelationBackend().Preflight(addition, prior).IsValid, "G2 append changed.");
            var solved = new DesignRelationEngine().Solve(oracle).Program;
            Program.Check(prior.Operations.All(o => OperationSemanticComparer.EqualsDefinition(o, solved.Operations.Single(s => s.Id == o.Id))), "G2 prior altered.");
        });
        yield return ("Stepwise prompt schema capability and committed observation match M10C exactly", () =>
        {
            var (prior, _) = Fixture(root);
            var state = new VerifiedObservation(root, "HeldOut").Project(prior);
            var runtime = new SolidWorksStepwiseRuntime(prior, state);
            var source = new PromptCapture("{\"outcome\":\"complete\",\"program\":null,\"reason\":\"\"}");
            _ = new StepwisePlanner(source).DecideAsync(BenchmarkData.Tasks().Single(t => t.Name == "HeldOut").Intent, runtime).GetAwaiter().GetResult();
            using var recorded = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone10c/call-12-request.json")));
            var p = recorded.RootElement;
            Program.Check(source.Prompt!.Instructions == p.GetProperty("Instructions").GetString() && source.Prompt.Intent == p.GetProperty("Intent").GetString() &&
                source.Prompt.CapabilityJson == p.GetProperty("CapabilityJson").GetString() && source.Prompt.ResponseSchemaJson == p.GetProperty("ResponseSchemaJson").GetString(), "Stepwise contract changed.");
        });
    }
    private static (CadProgram Prior, CadProgram Addition) Fixture(string root)
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone10c/HeldOut-Stepwise-decision-1-committed-snapshot.json")));
        using var response = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone10c/call-12-response.json")));
        using var envelope = JsonDocument.Parse(response.RootElement.GetProperty("Json").GetString()!);
        var codec = new CadProgramJson();
        return (codec.Parse(snapshot.RootElement.GetProperty("Program").GetRawText()).Program!, codec.Parse(envelope.RootElement.GetProperty("program").GetRawText()).Program!);
    }
}
