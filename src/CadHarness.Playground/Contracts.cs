using System.Text.Json;
using System.Text.Json.Serialization;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.State;

namespace CadHarness.Playground;

public sealed record ApiResult(bool Success, string Stage, string? FailureCode, string Message, object? Details = null)
{
    public static ApiResult Ok(string stage, object? details = null, string message = "Completed.") => new(true, stage, null, message, details);
    public static ApiResult Fail(string stage, string code, string message, object? details = null) => new(false, stage, code, message, details);
}
public sealed record LlmSettings(string Provider, string? ApiKey, string Endpoint, string Model, int TimeoutSeconds = 120,
    int MaxOutputTokens = 8192, string? EnvironmentCredential = null);
public sealed record IntentRequest(string Intent, long? Revision = null);
public sealed record PlanRequest(string PlanId, long? Revision = null);
public sealed record ExecuteRequest(string PlanId, string PreflightId, bool Confirmed, long? Revision = null, bool AutoClose = false);
public sealed record NativeStatus(bool Connected = false, string ConnectionMode = "Not connected", bool PartOpen = false,
    bool Healthy = true, long? Revision = null, int PartsCreated = 0, int PartsClosed = 0);
public sealed record NativeOutcome(bool Succeeded, string? FailureCode, string Message, MutationResult? Transaction,
    object Operations, ExecutionTiming Timing, NativeStatus Status, CadState? State);
public sealed record SessionEvent(DateTimeOffset Time, string Stage, bool Success, string? FailureCode, string Message);
public sealed record PlanView(string PlanId, string? PreflightId, bool Edit, long? Revision, string Intent,
    JsonElement Program, JsonElement Capabilities, object PlannerDetails, object? Preflight, object? EditDetails);
internal sealed record StoredPlan(PlanView View, CadProgram Program, IPlanningRuntime Runtime);

// Only serializable CADState records cross the boundary; native contexts never do.
public static class Wire
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, MaxDepth = 48,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
    public static JsonElement Json(string json) { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
    public static object StateView(CadState? state) => state is null ? new { available = false } : (object)new
    {
        available = true, state.Revision, state.Document,
        features = state.Features.Select(f => new { f.SemanticId, f.Kind, f.ReferenceHealth, advanced = f.NativeReference }),
        entities = state.Entities.Select(e => new { e.SemanticId, e.Type, e.OwnerFeatureSemanticId, e.ReferenceHealth, e.Geometry, advanced = e.NativeReference }),
        state.Parameters, state.Bindings, state.Relations, state.Dependencies
    };
}

public interface INativeSession : IDisposable
{
    NativeStatus Status { get; }
    CadState? State { get; }
    IPlanningRuntime EditRuntime();
    NativeStatus Connect();
    NativeOutcome Create(CadProgram program, bool autoClose);
    NativeOutcome Edit(CadProgram program, long revision);
    NativeStatus Close();
}
public interface IProviderFactory
{
    IStructuredPlanSource Create(LlmSettings settings, string key);
}
