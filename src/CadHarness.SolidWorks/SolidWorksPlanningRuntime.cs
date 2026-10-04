using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.State;

namespace CadHarness.SolidWorks;

// This projection is owned by the executable backend, not by the planner or
// schema-wide IR vocabulary. Construction and live-session edits are separate.
public sealed class SolidWorksPlanningRuntime : IPlanningRuntime
{
    public RuntimeCapabilityCatalog Capabilities { get; }
    public string ModelContextJson { get; }
    private readonly CadProgram? current;
    private readonly ParameterMutationRegistry mutations;
    private SolidWorksPlanningRuntime(RuntimeCapabilityCatalog capabilities, CadProgram? current, ParameterMutationRegistry? mutations = null)
    {
        Capabilities = capabilities; this.current = current; this.mutations = mutations ?? ParameterMutationRegistry.Default;
        ModelContextJson = current is null ? "null" : new CadProgramJson(ConstructionCatalog().Registry).Serialize(current);
    }
    public static SolidWorksPlanningRuntime ForConstruction() => new(ConstructionCatalog(), null);

    public static SolidWorksPlanningRuntime ForEdit(SolidWorksExecutionContext context, CadState state, ParameterMutationRegistry? mutations = null)
    {
        context.CheckThread();
        if (!context.RelationContextUsable || context.MutationInProgress || context.RelationProgram is null ||
            state.Revision != context.RelationRevision || !state.Document.Matches(DocumentIdentityAdapter.ReadForBinding(context)) ||
            context.RelationDependencies is null || !StateRelationData.Dependencies(state).ToHashSet().SetEquals(context.RelationDependencies.Edges))
            throw new StateException("STALE_REFERENCE", "No matching usable live edit session for planning.");
        return ForEditSnapshot(context.RelationProgram, state, mutations);
    }

    // Pure snapshot projection supports M7's zero-Part tests. Execution still
    // performs M6's native revision, identity, binding and transaction checks.
    public static SolidWorksPlanningRuntime ForEditSnapshot(CadProgram program, CadState state, ParameterMutationRegistry? mutations = null)
    {
        mutations ??= ParameterMutationRegistry.Default;
        StateValidation.Validate(state);
        var construction = ForConstruction(); var check = construction.Preflight(program);
        if (!check.IsValid) throw new StateException(check.Issues[0].Code, check.Issues[0].Message);
        if (!StateRelationData.Relations(state).ToHashSet().SetEquals(program.Relations) ||
            state.Features.Count != program.Operations.Count ||
            program.Operations.Any(o => !state.Features.Any(f => f.SemanticId == o.SemanticId && f.Kind == o.Kind)) ||
            !StateRelationData.Dependencies(state).ToHashSet().SetEquals(new DesignRelationEngine().Solve(program).Dependencies.Edges))
            throw new StateException("STALE_REFERENCE", "Planning snapshot does not match the managed construction program.");
        // Defensive strict round-trip: callers cannot mutate the projection's
        // internal current program through their original lists/dictionaries.
        var serializer = new CadProgramJson(construction.Capabilities.Registry);
        var normalized = new DesignRelationEngine().Solve(serializer.Parse(serializer.Serialize(program)).Program!).Program;
        var edits = new List<ParameterEditCapability>();
        foreach (var operation in normalized.Operations)
        {
            var bound = new SemanticEntityBinder().Bind(state, OperationRegistry.Default.Get(OperationKind.EditParameter).Inputs[0],
                new(operation.SemanticId!, SemanticType.FeatureRef));
            if (!bound.Succeeded) continue;
            foreach (var descriptor in mutations.Descriptors.Where(d => d.OwnerKind == operation.Kind))
            {
                var parameter = descriptor.Parameter;
                var bindings = state.Bindings.Where(b => b.OwnerFeatureSemanticId == operation.SemanticId && b.Parameter == parameter).ToArray();
                if (!mutations.TryGet(operation, parameter, out _) || bindings.Length != 1) continue;
                var affected = new DependencyGraph(StateRelationData.Dependencies(state)).AffectedBy(new[] { operation.SemanticId! });
                if (state.Features.Where(f => affected.Contains(f.SemanticId)).Any(f => f.ReferenceHealth != ReferenceHealth.Healthy) ||
                    state.Entities.Where(e => affected.Contains(e.OwnerFeatureSemanticId)).Any(e => e.ReferenceHealth != ReferenceHealth.Healthy)) continue;
                var scalar = mutations.Expected(operation, parameter);
                if (Math.Abs(state.Parameters.Single(p => p.SemanticId == bindings[0].ParameterSemanticId).Value - scalar) > GeometryMath.ToleranceMm) continue;
                var contract = EditableParameters.Contract(parameter);
                if (contract.Kind == ParameterKind.Count) contract = contract with { Minimum = 2, Maximum = FeaturePreflight.MaximumPatternInstances };
                edits.Add(new(operation.SemanticId!, operation.Kind, parameter, contract));
            }
        }
        var contracts = edits.Count == 0 ? Array.Empty<OperationContract>() : new[] { OperationRegistry.Default.Get(OperationKind.EditParameter) };
        var catalog = new RuntimeCapabilityCatalog("solidworks-v0.2-m9a", PlanningMode.EditModel, contracts,
            Array.Empty<ProfileKind>(), Array.Empty<RelationKind>(), edits, new[]
            {
                "One edit_parameter per plan, using a healthy bound target/parameter pair in this snapshot.",
                "Only active native pattern directions can be edited; count changes must preserve that direction's active status.",
                "Registered extrusion_depth and through-hole hole_diameter edits preserve host/layout legality and require native readback of dependent geometry.",
                "Existing design relations are retained and solved by the runtime; do not emit new relations.",
                "Execution requires the same usable live construction session and committed state revision."
            });
        return new(catalog, normalized, mutations);
    }

    private static RuntimeCapabilityCatalog ConstructionCatalog()
    {
        // Enumerate actual registered native handlers first; IR only supplies
        // contracts for that selected set. Circular pattern is never selected.
        var contracts = new FeatureBackendRegistry().SupportedKinds.Select(kind =>
        {
            var contract = OperationRegistry.Default.Get(kind);
            var inputs = contract.Inputs.Select(input => input with
            {
                AcceptedTypes = Array.AsReadOnly(input.Name == "host" ? new[] { SemanticType.PlanarFace } :
                    input.Name == "seed" ? new[] { SemanticType.FeatureRef } : new[] { SemanticType.LinearEdge })
            }).ToArray();
            var parameters = contract.Parameters.Select(p => p.Kind == ParameterKind.Count ? p with { Maximum = FeaturePreflight.MaximumPatternInstances } : p).ToArray();
            return contract with { Inputs = Array.AsReadOnly(inputs), Parameters = Array.AsReadOnly(parameters) };
        }).ToArray();
        return new("solidworks-v0.2-m7", PlanningMode.CreateModel, contracts, CreateExtrudeHandler.SupportedProfiles,
            new DesignRelationEngine().SupportedKinds, Array.Empty<ParameterEditCapability>(), new[]
            {
                "Exactly one initial centered_rectangle extrusion on the XY plane in an empty Part.",
                "All inputs must reference prior outputs within this construction program; no external model references.",
                "Holes use the initial extrusion's top_face; hole placement is local XY in millimeters, strictly inside the rectangle.",
                "Blind-hole depth is strictly less than the extrusion depth.",
                "Pattern seeds are through/blind holes; directions are the host's local direction_x/direction_y outputs.",
                "Every repeated direction requires explicit spacing greater than hole diameter; total instances <= 1024.",
                "Centering uses the host local_frame; symmetry uses its axis_x or axis_y.",
                "Edge treatments use currently healthy constructed linear edges and require native geometry feasibility.",
                "Construction cannot contain parameter edits. No unimplemented IR vocabulary is available."
            });
    }

    public ProgramValidationResult Preflight(CadProgram program)
    {
        var capabilityCheck = Capabilities.Validate(program);
        if (!capabilityCheck.IsValid) return capabilityCheck;
        try
        {
            var construction = program;
            if (Capabilities.Mode == PlanningMode.EditModel)
            {
                construction = mutations.ApplyProgram(current!, program.Operations.Single());
            }
            var solved = new DesignRelationEngine().Solve(construction).Program;
            var result = new RelationBackend().Preflight(solved);
            if (!result.IsValid) return Invalid(result.FailureCode!, result.Message);
            foreach (var hole in solved.Operations.Where(o => o.Kind == OperationKind.CreateBlindHole))
            {
                var root = solved.Operations.Single(o => hole.Input("host")!.References[0].SemanticId == o.SemanticId + ".top_face");
                if (hole.Parameter<LengthParameter>("depthMm").Millimeters >= root.Parameter<LengthParameter>("depthMm").Millimeters)
                    return Invalid(FailureCodes.PreconditionFailed, "Blind-hole depth must be less than the host thickness.");
            }
            return new(Array.Empty<ValidationIssue>());
        }
        catch (StateException error) { return Invalid(error.Code, error.Message); }
    }
    private static ProgramValidationResult Invalid(string code, string message) => new(new[] { new ValidationIssue(code, "$", message) });
}
