using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;

namespace CadHarness.State;

public static class ProfileGeometry
{
    public static bool ContainsHole(SketchProfile profile, Point2D center, double radiusMm) => profile switch
    {
        CenteredRectangleProfile r => Math.Abs(center.XMm) + radiusMm < r.WidthMm / 2 && Math.Abs(center.YMm) + radiusMm < r.HeightMm / 2,
        CircleProfile c => Math.Sqrt(center.XMm * center.XMm + center.YMm * center.YMm) + radiusMm < c.DiameterMm / 2,
        _ => false
    };
}

// World XY positions for the supported finite native layouts. A circular span
// includes the seed: full revolutions exclude the duplicate endpoint; partial
// spans include both endpoints. Positive steps rotate about local +Z.
public static class PatternGeometry
{
    public static double AngleDegrees(OperationNode pattern) => pattern.Parameters.TryGetValue("angleDeg", out var value) ? ((AngleParameter)value).Degrees : 360;
    public static string Seed(OperationNode pattern) => pattern.Input("seed")!.References[0].SemanticId;
    public static IReadOnlyList<Point2D> Positions(CadProgram program, OperationNode pattern)
    {
        var seed = program.Operations.Single(o => o.SemanticId == Seed(pattern));
        var p = seed.Parameters.TryGetValue("placement", out var value) ? ((PlacementParameter)value).Value : new Point2D(0, 0);
        if (pattern.Kind == OperationKind.CreateCircularPattern)
        {
            var host = seed.Input("host")!.References[0];
            var root = program.Operations.SingleOrDefault(o => host.SemanticId == o.SemanticId + ".top_face");
            var axis = pattern.Input("axis")!.References[0];
            if (root is null || root.Kind != OperationKind.CreateExtrude || root.Parameter<ProfileParameter>("profile").Value is not CircleProfile ||
                axis.Type != SemanticType.CylindricalFace || axis.SemanticId != root.SemanticId + ".rotational_reference")
                throw new StateException(FailureCodes.OperationUnsupported, "Circular patterns require the circle host's native outer cylindrical rotational_reference.");
            var count = pattern.Parameter<CountParameter>("count").Value;
            var angle = AngleDegrees(pattern);
            if (count < 2 || count > 1024 || !double.IsFinite(angle) || angle <= 0 || angle > 360)
                throw new StateException(FailureCodes.PreconditionFailed, "Circular count/span exceeds the supported finite range.");
            var step = angle * Math.PI / 180 / (angle == 360 ? count : count - 1);
            return Enumerable.Range(0, count).Select(i => new Point2D(p.XMm * Math.Cos(i * step) - p.YMm * Math.Sin(i * step),
                p.XMm * Math.Sin(i * step) + p.YMm * Math.Cos(i * step))).ToArray();
        }
        var layout = DesignRelationEngine.ReadLayout(program, pattern.SemanticId!);
        var points = new List<Point2D>();
        for (var i = 0; i < layout.Count1; i++) for (var j = 0; j < layout.Count2; j++)
            points.Add(new(p.XMm + i * layout.Spacing1Mm * layout.Direction1.X + j * layout.Spacing2Mm * layout.Direction2.X,
                p.YMm + i * layout.Spacing1Mm * layout.Direction1.Y + j * layout.Spacing2Mm * layout.Direction2.Y));
        return points;
    }
}
