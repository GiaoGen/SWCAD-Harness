using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CadHarness.Benchmark.Tests;
internal static class BenchmarkSummary
{
    internal static double? Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(x => x).ToArray();
        return sorted.Length == 0 ? null : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
    internal static double? KnownMedian(IEnumerable<int?> values)
    { var all = values.ToArray(); return all.Any(v => !v.HasValue) ? null : Median(all.Select(v => (double)v!.Value)); }
    internal static object Metrics(RunReport[] runs) => new
    {
        N = runs.Length, LlmCalls = Median(runs.Select(r => (double)r.LlmCalls)),
        InputTokens = KnownMedian(runs.Select(r => r.InputTokens)), OutputTokens = KnownMedian(runs.Select(r => r.OutputTokens)),
        TotalTokens = KnownMedian(runs.Select(r => r.InputTokens + r.OutputTokens)),
        LlmWallMs = Median(runs.Select(r => r.Timing!.LlmWallMs)), RuntimeWallMs = Median(runs.Select(r => r.Timing!.RuntimeWallMs)),
        ValidationWallMs = Median(runs.Select(r => r.Timing!.ValidationWallMs)), RebuildWallMs = Median(runs.Select(r => r.Timing!.RebuildWallMs)),
        ObservationWallMs = Median(runs.Select(r => r.Timing!.ObservationWallMs)), RecoveryWallMs = Median(runs.Select(r => r.Timing!.RecoveryWallMs)),
        RollbackWallMs = Median(runs.Select(r => r.Timing!.RollbackWallMs)), Rebuilds = Median(runs.Select(r => (double)r.Timing!.Rebuilds)),
        Recoveries = Median(runs.Select(r => (double)r.Timing!.Recoveries)), Rollbacks = Median(runs.Select(r => (double)r.Timing!.Rollbacks)),
        TotalWallMs = Median(runs.Select(r => r.TotalWallMs))
    };
    internal static int Run(string root)
    {
        Program.VerifyFreeze(root); var output = Program.Output(root);
        var schedule = BenchmarkData.Schedule(); var reports = new List<RunReport>();
        foreach (var slot in schedule)
        {
            var path = Path.Combine(output, slot.Id, "result.json");
            if (File.Exists(path)) reports.Add(JsonSerializer.Deserialize<RunReport>(File.ReadAllText(path))!);
        }
        var all = reports.ToArray();
        var complete = all.Length == schedule.Length && all.All(r => r.PartsCreated == 1 && r.PartsClosed == 1 && r.OriginalActiveRestored &&
            r.CleanupError is null && r.FrozenFilesUnchanged && r.IntegrityValid && r.Continuable && r.Timing is not null && r.ResourcesBefore?.GdiCount is not null && r.ResourcesAfter?.GdiCount is not null);
        var groups = all.Where(r => !r.Slot.Warmup).GroupBy(r => new { r.Slot.Task, r.Slot.Mode }).Select(g => new
        {
            g.Key.Task, g.Key.Mode, MeasuredRuns = g.Count(), CreationSuccesses = g.Count(r => r.Success), EditableModelSuccesses = g.Count(r => r.EditableModelSuccess),
            TaskSuccesses = g.Count(r => r.Success && r.EditableModelSuccess), CreationSuccessRate = (double)g.Count(r => r.Success) / g.Count(),
            EditableModelSuccessRate = (double)g.Count(r => r.EditableModelSuccess) / g.Count(),
            AllAttemptsMedian = Metrics(g.ToArray()), SuccessfulTasksMedian = Metrics(g.Where(r => r.Success && r.EditableModelSuccess).ToArray()),
            Failures = g.Where(r => !r.Success || !r.EditableModelSuccess).Select(r => new { r.Slot.Id, r.FailureStage, r.FailureCode, r.FailureClass, r.Message }).ToArray()
        }).ToArray();
        var successfulComparison = complete && groups.Length == 4 && groups.All(g => g.TaskSuccesses > 0);
        var summary = new { Status = successfulComparison ? "COMPLETE" : "BLOCKED", CoverageStatus = complete ? "COMPLETE" : "BLOCKED", SuccessfulTaskComparisonAvailable = successfulComparison,
            BenchmarkOnly = true, BaselineCommit = "a2edad2b22f8bd0a53b53ae8c484bda03a5d20b7", Model = "deepseek-chat", Endpoint = "https://api.deepseek.com/responses",
            SharedBackend = "RelationBackend + TransactionalParameterBackend", SharedCorrectnessValidator = "BenchmarkValidator.Validate",
            Schedule = schedule, RunsRecorded = all.Length, WarmupsExcluded = all.Count(r => r.Slot.Warmup), StatisticalMinimumMet = complete,
            PartsCreated = all.Sum(r => r.PartsCreated), PartsClosed = all.Sum(r => r.PartsClosed), MaximumConcurrentOwnedParts = 1,
            MetricDefinition = "Success=validated creation; editable success=all requested edits validated. Successful task requires both. Phase times are exclusive; actual rebuild calls are counted. Total includes connection, Part lifecycle and evidence overhead. Missing usage is null. Warm-ups excluded from all statistics.",
            InferenceLimits = "Two tasks on one SOLIDWORKS instance. No historical regression or efficiency claim beyond this sample. Failure-shortened runs must not support speedup claims. Edit validation is targeted; the common independent full oracle is additionally timed after each edit, so validation totals do not estimate dirty/full speedup.",
            SuccessfulSampleLimitations = groups.Where(g => g.TaskSuccesses < 5).Select(g => new { g.Task, g.Mode, g.TaskSuccesses, Note = "Fewer than five successful measured samples; descriptive statistics only. Failed attempt wall time does not support efficiency claims." }).ToArray(),
            Groups = groups, Runs = all };
        Program.Write(Path.Combine(output, "summary.json"), summary);
        Console.WriteLine(JsonSerializer.Serialize(new { summary.Status, summary.RunsRecorded, summary.WarmupsExcluded, summary.PartsCreated, summary.PartsClosed, Groups = groups }, Program.Json));
        return successfulComparison ? 0 : 1;
    }
}
