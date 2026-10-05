using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal sealed record QualificationRow(string Task, string Mode, string Stage, string Status, string? FailureStage = null, string? Code = null, string? Reason = null);

// Makes the merged capability check and pure preflight separately observable,
// without changing either runtime's checks or production capability projection.
internal sealed class QualifiedRuntime : IPlanningRuntime
{
    private readonly IPlanningRuntime runtime;
    private readonly CadProgram? prior;
    internal string? FailureStage;
    internal bool CapabilityPassed, PreflightPassed;
    public RuntimeCapabilityCatalog Capabilities => runtime.Capabilities;
    public string ModelContextJson => runtime.ModelContextJson;
    internal QualifiedRuntime(IPlanningRuntime runtime, CadProgram? prior = null) { this.runtime = runtime; this.prior = prior; }
    public ProgramValidationResult Preflight(CadProgram program)
    {
        FailureStage = "runtime_capability_validation";
        var merged = prior is null ? program : new CadProgram("0.2", prior.Operations.Concat(program.Operations).ToArray(), prior.Relations.Concat(program.Relations).ToArray());
        var capability = Capabilities.Validate(merged); if (!capability.IsValid) return capability;
        CapabilityPassed = true; FailureStage = "pure_preflight";
        var result = runtime.Preflight(program); PreflightPassed = result.IsValid;
        if (result.IsValid) FailureStage = null; return result;
    }
}
internal static class Qualification
{
    internal static string[] HarnessStages(BenchmarkTask t) => new[] { "creation" }.Concat(t.Edits.Select((_, i) => "edit-" + i)).ToArray();
    internal static List<QualificationRow> InitialRows() => BenchmarkData.Tasks().SelectMany(t => HarnessStages(t).Select(s => new QualificationRow(t.Name, "Harness", s, "NOT_RUN"))
        .Append(new QualificationRow(t.Name, "Stepwise", "creation-to-complete", "NOT_RUN"))).ToList();
    internal static bool Run(string root, RecordedSource source, List<QualificationRow> rows)
    {
        var output = Program.Output(root);
        void Save() => Program.Write(Path.Combine(output, "qualification-matrix.json"), rows);
        void Set(QualificationRow row) { var i = rows.FindIndex(r => r.Task == row.Task && r.Mode == row.Mode && r.Stage == row.Stage); rows[i] = row; Save(); }
        foreach (var task in BenchmarkData.Tasks())
        {
            for (var index = -1; index < task.Edits.Length; index++)
            {
                var stage = index < 0 ? "creation" : "edit-" + index; source.Stage = task.Name + "-Harness-" + stage;
                var snapshot = index < 0 ? null : new VerifiedObservation(root, task.Name, index == 1 ? "thickness-10" : "creation");
                var runtime = new QualifiedRuntime(snapshot is null ? SolidWorksPlanningRuntime.ForConstruction() : SolidWorksPlanningRuntime.ForEditSnapshot(snapshot.Oracle, snapshot.State));
                var intent = index < 0 ? task.Intent : task.Edits[index].Intent;
                var result = new CadPlanner(runtime, source).PlanAsync(intent).GetAwaiter().GetResult();
                Program.Write(Path.Combine(output, source.Stage + "-decision.json"), result);
                var failedStage = result.FailureStage;
                var code = result.FailureCode; var reason = result.Message;
                var ok = result.Succeeded;
                if (ok) try
                {
                    if (index < 0) new VerifiedObservation(root, task.Name).Match(result.Program!, false);
                    else
                    {
                        var edit = result.Program!.Operations.Single(); var requested = task.Edits[index];
                        var ownerKind = requested.Parameter == EditableParameter.ExtrusionDepth ? OperationKind.CreateExtrude : OperationKind.CreateThroughHole;
                        Program.Check(edit.Input("target")!.References[0].SemanticId == snapshot!.Oracle.Operations.Single(o => o.Kind == ownerKind).SemanticId &&
                            edit.Parameter<ParameterNameParameter>("parameter").Value == requested.Parameter &&
                            edit.Parameter<EditValueParameter>("value").Value is LengthParameter v && v.Millimeters == requested.Value, "Requested edit differs.");
                    }
                }
                catch (Exception e) { ok = false; failedStage = "task_target_validation"; code = "QUALIFICATION_TARGET_MISMATCH"; reason = e.Message; }
                Program.Write(Path.Combine(output, source.Stage + "-boundaries.json"), new { ProviderStructuredOutput = result.FailureStage != "provider_structured_output", StrictEnvelope = result.Succeeded || result.FailureStage is "cad_program_parse" or "runtime_capability_validation" or "pure_preflight", CadProgramParse = result.Succeeded || result.FailureStage is "runtime_capability_validation" or "pure_preflight", runtime.CapabilityPassed, runtime.PreflightPassed, FailureStage = failedStage, RejectionReason = ok ? null : reason });
                Set(new(task.Name, "Harness", stage, ok ? "PASS" : "FAIL", ok ? null : failedStage, ok ? null : code, ok ? null : reason));
                if (!ok) return false;
            }
        }
        foreach (var task in BenchmarkData.Tasks())
        {
            var oracle = new VerifiedObservation(root, task.Name); CadProgram? committed = null; CadState? state = null;
            for (var number = 0; number < StepwisePlanner.MaximumDecisions; number++)
            {
                source.Stage = task.Name + "-Stepwise-decision-" + number;
                var runtime = new QualifiedRuntime(new SolidWorksStepwiseRuntime(committed, state), committed);
                Program.Write(Path.Combine(output, source.Stage + "-observation.json"), JsonSerializer.Deserialize<JsonElement>(runtime.ModelContextJson));
                var decision = new StepwisePlanner(source).DecideAsync(task.Intent, runtime).GetAwaiter().GetResult();
                Program.Write(Path.Combine(output, source.Stage + "-decision.json"), decision);
                var ok = decision.Status is StepwiseStatus.Operation or StepwiseStatus.Complete;
                var failureStage = runtime.FailureStage ?? decision.FailureStage; var reason = decision.Message; var code = decision.FailureCode;
                try
                {
                    if (decision.Status == StepwiseStatus.Operation)
                    {
                        var merged = committed is null ? decision.Program! : new CadProgram("0.2", committed.Operations.Concat(decision.Program!.Operations).ToArray(), committed.Relations.Concat(decision.Program.Relations).ToArray());
                        committed = new DesignRelationEngine().Solve(merged).Program;
                        state = oracle.Project(committed);
                        _ = new SolidWorksStepwiseRuntime(committed, state);
                        Program.Write(Path.Combine(output, source.Stage + "-committed-snapshot.json"), new { Provenance = "M9G historical simulation, not live native state", Program = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(committed)), State = state });
                    }
                    else if (decision.Status == StepwiseStatus.Complete) { Program.Check(committed is not null, "Premature complete."); oracle.Match(committed!, false); }
                }
                catch (Exception e) { ok = false; failureStage = "committed_observation_target_validation"; code = "QUALIFICATION_TARGET_MISMATCH"; reason = e.Message; }
                Program.Write(Path.Combine(output, source.Stage + "-boundaries.json"), new { Status = ok ? "PASS" : "FAIL", ProviderStructuredOutput = decision.FailureStage != "provider_structured_output", StrictEnvelope = decision.Status is StepwiseStatus.Operation or StepwiseStatus.Complete || decision.FailureStage != "strict_envelope" && decision.FailureStage != "provider_structured_output", CadProgramParse = decision.Status == StepwiseStatus.Complete ? "N/A-complete-envelope" : decision.Program is null ? "NOT_PASSED" : "PASS", runtime.CapabilityPassed, runtime.PreflightPassed, FailureStage = ok ? null : failureStage, RejectionReason = ok ? null : reason });
                if (!ok) { Set(new(task.Name, "Stepwise", "creation-to-complete", "FAIL", failureStage, code, reason)); return false; }
                if (decision.Status == StepwiseStatus.Complete) { Set(new(task.Name, "Stepwise", "creation-to-complete", "PASS")); break; }
                if (number == StepwisePlanner.MaximumDecisions - 1) { Set(new(task.Name, "Stepwise", "creation-to-complete", "FAIL", "completion", "STEPWISE_DECISION_LIMIT", "No complete decision.")); return false; }
            }
        }
        return rows.All(r => r.Status == "PASS");
    }
}
