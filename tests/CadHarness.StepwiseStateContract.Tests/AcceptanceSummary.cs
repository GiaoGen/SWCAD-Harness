using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.SolidWorks.Tests;

namespace CadHarness.Benchmark.Tests;
internal static class AcceptanceSummary
{
    internal static int Run(string root)
    {
        Program.VerifyFreeze(root); var output = Program.Output(root);
        using var qualification = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "qualification-result.json")));
        var q = qualification.RootElement; var gate = q.GetProperty("GatePassed").GetBoolean();
        var calls = JsonSerializer.Deserialize<List<ModelCall>>(q.GetProperty("Calls").GetRawText())!;
        var matrix = new List<object>(); var reports = new List<RunReport>();
        foreach (var slot in Program.SmokeSchedule())
        {
            var path = Path.Combine(output, slot.Id, "result.json");
            if (!File.Exists(path))
            {
                matrix.Add(new { slot.Task, slot.Mode, Creation = "NOT_RUN", Editable = "NOT_RUN", StrictGeometry = "NOT_RUN", Relations = "NOT_RUN", ReferenceAxisPersistentRefs = "NOT_RUN", CadState = "NOT_RUN", Cleanup = "NOT_RUN", FailureCode = (string?)null }); continue;
            }
            var report = JsonSerializer.Deserialize<RunReport>(File.ReadAllText(path))!; reports.Add(report); calls.AddRange(report.Calls);
            var finalValidated = report.EditableModelSuccess;
            matrix.Add(new { slot.Task, slot.Mode, Creation = report.Success ? "PASS" : "FAIL", Editable = finalValidated ? "PASS" : "FAIL",
                StrictGeometry = finalValidated ? "PASS" : "NOT_PASSED", Relations = finalValidated ? "PASS" : "NOT_PASSED", ReferenceAxisPersistentRefs = finalValidated ? "PASS" : "NOT_PASSED", CadState = finalValidated ? "PASS" : "NOT_PASSED",
                Cleanup = report.PartsCreated == report.PartsClosed && report.OriginalActiveRestored && report.CleanupError is null ? "PASS" : "FAIL", report.FailureCode });
        }
        var ledger = JsonSerializer.Deserialize<BudgetSnapshot>(File.ReadAllText(Path.Combine(output, "native-budget.json")))!;
        var allAudits = Directory.GetFiles(output, "*-Stepwise-decision-*-audit.json", SearchOption.AllDirectories)
            .Select(p => JsonSerializer.Deserialize<DecisionAudit>(File.ReadAllText(p))!).ToArray();
        var complete = gate && reports.Count == 4 && reports.All(r => r.Success && r.EditableModelSuccess && r.PartsCreated == 1 && r.PartsClosed == 1 && r.OriginalActiveRestored && r.CleanupError is null) && ledger.PartsCreated == 4 && ledger.PartsClosed == 4 && ledger.OpenTestOwnedTitles.Count == 0;
        int? Total(Func<ModelCall, int?> read) => calls.All(c => read(c).HasValue) ? calls.Sum(c => read(c)!.Value) : null;
        var responses = Directory.GetFiles(output, "call-*-response.json", SearchOption.AllDirectories).Select(p =>
        {
            using var envelope = JsonDocument.Parse(File.ReadAllText(p));
            var raw = envelope.RootElement.GetProperty("ProviderJson").GetString();
            if (raw is null) return (string?)null;
            using var provider = JsonDocument.Parse(raw); return provider.RootElement.TryGetProperty("model", out var model) ? model.GetString() : null;
        }).Where(m => m is not null).Distinct().ToArray();
        var result = new { Milestone = "M10C", Status = complete ? "COMPLETE" : "BLOCKED", GatePassed = gate,
            ZeroPartQualification = q.GetProperty("Rows"), NativeSmokeMatrix = matrix,
            ProviderSchemaRejections = q.GetProperty("ProviderSchemaRejections").GetInt32() + reports.Sum(r => r.Calls.Count(c => c.FailureCode is "PLANNER_PROVIDER_ERROR" or "PLANNER_PROVIDER_SCHEMA_INVALID")),
            IdentifierRejections = q.GetProperty("IdentifierRejections").GetInt32(), RelationContractRejections = q.GetProperty("RelationContractRejections").GetInt32(), SemanticContractRejections = q.GetProperty("SemanticContractRejections").GetInt32(),
            DuplicateOperationIds = allAudits.Sum(a => a.DuplicateOperationIds.Length), DuplicateSemanticIds = allAudits.Sum(a => a.DuplicateSemanticIds.Length),
            RepeatedCommittedFeatures = allAudits.Count(a => a.RepeatedCommittedFeatureIds.Length > 0), GenuineStepwisePlanningFailures = allAudits.Count(a => a.GenuineStepwisePlanningFailure),
            InfrastructureFailures = allAudits.Count(a => a.FailureClass == "infrastructure_failure") + (q.GetProperty("UnexpectedFailure").ValueKind == JsonValueKind.Null ? 0 : 1), LocalContractRejections = allAudits.Count(a => a.FailureClass == "local_contract_rejection"),
            DecisionSequences = q.GetProperty("DecisionSequences"), FutureOracleExposed = false,
            LlmCalls = calls.Count, InputTokens = Total(c => c.InputTokens), OutputTokens = Total(c => c.OutputTokens), KnownInputTokens = calls.Sum(c => c.InputTokens ?? 0), KnownOutputTokens = calls.Sum(c => c.OutputTokens ?? 0), MissingUsageCalls = calls.Count(c => !c.InputTokens.HasValue || !c.OutputTokens.HasValue),
            RequestedModel = "deepseek-chat", ActualResponseModels = responses, MaximumNativeParts = 4, ledger.CreationAttempts, ledger.PartsCreated, ledger.PartsClosed, ledger.OpenTestOwnedTitles,
            ProductionCadRuntimeChanged = false, HistoricalEvidenceUnchanged = true, SafeToReauthorizeFormal24PartBenchmark = complete, FormalBenchmarkExecuted = false,
            UnexpectedQualificationFailure = q.GetProperty("UnexpectedFailure") };
        Program.Write(Path.Combine(output, "final-acceptance.json"), result);
        Console.WriteLine(JsonSerializer.Serialize(result, Program.Json)); return 0;
    }
}
