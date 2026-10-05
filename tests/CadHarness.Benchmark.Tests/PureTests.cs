using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal sealed class QueueSource : IStructuredPlanSource
{
    public bool IsModelBacked => true;
    internal readonly Queue<string> Responses = new();
    internal readonly List<PlannerPrompt> Prompts = new();
    public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Prompts.Add(prompt); return Task.FromResult(new StructuredPlanResponse(Responses.Dequeue(), 17, 23)); }
}
internal sealed class MockHttp : HttpMessageHandler
{
    internal string Response = "";
    internal HttpStatusCode Status = HttpStatusCode.OK;
    internal string? Body;
    internal int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    { Calls++; Body = await request.Content!.ReadAsStringAsync(token); return new(Status) { Content = new StringContent(Response) }; }
}
internal sealed class AcceptingRuntime : IPlanningRuntime
{
    public RuntimeCapabilityCatalog Capabilities => SolidWorksPlanningRuntime.ForConstruction().Capabilities;
    public string ModelContextJson => "null";
    public ProgramValidationResult Preflight(CadProgram program) => new(Array.Empty<ValidationIssue>());
}
internal sealed record ProbePreparation() : MutationPreparation(ChangeSet.Empty, FullValidationReason.None);
internal sealed class ProbeStore : ICadStateStore
{
    internal CadState State;
    internal bool FailCommit;
    internal ProbeStore(CadState state) => State = state;
    public CadState Load() => State;
    public void Commit(CadState state) { if (FailCommit) throw new StateException("STATE_COMMIT_FAILED", "Controlled failure."); State = state; }
}
internal sealed class ProbeBackend : IMutationBackend<ProbePreparation, long>
{
    internal long SessionRevision;
    public ProbePreparation ResolveInputs(CadState state, OperationNode operation) => new();
    public void Preflight(CadState state, ProbePreparation prepared) { }
    public long CaptureRollback(CadState state, ProbePreparation prepared) => SessionRevision;
    public ChangeSet Execute(ProbePreparation prepared) => ChangeSet.Empty;
    public bool Rebuild() => ExecutionTelemetry.Rebuild(() => true);
    public void ValidatePostconditions(ProbePreparation prepared) { }
    public bool RecoveryAllowed => false;
    public bool Recover(ProbePreparation prepared) => false;
    public CadState ValidateFinal(CadState state, ProbePreparation prepared, ValidationScope scope) => state with { Revision = state.Revision + 1 };
    public void StageState(ProbePreparation prepared, CadState state) => SessionRevision = state.Revision;
    public void Rollback(long before) => SessionRevision = before;
    public void ValidateRestored(CadState state, long before, ValidationScope scope) => Program.Check(SessionRevision == before, "Probe session was not restored.");
    public void Invalidate() => throw new Exception("Unexpected rollback failure.");
}
internal static class PureTests
{
    internal static string Envelope(CadProgram p) => JsonSerializer.Serialize(new { outcome = "planned", program = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(p)), reason = "" });
    internal static CadProgram Stable(CadProgram p) => p with { Operations = p.Operations.Select(o => o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern ?
        o with { Inputs = o.Inputs.Select(i => i.Name == "seed" ? i : i with { References = i.References.Select(r => r with { Type = SemanticType.ReferenceAxis }).ToArray() }).ToArray() } : o).ToArray() };
    internal static CadState Snapshot(CadProgram p)
    {
        var reference = new NativePersistentReference("AQID");
        var frame = new LocalFrameGeometry(new(0,0,0), new(1,0,0), new(0,1,0), new(0,0,1));
        return new() { SchemaVersion = "0.2", Revision = 1, Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""),
            Features = p.Operations.Select(o => new FeatureNode(o.SemanticId!, o.Kind, reference, ReferenceHealth.Healthy)).ToArray(),
            Entities = p.Operations.SelectMany(o => ProfileOutputs.For(o).Select(e => new SemanticEntityNode(o.SemanticId + e.Suffix, e.Type, o.SemanticId!, reference, ReferenceHealth.Healthy)
            { Geometry = e.Type == SemanticType.LocalFrame ? new(new(0,0,0), Frame: frame) : e.Type == SemanticType.ReferenceAxis ? new(new(0,0,0), new(1,0,0)) : null })).ToArray(),
            Parameters = Array.Empty<ParameterNode>(), Bindings = Array.Empty<ParameterBinding>(), Relations = StateRelationData.Encode(p.Relations),
            Dependencies = StateRelationData.Encode(new DesignRelationEngine().Solve(p).Dependencies.Edges) };
    }
    internal static int Run(string root)
    {
        var tests = new List<(string, Action)>();
        void Add(string name, Action action) => tests.Add((name, action));
        var p = Stable(CaseData.All().Single(c => c.Name == "G2").Program!);
        var extrusion = new CadProgram("0.2", new[] { p.Operations[0] }, Array.Empty<DesignRelation>());
        var firstTwo = new CadProgram("0.2", p.Operations.Take(2).ToArray(), p.Relations.Where(r => r.Kind == RelationKind.HostedOn).ToArray());
        firstTwo = firstTwo with { Operations = new[] { firstTwo.Operations[0], firstTwo.Operations[1] with { Parameters = new Dictionary<string, OperationParameter>
            { ["diameterMm"] = new LengthParameter(6), ["placement"] = new PlacementParameter(new(-30,-15)) } } } };
        var hole = firstTwo with { Operations = new[] { firstTwo.Operations[1] } };
        var pattern = new CadProgram("0.2", new[] { p.Operations[2] }, p.Relations.Where(r => r.Kind != RelationKind.HostedOn).ToArray());
        const string complete = "{\"outcome\":\"complete\",\"program\":null,\"reason\":\"\"}";
        Add("same backend vocabulary and ReferenceAxis contracts", () =>
        {
            var a = SolidWorksPlanningRuntime.ForConstruction().Capabilities; var b = new SolidWorksStepwiseRuntime().Capabilities;
            Program.Check(JsonSerializer.Serialize(a.Registry.Contracts) == JsonSerializer.Serialize(b.Registry.Contracts) && a.Profiles.SequenceEqual(b.Profiles) && a.Relations.SequenceEqual(b.Relations) &&
                a.ProfileOutputs.SelectMany(o => o.Outputs).SequenceEqual(b.ProfileOutputs.SelectMany(o => o.Outputs)), "Stepwise backend vocabulary differs.");
            Program.Check(b.Registry.Contracts.SelectMany(c => c.Inputs).Where(i => i.Role == SemanticRole.PatternDirection).All(i => i.AcceptedTypes.SequenceEqual(new[] { SemanticType.ReferenceAxis })), "Unstable direction leaked.");
        });
        Add("schema exposes one operation and explicit completion only in stepwise", () =>
        {
            var a = PlannerResponseSchema.Create(new SolidWorksStepwiseRuntime().Capabilities, true);
            var b = PlannerResponseSchema.Create(SolidWorksPlanningRuntime.ForConstruction().Capabilities);
            Program.Check(a.Contains("complete", StringComparison.Ordinal) && !b.Contains("complete", StringComparison.Ordinal), "Completion schema invalid.");
            using var doc = JsonDocument.Parse(a);
            Program.Check(doc.RootElement.GetProperty("properties").GetProperty("program").GetProperty("anyOf")[0].GetProperty("properties").GetProperty("operations").GetProperty("maxItems").GetInt32() == 1, "Multi-operation decision advertised.");
        });
        Add("model identifier schemas exactly match existing IR validation", () =>
        {
            using var schema = JsonDocument.Parse(PlannerResponseSchema.Create(SolidWorksPlanningRuntime.ForConstruction().Capabilities));
            var operationSchema = schema.RootElement.GetProperty("properties").GetProperty("program").GetProperty("anyOf")[0]
                .GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("anyOf").EnumerateArray()
                .Single(s => s.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString() == "create_extrude");
            var patternText = operationSchema.GetProperty("properties").GetProperty("id").GetProperty("pattern").GetString()!;
            foreach (var name in new[] { "base", "base_op", "extrude1", "throughHoleSeed", "sldworks", "featureextrusion3", "com", "d1", "base.top_face" })
            {
                var changed = extrusion with { Operations = new[] { extrusion.Operations.Single() with { Id = name } } };
                Program.Check(System.Text.RegularExpressions.Regex.IsMatch(name, patternText) == new ProgramValidator().Validate(changed).IsValid, "Schema/parser identifier rules diverged: " + name);
            }
            Program.Check(System.Text.RegularExpressions.Regex.IsMatch("plate.direction_x", Identifiers.SchemaPattern(true)) &&
                !System.Text.RegularExpressions.Regex.IsMatch("plate.direction_x", Identifiers.SchemaPattern(false)), "Reference and root ID patterns confused.");
        });
        Add("relation projection describes existing handler reference semantics", () =>
        {
            using var json = JsonDocument.Parse(SolidWorksPlanningRuntime.ForConstruction().Capabilities.ToPromptJson());
            var contracts = json.RootElement.GetProperty("relationContracts").EnumerateArray().ToDictionary(c => c.GetProperty("kind").GetString()!);
            Program.Check(contracts.Count == json.RootElement.GetProperty("relations").GetArrayLength() &&
                contracts["centered_about"].GetProperty("referenceType").GetString() == "local_frame" &&
                contracts["equal_spacing"].GetProperty("referenceType").GetString() == "feature_ref" &&
                contracts["pattern_seed"].GetProperty("referenceType").GetString() == "feature_ref", "Generic relation contract mismatches backend.");
            var source = new QueueSource(); source.Responses.Enqueue(Envelope(extrusion));
            var harness = new CadPlanner(SolidWorksPlanningRuntime.ForConstruction(), source).PlanAsync("target").GetAwaiter().GetResult();
            source.Responses.Enqueue(Envelope(extrusion)); var stepwise = new StepwisePlanner(source).DecideAsync("target", new SolidWorksStepwiseRuntime()).GetAwaiter().GetResult();
            Program.Check(harness.Succeeded && stepwise.Status == StepwiseStatus.Operation && source.Prompts.All(q => q.Instructions.Contains(PlannerResponseSchema.IdentifierInstructions, StringComparison.Ordinal) && q.CapabilityJson.Contains("relationContracts", StringComparison.Ordinal)),
                "Modes do not share strict identifier/relation instructions.");
        });
        Add("stepwise execution precedes next real observation", () =>
        {
            var source = new QueueSource(); foreach (var json in new[] { Envelope(extrusion), Envelope(hole), Envelope(pattern), complete }) source.Responses.Enqueue(json);
            CadProgram? current = null; var observations = 0; var executions = 0;
            var result = new StepwiseAgent(source).Run("Create target", () => { observations++; return new SolidWorksStepwiseRuntime(current, current is null ? null : Snapshot(current)); },
                addition => { executions++; current = current is null ? addition : new("0.2", current.Operations.Concat(addition.Operations).ToArray(), current.Relations.Concat(addition.Relations).ToArray()); return new(true, null, "executed"); });
            Program.Check(result.Completed && result.Decisions == 4 && result.OperationsExecuted == 3 && observations == 4 && executions == 3, "Decisions were sliced or observations skipped.");
            Program.Check(source.Prompts[0].Instructions.Contains("\"program\":null", StringComparison.Ordinal) && source.Prompts[1].Instructions.Contains("stock.top_face", StringComparison.Ordinal), "Current observation missing.");
        });
        Add("multi-operation decision rejected before execution", () =>
        {
            var source = new QueueSource(); source.Responses.Enqueue(Envelope(p)); var execute = 0;
            var result = new StepwiseAgent(source).Run("target", () => new SolidWorksStepwiseRuntime(), _ => { execute++; return new(true, null, ""); });
            Program.Check(!result.Completed && execute == 0 && source.Prompts.Count == 1, "Multiple operations executed.");
        });
        Add("append uses Binder and rejects unhealthy references", () =>
        {
            var state = Snapshot(extrusion); var healthy = new SolidWorksStepwiseRuntime(extrusion, state);
            Program.Check(healthy.Preflight(hole).IsValid, "Valid native append contract rejected.");
            var unhealthy = state with { Entities = state.Entities.Select(e => e.SemanticId.EndsWith(".top_face", StringComparison.Ordinal) ? e with { ReferenceHealth = ReferenceHealth.Stale } : e).ToArray() };
            Program.Check(!new SolidWorksStepwiseRuntime(extrusion, unhealthy).Preflight(hole).IsValid, "Stale input accepted.");
        });
        Add("centered append cannot silently move an existing seed", () =>
        {
            Program.Check(new SolidWorksStepwiseRuntime(firstTwo, Snapshot(firstTwo)).Preflight(pattern).IsValid, "Legal centered pattern rejected.");
            var wrong = firstTwo with { Operations = firstTwo.Operations.Select(o => o.Kind == OperationKind.CreateThroughHole ? p.Operations[1] : o).ToArray() };
            Program.Check(!new SolidWorksStepwiseRuntime(wrong, Snapshot(wrong)).Preflight(pattern).IsValid, "Existing placement was changed implicitly.");
        });
        foreach (var invalid in new[] { "{\"outcome\":\"complete\",\"program\":null,\"reason\":\"\",\"extra\":1}",
            "{\"outcome\":\"complete\",\"outcome\":\"complete\",\"program\":null,\"reason\":\"\"}" })
            Add("strict envelope rejects extra/duplicate keys " + tests.Count, () =>
            { var source = new QueueSource(); source.Responses.Enqueue(invalid); Program.Check(new StepwisePlanner(source).DecideAsync("target", new SolidWorksStepwiseRuntime()).GetAwaiter().GetResult().Status == StepwiseStatus.Rejected, "Malformed envelope accepted."); });
        Add("native failure stops the agent without recovery or retry", () =>
        {
            var source = new QueueSource(); source.Responses.Enqueue(Envelope(extrusion)); source.Responses.Enqueue(complete);
            var result = new StepwiseAgent(source).Run("target", () => new SolidWorksStepwiseRuntime(), _ => new(false, "NATIVE_FAILURE", "failed"));
            Program.Check(!result.Completed && result.Decisions == 1 && source.Prompts.Count == 1 && result.FailureCode == "NATIVE_FAILURE", "Failure triggered hidden decisions.");
        });
        Add("explicit decision limit and cancellation stop without hidden calls", () =>
        {
            var source = new QueueSource(); for (var i = 0; i < StepwisePlanner.MaximumDecisions; i++) source.Responses.Enqueue(Envelope(extrusion));
            var result = new StepwiseAgent(source).Run("target", () => new AcceptingRuntime(), _ => new(true, null, ""));
            Program.Check(result.FailureCode == "STEPWISE_DECISION_LIMIT" && source.Prompts.Count == StepwisePlanner.MaximumDecisions, "Decision limit ignored.");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var cancelled = new QueueSource();
            Program.Check(new StepwisePlanner(cancelled).DecideAsync("target", new SolidWorksStepwiseRuntime(), cancellation.Token).GetAwaiter().GetResult().Status == StepwiseStatus.Cancelled && cancelled.Prompts.Count == 0, "Cancellation called provider.");
        });
        Add("telemetry records actual rebuild/recovery/rollback including failures", () =>
        {
            using var t = ExecutionTelemetry.Start();
            using (ExecutionTelemetry.Measure(ExecutionPhase.Runtime))
            {
                Program.Check(!ExecutionTelemetry.Rebuild(() => false), "Failed rebuild changed.");
                try { ExecutionTelemetry.Rebuild(() => throw new InvalidOperationException()); } catch (InvalidOperationException) { }
                using (ExecutionTelemetry.Measure(ExecutionPhase.Validation)) { ExecutionTelemetry.Recover(() => true); }
                ExecutionTelemetry.Rollback(() => { });
            }
            var read = t.Snapshot(); Program.Check(read.Rebuilds == 2 && read.Recoveries == 1 && read.Rollbacks == 1 && read.RuntimeWallMs >= 0 && read.ValidationWallMs >= 0, "Telemetry lost actual calls.");
            using (var nested = ExecutionTelemetry.Start()) { ExecutionTelemetry.Rebuild(() => true); Program.Check(nested.Snapshot().Rebuilds == 1, "Nested collector invalid."); }
            Program.Check(t.Snapshot().Rebuilds == 2, "Collector leaked.");
        });
        Add("instrumented transaction preserves atomic commit and rollback flags", () =>
        {
            var initial = Snapshot(extrusion); var store = new ProbeStore(initial); var backend = new ProbeBackend { SessionRevision = initial.Revision };
            using var metrics = ExecutionTelemetry.Start();
            using (ExecutionTelemetry.Measure(ExecutionPhase.Runtime))
            {
                var good = new MutationTransaction<ProbePreparation, long>(store, backend).Execute(extrusion.Operations.Single());
                Program.Check(good.Succeeded && good.StateCommitted && !good.RollbackAttempted && store.State.Revision == initial.Revision + 1, "Commit behavior changed.");
                var committed = store.State; store.FailCommit = true;
                var failed = new MutationTransaction<ProbePreparation, long>(store, backend).Execute(extrusion.Operations.Single());
                Program.Check(!failed.Succeeded && failed.MutationStarted && failed.RollbackAttempted && failed.RollbackSucceeded && !failed.StateCommitted &&
                    failed.FailureCode == "STATE_COMMIT_FAILED" && store.State == committed && backend.SessionRevision == committed.Revision, "Atomic rollback behavior changed.");
            }
            Program.Check(metrics.Snapshot().Rebuilds == 3 && metrics.Snapshot().Rollbacks == 1 && metrics.Snapshot().Recoveries == 0, "Transaction telemetry miscounted actual calls.");
        });
        Add("frozen schedule has independent warm-up and five measured per pair", () =>
        {
            var schedule = BenchmarkData.Schedule(); Program.Check(schedule.Length == 24 && schedule.Select(s => s.Id).Distinct().Count() == 24, "Budget differs.");
            foreach (var group in schedule.GroupBy(s => new { s.Task, s.Mode })) Program.Check(group.Count(s => s.Warmup) == 1 && group.Count(s => !s.Warmup) == 5, "Statistical minimum differs.");
            Program.Check(schedule.Take(4).All(s => s.Warmup), "Measurement started before warm-ups.");
        });
        Add("statistics retain missing usage and use medians", () =>
        { Program.Check(BenchmarkSummary.Median(new[] { 2.0, 100.0, 1.0, 3.0, 4.0 }) == 3 && BenchmarkSummary.Median(Array.Empty<double>()) is null && BenchmarkSummary.KnownMedian(new int?[] { 1, null }) is null, "Statistics fabricated missing data."); });
        Add("independent final edit oracles preserve blind geometry", () =>
        {
            var held = BenchmarkData.Tasks().Single(t => t.Name == "HeldOut"); var after = BenchmarkData.AfterEdit(held.Initial, held.Edits.Single());
            Program.Check(after.Holes.Single(h => h.Bottom > 0) == held.Initial.Holes.Single(h => h.Bottom > 0) && after.Holes.Count(h => h.Diameter == 9) == 3, "Blind hole changed.");
            Program.Check(Math.Abs(after.Volume - 159209.79449074305) < 0.001, "Held-out oracle differs.");
        });
        Add("shared chat adapter transmits same schema and actual usage", () =>
        {
            var handler = new MockHttp { Response = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = complete } } }, usage = new { prompt_tokens = 101, completion_tokens = 29 } }) };
            using var client = new HttpClient(handler); var adapter = new ChatCompletionPlanSource(client, new("test-model", "test-key", new Uri("https://example.invalid/chat/completions")));
            var response = adapter.GenerateAsync(new("Instructions", "target", "{}", "{\"type\":\"object\"}"), default).GetAwaiter().GetResult();
            using var request = JsonDocument.Parse(handler.Body!);
            Program.Check(response.InputTokens == 101 && response.OutputTokens == 29 && response.ProviderJson == handler.Response && handler.Calls == 1 && request.RootElement.GetProperty("temperature").GetInt32() == 0,
                "Usage/raw response or deterministic shared settings lost.");
        });
        Add("provider missing usage stays null and truncated responses retain evidence", () =>
        {
            var raw = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = complete } } } });
            var handler = new MockHttp { Response = raw }; using var client = new HttpClient(handler);
            var source = new ChatCompletionPlanSource(client, new("model", "test-key", new Uri("https://example.invalid/chat/completions")));
            var response = source.GenerateAsync(new("", "", "{}", "{}"), default).GetAwaiter().GetResult();
            Program.Check(response.InputTokens is null && response.OutputTokens is null, "Missing usage fabricated.");
            handler.Response = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "length", message = new { role = "assistant", content = "partial" } } }, usage = new { prompt_tokens = 2, completion_tokens = 9 } });
            try { source.GenerateAsync(new("", "", "{}", "{}"), default).GetAwaiter().GetResult(); throw new Exception("Partial response accepted."); }
            catch (PlannerException e) { Program.Check(e.Code == "PLANNER_RESPONSE_INCOMPLETE" && e.InputTokens == 2 && e.OutputTokens == 9 && e.ProviderJson == handler.Response, "Failed response evidence lost."); }
        });
        Add("provider failure has no retry and no secret error body", () =>
        {
            var handler = new MockHttp { Response = "sensitive-provider-body", Status = HttpStatusCode.Unauthorized };
            using var client = new HttpClient(handler); var adapter = new ChatCompletionPlanSource(client, new("model", "test-key", new Uri("https://example.invalid/chat/completions")));
            try { adapter.GenerateAsync(new("", "", "{}", "{}"), default).GetAwaiter().GetResult(); throw new Exception("Failure ignored."); }
            catch (PlannerException e) { Program.Check(e.Code == "PLANNER_PROVIDER_ERROR" && !e.Message.Contains("sensitive", StringComparison.Ordinal) && handler.Calls == 1, "Error leaked or retried."); }
        });
        Add("recording happens before envelope parse and preserves token usage", () =>
        {
            var dir = Path.Combine(Program.Output(root), "pure-recording"); Directory.CreateDirectory(dir);
            var source = new QueueSource(); source.Responses.Enqueue("not JSON"); var recorded = new RecordedSource(source, dir);
            var result = new CadPlanner(SolidWorksPlanningRuntime.ForConstruction(), recorded).PlanAsync("target").GetAwaiter().GetResult();
            Program.Check(!result.Succeeded && recorded.Calls.Count == 1 && recorded.InputTokens == 17 && recorded.OutputTokens == 23 && File.Exists(Path.Combine(dir, "call-01-response.json")), "Parse failure usage lost.");
        });
        var passed = new List<string>();
        foreach (var (name, action) in tests) { action(); passed.Add(name); Console.WriteLine("PASS " + name); }
        Directory.CreateDirectory(Program.Output(root)); Program.Write(Path.Combine(Program.Output(root), "pure-results.json"), new { Status = "PASS", Tests = passed, PartsCreated = 0 });
        return 0;
    }
}
