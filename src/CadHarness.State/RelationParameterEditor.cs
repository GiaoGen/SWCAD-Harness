using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;

namespace CadHarness.State;

public static class RelationParameterEditor
{
    public static CadProgram Apply(CadProgram current, OperationNode edit)
    {
        var check = new ProgramValidator().Validate(new("0.2", new[] { edit }, Array.Empty<DesignRelation>()));
        if (!check.IsValid) throw new StateException(check.Issues[0].Code, check.Issues[0].Message);
        if (edit.Kind != OperationKind.EditParameter) throw new StateException(FailureCodes.OperationUnsupported, "Expected edit_parameter.");
        var target = edit.Input("target")!.References[0].SemanticId;
        var owner = current.Operations.SingleOrDefault(o => o.SemanticId == target) ?? throw new StateException("BINDING_UNRESOLVED", "No managed owner for edit target.");
        var parameter = edit.Parameter<ParameterNameParameter>("parameter").Value;
        if (!EditableParameters.IsOwnedBy(parameter, owner)) throw new StateException(FailureCodes.PreconditionFailed, "The target does not own this parameter.");
        var field = parameter switch
        {
            EditableParameter.PatternSpacing => "spacingMm", EditableParameter.PatternCount => "count",
            EditableParameter.PatternSpacingX => "spacingXMm", EditableParameter.PatternSpacingY => "spacingYMm",
            EditableParameter.PatternCountX => "countX", EditableParameter.PatternCountY => "countY",
            _ => throw new StateException(FailureCodes.OperationUnsupported, "M5 edits are limited to linear/rectangular pattern counts and spacings required by relation verification.")
        };
        var parameters = new Dictionary<string, OperationParameter>(owner.Parameters) { [field] = edit.Parameter<EditValueParameter>("value").Value };
        var changed = owner with { Parameters = parameters };
        return current with { Operations = current.Operations.Select(o => o.SemanticId == target ? changed : o).ToArray() };
    }
}
