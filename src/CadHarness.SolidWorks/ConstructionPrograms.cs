using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;

namespace CadHarness.SolidWorks;

internal static class ConstructionPrograms
{
    internal static RelationPlan Plan(CadProgram request, CadProgram? prior)
    {
        var check = new ProgramValidator().Validate(request);
        if (!check.IsValid) throw new StateException(check.Issues[0].Code, check.Issues[0].Message);
        // Defensive copy through the finite IR serializer. A caller's mutable
        // collection cannot change a prepared native transaction afterward.
        var json = new CadProgramJson(); request = json.Parse(json.Serialize(request)).Program!;
        var merged = prior is null ? request : new CadProgram("0.2", prior.Operations.Concat(request.Operations).ToArray(),
            prior.Relations.Concat(request.Relations).ToArray());
        var plan = new DesignRelationEngine().Solve(merged);
        var native = new CompositionBackend().PreflightPrepared(plan.Program with { Relations = Array.Empty<DesignRelation>() });
        if (!native.IsValid) throw new StateException(native.FailureCode!, native.Message);
        foreach (var hole in plan.Program.Operations.Where(o => o.Kind == OperationKind.CreateBlindHole))
        {
            var root = plan.Program.Operations.Single(o => hole.Input("host")!.References[0].SemanticId == o.SemanticId + ".top_face");
            if (hole.Parameter<LengthParameter>("depthMm").Millimeters >= root.Parameter<LengthParameter>("depthMm").Millimeters)
                throw new StateException(FailureCodes.PreconditionFailed, "Blind-hole depth must be less than host thickness.");
        }
        if (prior is not null)
            foreach (var before in prior.Operations)
                if (json.Serialize(new("0.2", new[] { before }, Array.Empty<DesignRelation>())) !=
                    json.Serialize(new("0.2", new[] { plan.Program.Operations.Single(o => o.SemanticId == before.SemanticId) }, Array.Empty<DesignRelation>())))
                    throw new StateException(FailureCodes.OperationUnsupported, "Construction extensions cannot move or edit existing operation definitions.");
        return plan;
    }
}
