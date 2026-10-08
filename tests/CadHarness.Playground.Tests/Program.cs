using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CadHarness.Playground;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CadHarness.Playground.Tests;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static async Task Main(string[] args)
    {
        var fake = new FakeNative(); var provider = new MockProvider();
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        if (args.Contains("--serve")) port = 5187;
        await using var app = PlaygroundHost.Build([], fake, provider, port);
        await app.StartAsync();
        if (args.Contains("--serve")) { Console.WriteLine("MOCK ONLY Playground http://127.0.0.1:5187 · no COM / no real LLM"); await app.WaitForShutdownAsync(); return; }
        using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + port) };
        var secret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        async Task<JsonElement> Post(string route, object body)
        {
            var response = await client.PostAsync(route, new StringContent(JsonSerializer.Serialize(body, Wire.Options), Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync(); Check(!json.Contains(secret, StringComparison.Ordinal), "response contains no credential"); return Wire.Json(json);
        }
        async Task<JsonElement> Configure(int timeout = 120) => await Post("/api/llm/configure", new LlmSettings("deepseek", secret, "https://api.deepseek.com/responses", "deepseek-chat", timeout));
        bool Pass(JsonElement r) => r.GetProperty("success").GetBoolean();
        string Code(JsonElement r) => r.GetProperty("failureCode").GetString()!;
        async Task<JsonElement> Plan(bool edit = false, long? revision = null) => await Post(edit ? "/api/edit/plan" : "/api/plan", new IntentRequest("test intent", revision));
        async Task<JsonElement> Preflight(JsonElement p, bool edit = false, long? revision = null) => await Post(edit ? "/api/edit/preflight" : "/api/preflight", new PlanRequest(p.GetProperty("planId").GetString()!, revision));
        ExecuteRequest ExecuteBody(JsonElement p, long? revision = null) => new(p.GetProperty("planId").GetString()!, p.GetProperty("preflightId").GetString()!, true, revision);

        var s = Wire.Json(await client.GetStringAsync("/api/status"));
        Check(!s.GetProperty("details").GetProperty("solidworks").GetProperty("connected").GetBoolean(), "startup never connects COM");
        var noPlan = await Post("/api/execute", new ExecuteRequest("missing", "missing", true)); Check(Code(noPlan) == "STALE_PLAN", "execute before plan rejected");
        Check(Code(await Post("/api/edit/plan", new IntentRequest("edit", 1))) == "NO_LIVE_EDIT_STATE", "edit without live state rejected");
        Check(!Pass(await Post("/api/llm/configure", new LlmSettings("responses", secret, "http://provider.invalid/responses", "model"))), "HTTP endpoint rejected");
        Check(!Pass(await Post("/api/llm/configure", new LlmSettings("responses", secret, "https://provider.invalid/responses?key=credential", "model"))), "endpoint query rejected");
        Check(Pass(await Configure()), "memory configuration accepted");
        var tested = await Post("/api/llm/test", new { }); Check(Pass(tested) && fake.Status.PartsCreated == 0, "connection test only calls provider");
        Check(tested.GetProperty("details").GetProperty("actualModel").GetString() == "mock-response-alias", "actual alias visible");
        Check(Code(await Post("/api/plan", new IntentRequest(""))) == "PLANNER_INPUT_INVALID", "empty intent rejected");
        Check(Code(await Post("/api/plan", new IntentRequest(new string('中', 4000)))) == "PLANNER_INPUT_INVALID", "UTF8 oversized intent rejected");
        var invalid = await client.PostAsync("/api/plan", new StringContent("{invalid", Encoding.UTF8, "application/json"));
        Check(Code(Wire.Json(await invalid.Content.ReadAsStringAsync())) == "REQUEST_INVALID", "invalid JSON uniform error");
        var duplicate = await client.PostAsync("/api/plan", new StringContent("{\"intent\":\"one\",\"intent\":\"two\"}", Encoding.UTF8, "application/json"));
        Check(Code(Wire.Json(await duplicate.Content.ReadAsStringAsync())) == "REQUEST_INVALID", "duplicate request fields rejected");
        var extra = await Post("/api/plan", new { intent = "test", unexpected = true }); Check(Code(extra) == "REQUEST_INVALID", "unknown DTO fields rejected");
        provider.Mode = "error"; Check(Code(await Plan()) == "PLANNER_PROVIDER_ERROR", "provider HTTP error mapped");
        provider.Mode = "invalid"; var reject = await Plan(); Check(Code(reject) == "PLANNER_RESPONSE_INVALID" && reject.GetProperty("stage").GetString() == "strict_envelope", "strict envelope rejection visible");
        provider.Mode = "preflight"; var preRejected = await Plan();
        Check(!Pass(preRejected) && preRejected.GetProperty("stage").GetString() == "pure_preflight", "production relation/layout preflight rejects overlapping holes");
        Check(Code(await Post("/api/execute", new ExecuteRequest("rejected", "missing", true))) == "STALE_PLAN", "failed plan cannot execute");
        provider.Mode = "timeout"; await Configure(1); Check(Code(await Plan()) == "PLANNER_PROVIDER_TIMEOUT", "provider deadline mapped");
        provider.Mode = "wait"; provider.Wait = new();
        var session = app.Services.GetRequiredService<PlaygroundSession>(); using var cancel = new CancellationTokenSource();
        var cancelled = session.Plan(new("cancelled"), false, cancel.Token); await Task.Delay(50); cancel.Cancel();
        Check((await cancelled).FailureCode == "PLANNER_CANCELLED", "planning cancellation does not publish plan");
        provider.Wait = new(); var waiting = Plan(); await Task.Delay(50);
        Check(Code(await Post("/api/solidworks/connect", new { })) == "SESSION_BUSY", "concurrent action rejected without queuing");
        provider.Wait.SetResult(); await waiting; provider.Mode = "valid";
        var p1 = (await Plan()).GetProperty("details"); Check(provider.SchemaChecked, "shared Responses adapter sends current strict schema");
        Check(Code(await Post("/api/execute", new ExecuteRequest(p1.GetProperty("planId").GetString()!, "missing", true))) == "PREFLIGHT_REQUIRED", "execute before explicit preflight rejected");
        var p2 = (await Plan()).GetProperty("details");
        Check(Code(await Post("/api/preflight", new PlanRequest(p1.GetProperty("planId").GetString()!))) == "STALE_PLAN", "replaced plan rejected");
        var ready = (await Preflight(p2)).GetProperty("details"); Check(ready.GetProperty("preflightId").GetString() is not null && fake.Status.PartsCreated == 0, "production pure preflight is COM-free");
        Check(Code(await Post("/api/execute", ExecuteBody(ready))) == "SOLIDWORKS_NOT_CONNECTED", "native requires explicit connection");
        Check(Code(await Post("/api/execute", new { planId = ready.GetProperty("planId").GetString(), preflightId = ready.GetProperty("preflightId").GetString(), confirmed = true, program = new { arbitrary = true } })) == "REQUEST_INVALID", "browser cannot substitute execution IR");
        await Post("/api/solidworks/connect", new { });
        Check(Code(await Post("/api/execute", ExecuteBody(ready) with { Confirmed = false })) == "PREFLIGHT_REQUIRED", "explicit second confirmation required");
        fake.BlockCreation = new(); var execute = Post("/api/execute", ExecuteBody(ready)); await Task.Delay(80);
        Check(Code(await Post("/api/execute", ExecuteBody(ready))) == "SESSION_BUSY", "double native execute rejected immediately");
        fake.BlockCreation.Set(); Check(Pass(await execute), "mock native execution succeeds once"); fake.BlockCreation = null;
        Check(Code(await Post("/api/execute", ExecuteBody(ready))) == "PREFLIGHT_REQUIRED", "consumed approval cannot execute twice");
        var second = (await Preflight((await Plan()).GetProperty("details"))).GetProperty("details");
        Check(Code(await Post("/api/execute", ExecuteBody(second))) == "TEST_RESOURCE_LIMIT" && fake.Status.PartsCreated == 1, "second owned Part blocked");
        Check(Code(await Plan(true, 0)) == "STALE_REVISION", "stale edit revision rejected");
        var ep = (await Plan(true, 1)).GetProperty("details"); fake.RejectPreflight = true;
        Check(!Pass(await Preflight(ep, true, 1)), "failed edit preflight rejects approval");
        Check(Code(await Post("/api/edit/execute", new ExecuteRequest(ep.GetProperty("planId").GetString()!, "invalid", true, 1))) == "PREFLIGHT_REQUIRED", "failed preflight cannot execute");
        fake.RejectPreflight = false;
        ep = (await Preflight((await Plan(true, 1)).GetProperty("details"), true, 1)).GetProperty("details");
        Check(Code(await Post("/api/edit/execute", ExecuteBody(ep, 0))) == "STALE_REVISION", "execute edit wrong revision rejected");
        fake.FailEdit = true; var failedEdit = await Post("/api/edit/execute", ExecuteBody(ep, 1));
        Check(!Pass(failedEdit) && !failedEdit.GetProperty("details").GetProperty("transaction").GetProperty("stateCommitted").GetBoolean(), "failure never fakes commit");
        Check(fake.State?.Revision == 1, "failed edit preserves revision"); fake.FailEdit = false;
        ep = (await Preflight((await Plan(true, 1)).GetProperty("details"), true, 1)).GetProperty("details");
        Check(Pass(await Post("/api/edit/execute", ExecuteBody(ep, 1))) && fake.State?.Revision == 2, "mock edit uses current projection and advances revision");
        var dto = await client.GetStringAsync("/api/state"); Check(dto.Contains("\"revision\": 2", StringComparison.Ordinal) && !dto.Contains("ComObject", StringComparison.Ordinal), "state DTO preserves revision without COM");
        var exported = await Post("/api/session/export", new { }); Check(!exported.GetRawText().Contains(secret, StringComparison.Ordinal), "export does not contain secret");
        var exportedAgain = await Post("/api/session/export", new { });
        Check(exportedAgain.GetRawText().Length < exported.GetRawText().Length + 1000, "repeated export does not recursively embed prior reports");
        await Post("/api/llm/clear", new { }); Check(Code(await Plan()) == "PLANNER_CONFIGURATION_MISSING", "clear removes active credential");
        var afterClear = await client.GetStringAsync("/api/status"); Check(!afterClear.Contains(secret, StringComparison.Ordinal), "retained results safe after clear");
        await Post("/api/part/close", new { }); Check(!fake.Status.PartOpen && fake.Status.PartsClosed == 1, "mock owned lifecycle releases state");
        foreach (var asset in new[] { "/", "/app.js", "/app.css" }) Check((await client.GetAsync(asset)).IsSuccessStatusCode, "local asset available " + asset);
        Check(Code(Wire.Json(await (await client.GetAsync("/api/unknown")).Content.ReadAsStringAsync())) == "API_NOT_FOUND", "unknown API uniform error");
        using var evil = new HttpRequestMessage(HttpMethod.Post, "/api/part/close") { Content = new StringContent("{}", Encoding.UTF8, "application/json") }; evil.Headers.Add("Origin", "https://unrelated.example");
        Check((await client.SendAsync(evil)).StatusCode == HttpStatusCode.Forbidden, "cross-origin native request blocked");
        Console.WriteLine($"{passed} checks PASS. SOLIDWORKS launched: NO. Native Parts created: 0. Mock Parts: {fake.Status.PartsCreated}/{fake.Status.PartsClosed}.");
        await app.StopAsync();
    }
}
