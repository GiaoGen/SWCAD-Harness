using System.Text.Json;
using CadHarness.Planning;

namespace CadHarness.Playground;

public sealed class ProviderFactory : IProviderFactory, IDisposable
{
    private readonly HttpClient client = OpenAiPlanSource.CreateClient();
    public IStructuredPlanSource Create(LlmSettings settings, string key) => settings.Provider == "deepseek"
        ? new DeepSeekPlanSource(client, settings.Model, key, settings.MaxOutputTokens, settings.TimeoutSeconds)
        : new OpenAiPlanSource(client, new(settings.Model, key, new Uri(settings.Endpoint), settings.MaxOutputTokens, settings.TimeoutSeconds));
    public void Dispose() => client.Dispose();
}

internal sealed class ObservedSource(IStructuredPlanSource inner) : IStructuredPlanSource
{
    public bool IsModelBacked => inner.IsModelBacked;
    public string? ActualModel { get; private set; }
    public double ProviderWallMs { get; private set; }
    public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken cancellationToken)
    {
        StructuredPlanResponse response;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { response = await inner.GenerateAsync(prompt, cancellationToken); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new PlannerException("PLANNER_PROVIDER_TIMEOUT", "Provider request exceeded its configured deadline; no automatic retry."); }
        finally { ProviderWallMs = watch.Elapsed.TotalMilliseconds; }
        if (response.ProviderJson is not null)
        {
            using var doc = JsonDocument.Parse(response.ProviderJson);
            if (doc.RootElement.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
                ActualModel = model.GetString();
        }
        return response;
    }
}

// No key is part of an exported configuration or a log record.
internal sealed class Credentials
{
    private string? key;
    public LlmSettings? Settings { get; private set; }
    public bool Configured => key is not null;
    public object View
    {
        get
        {
            var secret = key; var settings = Settings;
            return new { configured = secret is not null, provider = settings?.Provider, endpoint = settings?.Endpoint,
                model = settings?.Model, settings?.TimeoutSeconds, settings?.MaxOutputTokens,
                maskedKey = secret is null ? null : "••••" + (secret.Length > 4 ? secret[^4..] : "") };
        }
    }
    public void Configure(LlmSettings settings)
    {
        if (settings.Provider is not ("deepseek" or "responses")) throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Unknown provider.");
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri)) throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Invalid HTTPS endpoint.");
        if (settings.Provider == "deepseek" && uri != DeepSeekPlanSource.Endpoint)
            throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "DeepSeek uses https://api.deepseek.com/responses. Select Responses for another endpoint.");
        var candidate = settings.ApiKey;
        if (settings.EnvironmentCredential is not null)
        {
            if (settings.EnvironmentCredential is not ("DEEPSEEK_API_KEY" or "OPENAI_API_KEY" or "CAD_HARNESS_LLM_API_KEY"))
                throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Environment credential is not allowed.");
            candidate = Environment.GetEnvironmentVariable(settings.EnvironmentCredential);
        }
        if (candidate?.Length > 4096) throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Credential exceeds length limit.");
        _ = new OpenAiPlannerOptions(settings.Model, candidate ?? "", uri, settings.MaxOutputTokens, settings.TimeoutSeconds);
        if (settings.Endpoint.Contains(candidate!, StringComparison.Ordinal) || settings.Model.Contains(candidate!, StringComparison.Ordinal))
            throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Do not put credentials in endpoint or model fields.");
        key = candidate; Settings = settings with { ApiKey = null };
    }
    public IStructuredPlanSource Source(IProviderFactory factory) => Configured
        ? factory.Create(Settings!, key!) : throw new PlannerException("PLANNER_CONFIGURATION_MISSING", "Configure an API credential first.");
    public string Sanitize(string text) { var secret = key; return secret is null ? text : text.Replace(secret, "[REDACTED]", StringComparison.Ordinal); }
    public string SanitizeJson(string text) { var secret = key; return secret is null ? text : text.Replace(JsonSerializer.Serialize(secret)[1..^1], "[REDACTED]", StringComparison.Ordinal); }
    public void Clear() { key = null; Settings = null; }
}
