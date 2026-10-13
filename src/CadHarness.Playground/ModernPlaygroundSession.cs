using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;

namespace CadHarness.Playground;

public sealed partial class PlaygroundSession
{
    private sealed record PendingModern(ModernPreview View, ConstructionPlanPreview? Construction = null,
        IConstructionPlanningRuntime? Runtime = null, string? RawResponse = null,
        ExternalQueryPreview? Batch = null, ScalarPreviewRequest? Scalar = null);
    private PendingModern? modernPending;
    private ModelView? model;
    private IModernNativeSession Modern => native as IModernNativeSession ?? throw new StateException("CAPABILITY_UNAVAILABLE", "Modern adapter not configured.");
    private void InvalidatePlans() { plan = null; modernPending = null; }
    public object? Model => model;
    public ApiResult Relations(string id)
    {
        try { return ApiResult.Ok("relations", ModelViews.Relations(model ?? throw new StateException("NO_MODEL", "Inspect or open a model first."), id)); }
        catch (StateException e) { return ApiResult.Fail("relations", e.Code, e.Message); }
    }
    private void CaptureModern() { model = Modern.Model; status = native.Status; state = native.State; }
    public Task<ApiResult> OpenModel(ModelOpenRequest request) => Guard("inspection", async () =>
    {
        InvalidatePlans();
        if (!request.Confirmed || request.Mode is not ("inspect" or "external_copy" or "external_restore" or "managed_restore") ||
            !Path.IsPathFullyQualified(request.Path) || !string.Equals(Path.GetExtension(request.Path), ".SLDPRT", StringComparison.OrdinalIgnoreCase) ||
            request.Mode.EndsWith("restore", StringComparison.Ordinal) && (request.Root is null || !Path.IsPathFullyQualified(request.Root)))
            return ApiResult.Fail("input", "REQUEST_INVALID", "Explicit confirmation, an absolute .SLDPRT path and a supported intake mode are required; restore also requires its package root.");
        if (!status.Connected) throw new StateException("SOLIDWORKS_NOT_CONNECTED", "Connect explicitly before native intake.");
        if (status.PartOpen) throw new StateException("TEST_RESOURCE_LIMIT", "Close owned Part first.");
        await dispatcher.Invoke(() => Modern.OpenModel(request)); CaptureModern();
        return ApiResult.Ok("inspection", model);
    });
    public Task<ApiResult> PlanConstruction(ConstructionPlanRequest request, CancellationToken cancellation) => Guard("planning", async () =>
    {
        InvalidatePlans();
        if (status.PartOpen && !status.Healthy) throw new StateException("SESSION_INVALID", "Close or recover the quarantined owned model before planning.");
        var runtime = await dispatcher.Invoke(() => Modern.ConstructionRuntime());
        var source = new ObservedSource(credentials.Source(providers));
        var result = await new ConstructionPlanner(runtime, source).PlanAsync(request, cancellation);
        if (result.Preview is not { } preview) return ApiResult.Fail("planning", result.FailureCode ?? "PLAN_REJECTED", result.Message, result);
        var raw = source.ResponseJson!;
        if (credentials.SanitizeJson(raw) != raw || credentials.SanitizeJson(preview.ResponseJson) != preview.ResponseJson || credentials.SanitizeJson(preview.RequestJson) != preview.RequestJson)
            return ApiResult.Fail("planning", "SECRET_IN_PLAN_REJECTED", "Credential-bearing response rejected; no executable preview.");
        var view = new ModernPreview(Guid.NewGuid().ToString("N"), "construction", preview.Sha256, preview.Revision, "plan", false,
            new { response = Wire.Json(preview.ResponseJson), compilation = preview.CompilationJson is null ? (JsonElement?)null : Wire.Json(preview.CompilationJson),
                capabilities = Wire.Json(preview.CapabilityJson), result.ModelCalls, result.Provenance, source.ActualModel, source.ProviderWallMs });
        modernPending = new(view, preview, runtime, raw);
        return ApiResult.Ok("planning", view, result.Message);
    });
    public Task<ApiResult> PreflightConstruction(PlanRequest request) => Guard("preflight", async () =>
    {
        var pending = RequireModern(request.PlanId);
        if (pending.Construction is not { } p) throw new StateException("REQUEST_MODE_MISMATCH", "Construction preview required.");
        modernPending = pending with { View = pending.View with { Executable = false, Stage = "plan" } };
        if (request.Revision != p.Revision) throw new StateException("STALE_REVISION", "Preflight must use the exact construction preview revision.");
        var runtime = model?.Owned == true ? await dispatcher.Invoke(() => Modern.ConstructionRuntime()) : pending.Runtime!;
        if (runtime.ContextIdentity != p.ContextIdentity || runtime.Revision != p.Revision || runtime.CapabilityJson != p.CapabilityJson)
            throw new StateException("PREVIEW_EXPIRED", "Construction session, revision or capability changed.");
        runtime.Preflight(p.Read().Program!);
        modernPending = pending with { View = pending.View with { Stage = "preflight", Executable = true } };
        return ApiResult.Ok("preflight", modernPending.View, "Production preflight passed; native feasibility remains an execution check.");
    });
    public Task<ApiResult> PreviewBatch(BatchPreviewRequest request) => Guard("preflight", async () =>
    {
        InvalidatePlans(); RequireModernEdit(request.Revision);
        var result = await dispatcher.Invoke(() => Modern.PreviewBatch(request)); CaptureModern();
        if (result.Preview is not { } p) return ApiResult.Fail("preflight", result.FailureCode ?? "QUERY_REJECTED", result.Message,
            new { result.Outcome, result.Matches, executable = false });
        var view = new ModernPreview(Guid.NewGuid().ToString("N"), "batch", p.Sha256, request.Revision, "preflight", true, p.Snapshot());
        modernPending = new(view, Batch: p); return ApiResult.Ok("preflight", view, result.Message);
    });
    public Task<ApiResult> PreviewScalar(ScalarPreviewRequest request) => Guard("preflight", async () =>
    {
        InvalidatePlans(); RequireModernEdit(request.Revision);
        var prepared = await dispatcher.Invoke(() => Modern.PreviewScalar(request)); CaptureModern();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Wire.Options)))).ToLowerInvariant();
        var view = new ModernPreview(Guid.NewGuid().ToString("N"), "scalar", hash, request.Revision, "preflight", true, new { request, prepared });
        modernPending = new(view, Scalar: request); return ApiResult.Ok("preflight", view);
    });
    private void RequireModernEdit(long revision)
    {
        if (model is not { Origin: "external", Owned: true, Editable: true } || !status.PartOpen || !status.Healthy)
            throw new StateException("NO_LIVE_EDIT_STATE", "Healthy owned qualified external session required.");
        if (model.Revision != revision) throw new StateException("STALE_REVISION", "Use current committed revision.");
    }
    private PendingModern RequireModern(string id) => modernPending is { } pending && pending.View.Id == id ? pending :
        throw new StateException("STALE_PLAN", "Preview was replaced, consumed or invalidated.");
    public Task<ApiResult> ExecuteModern(ModernConfirmation request) => Guard("execution", async () =>
    {
        var pending = RequireModern(request.Id);
        if (!request.Confirmed || !pending.View.Executable || request.Sha256 != pending.View.Sha256 || request.Revision != pending.View.Revision)
            throw new StateException("PREFLIGHT_REQUIRED", "Confirm the exact current successful preflight/hash/revision.");
        var targets = pending.Batch?.Snapshot().OrderedEdits.Select(e => e.Target).ToArray() ??
            (pending.Scalar is { } scalar ? new[] { scalar.Target } : Array.Empty<string>());
        if (!request.Targets.SequenceEqual(targets)) throw new StateException("QUERY_CONFIRMATION_MISMATCH", "Confirm the exact ordered target set.");
        if (!status.Connected) throw new StateException("SOLIDWORKS_NOT_CONNECTED", "Explicit native connection required.");
        if (pending.Batch is not null || pending.Scalar is not null) RequireModernEdit(request.Revision!.Value);
        modernPending = null; // Consume before dispatch, including native failure or lost HTTP response.
        var result = await dispatcher.Invoke(() => pending.Construction is { } construction ? Modern.ExecuteConstruction(construction, pending.RawResponse!) :
            pending.Batch is { } batch ? Modern.ExecuteBatch(batch, request) : Modern.ExecuteScalar(pending.Scalar!));
        CaptureModern();
        return result.Transaction.Succeeded ? ApiResult.Ok("execution", result) : ApiResult.Fail(result.Transaction.Stage,
            result.Transaction.FailureCode ?? "NATIVE_EXECUTION_FAILED", result.Transaction.Message, result);
    });
}
