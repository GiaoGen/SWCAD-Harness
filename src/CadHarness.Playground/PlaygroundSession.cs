using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Playground;

public sealed class PlaygroundSession(INativeSession native, StaDispatcher dispatcher, IProviderFactory providers) : IAsyncDisposable
{
    private readonly SemaphoreSlim admission = new(1, 1);
    private readonly Credentials credentials = new();
    private readonly ConcurrentQueue<SessionEvent> events = new();
    private StoredPlan? plan;
    private NativeStatus status = new();
    private CadState? state;
    private object? lastResult;
    private bool shuttingDown;
    public object Status => new { llm = credentials.View, solidworks = status, busy = admission.CurrentCount == 0,
        plan = plan?.View, lastResult };
    public object State => Wire.StateView(state);
    public object Events => events.ToArray();
    public string Serialize(object value) => credentials.SanitizeJson(JsonSerializer.Serialize(value, Wire.Options));
    public ApiResult Capabilities() => ApiResult.Ok("capability", Wire.Json(SolidWorksPlanningRuntime.ForConstruction().Capabilities.ToPromptJson()));

    // Nonblocking admission rejects overlapping requests, including double-clicks.
    // A cancelled HTTP request cannot detach an already-running native transaction.
    private async Task<ApiResult> Guard(string stage, Func<Task<ApiResult>> action)
    {
        if (shuttingDown || !await admission.WaitAsync(0)) return ApiResult.Fail(stage, "SESSION_BUSY", "An action is already running or the host is closing.");
        try
        {
            var result = await action();
            // Sanitize before retention, so Clear also removes the active secret.
            result = result with { Message = credentials.Sanitize(result.Message) };
            events.Enqueue(new(DateTimeOffset.Now, result.Stage, result.Success, result.FailureCode, result.Message));
            while (events.Count > 500) events.TryDequeue(out _);
            if (result.Stage != "export") lastResult = Wire.Json(Serialize(result));
            return result;
        }
        catch (Exception error)
        {
            var code = error switch { PlannerException p => p.Code, StateException s => s.Code, OperationCanceledException => "REQUEST_CANCELLED",
                _ when stage == "connection" => "SOLIDWORKS_CONNECTION_FAILED", _ when stage == "execution" => "NATIVE_EXECUTION_FAILED",
                _ when stage == "cleanup" => "CLEANUP_FAILED", _ => "PLAYGROUND_ACTION_FAILED" };
            var message = error is PlannerException or StateException ? credentials.Sanitize(error.Message) : "Action failed; no automatic retry. Inspect session status before continuing.";
            // Pull updated ownership flags even if native creation failed early.
            status = native.Status; state = native.State;
            var result = ApiResult.Fail(stage, code, message);
            events.Enqueue(new(DateTimeOffset.Now, stage, false, code, message)); lastResult = result;
            return result;
        }
        finally { admission.Release(); }
    }
    public Task<ApiResult> Configure(LlmSettings input) => Guard("configuration", () =>
    {
        credentials.Configure(input); plan = null;
        return Task.FromResult(ApiResult.Ok("configuration", credentials.View, "Credential held in server memory."));
    });
    public Task<ApiResult> Clear() => Guard("configuration", () =>
    { credentials.Clear(); plan = null; return Task.FromResult(ApiResult.Ok("configuration", credentials.View, "Credential cleared.")); });

    public Task<ApiResult> TestConnection(CancellationToken cancellation) => Guard("provider", async () =>
    {
        // The shared Responses adapter sends the actual current planner schema.
        // Output is discarded: this connectivity action does not publish a plan.
        var runtime = SolidWorksPlanningRuntime.ForConstruction();
        var source = new ObservedSource(credentials.Source(providers));
        var watch = Stopwatch.StartNew();
        var result = await source.GenerateAsync(new("Connectivity test. Return outcome=unsupported, program=null, reason=connectivity test. No CAD action.",
            "Connectivity test only.", runtime.Capabilities.ToPromptJson(), PlannerResponseSchema.Create(runtime.Capabilities)), cancellation);
        return ApiResult.Ok("provider", new { credentials.Settings!.Provider, requestedModel = credentials.Settings.Model,
            actualModel = source.ActualModel, modelCalls = source.IsModelBacked ? 1 : 0, result.InputTokens, result.OutputTokens, wallMs = watch.Elapsed.TotalMilliseconds }, "Responses endpoint connected. No plan published or CAD action performed.");
    });

    public Task<ApiResult> Plan(IntentRequest request, bool edit, CancellationToken cancellation) => Guard("planning", async () =>
    {
        plan = null; // Every attempt invalidates the earlier approval, even on rejection.
        if (string.IsNullOrWhiteSpace(request.Intent) || System.Text.Encoding.UTF8.GetByteCount(request.Intent) > CadPlanner.MaximumIntentBytes)
            return ApiResult.Fail("input", "PLANNER_INPUT_INVALID", "Intent must be nonempty and at most 8192 UTF-8 bytes.");
        IPlanningRuntime runtime;
        if (edit)
        {
            RequireEdit(request.Revision);
            runtime = await dispatcher.Invoke(() => native.EditRuntime());
        }
        else runtime = SolidWorksPlanningRuntime.ForConstruction();
        var source = new ObservedSource(credentials.Source(providers));
        var watch = Stopwatch.StartNew();
        var result = await new CadPlanner(runtime, source).PlanAsync(request.Intent, cancellation);
        var details = new { credentials.Settings!.Provider, requestedModel = credentials.Settings.Model, actualModel = source.ActualModel,
            result.ModelCalls, result.InputTokens, result.OutputTokens, source.ProviderWallMs, planningWallMs = watch.Elapsed.TotalMilliseconds, result.Status,
            result.FailureCode, result.FailureStage, result.Issues };
        if (!result.Succeeded) return ApiResult.Fail(result.FailureStage ?? "planning", result.FailureCode!, result.Message, details);
        var programJson = new CadProgramJson(runtime.Capabilities.Registry).Serialize(result.Program!);
        if (credentials.SanitizeJson(programJson) != programJson)
            return ApiResult.Fail("planning", "SECRET_IN_PLAN_REJECTED", "Provider output contains the configured credential; no plan published.");
        object? editDetails = null;
        if (edit)
        {
            var op = result.Program!.Operations.Single(); var target = op.Input("target")!.References.Single().SemanticId;
            var parameter = op.Parameter<ParameterNameParameter>("parameter").Value;
            var binding = state!.Bindings.Single(b => b.OwnerFeatureSemanticId == target && b.Parameter == parameter);
            editDetails = new { target, parameter, current = state.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value,
                requested = ParameterMutationRegistry.Scalar(op.Parameter<EditValueParameter>("value").Value), binding };
        }
        var view = new PlanView(Guid.NewGuid().ToString("N"), null, edit, edit ? state!.Revision : null, credentials.Sanitize(request.Intent),
            Wire.Json(programJson), Wire.Json(runtime.Capabilities.ToPromptJson()),
            Wire.Json(Serialize(details)), null, editDetails);
        plan = new(view, result.Program!, runtime);
        return ApiResult.Ok("planning", view, result.Message);
    });
    private StoredPlan RequirePlan(string id, bool edit)
    {
        var current = plan;
        if (current is null || current.View.PlanId != id || current.View.Edit != edit)
            throw new StateException("STALE_PLAN", "No matching current successful plan.");
        return current;
    }
    private void RequireEdit(long? revision)
    {
        if (!status.PartOpen || !status.Healthy || state is null) throw new StateException("NO_LIVE_EDIT_STATE", "A healthy owned Part and committed state are required.");
        if (revision != state.Revision) throw new StateException("STALE_REVISION", "Edit revision does not match committed CADState.");
    }
    public Task<ApiResult> Preflight(PlanRequest request, bool edit) => Guard("preflight", async () =>
    {
        var stored = RequirePlan(request.PlanId, edit);
        plan = stored with { View = stored.View with { PreflightId = null } };
        if (edit) { RequireEdit(request.Revision); if (stored.View.Revision != request.Revision) throw new StateException("STALE_REVISION", "Plan revision changed."); }
        var runtime = edit ? await dispatcher.Invoke(() => native.EditRuntime()) : stored.Runtime;
        var watch = Stopwatch.StartNew();
        // This runtime entry uses capability validation, relation solve and
        // RelationBackend.Preflight. No duplicate layout implementation here.
        var check = runtime.Preflight(stored.Program);
        var aggregate = new { passed = check.IsValid, check.Issues, wallMs = watch.Elapsed.TotalMilliseconds, mutationStarted = false,
            message = "Aggregate production capability + relation/layout preflight. Native feasibility remains an execution check." };
        plan = stored with { Runtime = runtime, View = stored.View with { Preflight = aggregate, PreflightId = check.IsValid ? Guid.NewGuid().ToString("N") : null } };
        return check.IsValid ? ApiResult.Ok("preflight", plan.View, "PRECHECK PASSED; no native mutation.") :
            ApiResult.Fail("preflight", check.Issues[0].Code, check.Issues[0].Message, plan.View);
    });
    public Task<ApiResult> Connect() => Guard("connection", async () =>
    { status = await dispatcher.Invoke(() => native.Connect()); return ApiResult.Ok("connection", status, status.ConnectionMode); });
    public Task<ApiResult> Execute(ExecuteRequest request, bool edit) => Guard("execution", async () =>
    {
        var stored = RequirePlan(request.PlanId, edit);
        if (!request.Confirmed || stored.View.PreflightId is null || stored.View.PreflightId != request.PreflightId)
            throw new StateException("PREFLIGHT_REQUIRED", "Explicit confirmation and the current successful preflight are required.");
        if (edit) { RequireEdit(request.Revision); if (stored.View.Revision != request.Revision) throw new StateException("STALE_REVISION", "Plan revision changed."); }
        else if (status.PartOpen) throw new StateException("TEST_RESOURCE_LIMIT", "Close current Playground test Part first.");
        if (!status.Connected) throw new StateException("SOLIDWORKS_NOT_CONNECTED", "Connect SOLIDWORKS explicitly first.");
        // Consume once before dispatch. A repeat request cannot reuse approval.
        plan = stored with { View = stored.View with { PreflightId = null } };
        events.Enqueue(new(DateTimeOffset.Now, "authorization", true, null, edit ? "User confirmed one edit." : "User confirmed one test Part."));
        var result = await dispatcher.Invoke(() => edit ? native.Edit(stored.Program, request.Revision!.Value) : native.Create(stored.Program, request.AutoClose));
        status = result.Status; state = native.State;
        var output = new { result.Transaction, result.Operations, result.Timing, result.Status, state = Wire.StateView(result.State) };
        return result.Succeeded ? ApiResult.Ok("execution", output, result.Message) : ApiResult.Fail(result.Transaction?.Stage ?? "execution", result.FailureCode!, result.Message, output);
    });
    public Task<ApiResult> Close() => Guard("cleanup", async () =>
    { plan = null; status = await dispatcher.Invoke(() => native.Close()); state = native.State; return ApiResult.Ok("cleanup", status, "Owned Part closed/discarded; original active document restored when available."); });
    public Task<ApiResult> Export() => Guard("export", () => Task.FromResult(ApiResult.Ok("export", new
    { exported = DateTimeOffset.Now, status, plan = plan?.View, lastResult, state = Wire.StateView(state), events = events.ToArray() })));
    public async ValueTask DisposeAsync()
    {
        shuttingDown = true; await admission.WaitAsync();
        try { await dispatcher.Invoke(() => { native.Dispose(); return true; }); }
        finally { credentials.Clear(); admission.Release(); dispatcher.Dispose(); }
    }
}
