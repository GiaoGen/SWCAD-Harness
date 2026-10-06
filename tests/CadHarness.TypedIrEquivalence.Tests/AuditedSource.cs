using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Planning;

namespace CadHarness.Benchmark.Tests;
internal sealed class AuditedSource : IStructuredPlanSource
{
    private readonly IStructuredPlanSource source;
    private readonly string output;
    private int calls;
    internal StructuredPlanResponse? LastResponse { get; private set; }
    public bool IsModelBacked => source.IsModelBacked;
    internal AuditedSource(IStructuredPlanSource source, string output) { this.source = source; this.output = output; }
    public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken cancellationToken)
    {
        LastResponse = null;
        var issues = ProviderSchemaCompatibility.Audit(prompt.ResponseSchemaJson);
        Program.Write(Path.Combine(output, $"call-{++calls:D2}-schema-audit.json"), new { Status = issues.Count == 0 ? "PASS" : "FAIL", Issues = issues,
            Generator = "PlannerResponseSchema", Adapter = "DeepSeekPlanSource", LocalValidationRetained = true });
        ProviderSchemaCompatibility.RequireCompatible(prompt.ResponseSchemaJson);
        LastResponse = await source.GenerateAsync(prompt, cancellationToken).ConfigureAwait(false);
        return LastResponse;
    }
}
