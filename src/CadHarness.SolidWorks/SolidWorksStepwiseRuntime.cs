using System;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.State;

namespace CadHarness.SolidWorks;

// Orchestration projection only; appends use the existing construction
// transaction, handlers, Binder and final validators without another backend.
public sealed class SolidWorksStepwiseRuntime : IPlanningRuntime
{
    public RuntimeCapabilityCatalog Capabilities { get; }
    public string ModelContextJson { get; }
    private readonly CadProgram? prior;
    private readonly CadState? state;
    public CadProgram? ManagedProgram => prior is null ? null : new CadProgramJson().Parse(new CadProgramJson().Serialize(prior)).Program;
    public static SolidWorksStepwiseRuntime ForSession(SolidWorksExecutionContext context, CadState state)
    {
        context.CheckThread();
        if (context.RelationProgram is null)
        {
            StateValidation.Validate(state);
            if (state.Features.Count != 0 || context.MutationInProgress || !context.RelationContextUsable)
                throw new StateException("STALE_REFERENCE", "No healthy initial construction session.");
            return new();
        }
        // Same committed revision/identity/program checks as edit planning.
        SolidWorksPlanningRuntime.ForEdit(context, state);
        return new(context.RelationProgram, state);
    }
    public SolidWorksStepwiseRuntime(CadProgram? prior = null, CadState? state = null)
    {
        if ((prior is null) != (state is null)) throw new ArgumentException("Prior program and state must be supplied together.");
        var copied = prior is null ? null : new CadProgramJson().Parse(new CadProgramJson().Serialize(prior));
        if (copied is { IsValid: false }) throw new StateException(copied.Issues[0].Code, copied.Issues[0].Message);
        this.prior = copied?.Program;
        this.state = state is null ? null : JsonSerializer.Deserialize<CadState>(JsonSerializer.Serialize(state))!;
        var construction = SolidWorksPlanningRuntime.ForConstruction().Capabilities;
        Capabilities = new(construction.RuntimeId, PlanningMode.CreateModel, construction.Registry.Contracts,
            construction.Profiles, construction.Relations, Array.Empty<ParameterEditCapability>(),
            construction.Constraints.Where(c => !c.StartsWith("Exactly one initial", StringComparison.Ordinal) && !c.StartsWith("All inputs must", StringComparison.Ordinal))
                .Append("One CAD operation per decision. Start with the single extrusion in a pristine Part; later append to the managed model.")
                .Append("Inputs may use existing healthy semantic outputs. Appending cannot move or edit existing operation definitions; choose seed placement for the requested final layout before creating its pattern."), construction.ProfileOutputs);
        if (this.prior is not null)
        {
            StateValidation.Validate(this.state!);
            var executable = Capabilities.Validate(this.prior);
            if (!executable.IsValid) throw new StateException(executable.Issues[0].Code, executable.Issues[0].Message);
            if (this.state!.Features.Count != this.prior.Operations.Count ||
                this.prior.Operations.Any(o => !this.state.Features.Any(f => f.SemanticId == o.SemanticId && f.Kind == o.Kind)) ||
                !StateRelationData.Relations(this.state).ToHashSet().SetEquals(this.prior.Relations) ||
                !StateRelationData.Dependencies(this.state).ToHashSet().SetEquals(new DesignRelationEngine().Solve(this.prior).Dependencies.Edges))
                throw new StateException("STALE_REFERENCE", "Stepwise observation differs from the managed program.");
        }
        // All fields are derived solely from this defensive committed snapshot.
        // They contain no expected task, next operation or future program.
        var operations = this.prior?.Operations ?? Array.Empty<OperationNode>();
        var features = this.state?.Features ?? Array.Empty<FeatureNode>();
        var entities = this.state?.Entities ?? Array.Empty<SemanticEntityNode>();
        var parameters = this.state?.Parameters ?? Array.Empty<ParameterNode>();
        var healthyOwners = features.Where(f => f.ReferenceHealth == ReferenceHealth.Healthy).Select(f => f.SemanticId).ToHashSet(StringComparer.Ordinal);
        ModelContextJson = JsonSerializer.Serialize(new { program = this.prior is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(this.prior)),
            committedOperationIds = operations.Select(o => o.Id),
            committedSemanticIds = features.Select(f => f.SemanticId).Concat(entities.Select(e => e.SemanticId)).Concat(parameters.Select(p => p.SemanticId)).Distinct(StringComparer.Ordinal),
            committedOperations = operations.Select(o => new { id = o.Id, semanticId = o.SemanticId, kind = WireNames.Of(o.Kind) }),
            healthySemanticOutputs = entities.Where(e => e.ReferenceHealth == ReferenceHealth.Healthy && healthyOwners.Contains(e.OwnerFeatureSemanticId))
                .Select(e => new { semanticId = e.SemanticId, type = WireNames.Of(e.Type), owner = e.OwnerFeatureSemanticId }),
            revision = this.state?.Revision ?? 0, entities = this.state?.Entities.Select(e => new { semanticId = e.SemanticId, type = WireNames.Of(e.Type),
                owner = e.OwnerFeatureSemanticId, health = e.ReferenceHealth.ToString(), geometry = e.Geometry }) });
    }
    public ProgramValidationResult Preflight(CadProgram addition)
    {
        using var measured = ExecutionTelemetry.Measure(ExecutionPhase.Validation);
        if (addition.Operations.Count != 1) return Invalid(FailureCodes.OperationUnsupported, "Stepwise mode accepts exactly one CAD operation.");
        var parsed = new ProgramValidator(Capabilities.Registry).Validate(addition); if (!parsed.IsValid) return parsed;
        var merged = prior is null ? addition : new CadProgram("0.2", prior.Operations.Concat(addition.Operations).ToArray(), prior.Relations.Concat(addition.Relations).ToArray());
        var capability = Capabilities.Validate(merged); if (!capability.IsValid) return capability;
        if (state is not null)
            foreach (var input in addition.Operations.Single().Inputs)
                foreach (var reference in input.References)
                {
                    var bound = new SemanticEntityBinder().Bind(state, Capabilities.Registry.Get(addition.Operations.Single().Kind).Inputs.Single(i => i.Name == input.Name), new(reference.SemanticId, reference.Type));
                    if (!bound.Succeeded) return Invalid(bound.FailureCode!, bound.Message);
                }
        var check = new RelationBackend().Preflight(addition, prior);
        return check.IsValid ? new(Array.Empty<ValidationIssue>()) : Invalid(check.FailureCode!, check.Message);
    }
    private static ProgramValidationResult Invalid(string code, string message) => new(new[] { new ValidationIssue(code, "$", message) });
}
