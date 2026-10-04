using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CadHarness.Ir;

internal static class Program
{
    private static readonly CadProgramJson Codec = new();
    private static readonly List<(string Name, Action Run)> Cases = new();
    private const string Extrude = """
        {"id":"base","kind":"create_extrude","profile":{"kind":"centered_rectangle","widthMm":100,"heightMm":60},"depthMm":8,"semanticId":"base_plate"}
        """;
    private const string Hole = """
        {"id":"hole","kind":"create_through_hole","host":"base_plate.top_face","diameterMm":6,"semanticId":"mounting_hole_seed"}
        """;
    private const string Pattern = """
        {"id":"pattern","kind":"create_rectangular_pattern","seed":"mounting_hole_seed","countX":2,"countY":2,"semanticId":"mounting_holes"}
        """;
    private static string Plan(params string[] operations) => "{\"programVersion\":\"0.2\",\"operations\":[" + string.Join(",", operations) + "]}";

    private static int Main(string[] args)
    {
        var root = args.Length >= 1 ? args[0] : Directory.GetCurrentDirectory();
        if (args.Length == 2 && args[1] == "--write-schema")
        {
            Directory.CreateDirectory(Path.Combine(root, "schemas"));
            File.WriteAllText(Path.Combine(root, "schemas", "cad-program.schema.json"), ProgramJsonSchema.Create() + Environment.NewLine);
            Console.WriteLine("Schema generated from operation contracts.");
            return 0;
        }
        RegisterCases(root);
        var failures = 0;
        foreach (var test in Cases)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"{Cases.Count - failures}/{Cases.Count} pure tests passed; native Parts created=0, closed=0.");
        return failures == 0 ? 0 : 1;
    }

    private static void RegisterCases(string root)
    {
        Add("PRD example parses into typed IR", () =>
        {
            var result = Good(Plan(Extrude, Hole, Pattern)[..^1] + ",\"relations\":[{\"kind\":\"centered_about\",\"subject\":\"mounting_holes\",\"reference\":\"base_plate.local_frame\"}]}");
            Assert(result.Operations.Count == 3 && result.Relations.Count == 1, "Example structure differs.");
            Assert(result.Operations[0].Parameter<ProfileParameter>("profile").Value is CenteredRectangleProfile { WidthMm: 100, HeightMm: 60 }, "Profile lost typing.");
            Assert(result.Operations[1].Input("host")!.References[0].Type == SemanticType.PlanarFace, "Host not typed.");
        });
        Add("all P0 feature operations parse and round trip", () =>
        {
            var program = Good(Plan(Extrude, Hole, Pattern,
                """{"id":"blind","kind":"create_blind_hole","host":"base_plate.top_face","diameterMm":4,"depthMm":3,"placement":{"xMm":-10,"yMm":0},"semanticId":"blind_hole"}""",
                """{"id":"linear","kind":"create_linear_pattern","seed":"blind_hole","count":3,"spacingMm":12,"direction":{"semanticId":"fixture.axis","type":"reference_axis"},"semanticId":"linear_holes"}""",
                """{"id":"circular","kind":"create_circular_pattern","seed":"mounting_hole_seed","axis":{"semanticId":"fixture.axis","type":"reference_axis"},"count":6,"angleDeg":360,"semanticId":"bolt_holes"}""",
                """{"id":"fillet","kind":"apply_fillet","edges":[{"semanticId":"fixture.outer_edge","type":"linear_edge"}],"radiusMm":3,"semanticId":"outer_fillet"}""",
                """{"id":"chamfer","kind":"apply_chamfer","edges":[{"semanticId":"fixture.rim","type":"circular_edge"}],"distanceMm":2,"semanticId":"rim_chamfer"}""",
                """{"id":"edit","kind":"edit_parameter","target":"base_plate","parameter":"extrusion_depth","value":10}"""));
            var serialized = Codec.Serialize(program);
            Assert(Codec.Serialize(Good(serialized)) == serialized, "Round trip is not stable.");
            Assert(program.Operations.Select(x => x.Kind).Distinct().Count() == 9, "Operation coverage incomplete.");
        });
        Add("circle profile parses and round trips", () =>
        {
            var program = Good(Plan(Extrude.Replace("\"centered_rectangle\",\"widthMm\":100,\"heightMm\":60", "\"circle\",\"diameterMm\":100")));
            Assert(program.Operations[0].Parameter<ProfileParameter>("profile").Value is CircleProfile { DiameterMm: 100 }, "Circle lost typing.");
            Good(Codec.Serialize(program));
        });
        Add("unknown operation rejects with stable failure", () => Bad(Plan(Extrude.Replace("create_extrude", "create_turbine")), FailureCodes.OperationUnsupported));
        Add("unknown operation enum rejects direct typed program", () =>
        {
            var program = Good(Plan(Extrude));
            Invalid(program with { Operations = new[] { program.Operations[0] with { Kind = (OperationKind)999 } } }, FailureCodes.OperationUnsupported);
        });
        Add("missing input rejects", () => Bad(Plan(Extrude, Hole.Replace("\"host\":\"base_plate.top_face\",", ""))));
        Add("missing numeric parameter rejects", () => Bad(Plan(Extrude.Replace("\"depthMm\":8,", ""))));
        Add("explicit wrong semantic type rejects", () => Bad(Plan(Extrude, Hole.Replace("\"base_plate.top_face\"", "{\"semanticId\":\"base_plate.top_face\",\"type\":\"cylindrical_face\"}"))));
        Add("declared type cannot disguise output type", () => Bad(Plan(Extrude, Hole.Replace("base_plate.top_face", "base_plate.body"))));
        Add("unknown semantic enum rejects", () => Bad(Plan(Extrude, Hole.Replace("\"base_plate.top_face\"", "{\"semanticId\":\"fixture.face\",\"type\":\"iface2\"}"))));
        Add("forward input reference rejects", () => Bad(Plan(Hole, Extrude)));
        Add("unknown program output rejects", () => Bad(Plan(Extrude, Hole.Replace("top_face", "missing_face"))));
        Add("external typed semantic input is legal", () => Good(Plan(Hole.Replace("\"base_plate.top_face\"", "{\"semanticId\":\"existing.host\",\"type\":\"reference_plane\"}"))));
        foreach (var value in new[] { "0", "-1", "1e999", "\"8\"", "null", "true", "NaN", "Infinity" })
        {
            var numeric = value;
            Add("invalid numeric value rejects: " + numeric, () => Bad(Plan(Extrude.Replace("\"depthMm\":8", "\"depthMm\":" + numeric))));
        }
        Add("invalid nested profile dimension rejects", () => Bad(Plan(Extrude.Replace("\"widthMm\":100", "\"widthMm\":0"))));
        Add("typed NaN rejects before serialization", () =>
        {
            var program = Good(Plan(Extrude));
            var parameters = new Dictionary<string, OperationParameter>(program.Operations[0].Parameters) { ["depthMm"] = new LengthParameter(double.NaN) };
            var invalid = program with { Operations = new[] { program.Operations[0] with { Parameters = parameters } } };
            Invalid(invalid);
            Throws<ArgumentException>(() => Codec.Serialize(invalid));
        });
        Add("count must be integer", () => Bad(Plan(Extrude, Hole, Pattern.Replace("\"countX\":2", "\"countX\":2.5"))));
        Add("count must fit its bounded representation", () => Bad(Plan(Extrude, Hole, Pattern.Replace("\"countX\":2", "\"countX\":2147483648"))));
        Add("one-instance rectangular pattern rejects", () => Bad(Plan(Extrude, Hole, Pattern.Replace("\"countX\":2", "\"countX\":1").Replace("\"countY\":2", "\"countY\":1"))));
        Add("invalid optional spacing rejects", () => Bad(Plan(Extrude, Hole, Pattern.Replace("\"countY\":2", "\"countY\":2,\"spacingYMm\":-10"))));
        Add("empty edge set rejects", () => Bad(Plan("""{"id":"fillet","kind":"apply_fillet","edges":[],"radiusMm":2,"semanticId":"fillet"}""")));
        Add("duplicate edge references reject", () => Bad(Plan("""{"id":"fillet","kind":"apply_fillet","edges":["fixture.edge","fixture.edge"],"radiusMm":2,"semanticId":"fillet"}""")));
        Add("wrong edge type rejects", () => Bad(Plan("""{"id":"fillet","kind":"apply_fillet","edges":[{"semanticId":"fixture.face","type":"planar_face"}],"radiusMm":2,"semanticId":"fillet"}""")));
        Add("rotational role accepts cylinder", () =>
        {
            var contract = OperationRegistry.Default.Get(OperationKind.CreateCircularPattern);
            Assert(contract.Inputs.Single(x => x.Name == "axis").Accepts(SemanticType.CylindricalFace), "Cylinder cannot satisfy rotation.");
        });
        Add("unknown field rejects executable payload", () => Bad(Plan(Extrude.Replace("\"depthMm\":8", "\"depthMm\":8,\"code\":\"return null;\""))));
        Add("unknown nested field rejects", () => Bad(Plan(Extrude.Replace("\"widthMm\":100", "\"widthMm\":100,\"api\":\"FeatureExtrusion3\""))));
        Add("unknown root field rejects", () => Bad(Plan(Extrude)[..^1] + ",\"selectionMark\":1}"));
        foreach (var name in new[] { "FeatureExtrusion3", "featureextrusion3", "featurecut4", "select4", "FeatureLinearPattern5", "featurefillet3", "IFace2", "imodeldoc2", "sldworks.application", "boss_extrude1", "D1@Sketch1", "sw.FeatureCut4()", "base;return", "base plate", "基准面" })
        {
            var forbidden = name;
            Add("API/COM/code name rejects: " + forbidden, () =>
            {
                Bad(Plan(Extrude.Replace("\"base\"", JsonSerializer.Serialize(forbidden))));
                Bad(Plan(Extrude.Replace("\"base_plate\"", JsonSerializer.Serialize(forbidden))));
                Bad(Plan(Hole.Replace("\"base_plate.top_face\"", JsonSerializer.Serialize(forbidden))));
            });
        }
        Add("native method cannot be an operation kind", () => Bad(Plan(Extrude.Replace("create_extrude", "FeatureExtrusion3")), FailureCodes.OperationUnsupported));
        Add("duplicate JSON field rejects", () => Bad(Plan(Extrude.Replace("\"depthMm\":8", "\"depthMm\":8,\"depthMm\":10"))));
        Add("escaped duplicate JSON field rejects", () => Bad(Plan(Extrude.Replace("\"depthMm\":8", "\"depthMm\":8,\"depth\\u004dm\":10"))));
        Add("duplicate operation IDs reject", () => Bad(Plan(Extrude, Hole.Replace("\"hole\"", "\"base\""))));
        Add("duplicate semantic IDs reject", () => Bad(Plan(Extrude, Hole.Replace("\"mounting_hole_seed\"", "\"base_plate\""))));
        Add("version is exact", () => Bad(Plan(Extrude).Replace("\"0.2\"", "\"0.1\"")));
        Add("JSON case is exact", () => Bad(Plan(Extrude).Replace("programVersion", "ProgramVersion")));
        Add("empty operation list rejects", () => Bad(Plan()));
        Add("12-operation boundary accepts", () => Good(Plan(Enumerable.Range(0, 12).Select(i => Extrude.Replace("\"base\"", $"\"op_{i}\"").Replace("\"base_plate\"", $"\"plate_{i}\"")).ToArray())));
        Add("13-operation plan rejects", () => Bad(Plan(Enumerable.Range(0, 13).Select(i => Extrude.Replace("\"base\"", $"\"op_{i}\"").Replace("\"base_plate\"", $"\"plate_{i}\"")).ToArray())));
        Add("oversized input rejects", () => Bad(new string(' ', CadProgramJson.MaximumJsonBytes + 1)));
        Add("comments reject", () => Bad("/* comment */" + Plan(Extrude)));
        Add("trailing comma rejects", () => Bad(Plan(Extrude)[..^1] + ",}"));
        Add("malformed JSON rejects", () => Bad("{"));
        Add("null root rejects", () => Bad("null"));
        Add("excessive nesting rejects", () => Bad(new string('[', 40) + "0" + new string(']', 40)));
        Add("nonfinite placement rejects", () => Bad(Plan(Hole.Replace("\"diameterMm\":6", "\"diameterMm\":6,\"placement\":{\"xMm\":1e999,\"yMm\":0}"))));
        Add("unknown profile rejects", () => Bad(Plan(Extrude.Replace("centered_rectangle", "freeform"))));
        Add("unknown editable parameter rejects", () => Bad(Plan("""{"id":"edit","kind":"edit_parameter","target":"fixture.feature","parameter":"native_dimension","value":5}""")));
        Add("invalid edited length rejects", () => Bad(Plan("""{"id":"edit","kind":"edit_parameter","target":"fixture.feature","parameter":"hole_diameter","value":-5}""")));
        Add("invalid edited count rejects", () => Bad(Plan("""{"id":"edit","kind":"edit_parameter","target":"fixture.feature","parameter":"pattern_count","value":2.5}""")));
        Add("known target parameter ownership rejects", () => Bad(Plan(Extrude, """{"id":"edit","kind":"edit_parameter","target":"base_plate","parameter":"hole_diameter","value":5}""")));
        Add("edit value type checked for direct IR", () =>
        {
            var program = Good(Plan("""{"id":"edit","kind":"edit_parameter","target":"fixture.feature","parameter":"pattern_count","value":3}"""));
            var parameters = new Dictionary<string, OperationParameter>(program.Operations[0].Parameters) { ["value"] = new EditValueParameter(new LengthParameter(3)) };
            Invalid(program with { Operations = new[] { program.Operations[0] with { Parameters = parameters } } });
        });
        Add("all relation declarations parse", () =>
        {
            var relations = Enum.GetValues<RelationKind>().Select(kind => kind == RelationKind.ThroughAll
                ? $"{{\"kind\":\"{WireNames.Of(kind)}\",\"subject\":\"fixture.subject\"}}"
                : $"{{\"kind\":\"{WireNames.Of(kind)}\",\"subject\":\"fixture.subject\",\"reference\":\"fixture.reference\"}}");
            var program = Good(Plan(Extrude)[..^1] + ",\"relations\":[" + string.Join(",", relations) + "]}");
            Assert(program.Relations.Count == 8, "Relation vocabulary incomplete.");
            Good(Codec.Serialize(program));
        });
        Add("unknown relation rejects", () => Bad(Plan(Extrude)[..^1] + ",\"relations\":[{\"kind\":\"arbitrary_expression\",\"subject\":\"base_plate\"}]}"));
        Add("binary relation missing reference rejects", () => Bad(Plan(Extrude)[..^1] + ",\"relations\":[{\"kind\":\"centered_about\",\"subject\":\"base_plate\"}]}"));
        Add("relation native reference rejects", () => Bad(Plan(Extrude)[..^1] + ",\"relations\":[{\"kind\":\"centered_about\",\"subject\":\"base_plate\",\"reference\":\"iface2\"}]}"));
        Add("unknown internal relation output rejects", () => Bad(Plan(Extrude)[..^1] + ",\"relations\":[{\"kind\":\"centered_about\",\"subject\":\"base_plate.missing\",\"reference\":\"fixture.frame\"}]}"));
        Add("registry lookup and complete contracts", () =>
        {
            var registry = OperationRegistry.Default;
            Assert(registry.Contracts.Count == 9, "Registry count differs.");
            foreach (var kind in Enum.GetValues<OperationKind>())
            {
                Assert(registry.TryGet(kind, out var contract) && registry.TryGet(contract.WireName, out var same) && ReferenceEquals(contract, same), "Lookup differs.");
                Assert(contract.Preconditions.Count > 0 && contract.Effects.Count > 0 && contract.Postconditions.Count > 0, "Incomplete contract.");
            }
            Assert(!registry.TryGet("unknown", out _), "Unknown registry name accepted.");
            Throws<ArgumentException>(() => new OperationRegistry(new[] { registry.Contracts[0], registry.Contracts[0] }));
        });
        Add("future rotational inputs need no native types", () =>
        {
            var input = new InputContract("a", SemanticRole.RotationalReference, SemanticTypes.ForRole(SemanticRole.RotationalReference));
            Assert(input.Accepts(SemanticType.ReferenceAxis) && input.Accepts(SemanticType.CylindricalFace) && !input.Accepts(SemanticType.PlanarFace), "Semantic roles differ.");
        });
        Add("JSON schema is generated from contracts", () =>
        {
            var schema = ProgramJsonSchema.Create();
            using var document = JsonDocument.Parse(schema);
            Assert(document.RootElement.GetProperty("$defs").EnumerateObject().Count() == 13, "Schema vocabulary differs.");
            var path = Path.Combine(root, "schemas", "cad-program.schema.json");
            Assert(File.Exists(path), "Committed schema is missing.");
            Assert(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path)), JsonNode.Parse(schema)), "Committed schema differs from contracts.");
        });
    }

    private static void Add(string name, Action run) => Cases.Add((name, run));
    private static CadProgram Good(string json)
    {
        var result = Codec.Parse(json);
        Assert(result.IsValid, string.Join("; ", result.Issues.Select(x => x.Path + ": " + x.Message)));
        return result.Program!;
    }
    private static void Bad(string json, string code = FailureCodes.SchemaInvalid)
    {
        var result = Codec.Parse(json);
        Assert(!result.IsValid && result.Program is null && result.Issues.Any(x => x.Code == code), "Expected structured rejection: " + code);
    }
    private static void Invalid(CadProgram program, string code = FailureCodes.SchemaInvalid) =>
        Assert(new ProgramValidator().Validate(program).Issues.Any(x => x.Code == code), "Expected typed-program rejection.");
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
}
