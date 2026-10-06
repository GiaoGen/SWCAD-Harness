using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal static class CompatibilityTests
{
    internal static IEnumerable<(string, Action)> Cases(string root)
    {
        yield return ("provider compatibility auditor rejects empty objects/anyOf and malformed strict shapes", () =>
        {
            foreach (var schema in new[] {
                "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}",
                "{\"anyOf\":[]}", "{\"type\":\"array\"}",
                "{\"type\":\"array\",\"maxItems\":0,\"items\":{\"type\":\"object\",\"properties\":{}}}",
                "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\"}},\"required\":[],\"additionalProperties\":false}",
                "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\"}},\"required\":[\"value\"],\"additionalProperties\":true}" })
                Program.Check(ProviderSchemaCompatibility.Audit(schema).Count > 0, "Malformed provider schema accepted.");
            Program.Check(ProviderSchemaCompatibility.Audit("{\"type\":\"array\",\"maxItems\":0,\"items\":{\"type\":\"string\"}}").Count == 0, "Unreachable scalar items rejected.");
        });
        yield return ("DeepSeek refuses incompatible schema before HTTP without replacing local validation", () =>
        {
            var handler = new HttpProbe(); using var client = new HttpClient(handler);
            try { new DeepSeekPlanSource(client, "deepseek-chat", "test-key").GenerateAsync(new("", "", "{}", "{\"anyOf\":[]}"), default).GetAwaiter().GetResult(); throw new Exception("Schema accepted."); }
            catch (PlannerException e) { Program.Check(e.Code == "PLANNER_PROVIDER_SCHEMA_INVALID" && handler.Calls == 0, "Schema failure reached inference."); }
        });
        foreach (var task in BenchmarkData.Tasks())
        {
            var taskCopy = task;
            yield return (task.Name + " actual Harness/Stepwise request schemas preserve catalog, identifiers and relations", () => AuditTask(root, taskCopy));
        }
        yield return ("zero relation edit arrays remain empty-only in schema and local capability checks", () =>
        {
            var fixture = new VerifiedObservation(root, "G2"); var runtime = SolidWorksPlanningRuntime.ForEditSnapshot(fixture.Oracle, fixture.State);
            using var schema = JsonDocument.Parse(PlannerResponseSchema.Create(runtime.Capabilities));
            var relations = schema.RootElement.GetProperty("properties").GetProperty("program").GetProperty("anyOf")[0].GetProperty("properties").GetProperty("relations");
            Program.Check(relations.GetProperty("maxItems").GetInt32() == 0 && relations.GetProperty("items").GetProperty("type").GetString() == "string", "Zero relation schema weakened.");
            var target = runtime.Capabilities.ParameterEdits.First();
            var edit = new OperationNode("update", OperationKind.EditParameter, null, new[] { new OperationInput("target", new[] { new SemanticReference(target.Target, SemanticType.FeatureRef) }) },
                new Dictionary<string, OperationParameter> { ["parameter"] = new ParameterNameParameter(target.Parameter), ["value"] = new EditValueParameter(new LengthParameter(10)) });
            var program = new CadProgram("0.2", new[] { edit }, new[] { fixture.Oracle.Relations.First() });
            Program.Check(!runtime.Capabilities.Validate(program).IsValid, "Local relation prohibition weakened.");
        });
    }
    private static string Envelope(CadProgram p) => "{\"outcome\":\"planned\",\"program\":" + new CadProgramJson().Serialize(p) + ",\"reason\":\"\"}";
    private static IEnumerable<JsonElement> Schemas(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("anyOf", out var alternatives)) foreach (var child in alternatives.EnumerateArray()) foreach (var item in Schemas(child)) yield return item;
        if (node.TryGetProperty("properties", out var properties)) foreach (var property in properties.EnumerateObject()) foreach (var item in Schemas(property.Value)) yield return item;
        if (node.TryGetProperty("items", out var items)) foreach (var item in Schemas(items)) yield return item;
    }
    private static void AuditTask(string root, BenchmarkTask task)
    {
        var rows = new List<object>(); var oracle = new VerifiedObservation(root, task.Name);
        void Request(string name, IPlanningRuntime runtime, bool stepwise, string reply, CadProgram? prior = null)
        {
            var providerBody = JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = reply } } } }, usage = new { input_tokens = 1, output_tokens = 1 } });
            var handler = new HttpProbe { Response = providerBody }; using var client = new HttpClient(handler);
            var source = new DeepSeekPlanSource(client, "deepseek-chat", "test-key");
            if (stepwise)
            {
                var result = new StepwisePlanner(source).DecideAsync(task.Intent, new QualifiedRuntime(runtime, prior)).GetAwaiter().GetResult();
                Program.Check(result.Status is StepwiseStatus.Operation or StepwiseStatus.Complete, "Mocked actual Stepwise schema path rejected.");
            }
            else
            {
                var result = new CadPlanner(runtime, source).PlanAsync(task.Intent).GetAwaiter().GetResult();
                Program.Check(result.Succeeded, "Mocked actual Harness schema path rejected.");
            }
            using var request = JsonDocument.Parse(handler.Body!);
            var sent = request.RootElement.GetProperty("text").GetProperty("format").GetProperty("schema");
            var expected = JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(PlannerResponseSchema.Create(runtime.Capabilities, stepwise)));
            Program.Check(sent.GetRawText() == expected && ProviderSchemaCompatibility.Audit(sent.GetRawText()).Count == 0, "Sent schema differs from current generator/catalog.");
            var nodes = Schemas(sent).ToArray(); var patterns = nodes.Where(n => n.TryGetProperty("pattern", out _)).Select(n => n.GetProperty("pattern").GetString()!).ToArray();
            Program.Check(patterns.Length > 0 && patterns.All(p => p == Identifiers.SchemaPattern(false) || p == Identifiers.SchemaPattern(true)) && patterns.All(p => !Regex.IsMatch("extrude1", p) && !Regex.IsMatch("badCase", p)), "Identifier patterns weakened.");
            var programShape = sent.GetProperty("properties").GetProperty("program").GetProperty("anyOf")[0].GetProperty("properties");
            var operationShapes = programShape.GetProperty("operations").GetProperty("items").GetProperty("anyOf").EnumerateArray().ToArray();
            var kinds = operationShapes.Select(n => n.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString()).ToHashSet();
            Program.Check(kinds.SetEquals(runtime.Capabilities.Registry.Contracts.Select(c => c.WireName)), "Schema operation projection differs.");
            if (runtime.Capabilities.Mode == PlanningMode.EditModel)
            {
                var pairs = operationShapes.Select(n => (n.GetProperty("properties").GetProperty("target").GetProperty("properties").GetProperty("semanticId").GetProperty("enum")[0].GetString(), n.GetProperty("properties").GetProperty("parameter").GetProperty("enum")[0].GetString())).ToHashSet();
                Program.Check(pairs.SetEquals(runtime.Capabilities.ParameterEdits.Select(e => ((string?)e.Target, (string?)WireNames.Of(e.Parameter)))), "Edit target/parameter projection differs.");
            }
            var relationShape = programShape.GetProperty("relations");
            if (runtime.Capabilities.Relations.Count == 0) Program.Check(relationShape.GetProperty("maxItems").GetInt32() == 0, "No-relation projection weakened.");
            else
            {
                var relationKinds = relationShape.GetProperty("items").GetProperty("anyOf").EnumerateArray().Select(n => n.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString()).ToHashSet();
                Program.Check(relationKinds.SetEquals(runtime.Capabilities.Relations.Select(r => WireNames.Of(r))), "Relation schema lost catalog kinds.");
            }
            using var catalog = JsonDocument.Parse(runtime.Capabilities.ToPromptJson());
            var contracts = catalog.RootElement.GetProperty("relationContracts");
            Program.Check(contracts.GetArrayLength() == runtime.Capabilities.Relations.Count && contracts.EnumerateArray().All(c => c.GetProperty("subjectKinds").GetArrayLength() > 0 && !string.IsNullOrWhiteSpace(c.GetProperty("referenceType").GetString()) && !string.IsNullOrWhiteSpace(c.GetProperty("meaning").GetString())), "Projected relation contracts lost.");
            if (stepwise) Program.Check(sent.GetProperty("properties").GetProperty("outcome").GetProperty("enum").EnumerateArray().Any(e => e.GetString() == "complete"), "Complete envelope lost.");
            rows.Add(new { Stage = name, Status = "PASS", RecursiveSchemaNodes = nodes.Length, IdentifierPatterns = patterns.Length, CatalogRuntime = runtime.Capabilities.RuntimeId });
        }
        Request("Harness-construction", SolidWorksPlanningRuntime.ForConstruction(), false, Envelope(oracle.Oracle));
        for (var i = 0; i < task.Edits.Length; i++)
        {
            var snapshot = new VerifiedObservation(root, task.Name, i == 1 ? "thickness-10" : "creation");
            var runtime = SolidWorksPlanningRuntime.ForEditSnapshot(snapshot.Oracle, snapshot.State); var edit = task.Edits[i];
            var kind = edit.Parameter == EditableParameter.ExtrusionDepth ? OperationKind.CreateExtrude : OperationKind.CreateThroughHole;
            var target = snapshot.Oracle.Operations.Single(o => o.Kind == kind).SemanticId!;
            var raw = JsonSerializer.Serialize(new { outcome = "planned", reason = "", program = new { programVersion = "0.2", operations = new[] { new { id = "update", kind = "edit_parameter", target = new { semanticId = target, type = "feature_ref" }, parameter = WireNames.Of(edit.Parameter), value = edit.Value } }, relations = Array.Empty<object>() } });
            Request("Harness-edit-" + i, runtime, false, raw);
        }
        CadProgram? prior = null;
        for (var i = 0; i < oracle.Oracle.Operations.Count; i++)
        {
            var next = oracle.Oracle.Operations[i]; var addition = new CadProgram("0.2", new[] { next }, oracle.Oracle.Relations.Where(r => r.Subject == next.SemanticId).ToArray());
            Request(i == 0 ? "Stepwise-initial" : "Stepwise-append-" + i, new SolidWorksStepwiseRuntime(prior, prior is null ? null : oracle.Project(prior)), true, Envelope(addition), prior);
            prior = oracle.Oracle with { Operations = oracle.Oracle.Operations.Take(i + 1).ToArray(), Relations = oracle.Oracle.Relations.Where(r => oracle.Oracle.Operations.Take(i + 1).Any(o => o.SemanticId == r.Subject)).ToArray() };
        }
        Request("Stepwise-completion-capable", new SolidWorksStepwiseRuntime(prior, oracle.Project(prior!)), true, "{\"outcome\":\"complete\",\"program\":null,\"reason\":\"\"}", prior);
        Directory.CreateDirectory(Program.Output(root)); Program.Write(Path.Combine(Program.Output(root), task.Name + "-pure-schema-matrix.json"), rows);
    }
}
