using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CadHarness.Planning;

// Shared JSON-mode adapter for an explicitly configured compatible endpoint.
// The strict executable schema remains enforced locally in both orchestrators.
public sealed class ChatCompletionPlanSource : IStructuredPlanSource
{
    public bool IsModelBacked => true;
    private readonly HttpClient client;
    private readonly OpenAiPlannerOptions options;
    public ChatCompletionPlanSource(HttpClient client, OpenAiPlannerOptions options) { this.client = client; this.options = options; }
    public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = options.Model, temperature = 0, max_tokens = options.MaximumOutputTokens,
            response_format = new { type = "json_object" }, stream = false,
            messages = new[] { new { role = "system", content = prompt.Instructions + "\nReturn JSON matching this exact schema:\n" + prompt.ResponseSchemaJson },
                new { role = "user", content = prompt.Intent } }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(options.Timeout);
        string? raw = null; int? input = null; int? output = null;
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new PlannerException("PLANNER_PROVIDER_ERROR", "Chat endpoint returned HTTP " + (int)response.StatusCode + ".");
            if (response.Content.Headers.ContentLength > OpenAiPlanSource.MaximumResponseBytes) throw new PlannerException("PLANNER_RESPONSE_TOO_LARGE", "Provider response exceeds limit.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var buffer = new MemoryStream(); var chunk = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(chunk, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (buffer.Length + read > OpenAiPlanSource.MaximumResponseBytes) throw new PlannerException("PLANNER_RESPONSE_TOO_LARGE", "Provider response exceeds limit.");
                buffer.Write(chunk, 0, read);
            }
            raw = Encoding.UTF8.GetString(buffer.ToArray());
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 48 }); var root = document.RootElement;
            int? Tokens(string name) => root.TryGetProperty("usage", out var usage) && usage.TryGetProperty(name, out var value) && value.TryGetInt32(out var count) && count >= 0 ? count : null;
            input = Tokens("prompt_tokens"); output = Tokens("completion_tokens");
            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() != 1 || choices[0].GetProperty("finish_reason").GetString() != "stop")
                throw new PlannerException("PLANNER_RESPONSE_INCOMPLETE", "Exactly one completed response is required.", raw, input, output);
            var message = choices[0].GetProperty("message");
            if (message.GetProperty("role").GetString() != "assistant" || message.TryGetProperty("tool_calls", out _) || message.TryGetProperty("function_call", out _))
                throw new PlannerException("PLANNER_UNEXPECTED_OUTPUT", "Tool calls are not allowed.", raw, input, output);
            var content = message.GetProperty("content").GetString() ?? throw new JsonException();
            return new(content, input, output, raw);
        }
        catch (HttpRequestException) { throw new PlannerException("PLANNER_TRANSPORT_FAILED", "Cannot reach configured chat endpoint."); }
        catch (IOException) { throw new PlannerException("PLANNER_TRANSPORT_FAILED", "Cannot read provider response."); }
        catch (Exception e) when (e is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException)
        { throw new PlannerException("PLANNER_RESPONSE_INVALID", "Provider response does not contain one completed JSON message.", raw, input, output); }
    }
}
