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
internal static class NativeSmoke
{
    private static bool PriorPassed(string root, RunSlot slot)
    {
        var path = Path.Combine(Program.Output(root), slot.Id, "result.json");
        if (!File.Exists(path)) return false;
        var report = JsonSerializer.Deserialize<RunReport>(File.ReadAllText(path))!;
        return report.Success && report.EditableModelSuccess && report.PartsCreated == 1 && report.PartsClosed == 1 && report.OriginalActiveRestored && report.CleanupError is null;
    }
    private static void Require(bool success, string? code, string message)
    { if (!success) throw new StateException(code ?? "BENCHMARK_RUN_FAILED", message); }
    internal static int Run(string root, string id, string? template)
    {
        Program.VerifyGate(root);
        var schedule = Program.SmokeSchedule(); var slot = schedule.Single(s => s.Id == id);
        var output = Path.Combine(Program.Output(root), slot.Id); Directory.CreateDirectory(output);
        Program.Check(!File.Exists(Path.Combine(output, "attempt.json")), "Run already attempted; M10B does not retry.");
        Program.Check(schedule.Take(slot.Order - 1).All(s => PriorPassed(root, s)), "Frozen counterbalanced order must be followed.");
        var key = Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY");
        Program.Check(!string.IsNullOrWhiteSpace(key), "CAD_HARNESS_LLM_API_KEY is required before any native mutation.");
        using var client = OpenAiPlanSource.CreateClient();
        var source = new RecordedSource(new AuditedSource(new DeepSeekPlanSource(client, "deepseek-chat", key!, 8192, 120), output), output);
        var task = BenchmarkData.Tasks().Single(t => t.Name == slot.Task);
        Program.Write(Path.Combine(output, "attempt.json"), new { Slot = slot, StartedUtc = DateTimeOffset.UtcNow });
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM10BTest_";
        using var budget = new NativeTestBudget(Path.Combine(Program.Output(root), "native-budget.json"), "M10B", 4);
        var report = new RunReport { Slot = slot }; var wall = Stopwatch.StartNew();
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
                part = new(connection.Application, budget); var context = part.Create(connection, connection.ResolvePartTemplate(template));
                var store = new AtomicStateStore(Path.Combine(output, "state.json")); store.Commit(context.CaptureConstructionState());
                StepwiseExecution Execute(CadProgram program)
                {
                    using var execution = ExecutionTelemetry.Measure(ExecutionPhase.Runtime);
                    var result = new RelationBackend().Create(context, program, store);
                    Program.Write(Path.Combine(output, $"execution-{report.Stages.Count:D2}.json"), new { Request = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(program)), Result = result });
                    report.Stages.Add(result);
                    return new(result.Succeeded, result.FailureCode, result.Message);
                }
                if (slot.Mode == "Harness")
                {
                    PlanningResult planned;
                    using (ExecutionTelemetry.Measure(ExecutionPhase.Validation))
                        planned = new CadPlanner(SolidWorksPlanningRuntime.ForConstruction(), source).PlanAsync(task.Intent).GetAwaiter().GetResult();
                    Program.Write(Path.Combine(output, "creation-decision.json"), planned);
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
                    StepwiseRunResult result;
                    using (ExecutionTelemetry.Measure(ExecutionPhase.Validation)) result = new StepwiseAgent(source).Run(task.Intent, Observe, Execute);
                    Program.Write(Path.Combine(output, "creation-decision.json"), result);
                    Require(result.Completed, result.FailureCode, result.Message);
                }
                var expected = task.Initial;
                BenchmarkValidator.Validate(context, store, task, expected, output, "creation"); report.Success = true;
                Program.Write(Path.Combine(output, "creation-validation-pass.json"), new { Creation = true, StrictGeometry = true, Relations = true, ReferenceAxisPersistentRefs = true, CadState = true });
                for (var index = 0; index < task.Edits.Length; index++)
                {
                    var edit = task.Edits[index]; source.Stage = "edit-" + index;
                    PlanningResult planned;
                    using (ExecutionTelemetry.Measure(ExecutionPhase.Validation))
                        planned = new CadPlanner(SolidWorksPlanningRuntime.ForEdit(context, store.Load()), source).PlanAsync(edit.Intent).GetAwaiter().GetResult();
                    Require(planned.Succeeded, planned.FailureCode, planned.Message);
                    var backend = new TransactionalParameterBackend(context);
                    var result = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, backend).Execute(planned.Program!.Operations.Single());
                    Program.Write(Path.Combine(output, source.Stage + "-execution.json"), new { Decision = planned, Result = result, ValidationReads = backend.ValidationReads }); report.Stages.Add(result);
                    Require(result.Succeeded && result.StateCommitted, result.FailureCode, result.Message);
                    expected = BenchmarkData.AfterEdit(expected, edit);
                    BenchmarkValidator.Validate(context, store, task, expected, output, source.Stage);
                    Program.Write(Path.Combine(output, source.Stage + "-validation-pass.json"), new { EditableModel = true, StrictGeometry = true, Relations = true, ReferenceAxisPersistentRefs = true, CadState = true });
                }
                report.EditableModelSuccess = true;
            }
            catch (Exception e)
            {
                report.FailureCode = e is ICadFailure f ? f.Code : e is TestFailure test ? test.Code : "BENCHMARK_RUN_FAILED";
                report.Message = e.Message;
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
        Program.Write(Path.Combine(output, "result.json"), report);
        Console.WriteLine(JsonSerializer.Serialize(new { slot.Id, report.Success, report.EditableModelSuccess, report.LlmCalls, report.InputTokens, report.OutputTokens,
            report.Timing, report.TotalWallMs, report.FailureCode, report.Message, report.PartsCreated, report.PartsClosed, report.CleanupError }));
        return report.Success && report.EditableModelSuccess && report.OriginalActiveRestored && report.CleanupError is null && report.ResourcesAfter is { GdiCount: not null } after && NativeResourceGuard.Evaluate(after.Responding, after.GdiCount, after.OpenTestOwnedParts) is null ? 0 : 2;
    }
}
