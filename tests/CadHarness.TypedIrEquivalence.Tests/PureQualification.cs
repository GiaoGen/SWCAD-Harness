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

namespace CadHarness.Benchmark.Tests;
internal sealed class HttpProbe : HttpMessageHandler
{
    internal string Response = "";
    internal HttpStatusCode Status = HttpStatusCode.OK;
    internal string? Body; internal Uri? Endpoint; internal int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    { Calls++; Endpoint = request.RequestUri; Body = await request.Content!.ReadAsStringAsync(ct); return new(Status) { Content = new StringContent(Response) }; }
}
internal sealed class ReplySource : IStructuredPlanSource
{
    internal string Reply = "";
    public bool IsModelBacked => true;
    public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt p, CancellationToken ct) => Task.FromResult(new StructuredPlanResponse(Reply, 17, 23));
}
internal static class PureQualification
{
    private static string Envelope(CadProgram program) => "{\"outcome\":\"planned\",\"program\":" + new CadProgramJson().Serialize(program) + ",\"reason\":\"\"}";
    internal static int Run(string root)
    {
        var tests = new List<(string, Action)>();
        void Test(string name, Action test) => tests.Add((name, test));
        var runtime = SolidWorksPlanningRuntime.ForConstruction();
        Test("DeepSeek requests exact planner schema through shared Responses adapter in both modes", () =>
        {
            foreach (var stepwise in new[] { false, true })
            {
                var schema = PlannerResponseSchema.Create(runtime.Capabilities, stepwise);
                var body = JsonSerializer.Serialize(new { status = "completed", model = "deepseek-flash", output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = "{\"outcome\":\"complete\",\"program\":null,\"reason\":\"\"}" } } } }, usage = new { input_tokens = 101, output_tokens = 29 } });
                var handler = new HttpProbe { Response = body }; using var client = new HttpClient(handler);
                var response = new DeepSeekPlanSource(client, "deepseek-chat", "test-key").GenerateAsync(new("instruction", "intent", runtime.Capabilities.ToPromptJson(), schema), default).GetAwaiter().GetResult();
                using var request = JsonDocument.Parse(handler.Body!);
                var format = request.RootElement.GetProperty("text").GetProperty("format");
                Program.Check(handler.Endpoint == DeepSeekPlanSource.Endpoint && handler.Calls == 1 && format.GetProperty("type").GetString() == "json_schema" && format.GetProperty("strict").GetBoolean() && format.GetProperty("schema").GetRawText() == JsonSerializer.Serialize(JsonSerializer.Deserialize<JsonElement>(schema)), "Provider schema changed or non-schema protocol used.");
                Program.Check(!request.RootElement.TryGetProperty("tools", out _) && request.RootElement.GetProperty("temperature").GetInt32() == 0 && response.InputTokens == 101 && response.OutputTokens == 29 && response.ProviderJson == body, "Provider settings or evidence differ.");
            }
        });
        Test("provider errors retain sanitized raw evidence without fallback or retry", () =>
        {
            var handler = new HttpProbe { Response = "{\"error\":\"test-key schema invalid\",\"usage\":{\"input_tokens\":3,\"output_tokens\":0}}", Status = HttpStatusCode.BadRequest }; using var client = new HttpClient(handler);
            try { new DeepSeekPlanSource(client, "deepseek-chat", "test-key").GenerateAsync(new("", "", "{}", PlannerResponseSchema.Create(runtime.Capabilities)), default).GetAwaiter().GetResult(); throw new Exception("HTTP error ignored."); }
            catch (PlannerException e) { Program.Check(e.Code == "PLANNER_PROVIDER_ERROR" && e.ProviderJson!.Contains("[REDACTED]", StringComparison.Ordinal) && !e.ProviderJson.Contains("test-key", StringComparison.Ordinal) && handler.Calls == 1 && e.InputTokens == 3 && e.OutputTokens == 0, "Raw provider error leaked, usage lost or retried."); }
        });
        Test("incomplete provider response retains usage and fails closed", () =>
        {
            var handler = new HttpProbe { Response = "{\"status\":\"incomplete\",\"usage\":{\"input_tokens\":5,\"output_tokens\":8}}" }; using var client = new HttpClient(handler);
            try { new DeepSeekPlanSource(client, "deepseek-chat", "test-key").GenerateAsync(new("", "", "{}", PlannerResponseSchema.Create(runtime.Capabilities)), default).GetAwaiter().GetResult(); throw new Exception("Incomplete accepted."); }
            catch (PlannerException e) { Program.Check(e.InputTokens == 5 && e.OutputTokens == 8 && e.ProviderJson == handler.Response, "Failed usage lost."); }
        });
        Test("strict envelope rejects malformed replies and preserves usage/stage", () =>
        {
            var result = new CadPlanner(runtime, new ReplySource { Reply = "{}" }).PlanAsync("intent").GetAwaiter().GetResult();
            Program.Check(result.FailureStage == "strict_envelope" && result.InputTokens == 17 && result.OutputTokens == 23, "Envelope failure stage/usage lost.");
            var decision = new StepwisePlanner(new ReplySource { Reply = "{}" }).DecideAsync("intent", new SolidWorksStepwiseRuntime()).GetAwaiter().GetResult();
            Program.Check(decision.FailureStage == "strict_envelope", "Stepwise envelope failure stage lost.");
        });
        Test("identifier and relation failures remain local contract rejections", () =>
        {
            var fixture = new VerifiedObservation(root, "G2");
            var invalid = Envelope(fixture.Oracle).Replace("\"id\": \"base\"", "\"id\": \"extrude1\"", StringComparison.Ordinal);
            var result = new CadPlanner(runtime, new ReplySource { Reply = invalid }).PlanAsync("intent").GetAwaiter().GetResult();
            Program.Check(!result.Succeeded && result.FailureStage == "cad_program_parse", "Identifier rule bypassed.");
            var wrong = fixture.Oracle with { Relations = fixture.Oracle.Relations.Select(r => r.Kind == RelationKind.CenteredAbout ? r with { Reference = "stock.direction_x" } : r).ToArray() };
            result = new CadPlanner(runtime, new ReplySource { Reply = Envelope(wrong) }).PlanAsync("intent").GetAwaiter().GetResult();
            Program.Check(!result.Succeeded && result.FailureStage == "pure_preflight", "Relation contract bypassed.");
        });
        foreach (var task in BenchmarkData.Tasks()) Test(task.Name + " committed prefix remaps arbitrary IDs without exposing future operations", () =>
        {
            var fixture = new VerifiedObservation(root, task.Name); var codec = new CadProgramJson();
            var raw = codec.Serialize(fixture.Oracle);
            foreach (var o in fixture.Oracle.Operations) raw = raw.Replace("\"semanticId\": \"" + o.SemanticId, "\"semanticId\": \"qual_" + o.SemanticId, StringComparison.Ordinal)
                .Replace("\"subject\": \"" + o.SemanticId, "\"subject\": \"qual_" + o.SemanticId, StringComparison.Ordinal)
                .Replace("\"reference\": \"" + o.SemanticId, "\"reference\": \"qual_" + o.SemanticId, StringComparison.Ordinal);
            var actual = codec.Parse(raw).Program!;
            for (var count = 1; count <= actual.Operations.Count; count++)
            {
                var owners = actual.Operations.Take(count).Select(o => o.SemanticId!).ToHashSet();
                var prefix = actual with { Operations = actual.Operations.Take(count).ToArray(), Relations = actual.Relations.Where(r => owners.Contains(r.Subject)).ToArray() };
                var snapshot = fixture.Project(prefix); var observation = new SolidWorksStepwiseRuntime(prefix, snapshot);
                Program.Check(snapshot.Features.Count == count && snapshot.Revision == count && snapshot.Entities.All(e => owners.Contains(e.OwnerFeatureSemanticId)), "Future state leaked.");
                foreach (var future in actual.Operations.Skip(count)) Program.Check(!observation.ModelContextJson.Contains(future.SemanticId!, StringComparison.Ordinal), "Future program leaked.");
                if (count > 1)
                {
                    var old = prefix with { Operations = prefix.Operations.Take(count - 1).ToArray(), Relations = prefix.Relations.Where(r => r.Subject != prefix.Operations.Last().SemanticId).ToArray() };
                    var tracked = new QualifiedRuntime(new SolidWorksStepwiseRuntime(old, fixture.Project(old)), old);
                    var addition = prefix with { Operations = new[] { prefix.Operations.Last() }, Relations = prefix.Relations.Where(r => r.Subject == prefix.Operations.Last().SemanticId).ToArray() };
                    var decision = new StepwisePlanner(new ReplySource { Reply = Envelope(addition) }).DecideAsync(task.Intent, tracked).GetAwaiter().GetResult();
                    Program.Check(decision.Status == StepwiseStatus.Operation && tracked.CapabilityPassed && tracked.PreflightPassed, "Real StepwisePlanner prefix validation failed.");
                }
            }
        });
        Test("pure edit snapshots expose all three requested edits", () =>
        {
            foreach (var task in BenchmarkData.Tasks()) for (var i = 0; i < task.Edits.Length; i++)
            {
                var verified = new VerifiedObservation(root, task.Name, i == 1 ? "thickness-10" : "creation");
                var catalog = SolidWorksPlanningRuntime.ForEditSnapshot(verified.Oracle, verified.State).Capabilities;
                Program.Check(catalog.ParameterEdits.Any(e => e.Parameter == task.Edits[i].Parameter), "Requested edit unavailable.");
            }
        });
        Test("premature complete and missing relations cannot pass the qualification gate", () =>
        {
            var fixture = new VerifiedObservation(root, "G2");
            var prefix = fixture.Oracle with { Operations = fixture.Oracle.Operations.Take(1).ToArray(), Relations = Array.Empty<DesignRelation>() };
            try { fixture.Match(prefix, false); throw new Exception("Early complete accepted."); }
            catch (CadHarness.SolidWorks.Tests.TestFailure) { }
            try { fixture.Match(fixture.Oracle with { Relations = Array.Empty<DesignRelation>() }, false); throw new Exception("Missing relations accepted."); }
            catch (CadHarness.SolidWorks.Tests.TestFailure) { }
        });
        Test("smoke cannot connect before zero-Part gate and is limited to four slots", () =>
        {
            Program.Check(Program.SmokeSchedule().Length == 4 && Program.SmokeSchedule().Select(s => s.Task + s.Mode).Distinct().Count() == 4, "Smoke scope expanded.");
            var path = Path.Combine(root, "artifacts/milestone10d/pure-missing-gate");
            try { NativeSmoke.Run(path, "invalid", null); throw new Exception("Missing gate accepted."); }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }
        });
        tests.AddRange(CompatibilityTests.Cases(root));
        tests.AddRange(StateContractTests.Cases(root));
        tests.AddRange(TypedEquivalenceTests.Cases(root));
        var passed = new List<string>();
        foreach (var (name, test) in tests) { test(); passed.Add(name); Console.WriteLine("PASS " + name); }
        Directory.CreateDirectory(Program.Output(root)); Program.Write(Path.Combine(Program.Output(root), "pure-results.json"), new { Status = "PASS", Tests = passed, PartsCreated = 0 }); return 0;
    }
}
