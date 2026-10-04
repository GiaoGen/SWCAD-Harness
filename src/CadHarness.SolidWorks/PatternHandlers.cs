using System;
using CadHarness.Ir;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed class CreateLinearPatternHandler : LinearPatternHandler
{ public override OperationKind Kind => OperationKind.CreateLinearPattern; }
public sealed class CreateRectangularPatternHandler : LinearPatternHandler
{ public override OperationKind Kind => OperationKind.CreateRectangularPattern; }

public abstract class LinearPatternHandler : NativeFeatureHandler
{
    internal sealed record PatternDimensions(int X, int Y, double SpacingX, double SpacingY, bool Swap);
    public override PreflightResult Preflight(OperationNode operation)
    {
        var check = base.Preflight(operation);
        if (!check.IsValid) return check;
        if (Kind == OperationKind.CreateRectangularPattern)
        {
            foreach (var pair in new[] { ("countX", "spacingXMm"), ("countY", "spacingYMm") })
                if (operation.Parameter<CountParameter>(pair.Item1).Value > 1 && !operation.Parameters.ContainsKey(pair.Item2))
                    return FeaturePreflight.Failure("Explicit spacing is required in each repeated direction; relation solving belongs to M5.");
        }
        var dimensions = Dimensions(operation);
        if ((long)dimensions.X * dimensions.Y > FeaturePreflight.MaximumPatternInstances)
            return FeaturePreflight.Failure("Pattern exceeds the finite backend limit of 1024 instances.");
        foreach (var input in operation.Inputs)
            if (input.Name != "seed" && input.References[0].Type != SemanticType.LinearEdge)
                return FeaturePreflight.Failure("M4 pattern directions use constructed linear edges.");
        return check;
    }
    protected override void ValidateInputs(SolidWorksExecutionContext context, OperationNode operation)
    {
        context.Resolve<IFeature>(operation.Input("seed")!.References[0]);
        var d = Dimensions(operation);
        var x = Direction(context, operation, d.Swap ? 1 : 0);
        Axis(context.Resolve<IEdge>(x));
        if (d.Y > 1)
        {
            var y = Direction(context, operation, 1);
            var a = Axis(context.Resolve<IEdge>(x)); var b = Axis(context.Resolve<IEdge>(y));
            if (Math.Abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2]) > 1e-8)
                throw new NativeOperationException(FailureCodes.PreconditionFailed, "Rectangular pattern directions must be perpendicular.");
        }
    }
    protected override object? CreateNative(SolidWorksExecutionContext context, OperationNode operation)
    {
        var doc = context.Document;
        var d = Dimensions(operation);
        var x = context.Resolve<IEdge>(Direction(context, operation, d.Swap ? 1 : 0));
        var flipX = IsReversed(Axis(x));
        NativeTopology.Select(doc, x, false, 1);
        var flipY = false;
        if (d.Y > 1)
        {
            var y = context.Resolve<IEdge>(Direction(context, operation, 1));
            flipY = IsReversed(Axis(y));
            NativeTopology.Select(doc, y, true, 2);
        }
        NativeTopology.Select(doc, context.Resolve<IFeature>(operation.Input("seed")!.References[0]), true, 4);
        return ((IFeatureManager)doc.FeatureManager).FeatureLinearPattern5(d.X, d.SpacingX / 1000, d.Y, d.SpacingY / 1000,
            flipX, flipY, "", "", false, false, false, false, false, false, false, false, false, false,
            0, 0, false, false);
    }
    protected override void ValidateFeature(SolidWorksExecutionContext context, OperationNode operation, object native)
    {
        Require(((IFeature)native).GetDefinition() is ILinearPatternFeatureData, "Linear pattern definition is missing.");
        var data = (ILinearPatternFeatureData)((IFeature)native).GetDefinition();
        var d = Dimensions(operation);
        Require(data.D1TotalInstances == d.X && data.D2TotalInstances == d.Y && Matches(data.D1Spacing, d.SpacingX), "Pattern instance count or first spacing differs.");
        if (d.Y > 1) Require(Matches(data.D2Spacing, d.SpacingY) && !data.D2PatternSeedOnly, "Rectangular spacing or seed-only mode differs.");
    }
    internal static PatternDimensions Dimensions(OperationNode operation)
    {
        if (operation.Kind == OperationKind.CreateLinearPattern) return new(operation.Parameter<CountParameter>("count").Value, 1,
            operation.Parameter<LengthParameter>("spacingMm").Millimeters, 0, false);
        var x = operation.Parameter<CountParameter>("countX").Value; var y = operation.Parameter<CountParameter>("countY").Value;
        var sx = operation.Parameters.TryGetValue("spacingXMm", out var px) ? ((LengthParameter)px).Millimeters : 0;
        var sy = operation.Parameters.TryGetValue("spacingYMm", out var py) ? ((LengthParameter)py).Millimeters : 0;
        return x == 1 ? new(y, 1, sy, 0, true) : new(x, y, sx, sy, false);
    }
    internal static SemanticReference Direction(SolidWorksExecutionContext context, OperationNode operation, int axis)
    {
        var name = operation.Kind == OperationKind.CreateLinearPattern ? "direction" : axis == 0 ? "directionX" : "directionY";
        return operation.Input(name)?.References[0] ?? new(context.ConstructionRoot + (axis == 0 ? ".direction_x" : ".direction_y"), SemanticType.LinearEdge);
    }
    internal static double[] Axis(IEdge edge)
    {
        if (edge.GetCurve() is not ICurve curve || !curve.IsLine())
            throw new NativeOperationException(FailureCodes.PreconditionFailed, "Pattern direction is not a native linear edge.");
        var p = NativeGeometry.Doubles(curve.LineParams);
        var length = Math.Sqrt(p[3] * p[3] + p[4] * p[4] + p[5] * p[5]);
        if (!double.IsFinite(length) || length < 1e-12) throw new NativeOperationException(FailureCodes.PreconditionFailed, "Pattern direction is degenerate.");
        return new[] { p[3] / length, p[4] / length, p[5] / length };
    }
    // A constructed edge defines its line; orient it toward the positive dominant
    // component of the extrusion frame, independently of native edge enumeration.
    internal static bool IsReversed(double[] direction)
    {
        var axis = Math.Abs(direction[0]) >= Math.Abs(direction[1]) ? 0 : 1;
        if (Math.Abs(direction[2]) > Math.Abs(direction[axis])) axis = 2;
        return direction[axis] < 0;
    }
}
