using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CadHarness.Planning;

public sealed class OpenAiPlannerOptions
{
    public string Model { get; }
    public Uri Endpoint { get; }
    public int MaximumOutputTokens { get; }
    public TimeSpan Timeout { get; }
    public double? Temperature { get; }
    internal string ApiKey { get; }
    public OpenAiPlannerOptions(string model, string apiKey, Uri? endpoint = null, int maximumOutputTokens = 8192, int timeoutSeconds = 120, double? temperature = null)
    {
        Endpoint = endpoint ?? new Uri("https://api.openai.com/v1/responses");
        if (string.IsNullOrWhiteSpace(model) || model.Length > 128 || string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsControl) ||
            !Endpoint.IsAbsoluteUri || Endpoint.Scheme != Uri.UriSchemeHttps || Endpoint.UserInfo.Length != 0 || Endpoint.Query.Length != 0 || Endpoint.Fragment.Length != 0 ||
            maximumOutputTokens is < 256 or > 32768 || timeoutSeconds is < 1 or > 300 || temperature is { } t && (!double.IsFinite(t) || t < 0 || t > 2))
            throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Configure a model, API key, HTTPS Responses endpoint and bounded token/timeout limits.");
        Model = model; ApiKey = apiKey; MaximumOutputTokens = maximumOutputTokens; Timeout = TimeSpan.FromSeconds(timeoutSeconds); Temperature = temperature;
    }
    public static OpenAiPlannerOptions FromEnvironment()
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) ?? throw new PlannerException("PLANNER_CONFIGURATION_MISSING", "Required environment variable: " + name);
        int Number(string name, int fallback) => Environment.GetEnvironmentVariable(name) is { } value ? int.TryParse(value, out var n) ? n :
            throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Invalid integer setting: " + name) : fallback;
        var endpoint = Environment.GetEnvironmentVariable("CAD_HARNESS_PLANNER_ENDPOINT");
        if (endpoint is not null && !Uri.TryCreate(endpoint, UriKind.Absolute, out _)) throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Invalid Responses endpoint.");
        return new(Required("CAD_HARNESS_PLANNER_MODEL"), Required("OPENAI_API_KEY"), endpoint is null ? null : new Uri(endpoint),
            Number("CAD_HARNESS_PLANNER_MAX_OUTPUT_TOKENS", 8192), Number("CAD_HARNESS_PLANNER_TIMEOUT_SECONDS", 120));
    }
}

// One configurable frontier-model Responses request, no tools, no retry agent,
// no native callback. HttpClient injection permits complete offline verification.
public sealed class OpenAiPlanSource : IStructuredPlanSource
{
    public bool IsModelBacked => true;
    public const int MaximumResponseBytes = 524288;
    private readonly HttpClient client;
    private readonly OpenAiPlannerOptions options;
    public OpenAiPlanSource(HttpClient client, OpenAiPlannerOptions options) { this.client = client; this.options = options; }
    public static HttpClient CreateClient() => new(new SocketsHttpHandler { AllowAutoRedirect = false })
    { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken cancellationToken)
    {
        using var schema = JsonDocument.Parse(prompt.ResponseSchemaJson);
        var body = new System.Collections.Generic.Dictionary<string, object>
        {
            ["model"] = options.Model, ["store"] = false, ["max_output_tokens"] = options.MaximumOutputTokens,
            ["input"] = new[] { new { role = "system", content = prompt.Instructions }, new { role = "user", content = prompt.Intent } },
            ["text"] = new { format = new { type = "json_schema", name = "cad_plan", strict = true, schema = schema.RootElement } }
        };
        if (options.Temperature is { } temperature) body["temperature"] = temperature;
        var payload = JsonSerializer.Serialize(body);
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(options.Timeout);
        string? raw = null; int? inputTokens = null; int? outputTokens = null;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new PlannerException("PLANNER_RESPONSE_TOO_LARGE", "Provider response exceeds the byte limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > MaximumResponseBytes) throw new PlannerException("PLANNER_RESPONSE_TOO_LARGE", "Provider response exceeds the byte limit.");
                buffer.Write(chunk, 0, read);
            }
            raw = Encoding.UTF8.GetString(buffer.ToArray()).Replace(options.ApiKey, "[REDACTED]", StringComparison.Ordinal);
            if (!response.IsSuccessStatusCode)
            {
                // Error bodies may include real usage; keep unknown values null.
                try
                {
                    using var errorDocument = JsonDocument.Parse(raw);
                    if (errorDocument.RootElement.ValueKind == JsonValueKind.Object && errorDocument.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                    {
                        if (usage.TryGetProperty("input_tokens", out var value) && value.TryGetInt32(out var count) && count >= 0) inputTokens = count;
                        if (usage.TryGetProperty("output_tokens", out value) && value.TryGetInt32(out count) && count >= 0) outputTokens = count;
                    }
                }
                catch (JsonException) { }
                throw new PlannerException("PLANNER_PROVIDER_ERROR", "Responses endpoint returned HTTP " + (int)response.StatusCode + ".", raw, inputTokens, outputTokens);
            }
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 48 });
            var root = document.RootElement;
            int? Tokens(string name) => root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var tokens) && tokens >= 0 ? tokens : null;
            inputTokens = Tokens("input_tokens"); outputTokens = Tokens("output_tokens");
            if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
                throw new PlannerException("PLANNER_RESPONSE_INCOMPLETE", "Provider response did not complete; no partial program accepted.", raw, inputTokens, outputTokens);
            if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) throw new JsonException();
            var texts = new System.Collections.Generic.List<string>();
            foreach (var item in output.EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                if (type == "reasoning") continue;
                if (type != "message" || item.GetProperty("role").GetString() != "assistant")
                    throw new PlannerException("PLANNER_UNEXPECTED_OUTPUT", "Tool calls and non-message execution output are not permitted.", raw, inputTokens, outputTokens);
                foreach (var part in item.GetProperty("content").EnumerateArray())
                {
                    if (part.GetProperty("type").GetString() == "refusal") throw new PlannerException("PLANNER_REFUSED", "Provider refused to produce a plan.", raw, inputTokens, outputTokens);
                    if (part.GetProperty("type").GetString() != "output_text") throw new JsonException();
                    texts.Add(part.GetProperty("text").GetString() ?? throw new JsonException());
                }
            }
            if (texts.Count != 1) throw new PlannerException("PLANNER_RESPONSE_INVALID", "Exactly one structured plan response is required.", raw, inputTokens, outputTokens);
            return new(texts[0], inputTokens, outputTokens, raw);
        }
        catch (HttpRequestException) { throw new PlannerException("PLANNER_TRANSPORT_FAILED", "Cannot reach the configured Responses endpoint."); }
        catch (IOException) { throw new PlannerException("PLANNER_TRANSPORT_FAILED", "Cannot read the configured provider response."); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { throw new PlannerException("PLANNER_RESPONSE_INVALID", "Provider response does not contain one completed structured message.", raw, inputTokens, outputTokens); }
    }
}
