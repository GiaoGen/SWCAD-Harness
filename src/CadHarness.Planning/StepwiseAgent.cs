using System;
using System.Threading;

namespace CadHarness.Planning;

public sealed record StepwiseExecution(bool Succeeded, string? FailureCode, string Message);
public sealed record StepwiseRunResult(bool Completed, int Decisions, int OperationsExecuted, string? FailureCode, string Message);

public sealed class StepwiseAgent
{
    private readonly StepwisePlanner planner;
    public StepwiseAgent(IStructuredPlanSource source) => planner = new(source);

    // Synchronous orchestration deliberately preserves the caller's native STA.
    // Observation runs only after a successful execution; no predicted state
    // or future plan can be supplied in place of that observation.
    public StepwiseRunResult Run(string intent, Func<IPlanningRuntime> observe,
        Func<CadHarness.Ir.CadProgram, StepwiseExecution> execute, CancellationToken cancellationToken = default)
    {
        var operations = 0;
        for (var decision = 1; decision <= StepwisePlanner.MaximumDecisions; decision++)
        {
            var next = planner.DecideAsync(intent, observe(), cancellationToken).GetAwaiter().GetResult();
            if (next.Status == StepwiseStatus.Complete)
                return new(true, decision, operations, null, "Model requested completion; caller must apply shared final validator.");
            if (next.Status != StepwiseStatus.Operation) return new(false, decision, operations, next.FailureCode, next.Message);
            var result = execute(next.Program!);
            if (!result.Succeeded) return new(false, decision, operations, result.FailureCode, result.Message);
            operations++;
        }
        return new(false, StepwisePlanner.MaximumDecisions, operations, "STEPWISE_DECISION_LIMIT", "Bounded decision budget exhausted.");
    }
}
