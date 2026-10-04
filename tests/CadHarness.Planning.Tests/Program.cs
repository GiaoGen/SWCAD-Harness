using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Planning.Tests;

internal sealed class MockSource : IStructuredPlanSource
{
    public bool IsModelBacked => true;
    internal string Response;
    internal int Calls;
    internal PlannerPrompt? Prompt;
    internal MockSource(string response) => Response = response;
    public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken token)
    { Calls++; Prompt = prompt; token.ThrowIfCancellationRequested(); return Task.FromResult(new StructuredPlanResponse(Response)); }
}
internal sealed class MockHttp : HttpMessageHandler
{
    internal string Response;
    internal HttpStatusCode Status = HttpStatusCode.OK;
    internal string? Body;
    internal string? Authorization;
    internal Uri? Endpoint;
    internal int Calls;
    internal bool OmitLength;
    internal bool Cancel;
    internal MockHttp(string response) => Response = response;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Authorization = request.Headers.Authorization?.ToString(); Endpoint = request.RequestUri;
        if (Cancel) throw new OperationCanceledException();
        var content = new StringContent(Response);
        if (OmitLength) content.Headers.ContentLength = null;
        return new(Status) { Content = content };
    }
}

internal static class Program
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static string Envelope(CadProgram program) => JsonSerializer.Serialize(new
    { outcome = "planned", program = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(program)), reason = "" });
    private static OperationNode Edit(EditableParameter parameter, double value, string target = "holes") => new("edit", OperationKind.EditParameter, null,
        new[] { new OperationInput("target", new[] { new SemanticReference(target, SemanticType.FeatureRef) }) },
        new Dictionary<string, OperationParameter> { ["parameter"] = new ParameterNameParameter(parameter),
            ["value"] = new EditValueParameter(EditableParameters.Contract(parameter).Kind == ParameterKind.Count ? new CountParameter((int)value) : new LengthParameter(value)) });
    private static CadProgram EditProgram(EditableParameter parameter, double value, string target = "holes") => new("0.2", new[] { Edit(parameter, value, target) }, Array.Empty<DesignRelation>());
    private static CadState StateFor(CadProgram program)
    {
        var reference = new NativePersistentReference("AQID");
        var frame = new LocalFrameGeometry(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var entities = program.Operations.SelectMany(o => OperationRegistry.Default.Get(o.Kind).Outputs.Select(e =>
            new SemanticEntityNode(o.SemanticId + e.Suffix, e.Type, o.SemanticId!, reference, ReferenceHealth.Healthy)
            { Geometry = e.Type == SemanticType.LocalFrame ? new(new(0, 0, 0), Frame: frame) : e.Type == SemanticType.ReferenceAxis ? new(new(0, 0, 0), new(1, 0, 0)) : null })).ToArray();
        var bindings = program.Operations.SelectMany(o => RelationParameterEditor.SupportedFields.Keys.Where(p => EditableParameters.IsOwnedBy(p, o))
            .Where(p => o.Parameters.ContainsKey(RelationParameterEditor.SupportedFields[p])).Select(p => new ParameterBinding(o.SemanticId + "." + WireNames.Of(p), o.SemanticId!, p))).ToArray();
        var parameters = bindings.Select(b =>
        {
            var value = program.Operations.Single(o => o.SemanticId == b.OwnerFeatureSemanticId).Parameters[RelationParameterEditor.SupportedFields[b.Parameter]];
            return new ParameterNode(b.ParameterSemanticId, value.Kind, value is LengthParameter length ? length.Millimeters : ((CountParameter)value).Value);
        }).ToArray();
        return new() { SchemaVersion = "0.2", Revision = 0, Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""),
            Features = program.Operations.Select(o => new FeatureNode(o.SemanticId!, o.Kind, reference, ReferenceHealth.Healthy)).ToArray(), Entities = entities,
            Parameters = parameters, Bindings = bindings, Relations = StateRelationData.Encode(program.Relations), Dependencies = StateRelationData.Encode(new DesignRelationEngine().Solve(program).Dependencies.Edges) };
    }
    private static string ProviderResponse(string json, string status = "completed") => JsonSerializer.Serialize(new
    {
        status, output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = json } } } },
        usage = new { input_tokens = 13, output_tokens = 31 }
    });
    private static async Task<PlanningResult> Plan(string json, IPlanningRuntime? runtime = null)
    {
        var source = new MockSource(json); var result = await new CadPlanner(runtime ?? SolidWorksPlanningRuntime.ForConstruction(), source).PlanAsync("机械意图测试");
        Check(source.Calls == 1 && result.ModelCalls == 1, "Planner queried more than one model response."); return result;
    }
    private static async Task<PlanningResult> HttpPlan(MockHttp handler, IPlanningRuntime? runtime = null)
    {
        using var client = new HttpClient(handler);
        var options = new OpenAiPlannerOptions("configured-frontier-model", "test-placeholder-key", new Uri("https://example.test/v1/responses"), 4096, 10);
        return await new CadPlanner(runtime ?? SolidWorksPlanningRuntime.ForConstruction(), new OpenAiPlanSource(client, options)).PlanAsync("机械意图测试");
    }
    private static void StrictObjects(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "object")
            {
                var fields = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
                Check(schema.GetProperty("additionalProperties").ValueKind == JsonValueKind.False &&
                    fields.SetEquals(schema.GetProperty("required").EnumerateArray().Select(p => p.GetString()!)), "Provider strict schema has optional/open fields.");
            }
            foreach (var property in schema.EnumerateObject()) StrictObjects(property.Value);
        }
        else if (schema.ValueKind == JsonValueKind.Array) foreach (var item in schema.EnumerateArray()) StrictObjects(item);
    }
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1) return 2;
        var root = args[0]; var output = Path.Combine(root, "artifacts", "milestone7"); Directory.CreateDirectory(output);
        using var fixtures = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "CadHarness.Planning.Tests", "Fixtures", "intent-responses.json")));
        var map = fixtures.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => JsonSerializer.Serialize(p.Value), StringComparer.Ordinal);
        var intent = map.Keys.First(); var valid = map[intent];
        var program = new CadProgramJson().Parse(fixtures.RootElement.GetProperty(intent).GetProperty("program").GetRawText()).Program!;
        var runtime = SolidWorksPlanningRuntime.ForConstruction(); var state = StateFor(program);
        var editRuntime = SolidWorksPlanningRuntime.ForEditSnapshot(program, state);
        var tests = new List<(string Name, Func<Task> Run)>();
        void Add(string name, Action test) => tests.Add((name, () => { test(); return Task.CompletedTask; }));
        void Async(string name, Func<Task> test) => tests.Add((name, test));
        Add("catalog projects actual registered native handlers", () => Check(runtime.Capabilities.Registry.Contracts.Select(c => c.Kind).ToHashSet().SetEquals(new FeatureBackendRegistry().SupportedKinds), "Catalog and native registry diverged."));
        Add("construction hides unavailable profiles, edits, circular patterns and relations", () =>
        {
            var text = runtime.Capabilities.ToPromptJson(); var schema = PlannerResponseSchema.Create(runtime.Capabilities);
            foreach (var forbidden in new[] { "create_circular_pattern", "\"circle\"", "edit_parameter", "hole_diameter", "extrusion_depth", "through_all", "aligned_with", "depends_on" })
                Check(!text.Contains(forbidden, StringComparison.Ordinal) && !schema.Contains(forbidden, StringComparison.Ordinal), "Unsupported vocabulary leaked into planner input: " + forbidden);
            Check(runtime.Capabilities.Profiles.SequenceEqual(new[] { ProfileKind.CenteredRectangle }) && runtime.Capabilities.Relations.Count == 5, "Executable profiles/relations differ.");
        });
        Add("catalog narrows input types to executable construction outputs", () =>
        {
            foreach (var op in runtime.Capabilities.Registry.Contracts)
                foreach (var input in op.Inputs) Check(!input.AcceptedTypes.Contains(SemanticType.ReferencePlane) && !input.AcceptedTypes.Contains(SemanticType.ReferenceAxis) && !input.AcceptedTypes.Contains(SemanticType.CircularEdge), "IR-wide accepted input types leaked into runtime catalog.");
        });
        Add("creation and edit schemas satisfy provider closed-object rules", () =>
        {
            using var creation = JsonDocument.Parse(PlannerResponseSchema.Create(runtime.Capabilities)); StrictObjects(creation.RootElement);
            using var edit = JsonDocument.Parse(PlannerResponseSchema.Create(editRuntime.Capabilities)); StrictObjects(edit.RootElement);
        });
        Async("Chinese deterministic intent produces strict native-preflighted program", async () =>
        {
            var result = await new CadPlanner(runtime, new FixturePlanSource(map)).PlanAsync(intent);
            Check(result.Succeeded && result.ModelCalls == 0 && result.Program!.Operations.Count == 3 && runtime.Preflight(result.Program).IsValid, "Deterministic fixture did not reach a strict executable program without a model call.");
            var seed = new DesignRelationEngine().Solve(result.Program!).Program.Operations.Single(o => o.SemanticId == "seed").Parameter<PlacementParameter>("placement").Value;
            Check(seed == new Point2D(-20, 0), "Planner output did not preserve native centered relations.");
        });
        Async("unsupported intent does not degrade to another model", async () =>
        {
            var result = await new CadPlanner(runtime, new FixturePlanSource(map)).PlanAsync(map.Keys.Last());
            Check(result.Status == PlanningStatus.Unsupported && result.Program is null, "Unsupported intent yielded a substitute model.");
        });
        Async("unmatched deterministic intent is explicitly unsupported", async () => Check((await new CadPlanner(runtime, new FixturePlanSource(map)).PlanAsync("未登记的意图")).Status == PlanningStatus.Unsupported, "Fixture source invented a plan."));
        Async("one-shot prompt carries projection rather than whole IR registry", async () =>
        {
            var source = new MockSource(valid); var result = await new CadPlanner(runtime, source).PlanAsync(intent);
            Check(result.Succeeded && source.Calls == 1 && source.Prompt!.Instructions.Contains(runtime.Capabilities.ToPromptJson(), StringComparison.Ordinal) &&
                !source.Prompt.Instructions.Contains("create_circular_pattern", StringComparison.Ordinal), "Prompt ignored runtime projection.");
        });
        Async("IR-valid circle extrusion is rejected by runtime profile guard", async () =>
        {
            var disk = program with { Operations = new[] { program.Operations[0] with { Parameters = new Dictionary<string, OperationParameter>
                { ["profile"] = new ProfileParameter(new CircleProfile(80)), ["depthMm"] = new LengthParameter(10) } } }, Relations = Array.Empty<DesignRelation>() };
            Check(new ProgramValidator().Validate(disk).IsValid && (await Plan(Envelope(disk))).Status == PlanningStatus.Unsupported, "IR-valid unsupported profile was executable to the planner.");
        });
        Async("IR-valid circular pattern is rejected despite valid IR contract", async () =>
        {
            var p = program.Operations.ToArray(); p[2] = p[2] with { Kind = OperationKind.CreateCircularPattern,
                Inputs = new[] { p[2].Inputs[0], new OperationInput("axis", new[] { new SemanticReference("plate.axis_x", SemanticType.ReferenceAxis) }) },
                Parameters = new Dictionary<string, OperationParameter> { ["count"] = new CountParameter(6), ["angleDeg"] = new AngleParameter(360) } };
            var circular = program with { Operations = p, Relations = Array.Empty<DesignRelation>() };
            Check(new ProgramValidator().Validate(circular).IsValid && (await Plan(Envelope(circular))).Status == PlanningStatus.Unsupported, "Planner accepted unavailable circular pattern.");
        });
        foreach (var relation in new[] { RelationKind.ThroughAll, RelationKind.AlignedWith, RelationKind.DependsOn })
        {
            var captured = relation;
            Async("reject unsupported relation " + relation, async () =>
            {
                var p = program with { Relations = new[] { new DesignRelation(captured, "seed", captured == RelationKind.ThroughAll ? null : "plate.top_face") } };
                Check((await Plan(Envelope(p))).Status == PlanningStatus.Unsupported, "Unsupported relation escaped the planner guard.");
            });
        }
        Async("native host constraint rejects bottom_face", async () =>
        {
            var p = program.Operations.ToArray(); p[1] = p[1] with { Inputs = new[] { new OperationInput("host", new[] { new SemanticReference("plate.bottom_face", SemanticType.PlanarFace) }) } };
            Check(!(await Plan(Envelope(program with { Operations = p, Relations = Array.Empty<DesignRelation>() }))).Succeeded, "Unsupported host was advertised as executable.");
        });
        Async("out-of-host pattern fails pure runtime preflight", async () =>
        {
            var p = RelationParameterEditor.Apply(program, Edit(EditableParameter.PatternSpacing, 500));
            Check((await Plan(Envelope(p))).FailureCode == FailureCodes.PreconditionFailed, "Planner skipped runtime geometry preflight.");
        });
        Async("missing repeated-direction spacing is not invented", async () =>
        {
            var invalid = valid.Replace("\"spacingMm\":40", "\"unusedMm\":40", StringComparison.Ordinal);
            Check(!(await Plan(invalid)).Succeeded, "Unknown/missing spacing was accepted.");
        });
        foreach (var bad in new[] { "{}", "```json\n" + valid + "\n```", valid.Replace("\"outcome\":\"planned\"", "\"outcome\":\"planned\",\"outcome\":\"planned\"", StringComparison.Ordinal),
            valid.Replace("\"count\":2", "\"count\":2.5", StringComparison.Ordinal), valid.Replace("\"depthMm\":10", "\"depthMm\":0", StringComparison.Ordinal),
            valid.Replace("\"depthMm\":10", "\"depthMm\":10,\"code\":\"FeatureExtrusion3()\"", StringComparison.Ordinal) })
        {
            var captured = bad;
            Async("strict response rejects malformed/unsafe sample " + tests.Count, async () => Check(!(await Plan(captured)).Succeeded, "Malformed or executable-code response became a plan."));
        }
        Async("oversized response rejects without partial program", async () => Check(!(await Plan(new string(' ', CadProgramJson.MaximumJsonBytes + 5000))).Succeeded, "Byte limit bypassed."));
        Add("edit catalog includes only healthy bound active parameter pairs", () =>
        {
            Check(editRuntime.Capabilities.Registry.Contracts.Single().Kind == OperationKind.EditParameter && editRuntime.Capabilities.ParameterEdits.Count == 2 &&
                editRuntime.Capabilities.ParameterEdits.All(e => e.Target == "holes" && e.Parameter is EditableParameter.PatternSpacing or EditableParameter.PatternCount), "Edit projection included unsupported targets/parameters.");
        });
        Async("edit fixture emits executable transaction operation", async () =>
        {
            var result = await new CadPlanner(editRuntime, new FixturePlanSource(map)).PlanAsync(map.Keys.ElementAt(1));
            Check(result.Succeeded && result.Program!.Operations.Single().Kind == OperationKind.EditParameter, "Edit fixture did not pass capability/runtime validation.");
        });
        foreach (var parameter in new[] { EditableParameter.ExtrusionDepth, EditableParameter.HoleDiameter, EditableParameter.FilletRadius, EditableParameter.ChamferDistance })
        {
            var captured = parameter;
            Async("IR-valid unimplemented native edit rejects " + parameter, async () => Check((await Plan(Envelope(EditProgram(captured, 8)), editRuntime)).Status == PlanningStatus.Unsupported, "Unimplemented native parameter edit escaped capability guard."));
        }
        Async("edit rejects target without managed binding", async () => Check((await Plan(Envelope(EditProgram(EditableParameter.PatternSpacing, 50, "other")), editRuntime)).Status == PlanningStatus.Unsupported, "Unbound target accepted."));
        Async("edit runtime rejects creation program", async () => Check((await Plan(valid, editRuntime)).Status == PlanningStatus.Unsupported, "Edit mode admitted creation."));
        Async("construction runtime rejects parameter edits", async () => Check((await Plan(Envelope(EditProgram(EditableParameter.PatternSpacing, 50)))).Status == PlanningStatus.Unsupported, "Construction mode admitted edits."));
        Async("one edit transaction per plan is enforced", async () =>
        {
            var p = EditProgram(EditableParameter.PatternSpacing, 50); p = p with { Operations = new[] { p.Operations[0], p.Operations[0] with { Id = "second" } } };
            Check(!(await Plan(Envelope(p), editRuntime)).Succeeded, "Batch of unimplemented edit transactions accepted.");
        });
        Add("deleted/stale targets disappear from edit projection", () =>
        {
            var stale = state with { Entities = state.Entities.Select(e => e.SemanticId == "holes" ? e with { ReferenceHealth = ReferenceHealth.Stale } : e).ToArray() };
            var projected = SolidWorksPlanningRuntime.ForEditSnapshot(program, stale);
            Check(projected.Capabilities.ParameterEdits.Count == 0 && projected.Capabilities.Registry.Contracts.Count == 0, "Unhealthy edit target advertised.");
        });
        Add("non-unique bindings and drifted snapshot parameters are not advertised", () =>
        {
            var duplicate = state with
            {
                Parameters = state.Parameters.Concat(new[] { new ParameterNode("holes.other_spacing", ParameterKind.Length, 40) }).ToArray(),
                Bindings = state.Bindings.Concat(new[] { new ParameterBinding("holes.other_spacing", "holes", EditableParameter.PatternSpacing) }).ToArray()
            };
            var drifted = state with { Parameters = state.Parameters.Select(p => p.SemanticId == "holes.pattern_spacing" ? p with { Value = 41 } : p).ToArray() };
            Check(SolidWorksPlanningRuntime.ForEditSnapshot(program, duplicate).Capabilities.ParameterEdits.All(e => e.Parameter != EditableParameter.PatternSpacing) &&
                SolidWorksPlanningRuntime.ForEditSnapshot(program, drifted).Capabilities.ParameterEdits.All(e => e.Parameter != EditableParameter.PatternSpacing), "An edit that native preflight must reject was advertised.");
        });
        Add("mismatched snapshot dependencies reject projection", () =>
        {
            try { SolidWorksPlanningRuntime.ForEditSnapshot(program, state with { Dependencies = Array.Empty<JsonElement>() }); }
            catch (StateException error) when (error.Code == "STALE_REFERENCE") { return; }
            throw new Exception("Mismatched dependency snapshot yielded planner capabilities.");
        });
        Add("inactive rectangular directions disappear and active counts preserve dimensionality", () =>
        {
            var p = program.Operations.ToArray(); p[2] = p[2] with { Kind = OperationKind.CreateRectangularPattern, Parameters = new Dictionary<string, OperationParameter>
                { ["countX"] = new CountParameter(1), ["countY"] = new CountParameter(3), ["spacingYMm"] = new LengthParameter(12) } };
            var rect = program with { Operations = p }; var projected = SolidWorksPlanningRuntime.ForEditSnapshot(rect, StateFor(rect));
            Check(projected.Capabilities.ParameterEdits.Count == 2 && projected.Capabilities.ParameterEdits.All(e => e.Parameter is EditableParameter.PatternCountY or EditableParameter.PatternSpacingY), "Inactive X direction was advertised.");
            Check(!projected.Preflight(EditProgram(EditableParameter.PatternCountY, 1)).IsValid && projected.Preflight(EditProgram(EditableParameter.PatternCountY, 4)).IsValid, "Count edit changed active direction status.");
        });
        Async("pre-cancelled planning does not call the model", async () =>
        {
            var source = new MockSource(valid); var result = await new CadPlanner(runtime, source).PlanAsync(intent, new CancellationToken(true));
            Check(result.Status == PlanningStatus.Cancelled && source.Calls == 0 && result.ModelCalls == 0, "Cancellation still issued model request.");
        });
        Async("invalid/oversized intent rejects before source", async () =>
        {
            var source = new MockSource(valid); var planner = new CadPlanner(runtime, source);
            Check(!(await planner.PlanAsync(" ")).Succeeded && !(await planner.PlanAsync(new string('中', 3000))).Succeeded && source.Calls == 0, "Intent byte bounds ignored.");
        });
        Async("configured frontier adapter posts projected strict schema exactly once", async () =>
        {
            var handler = new MockHttp(ProviderResponse(valid)); var result = await HttpPlan(handler);
            Check(result.Succeeded && handler.Calls == 1 && result.InputTokens == 13 && result.OutputTokens == 31, "Responses adapter failed mocked one-shot request.");
            using var body = JsonDocument.Parse(handler.Body!); var request = body.RootElement;
            Check(request.GetProperty("model").GetString() == "configured-frontier-model" && request.GetProperty("store").ValueKind == JsonValueKind.False &&
                request.GetProperty("text").GetProperty("format").GetProperty("strict").ValueKind == JsonValueKind.True && !request.TryGetProperty("tools", out _) &&
                !handler.Body!.Contains("create_circular_pattern", StringComparison.Ordinal) && handler.Authorization == "Bearer test-placeholder-key" && handler.Endpoint!.Host == "example.test", "Adapter ignored configuration or exposed unsupported tools.");
        });
        Async("provider HTTP error does not retry or leak response/credentials", async () =>
        {
            var handler = new MockHttp("test-placeholder-key private provider body") { Status = HttpStatusCode.Unauthorized }; var result = await HttpPlan(handler);
            Check(result.FailureCode == "PLANNER_PROVIDER_ERROR" && handler.Calls == 1 && !result.Message.Contains("test-placeholder-key", StringComparison.Ordinal), "Provider failure leaked secrets or retried.");
        });
        Async("provider tool call is rejected", async () =>
        {
            var handler = new MockHttp("{\"status\":\"completed\",\"output\":[{\"type\":\"function_call\",\"name\":\"execute\"}]}");
            Check((await HttpPlan(handler)).FailureCode == "PLANNER_UNEXPECTED_OUTPUT", "Tool-agent output was accepted.");
        });
        Async("provider refusal is explicit failure", async () =>
        {
            var handler = new MockHttp("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"No\"}]}]}");
            Check((await HttpPlan(handler)).FailureCode == "PLANNER_REFUSED", "Refusal generated a partial plan.");
        });
        Async("provider truncated response rejects partial program", async () => Check((await HttpPlan(new MockHttp(ProviderResponse(valid, "incomplete")))).FailureCode == "PLANNER_RESPONSE_INCOMPLETE", "Incomplete plan accepted."));
        Async("provider stream limit rejects before JSON parse", async () => Check((await HttpPlan(new MockHttp(new string(' ', OpenAiPlanSource.MaximumResponseBytes + 1)) { OmitLength = true })).FailureCode == "PLANNER_RESPONSE_TOO_LARGE", "Streaming byte limit bypassed."));
        Async("provider cancellation is structured without retry", async () =>
        {
            var handler = new MockHttp("") { Cancel = true }; var result = await HttpPlan(handler);
            Check(result.Status == PlanningStatus.Cancelled && handler.Calls == 1, "Transport cancellation retried/escaped.");
        });
        Add("missing model/key and unsafe endpoints reject configuration", () =>
        {
            foreach (var invalid in new Action[] { () => _ = new OpenAiPlannerOptions("", "key"), () => _ = new OpenAiPlannerOptions("model", ""),
                () => _ = new OpenAiPlannerOptions("model", "key", new Uri("http://example.test/responses")), () => _ = new OpenAiPlannerOptions("model", "key", new Uri("https://example.test/responses?api_key=secret")) })
            { try { invalid(); throw new Exception("Invalid provider configuration accepted."); } catch (PlannerException error) { Check(error.Code == "PLANNER_CONFIGURATION_INVALID", "Wrong config error."); } }
        });
        var results = new List<object>(); var passed = 0;
        foreach (var test in tests)
        {
            try { await test.Run(); passed++; Console.WriteLine("PASS " + test.Name); results.Add(new { test.Name, Passed = true }); }
            catch (Exception error) { Console.WriteLine("FAIL " + test.Name + ": " + error.Message); results.Add(new { test.Name, Passed = false, Error = error.Message }); }
        }
        File.WriteAllText(Path.Combine(output, "pure-result.json"), JsonSerializer.Serialize(new { Status = passed == tests.Count ? "COMPLETE" : "BLOCKED", Passed = passed, Total = tests.Count,
            NativePartsCreated = 0, NativePartsClosed = 0, LiveModelRequests = 0, Tests = results }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "construction-capabilities.json"), runtime.Capabilities.ToPromptJson());
        File.WriteAllText(Path.Combine(output, "edit-capabilities.json"), editRuntime.Capabilities.ToPromptJson());
        File.WriteAllText(Path.Combine(output, "planner-response.schema.json"), PlannerResponseSchema.Create(runtime.Capabilities));
        File.WriteAllText(Path.Combine(output, "edit-response.schema.json"), PlannerResponseSchema.Create(editRuntime.Capabilities));
        File.WriteAllText(Path.Combine(output, "planned-program.json"), new CadProgramJson(runtime.Capabilities.Registry).Serialize(program));
        new AtomicStateStore(Path.Combine(output, "mock-model-state.json")).Commit(state);
        return passed == tests.Count ? 0 : 1;
    }
}
