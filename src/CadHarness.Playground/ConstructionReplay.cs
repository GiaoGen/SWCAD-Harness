using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.State;

namespace CadHarness.Playground;

public static class ConstructionReplay
{
    // Rebinding to a new owned context must preserve the approved provider bytes and compiled meaning.
    public static ConstructionPlanPreview Rebind(ConstructionPlanPreview approved, IConstructionPlanningRuntime runtime, string originalResponse)
    {
        var request = ContractJson.Read<ConstructionPlanRequest>(approved.RequestJson, r => { _ = ConstructionRequirements.Normalize(r); });
        var replay = new ConstructionPlanner(runtime, new ReplayResponse(originalResponse)).PlanAsync(request).GetAwaiter().GetResult();
        var accepted = replay.Preview ?? throw new StateException(replay.FailureCode ?? "PREVIEW_EXPIRED", replay.Message);
        if (accepted.ResponseJson != approved.ResponseJson || accepted.CompilationJson != approved.CompilationJson || accepted.CapabilityJson != approved.CapabilityJson)
            throw new StateException("PREVIEW_EXPIRED", "Native deterministic revalidation differs from approved preview.");
        return accepted;
    }
    private sealed class ReplayResponse(string json) : IStructuredPlanSource
    {
        public bool IsModelBacked => false;
        public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken cancellationToken) => Task.FromResult(new StructuredPlanResponse(json));
    }
}
