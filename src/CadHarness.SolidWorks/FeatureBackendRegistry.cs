using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;

namespace CadHarness.SolidWorks;

public sealed class FeatureBackendRegistry
{
    private readonly IReadOnlyDictionary<OperationKind, IOperationBackendHandler> handlers;
    public FeatureBackendRegistry() => handlers = new IOperationBackendHandler[]
    {
        new CreateExtrudeHandler(), new CreateThroughHoleHandler(), new CreateBlindHoleHandler(),
        new CreateLinearPatternHandler(), new CreateRectangularPatternHandler(), new ApplyFilletHandler(), new ApplyChamferHandler()
    }.ToDictionary(h => h.Kind);
    public IReadOnlyList<OperationKind> SupportedKinds => handlers.Keys.ToArray();
    public bool TryGet(OperationKind kind, out IOperationBackendHandler handler) => handlers.TryGetValue(kind, out handler!);
}

public sealed record CompositionExecutionResult(bool Succeeded, string? FailureCode, string Message,
    IReadOnlyList<OperationExecutionResult> Operations)
{
    public bool MutationStarted => Operations.Any(x => x.MutationStarted);
    public bool RollbackAttempted => false;
    public bool RollbackSucceeded => false;
    public bool StateCommitted => false;
}

// Executes a finite construction program in a fresh Part. Generic state binding,
// relations, editing and transactions are reserved for M5/M6.
public sealed class CompositionBackend
{
    private readonly FeatureBackendRegistry registry = new();
    public PreflightResult Preflight(CadProgram program)
    {
        var validation = new ProgramValidator().Validate(program);
        if (!validation.IsValid) return new(false, validation.Issues[0].Code, validation.Issues[0].Message);
        if (program.Relations.Count != 0) return new(false, FailureCodes.OperationUnsupported, "Design relation execution belongs to M5; supply explicit placements and spacings in M4.");
        if (program.Operations[0].Kind != OperationKind.CreateExtrude || program.Operations.Count(x => x.Kind == OperationKind.CreateExtrude) != 1)
            return new(false, FailureCodes.PreconditionFailed, "Construction requires exactly one initial extrusion.");
        var symbols = new Dictionary<string, SemanticType>(StringComparer.Ordinal);
        foreach (var operation in program.Operations)
        {
            if (!registry.TryGet(operation.Kind, out var handler)) return new(false, FailureCodes.OperationUnsupported, "No M4 backend handler for " + operation.Kind + ".");
            var check = handler.Preflight(operation);
            if (!check.IsValid) return check;
            foreach (var reference in operation.Inputs.SelectMany(i => i.References))
                if (!symbols.TryGetValue(reference.SemanticId, out var type) || reference.Type != type)
                    return new(false, "BINDING_UNRESOLVED", "Input is not a prior typed construction output: " + reference.SemanticId + ".");
            foreach (var output in OperationRegistry.Default.Get(operation.Kind).Outputs)
                if (output.Type != SemanticType.LocalFrame) // M4 has no native local-frame binding.
                    symbols.Add(operation.SemanticId! + output.Suffix, output.Type);
        }
        return PreflightResult.Success;
    }
    public CompositionExecutionResult Execute(SolidWorksExecutionContext context, CadProgram program)
    {
        var check = Preflight(program);
        if (!check.IsValid) return new(false, check.FailureCode, check.Message, Array.Empty<OperationExecutionResult>());
        context.CheckThread();
        var results = new List<OperationExecutionResult>();
        foreach (var operation in program.Operations)
        {
            registry.TryGet(operation.Kind, out var handler);
            var result = handler.Execute(context, operation);
            results.Add(result);
            if (!result.Succeeded) return new(false, result.FailureCode, result.Message, results.AsReadOnly());
        }
        return new(true, null, "Construction program completed through reusable native handlers.", results.AsReadOnly());
    }
}

internal static class FeaturePreflight
{
    internal const int MaximumPatternInstances = 1024;
    internal static PreflightResult Validate(OperationNode operation, OperationKind kind)
    {
        if (operation.Kind != kind) return new(false, FailureCodes.OperationUnsupported, "Operation does not match this backend handler.");
        var check = new ProgramValidator().Validate(new("0.2", new[] { operation }, Array.Empty<DesignRelation>()));
        if (!check.IsValid) return new(false, check.Issues[0].Code, check.Issues[0].Message);
        foreach (var length in operation.Parameters.Values.OfType<LengthParameter>())
            try { Millimeters.ToMeters(length.Millimeters); }
            catch (ArgumentOutOfRangeException) { return new(false, FailureCodes.PreconditionFailed, "Length is not representable in native units."); }
        return PreflightResult.Success;
    }
    internal static PreflightResult Failure(string message) => new(false, FailureCodes.PreconditionFailed, message);
}
