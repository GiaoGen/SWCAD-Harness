using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal sealed class RunReport
{
    public required RunSlot Slot { get; init; }
    public bool Success { get; set; }
    public bool EditableModelSuccess { get; set; }
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public string? FailureStage { get; set; }
    public string? FailureClass { get; set; }
    public bool IntegrityValid { get; set; }
    public bool Continuable { get; set; }
    public int LlmCalls { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public ExecutionTiming? Timing { get; set; }
    public double TotalWallMs { get; set; }
    public List<ModelCall> Calls { get; set; } = new();
    public List<object> Stages { get; set; } = new();
    public ResourceSnapshot? ResourcesBefore { get; set; }
    public ResourceSnapshot? ResourcesAfter { get; set; }
    public string? SolidWorksRevision { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public string? CleanupError { get; set; }
    public bool FrozenFilesUnchanged { get; set; }
}
internal static class NativeBenchmark
{
    private static bool PriorSafe(string root, RunSlot slot)
    {
        var path = Path.Combine(Program.Output(root), slot.Id, "result.json");
        if (!File.Exists(path)) return false;
        var report = JsonSerializer.Deserialize<RunReport>(File.ReadAllText(path))!;
        return report.IntegrityValid && report.Continuable;
    }
    private static void Require(bool success, string? code, string message)
    { if (!success) throw new StateException(code ?? "BENCHMARK_RUN_FAILED", message); }
    internal static int Run(string root, string id, string? template)
    {
        Program.VerifyFreeze(root);
        var schedule = BenchmarkData.Schedule(); var slot = schedule.Single(s => s.Id == id);
        var output = Path.Combine(Program.Output(root), slot.Id); Directory.CreateDirectory(output);
        Program.Check(!File.Exists(Path.Combine(output, "attempt.json")), "Run already attempted; M10Formal does not retry.");
        Program.Check(schedule.Take(slot.Order - 1).All(s => PriorSafe(root, s)), "Frozen counterbalanced order must be followed.");
        var key = Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY");
        Program.Check(!string.IsNullOrWhiteSpace(key), "CAD_HARNESS_LLM_API_KEY is required before any native mutation.");
        using var client = OpenAiPlanSource.CreateClient();
        var auditedSource = new AuditedSource(new DeepSeekPlanSource(client, "deepseek-chat", key!, 8192, 120), output);
        var source = new RecordedSource(auditedSource, output);
        var task = BenchmarkData.Tasks().Single(t => t.Name == slot.Task);
        Program.Write(Path.Combine(output, "attempt.json"), new { Slot = slot, StartedUtc = DateTimeOffset.UtcNow });
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM10FormalTest_";
        using var budget = new NativeTestBudget(Path.Combine(Program.Output(root), "native-budget.json"), "M10Formal", BenchmarkData.MaximumParts);
        var report = new RunReport { Slot = slot }; var wall = Stopwatch.StartNew();
        var stage = "native_connection";
        using var metrics = ExecutionTelemetry.Start();
        SolidWorksConnection? connection = null; TestPartScope? part = null;
        using (ExecutionTelemetry.Measure(ExecutionPhase.Runtime))
        {
            try
            {
                connection = SolidWorksConnection.Connect(); report.SolidWorksRevision = connection.Application.RevisionNumber();
                report.ResourcesBefore = NativeResourceGuard.Inspect(connection.Application, budget);
                Program.Check(report.ResourcesBefore.GdiCount.HasValue && NativeResourceGuard.Evaluate(report.ResourcesBefore.Responding, report.ResourcesBefore.GdiCount, report.ResourcesBefore.OpenTestOwnedParts) is null,
                    "TEST_RESOURCE_LIMIT: native resource guard rejected run.");
                stage = "part_creation";
                part = new(connection.Application, budget); var context = part.Create(connection, connection.ResolvePartTemplate(template));
                var store = new AtomicStateStore(Path.Combine(output, "state.json")); store.Commit(context.CaptureConstructionState());
                StepwiseExecution Execute(CadProgram program)
                {
                    stage = "native_construction";
                    using var execution = ExecutionTelemetry.Measure(ExecutionPhase.Runtime);
                    var result = new RelationBackend().Create(context, program, store);
                    Program.Write(Path.Combine(output, $"execution-{report.Stages.Count:D2}.json"), new { Request = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(program)), Result = result });
                    report.Stages.Add(result);
                    return new(result.Succeeded, result.FailureCode, result.Message);
                }
                if (slot.Mode == "Harness")
                {
                    stage = "creation_planning";
                    source.Stage = "creation";
                    PlanningResult planned;
                    using (ExecutionTelemetry.Measure(ExecutionPhase.Validation))
                        planned = new CadPlanner(SolidWorksPlanningRuntime.ForConstruction(), source).PlanAsync(task.Intent).GetAwaiter().GetResult();
                    Program.Write(Path.Combine(output, "creation-decision.json"), planned);
                    if (!planned.Succeeded) stage = planned.FailureStage ?? stage;
                    Require(planned.Succeeded, planned.FailureCode, planned.Message);
                    var result = Execute(planned.Program!); Require(result.Succeeded, result.FailureCode, result.Message);
                }
                else
                {
                    IPlanningRuntime Observe()
                    {
                        using var measured = ExecutionTelemetry.Measure(ExecutionPhase.Observation);
                        var runtime = SolidWorksStepwiseRuntime.ForSession(context, store.Load());
                        Program.Write(Path.Combine(output, $"observation-{report.Stages.Count:D2}.json"), JsonSerializer.Deserialize<JsonElement>(runtime.ModelContextJson));
                        return runtime;
                    }
                    StepwiseRunResult RunDecisions()
                    {
                        var planner = new StepwisePlanner(source); var executed = 0;
                        for (var number = 0; number < StepwisePlanner.MaximumDecisions; number++)
                        {
                            var observed = (SolidWorksStepwiseRuntime)Observe();
                            var prior = observed.ManagedProgram; var before = store.Load();
                            var tracked = new QualifiedRuntime(observed, prior);
                            source.Stage = task.Name + "-Stepwise-decision-" + number;
                            var decision = planner.DecideAsync(task.Intent, tracked).GetAwaiter().GetResult();
                            var accepted = decision.Status is StepwiseStatus.Operation or StepwiseStatus.Complete;
                            var failureStage = tracked.FailureStage ?? decision.FailureStage;
                            stage = accepted ? "creation_planning" : failureStage ?? "creation_planning";
                            Program.Write(Path.Combine(output, source.Stage + "-decision.json"), decision);
                            var audit = DecisionAuditor.Analyze(auditedSource.LastResponse?.Json, prior, before, observed.Capabilities, accepted, failureStage, decision.FailureCode);
                            Program.Write(Path.Combine(output, source.Stage + "-audit.json"), audit);
                            if (decision.Status == StepwiseStatus.Complete) return new(true, number + 1, executed, null, "Completion requires shared final validator.");
                            if (!accepted) return new(false, number + 1, executed, decision.FailureCode, decision.Message);
                            var execution = Execute(decision.Program!);
                            if (!execution.Succeeded) return new(false, number + 1, executed, execution.FailureCode, execution.Message);
                            executed++;
                        }
                        return new(false, StepwisePlanner.MaximumDecisions, executed, "STEPWISE_DECISION_LIMIT", "No complete decision.");
                    }
                    StepwiseRunResult result;
                    using (ExecutionTelemetry.Measure(ExecutionPhase.Validation)) result = RunDecisions();
                    Program.Write(Path.Combine(output, "creation-decision.json"), result);
                    Require(result.Completed, result.FailureCode, result.Message);
                }
                var expected = task.Initial;
                stage = "creation_final_validation";
                BenchmarkValidator.Validate(context, store, task, expected, output, "creation"); report.Success = true;
                Program.Write(Path.Combine(output, "creation-validation-pass.json"), new { Creation = true, StrictGeometry = true, Relations = true, ReferenceAxisPersistentRefs = true, CadState = true });
                for (var index = 0; index < task.Edits.Length; index++)
                {
                    var edit = task.Edits[index]; source.Stage = "edit-" + index;
                    stage = source.Stage + "_planning";
                    PlanningResult planned;
                    using (ExecutionTelemetry.Measure(ExecutionPhase.Validation))
                        planned = new CadPlanner(SolidWorksPlanningRuntime.ForEdit(context, store.Load()), source).PlanAsync(edit.Intent).GetAwaiter().GetResult();
                    Program.Write(Path.Combine(output, source.Stage + "-decision.json"), planned);
                    if (!planned.Succeeded) stage = planned.FailureStage ?? stage;
                    Require(planned.Succeeded, planned.FailureCode, planned.Message);
                    stage = source.Stage + "_native_execution";
                    var backend = new TransactionalParameterBackend(context);
                    var result = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, backend).Execute(planned.Program!.Operations.Single());
                    Program.Write(Path.Combine(output, source.Stage + "-execution.json"), new { Decision = planned, Result = result, ValidationReads = backend.ValidationReads }); report.Stages.Add(result);
                    Require(result.Succeeded && result.StateCommitted, result.FailureCode, result.Message);
                    expected = BenchmarkData.AfterEdit(expected, edit);
                    stage = source.Stage + "_final_validation";
                    BenchmarkValidator.Validate(context, store, task, expected, output, source.Stage);
                    Program.Write(Path.Combine(output, source.Stage + "-validation-pass.json"), new { EditableModel = true, StrictGeometry = true, Relations = true, ReferenceAxisPersistentRefs = true, CadState = true });
                }
                report.EditableModelSuccess = true;
            }
            catch (Exception e)
            {
                report.FailureCode = e is ICadFailure f ? f.Code : e is TestFailure test ? test.Code : "BENCHMARK_RUN_FAILED";
                report.Message = e.Message;
                report.FailureStage = stage;
            }
            finally
            {
                part?.Cleanup(); report.PartsCreated = part?.Created == true ? 1 : 0; report.PartsClosed = part?.Closed == true ? 1 : 0;
                report.OriginalActiveRestored = part?.OriginalActiveRestored == true; report.CleanupError = part?.CleanupError;
                if (connection is not null)
                {
                    try { report.ResourcesAfter = NativeResourceGuard.Inspect(connection.Application, budget); }
                    catch { report.CleanupError = "Post-run resource inspection failed."; }
                    try { connection.Dispose(); } catch { report.CleanupError = "Native connection disposal failed."; }
                }
                if (report.CleanupError is not null) { report.Success = false; report.EditableModelSuccess = false; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            }
        }
        wall.Stop(); report.TotalWallMs = wall.Elapsed.TotalMilliseconds; report.Timing = metrics.Snapshot(); report.Calls = source.Calls;
        report.LlmCalls = source.Calls.Count; report.InputTokens = source.InputTokens; report.OutputTokens = source.OutputTokens;
        Program.VerifyFreeze(root); report.FrozenFilesUnchanged = true;
        report.IntegrityValid = report.PartsCreated == 1 && report.PartsClosed == 1 && report.OriginalActiveRestored && report.CleanupError is null &&
            report.ResourcesAfter is { GdiCount: not null } after && NativeResourceGuard.Evaluate(after.Responding, after.GdiCount, after.OpenTestOwnedParts) is null;
        report.FailureClass = Classify(report.FailureCode, report.FailureStage, report.IntegrityValid);
        report.Continuable = report.IntegrityValid && report.FailureClass != "infrastructure_failure";
        Program.Write(Path.Combine(output, "result.json"), report);
        Console.WriteLine(JsonSerializer.Serialize(new { slot.Id, report.Success, report.EditableModelSuccess, report.LlmCalls, report.InputTokens, report.OutputTokens,
            report.Timing, report.TotalWallMs, report.FailureCode, report.Message, report.PartsCreated, report.PartsClosed, report.CleanupError }));
        return report.Continuable ? 0 : 2;
    }
    internal static string? Classify(string? code, string? stage, bool integrityValid) =>
        !integrityValid ? "experiment_integrity_failure" : code is null ? null :
        code is "PLANNER_TRANSPORT_FAILED" or "PLANNER_CANCELLED" or "PLANNER_PROVIDER_ERROR" or "PLANNER_PROVIDER_SCHEMA_INVALID" or
            "STATE_COMMIT_FAILED" or "ROLLBACK_FAILED" or "BENCHMARK_RUN_FAILED" or "TEST_RESOURCE_LIMIT" or "TEST_CLEANUP_FAILED" ||
            stage is "native_connection" or "part_creation" ? "infrastructure_failure" : "task_attempt_failure";
}
