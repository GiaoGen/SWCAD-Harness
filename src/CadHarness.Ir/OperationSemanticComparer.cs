using System;
using System.Linq;

namespace CadHarness.Ir;

// Definition equality is independent of the JSON wire representation. Numeric
// values compare exactly, including equal signed zeros, and fail closed for
// nonfinite values or shapes outside the finite IR contract.
public static class OperationSemanticComparer
{
    public static bool EqualsDefinition(OperationNode? left, OperationNode? right)
    {
        if (left is null || right is null || left.Id != right.Id || left.Kind != right.Kind ||
            !Enum.IsDefined(left.Kind) || left.SemanticId != right.SemanticId ||
            left.Inputs is null || right.Inputs is null || left.Parameters is null || right.Parameters is null ||
            left.Inputs.Count != right.Inputs.Count || left.Parameters.Count != right.Parameters.Count)
            return false;
        if (left.Inputs.Any(i => i is null) || right.Inputs.Any(i => i is null) ||
            left.Inputs.Select(i => i.Name).Distinct(StringComparer.Ordinal).Count() != left.Inputs.Count ||
            right.Inputs.Select(i => i.Name).Distinct(StringComparer.Ordinal).Count() != right.Inputs.Count)
            return false;
        var a = left.Inputs.OrderBy(i => i.Name, StringComparer.Ordinal).ToArray();
        var b = right.Inputs.OrderBy(i => i.Name, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i].Name != b[i].Name || a[i].References is null || b[i].References is null ||
                a[i].References.Count != b[i].References.Count) return false;
            for (var j = 0; j < a[i].References.Count; j++)
            {
                var x = a[i].References[j]; var y = b[i].References[j];
                if (x is null || y is null || x.SemanticId != y.SemanticId || x.Type != y.Type || !Enum.IsDefined(x.Type)) return false;
            }
        }
        var p = left.Parameters.OrderBy(v => v.Key, StringComparer.Ordinal).ToArray();
        var q = right.Parameters.OrderBy(v => v.Key, StringComparer.Ordinal).ToArray();
        for (var i = 0; i < p.Length; i++)
            if (p[i].Key != q[i].Key || !EqualsParameter(p[i].Value, q[i].Value)) return false;
        return true;
    }

    public static bool EqualsParameter(OperationParameter? left, OperationParameter? right)
    {
        if (left is null || right is null || left.Kind != right.Kind) return false;
        return (left, right) switch
        {
            (LengthParameter a, LengthParameter b) => Number(a.Millimeters, b.Millimeters),
            (AngleParameter a, AngleParameter b) => Number(a.Degrees, b.Degrees),
            (CountParameter a, CountParameter b) => a.Value == b.Value,
            (PlacementParameter a, PlacementParameter b) => a.Value is not null && b.Value is not null &&
                Number(a.Value.XMm, b.Value.XMm) && Number(a.Value.YMm, b.Value.YMm),
            (ProfileParameter a, ProfileParameter b) => EqualsProfile(a.Value, b.Value),
            (ParameterNameParameter a, ParameterNameParameter b) => a.Value == b.Value && Enum.IsDefined(a.Value),
            (EditValueParameter a, EditValueParameter b) => EqualsParameter(a.Value, b.Value),
            _ => false
        };
    }

    private static bool Number(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && a == b;
    private static bool EqualsProfile(SketchProfile? left, SketchProfile? right) => (left, right) switch
    {
        (CenteredRectangleProfile a, CenteredRectangleProfile b) => Number(a.WidthMm, b.WidthMm) && Number(a.HeightMm, b.HeightMm),
        (CircleProfile a, CircleProfile b) => Number(a.DiameterMm, b.DiameterMm),
        _ => false
    };
}
