using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Planning;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal sealed record ModelCall(int Number, string Stage, double WallMs, int? InputTokens, int? OutputTokens, string? FailureCode);
internal sealed class RecordedSource : IStructuredPlanSource
{
    public bool IsModelBacked => source.IsModelBacked;
    private readonly IStructuredPlanSource source;
    private readonly string output;
    internal string Stage { get; set; } = "creation";
    internal List<ModelCall> Calls { get; } = new();
    internal RecordedSource(IStructuredPlanSource source, string output) { this.source = source; this.output = output; }
    public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken cancellationToken)
    {
        var number = Calls.Count + 1; var path = Path.Combine(output, $"call-{number:D2}");
        Program.Write(path + "-request.json", prompt);
        StructuredPlanResponse? response = null; PlannerException? providerFailure = null; string? failure = null; var clock = Stopwatch.StartNew();
        try
        {
            using (ExecutionTelemetry.Measure(ExecutionPhase.Llm))
            {
                try { response = await source.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false); }
                finally { clock.Stop(); }
            }
            Program.Write(path + "-response.json", response);
            return response;
        }
        catch (Exception e)
        {
            providerFailure = e as PlannerException;
            failure = providerFailure?.Code ?? (e is OperationCanceledException ? "PLANNER_CANCELLED" : "MODEL_CALL_FAILED");
            // Persist codes only: transport messages can contain provider secrets.
            Program.Write(path + "-error.json", new { FailureCode = failure, providerFailure?.ProviderJson, providerFailure?.InputTokens, providerFailure?.OutputTokens }); throw;
        }
        finally { Calls.Add(new(number, Stage, clock.Elapsed.TotalMilliseconds, response?.InputTokens ?? providerFailure?.InputTokens, response?.OutputTokens ?? providerFailure?.OutputTokens, failure)); }
    }
    internal int? InputTokens => Sum(c => c.InputTokens);
    internal int? OutputTokens => Sum(c => c.OutputTokens);
    private int? Sum(Func<ModelCall, int?> read) => Calls.TrueForAll(c => read(c).HasValue) ? Calls.Count == 0 ? 0 : System.Linq.Enumerable.Sum(Calls, c => read(c)!.Value) : null;
}
