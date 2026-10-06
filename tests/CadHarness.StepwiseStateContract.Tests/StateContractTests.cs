using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal sealed class PromptCapture : IStructuredPlanSource
{
    internal readonly string Reply;
    internal PlannerPrompt? Prompt;
    internal int Calls;
    public bool IsModelBacked => true;
    internal PromptCapture(string reply) => Reply = reply;
    public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken cancellationToken)
    { Calls++; Prompt = prompt; return Task.FromResult(new StructuredPlanResponse(Reply, 1, 1)); }
}
internal static class StateContractTests
{
    internal static string Envelope(CadProgram p) => "{\"outcome\":\"planned\",\"program\":" + new CadProgramJson().Serialize(p) + ",\"reason\":\"\"}";
    internal static CadProgram Prefix(CadProgram p, int count)
    {
        var operations = p.Operations.Take(count).ToArray(); var owners = operations.Select(o => o.SemanticId!).ToHashSet();
        return p with { Operations = operations, Relations = p.Relations.Where(r => owners.Contains(r.Subject)).ToArray() };
    }
    internal static CadProgram Addition(CadProgram p, int i) => new("0.2", new[] { p.Operations[i] }, p.Relations.Where(r => r.Subject == p.Operations[i].SemanticId).ToArray());
    internal static IEnumerable<(string, Action)> Cases(string root)
    {
        var verified = new VerifiedObservation(root, "HeldOut"); var prefix = Prefix(verified.Oracle, 2); var state = verified.Project(prefix);
        var runtime = new SolidWorksStepwiseRuntime(prefix, state);
        StepwiseDecision Decide(CadProgram addition, SolidWorksStepwiseRuntime? custom = null) => new StepwisePlanner(new PromptCapture(Envelope(addition))).DecideAsync("Mechanical intent", custom ?? runtime).GetAwaiter().GetResult();
        yield return ("committed operation/semantic IDs, kinds, healthy outputs and revision are explicit", () =>
        {
            using var doc = JsonDocument.Parse(runtime.ModelContextJson); var observation = doc.RootElement;
            Program.Check(observation.GetProperty("committedOperationIds").EnumerateArray().Select(e => e.GetString()).SequenceEqual(prefix.Operations.Select(o => o.Id)), "Operation IDs missing.");
            var occupied = state.Features.Select(f => f.SemanticId).Concat(state.Entities.Select(e => e.SemanticId)).Concat(state.Parameters.Select(p => p.SemanticId)).ToHashSet();
            Program.Check(observation.GetProperty("committedSemanticIds").EnumerateArray().Select(e => e.GetString()!).ToHashSet().SetEquals(occupied), "Semantic IDs missing.");
            Program.Check(observation.GetProperty("committedOperations").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).SequenceEqual(prefix.Operations.Select(o => WireNames.Of(o.Kind))) && observation.GetProperty("revision").GetInt64() == state.Revision, "Kinds/revision missing.");
            Program.Check(observation.GetProperty("healthySemanticOutputs").GetArrayLength() == state.Entities.Count, "Healthy outputs missing.");
            var source = new PromptCapture("{\"outcome\":\"complete\",\"program\":null,\"reason\":\"\"}");
            _ = new StepwisePlanner(source).DecideAsync("Mechanical intent", runtime).GetAwaiter().GetResult();
            Program.Check(source.Prompt!.Instructions.Contains(runtime.ModelContextJson, StringComparison.Ordinal) && source.Prompt.Instructions.Contains(StepwisePlanner.CommittedStateInstructions, StringComparison.Ordinal), "Explicit state contract absent from prompt.");
            var oldObservation = JsonSerializer.Serialize(new { program = observation.GetProperty("program"), revision = observation.GetProperty("revision"), entities = observation.GetProperty("entities") });
            Program.Write(Path.Combine(Program.Output(root), "prompt-contract-diff.json"), new {
                Baseline = "890c493", AddedInstructions = StepwisePlanner.CommittedStateInstructions,
                AddedObservationFields = new[] { "committedOperationIds", "committedSemanticIds", "committedOperations", "healthySemanticOutputs" },
                BeforeObservation = JsonSerializer.Deserialize<JsonElement>(oldObservation), AfterObservation = observation,
                Origin = "All added observation values are derived from this committed prefix and its CADState; no task/oracle/future input to observation constructor.",
                FutureOracleExposed = false, HarnessChanged = false });
        });
        yield return ("local merged validation rejects a duplicate operation ID without renaming", () =>
        {
            var addition = Addition(verified.Oracle, 2); var duplicate = addition with { Operations = new[] { addition.Operations[0] with { Id = prefix.Operations[0].Id } } };
            var decision = Decide(duplicate);
            Program.Check(decision.Status == StepwiseStatus.Rejected && decision.Message.Contains("Duplicate operation ID", StringComparison.Ordinal) && duplicate.Operations[0].Id == prefix.Operations[0].Id, "Duplicate ID repaired or accepted.");
        });
        yield return ("local merged validation rejects a duplicate semantic ID without repair", () =>
        {
            var addition = Addition(verified.Oracle, 1); var duplicate = addition with { Operations = new[] { addition.Operations[0] with { Id = "another_id" } }, Relations = Array.Empty<DesignRelation>() };
            var decision = Decide(duplicate);
            Program.Check(decision.Status == StepwiseStatus.Rejected && decision.Message.Contains("semantic", StringComparison.OrdinalIgnoreCase), "Duplicate semantic ID accepted.");
        });
        yield return ("committed observation exposes no future operations", () =>
        {
            using var doc = JsonDocument.Parse(runtime.ModelContextJson);
            Program.Check(doc.RootElement.GetProperty("program").GetProperty("operations").GetArrayLength() == 2 && doc.RootElement.GetProperty("committedOperations").GetArrayLength() == 2, "Future operations exposed.");
        });
        yield return ("committed observation exposes no future semantic IDs", () =>
        {
            foreach (var future in verified.Oracle.Operations.Skip(2)) Program.Check(!runtime.ModelContextJson.Contains(future.SemanticId!, StringComparison.Ordinal), "Future semantic ID exposed.");
        });
        yield return ("committed observation exposes no future relations", () =>
        {
            using var doc = JsonDocument.Parse(runtime.ModelContextJson); var codec = new CadProgramJson();
            var observed = codec.Parse(doc.RootElement.GetProperty("program").GetRawText()).Program!;
            Program.Check(observed.Relations.ToHashSet().SetEquals(prefix.Relations) && !observed.Relations.Any(r => verified.Oracle.Relations.Except(prefix.Relations).Contains(r)), "Future relations exposed.");
        });
        yield return ("arbitrary valid model IDs work and existing outputs remain bindable", () =>
        {
            var raw = new CadProgramJson().Serialize(verified.Oracle);
            foreach (var o in verified.Oracle.Operations) raw = raw.Replace("\"semanticId\": \"" + o.SemanticId, "\"semanticId\": \"user_" + o.SemanticId, StringComparison.Ordinal)
                .Replace("\"subject\": \"" + o.SemanticId, "\"subject\": \"user_" + o.SemanticId, StringComparison.Ordinal)
                .Replace("\"reference\": \"" + o.SemanticId, "\"reference\": \"user_" + o.SemanticId, StringComparison.Ordinal)
                .Replace("\"id\": \"" + o.Id + "\"", "\"id\": \"user_" + o.Id + "\"", StringComparison.Ordinal);
            var arbitrary = new CadProgramJson().Parse(raw).Program!; var old = Prefix(arbitrary, 2); var custom = new SolidWorksStepwiseRuntime(old, verified.Project(old));
            var decision = Decide(Addition(arbitrary, 2), custom);
            Program.Check(decision.Status == StepwiseStatus.Operation && decision.Program!.Operations[0].Id == arbitrary.Operations[2].Id, "Arbitrary ID or existing input failed.");
        });
        yield return ("healthy output projection excludes unhealthy entities and owners", () =>
        {
            var changed = state with { Entities = state.Entities.Select(e => e.SemanticId == prefix.Operations[0].SemanticId + ".top_face" ? e with { ReferenceHealth = ReferenceHealth.Stale } : e).ToArray(),
                Features = state.Features.Select(f => f.SemanticId == prefix.Operations[1].SemanticId ? f with { ReferenceHealth = ReferenceHealth.Suppressed } : f).ToArray() };
            using var doc = JsonDocument.Parse(new SolidWorksStepwiseRuntime(prefix, changed).ModelContextJson);
            var outputs = doc.RootElement.GetProperty("healthySemanticOutputs").EnumerateArray().Select(e => e.GetProperty("semanticId").GetString()).ToArray();
            Program.Check(!outputs.Contains(prefix.Operations[0].SemanticId + ".top_face") && !outputs.Contains(prefix.Operations[1].SemanticId), "Unhealthy output advertised as reusable.");
        });
        yield return ("committed definitions cannot be renamed or redefined by an append", () =>
        {
            var redefined = prefix.Operations[1] with { Id = "renamed", Parameters = new Dictionary<string, OperationParameter>(prefix.Operations[1].Parameters) { ["diameterMm"] = new LengthParameter(9) } };
            var decision = Decide(new("0.2", new[] { redefined }, Array.Empty<DesignRelation>()));
            Program.Check(decision.Status == StepwiseStatus.Rejected && runtime.ManagedProgram!.Operations[1].Id == prefix.Operations[1].Id && runtime.ManagedProgram.Operations[1].Parameter<LengthParameter>("diameterMm").Millimeters == 7, "Committed definition changed.");
        });
        yield return ("premature complete still fails the task validator and never creates success", () =>
        {
            var source = new PromptCapture("{\"outcome\":\"complete\",\"program\":null,\"reason\":\"\"}");
            var decision = new StepwisePlanner(source).DecideAsync("Mechanical intent", runtime).GetAwaiter().GetResult();
            Program.Check(decision.Status == StepwiseStatus.Complete && source.Calls == 1, "Completion response altered or retried.");
            try { verified.Match(prefix, false); throw new Exception("Incomplete task accepted."); }
            catch (CadHarness.SolidWorks.Tests.TestFailure) { }
        });
        yield return ("generic state instructions contain no task, next step or future oracle", () =>
        {
            var instructions = StepwisePlanner.CommittedStateInstructions;
            foreach (var forbidden in new[] { "G2", "HeldOut", "pattern", "blind hole", "next step", "stock", "seed", "layout", "pocket" }) Program.Check(!instructions.Contains(forbidden, StringComparison.OrdinalIgnoreCase), "Task-specific guidance added.");
            Program.Check(instructions.Contains("Do not recreate", StringComparison.Ordinal) && instructions.Contains("Do not modify, rename", StringComparison.Ordinal) && instructions.Contains("ENTIRE user intent", StringComparison.Ordinal), "State obligations incomplete.");
        });
        yield return ("Harness prompt stays byte-equivalent to recorded M10B contract and settings are shared", () =>
        {
            var task = BenchmarkData.Tasks().Single(t => t.Name == "G2"); var source = new PromptCapture(Envelope(new VerifiedObservation(root, "G2").Oracle));
            _ = new CadPlanner(SolidWorksPlanningRuntime.ForConstruction(), source).PlanAsync(task.Intent).GetAwaiter().GetResult();
            using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone10b/call-01-request.json")));
            var old = baseline.RootElement;
            Program.Check(source.Prompt!.Instructions == old.GetProperty("Instructions").GetString() && source.Prompt.Intent == old.GetProperty("Intent").GetString() && source.Prompt.CapabilityJson == old.GetProperty("CapabilityJson").GetString() && source.Prompt.ResponseSchemaJson == old.GetProperty("ResponseSchemaJson").GetString(), "Harness information changed.");
            Program.Check(!source.Prompt.Instructions.Contains(StepwisePlanner.CommittedStateInstructions, StringComparison.Ordinal), "Stepwise state instructions added to Harness.");
            // Both native modes construct the same source before branching;
            // captured request tests cover identical model/endpoint/settings.
            Program.Check(DeepSeekPlanSource.Endpoint.ToString() == "https://api.deepseek.com/responses" && DeepSeekPlanSource.Format == "responses/json_schema/strict", "Provider contract changed.");
        });
        yield return ("decision diagnostics count both ID collisions and preserve parse stage", () =>
        {
            var repeated = Addition(verified.Oracle, 1);
            var audit = DecisionAuditor.Analyze(Envelope(repeated), prefix, state, runtime.Capabilities, false, "runtime_capability_validation", "PROGRAM_SCHEMA_INVALID");
            Program.Check(audit.SingleDecisionParsePassed && audit.DuplicateOperationIds.Length == 1 && audit.DuplicateSemanticIds.Length == 1 && audit.RepeatedCommittedFeatureIds.Length == 1 && audit.GenuineStepwisePlanningFailure, "Collision stage/counts lost.");
            var renamed = repeated with { Operations = new[] { repeated.Operations[0] with { Id = "another", SemanticId = "another" } }, Relations = Array.Empty<DesignRelation>() };
            audit = DecisionAuditor.Analyze(Envelope(renamed), prefix, state, runtime.Capabilities, false, "committed_observation_target_validation", "QUALIFICATION_TARGET_MISMATCH");
            Program.Check(audit.DuplicateOperationIds.Length == 0 && audit.RepeatedCommittedFeatureIds.Length == 1 && audit.GenuineStepwisePlanningFailure, "Different-ID recreation hidden.");
        });
    }
}
