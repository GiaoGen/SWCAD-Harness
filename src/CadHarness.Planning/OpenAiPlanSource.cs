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
    internal string ApiKey { get; }
    public OpenAiPlannerOptions(string model, string apiKey, Uri? endpoint = null, int maximumOutputTokens = 8192, int timeoutSeconds = 120)
    {
        Endpoint = endpoint ?? new Uri("https://api.openai.com/v1/responses");
        if (string.IsNullOrWhiteSpace(model) || model.Length > 128 || string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsControl) ||
            !Endpoint.IsAbsoluteUri || Endpoint.Scheme != Uri.UriSchemeHttps || Endpoint.UserInfo.Length != 0 || Endpoint.Query.Length != 0 || Endpoint.Fragment.Length != 0 ||
            maximumOutputTokens is < 256 or > 32768 || timeoutSeconds is < 1 or > 300)
            throw new PlannerException("PLANNER_CONFIGURATION_INVALID", "Configure a model, API key, HTTPS Responses endpoint and bounded token/timeout limits.");
        Model = model; ApiKey = apiKey; MaximumOutputTokens = maximumOutputTokens; Timeout = TimeSpan.FromSeconds(timeoutSeconds);
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
        var payload = JsonSerializer.Serialize(new
        {
            model = options.Model, store = false, max_output_tokens = options.MaximumOutputTokens,
            input = new[] { new { role = "system", content = prompt.Instructions }, new { role = "user", content = prompt.Intent } },
            text = new { format = new { type = "json_schema", name = "cad_plan", strict = true, schema = schema.RootElement } }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(options.Timeout);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new PlannerException("PLANNER_PROVIDER_ERROR", "Responses endpoint returned HTTP " + (int)response.StatusCode + ".");
            if (response.Content.Headers.ContentLength > MaximumResponseBytes) throw new PlannerException("PLANNER_RESPONSE_TOO_LARGE", "Provider response exceeds the byte limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > MaximumResponseBytes) throw new PlannerException("PLANNER_RESPONSE_TOO_LARGE", "Provider response exceeds the byte limit.");
                buffer.Write(chunk, 0, read);
            }
            using var document = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 48 });
            var root = document.RootElement;
            if (!root.TryGetProperty("status", out var status) || status.GetString() != "completed")
                throw new PlannerException("PLANNER_RESPONSE_INCOMPLETE", "Provider response did not complete; no partial program accepted.");
            if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) throw new JsonException();
            var texts = new System.Collections.Generic.List<string>();
            foreach (var item in output.EnumerateArray())
            {
                var type = item.GetProperty("type").GetString();
                if (type == "reasoning") continue;
                if (type != "message" || item.GetProperty("role").GetString() != "assistant")
                    throw new PlannerException("PLANNER_UNEXPECTED_OUTPUT", "Tool calls and non-message execution output are not permitted.");
                foreach (var part in item.GetProperty("content").EnumerateArray())
                {
                    if (part.GetProperty("type").GetString() == "refusal") throw new PlannerException("PLANNER_REFUSED", "Provider refused to produce a plan.");
                    if (part.GetProperty("type").GetString() != "output_text") throw new JsonException();
                    texts.Add(part.GetProperty("text").GetString() ?? throw new JsonException());
                }
            }
            if (texts.Count != 1) throw new PlannerException("PLANNER_RESPONSE_INVALID", "Exactly one structured plan response is required.");
            int? Tokens(string name) => root.TryGetProperty("usage", out var usage) && usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var tokens) && tokens >= 0 ? tokens : null;
            return new(texts[0], Tokens("input_tokens"), Tokens("output_tokens"));
        }
        catch (HttpRequestException) { throw new PlannerException("PLANNER_TRANSPORT_FAILED", "Cannot reach the configured Responses endpoint."); }
        catch (IOException) { throw new PlannerException("PLANNER_TRANSPORT_FAILED", "Cannot read the configured provider response."); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { throw new PlannerException("PLANNER_RESPONSE_INVALID", "Provider response does not contain one completed structured message."); }
    }
}
