using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CadHarness.Planning;

// DeepSeek's documented Responses structured-output protocol. Both orchestration
// modes share this adapter and the exact schema supplied by their planner.
// No JSON-mode fallback, tool calls, answer templates or provider retries.
public sealed class DeepSeekPlanSource : IStructuredPlanSource
{
    public static readonly Uri Endpoint = new("https://api.deepseek.com/responses");
    public const string Format = "responses/json_schema/strict";
    public bool IsModelBacked => true;
    private readonly OpenAiPlanSource source;
    public DeepSeekPlanSource(HttpClient client, string model, string apiKey, int maximumOutputTokens = 8192, int timeoutSeconds = 120) =>
        source = new(client, new(model, apiKey, Endpoint, maximumOutputTokens, timeoutSeconds, temperature: 0));
    public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken cancellationToken) => source.GenerateAsync(prompt, cancellationToken);
}
