using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Planning;

namespace CadHarness.Benchmark.Tests;

// The same stage instrumentation used in M10D, without qualification snapshots.
internal sealed class QualifiedRuntime : IPlanningRuntime
{
    private readonly IPlanningRuntime runtime;
    private readonly CadProgram? prior;
    internal string? FailureStage;
    public RuntimeCapabilityCatalog Capabilities => runtime.Capabilities;
    public string ModelContextJson => runtime.ModelContextJson;
    internal QualifiedRuntime(IPlanningRuntime runtime, CadProgram? prior = null) { this.runtime = runtime; this.prior = prior; }
    public ProgramValidationResult Preflight(CadProgram program)
    {
        FailureStage = "runtime_capability_validation";
        var merged = prior is null ? program : new CadProgram("0.2", prior.Operations.Concat(program.Operations).ToArray(), prior.Relations.Concat(program.Relations).ToArray());
        var capability = Capabilities.Validate(merged); if (!capability.IsValid) return capability;
        FailureStage = "pure_preflight";
        var result = runtime.Preflight(program);
        if (result.IsValid) FailureStage = null;
        return result;
    }
}
