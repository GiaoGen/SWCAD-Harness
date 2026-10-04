using System;

namespace CadHarness.Ir;

public static class EditableParameters
{
    public static ParameterContract Contract(EditableParameter parameter) => parameter switch
    {
        EditableParameter.PatternCount => new("value", ParameterKind.Count, Minimum: 2),
        EditableParameter.PatternCountX or EditableParameter.PatternCountY => new("value", ParameterKind.Count, Minimum: 1),
        EditableParameter.PatternAngle => new("value", ParameterKind.Angle, ExclusiveMinimum: 0, Maximum: 360),
        _ when Enum.IsDefined(parameter) => new("value", ParameterKind.Length, ExclusiveMinimum: 0),
        _ => throw new ArgumentOutOfRangeException(nameof(parameter))
    };

    public static bool IsOwnedBy(EditableParameter parameter, OperationNode owner) => parameter switch
    {
        EditableParameter.ProfileWidth or EditableParameter.ProfileHeight => owner.Kind == OperationKind.CreateExtrude &&
            owner.Parameters.TryGetValue("profile", out var rectangle) && rectangle is ProfileParameter { Value: CenteredRectangleProfile },
        EditableParameter.ProfileDiameter => owner.Kind == OperationKind.CreateExtrude &&
            owner.Parameters.TryGetValue("profile", out var circle) && circle is ProfileParameter { Value: CircleProfile },
        EditableParameter.ExtrusionDepth => owner.Kind == OperationKind.CreateExtrude,
        EditableParameter.HoleDiameter => owner.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole,
        EditableParameter.BlindHoleDepth => owner.Kind == OperationKind.CreateBlindHole,
        EditableParameter.PatternSpacing or EditableParameter.PatternCount => owner.Kind == OperationKind.CreateLinearPattern ||
            (parameter == EditableParameter.PatternCount && owner.Kind == OperationKind.CreateCircularPattern),
        EditableParameter.PatternSpacingX or EditableParameter.PatternSpacingY or EditableParameter.PatternCountX or EditableParameter.PatternCountY =>
            owner.Kind == OperationKind.CreateRectangularPattern,
        EditableParameter.PatternAngle => owner.Kind == OperationKind.CreateCircularPattern,
        EditableParameter.FilletRadius => owner.Kind == OperationKind.ApplyFillet,
        EditableParameter.ChamferDistance => owner.Kind == OperationKind.ApplyChamfer,
        _ => false
    };
}
