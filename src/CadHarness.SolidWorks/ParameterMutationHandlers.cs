using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public sealed class PatternScalarMutationHandler : IParameterMutationHandler
{
    public IReadOnlyList<ParameterMutationDescriptor> Descriptors { get; } = Array.AsReadOnly(new[]
    {
        new ParameterMutationDescriptor(OperationKind.CreateLinearPattern, EditableParameter.PatternCount, "count"),
        new ParameterMutationDescriptor(OperationKind.CreateLinearPattern, EditableParameter.PatternSpacing, "spacingMm"),
        new ParameterMutationDescriptor(OperationKind.CreateRectangularPattern, EditableParameter.PatternCountX, "countX"),
        new ParameterMutationDescriptor(OperationKind.CreateRectangularPattern, EditableParameter.PatternCountY, "countY"),
        new ParameterMutationDescriptor(OperationKind.CreateRectangularPattern, EditableParameter.PatternSpacingX, "spacingXMm"),
        new ParameterMutationDescriptor(OperationKind.CreateRectangularPattern, EditableParameter.PatternSpacingY, "spacingYMm")
    });
    public bool CanExecute(OperationNode owner, EditableParameter parameter)
    {
        if (owner.Kind == OperationKind.CreateLinearPattern) return true;
        if (owner.Kind != OperationKind.CreateRectangularPattern) return false;
        var d = LinearPatternHandler.Dimensions(owner);
        return parameter switch
        {
            EditableParameter.PatternCountX or EditableParameter.PatternSpacingX => !d.Swap,
            EditableParameter.PatternCountY or EditableParameter.PatternSpacingY => d.Swap || d.Y > 1,
            _ => false
        };
    }
    public void ValidateTransition(OperationNode before, OperationNode after, EditableParameter parameter)
    {
        var a = LinearPatternHandler.Dimensions(before); var b = LinearPatternHandler.Dimensions(after);
        if (a.Swap != b.Swap || (a.Y > 1) != (b.Y > 1))
            throw new StateException(FailureCodes.OperationUnsupported, "Scalar edits preserve active native pattern directions.");
    }
    public FullValidationReason ValidationReasons(OperationNode owner, EditableParameter parameter) =>
        parameter is EditableParameter.PatternCount or EditableParameter.PatternCountX or EditableParameter.PatternCountY ? FullValidationReason.HighRiskTopology : FullValidationReason.None;
    public double Read(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter)
    {
        var data = (ILinearPatternFeatureData)context.DirectFeature(owner.SemanticId!).GetDefinition();
        var d = LinearPatternHandler.Dimensions(owner);
        return parameter switch
        {
            EditableParameter.PatternCount => data.D1TotalInstances,
            EditableParameter.PatternSpacing => data.D1Spacing * 1000,
            EditableParameter.PatternCountX => d.Swap ? 1 : data.D1TotalInstances,
            EditableParameter.PatternCountY => d.Swap ? data.D1TotalInstances : data.D2TotalInstances,
            EditableParameter.PatternSpacingX => data.D1Spacing * 1000,
            EditableParameter.PatternSpacingY => (d.Swap ? data.D1Spacing : data.D2Spacing) * 1000,
            _ => throw new StateException(FailureCodes.OperationUnsupported, "No pattern scalar accessor.")
        };
    }
    private sealed record Rollback(OperationNode Owner);
    public object Capture(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter)
    {
        var fields = new Dictionary<string, OperationParameter>(owner.Parameters);
        foreach (var descriptor in Descriptors.Where(d => d.OwnerKind == owner.Kind && CanExecute(owner, d.Parameter)))
        {
            var actual = Read(context, owner, descriptor.Parameter);
            fields[descriptor.Field] = EditableParameters.Contract(descriptor.Parameter).Kind == ParameterKind.Count ?
                new CountParameter(checked((int)actual)) : new LengthParameter(actual);
        }
        return new Rollback(owner with { Parameters = fields });
    }
    public void Apply(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) => NativePatternEditor.Apply(context, owner, parameter);
    public void Restore(SolidWorksExecutionContext context, object rollback) => NativePatternEditor.Apply(context, ((Rollback)rollback).Owner);
    public void ValidateNative(SolidWorksExecutionContext context, CadProgram expected, string target, EditableParameter parameter) { }
}

public sealed class ExtrusionDepthMutationHandler : IParameterMutationHandler
{
    public IReadOnlyList<ParameterMutationDescriptor> Descriptors { get; } = Array.AsReadOnly(new[]
        { new ParameterMutationDescriptor(OperationKind.CreateExtrude, EditableParameter.ExtrusionDepth, "depthMm") });
    public bool CanExecute(OperationNode owner, EditableParameter parameter) => owner.Kind == OperationKind.CreateExtrude &&
        owner.Parameters["profile"] is ProfileParameter { Value: CenteredRectangleProfile };
    public void ValidateTransition(OperationNode before, OperationNode after, EditableParameter parameter) { }
    // Positive depth changes preserve the supported rectangular topology. The
    // dependency closure validates moved host/edges and all dependent cuts.
    public FullValidationReason ValidationReasons(OperationNode owner, EditableParameter parameter) => FullValidationReason.None;
    public double Read(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) =>
        Data(context, owner.SemanticId!).GetDepth(true) * 1000;
    private sealed record Rollback(string Owner, double DepthMm);
    public object Capture(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) =>
        new Rollback(owner.SemanticId!, Read(context, owner, parameter));
    public void Apply(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) =>
        Set(context, owner.SemanticId!, owner.Parameter<LengthParameter>("depthMm").Millimeters);
    public void Restore(SolidWorksExecutionContext context, object rollback)
    { var original = (Rollback)rollback; Set(context, original.Owner, original.DepthMm); }
    public void ValidateNative(SolidWorksExecutionContext context, CadProgram expected, string target, EditableParameter parameter)
    {
        var owner = expected.Operations.Single(o => o.SemanticId == target);
        RelationNativeReadback.Near(Read(context, owner, parameter), owner.Parameter<LengthParameter>("depthMm").Millimeters, "Native extrusion depth differs.");
        NativeHoleInstanceVerifier.Verify(context, expected,
            new DesignRelationEngine().Solve(expected).Dependencies.AffectedBy(new[] { target }));
    }
    private static IExtrudeFeatureData2 Data(SolidWorksExecutionContext context, string owner) =>
        context.DirectFeature(owner).GetDefinition() as IExtrudeFeatureData2 ?? throw new StateException("STALE_REFERENCE", "Bound extrusion has no native definition.");
    private static void Set(SolidWorksExecutionContext context, string owner, double value)
    {
        var feature = context.DirectFeature(owner); var data = Data(context, owner);
        var accessed = false; var modified = false;
        try
        {
            accessed = data.AccessSelections(context.Document, null);
            if (!accessed || data.GetEndCondition(true) != (int)swEndConditions_e.swEndCondBlind)
                throw new StateException(FailureCodes.PreconditionFailed, "Depth mutation requires an accessible blind extrusion definition.");
            data.SetDepth(true, Millimeters.ToMeters(value));
            modified = feature.ModifyDefinition(data, context.Document, null);
            if (!modified) throw new StateException("PARAMETER_NOT_APPLIED", "Native extrusion ModifyDefinition failed.");
        }
        finally { if (accessed && !modified) data.ReleaseSelectionAccess(); context.Document.ClearSelection2(true); }
    }
}

public sealed class HoleDiameterMutationHandler : IParameterMutationHandler
{
    // M9A exposes the through-hole path that is actually verified. Blind-hole
    // diameter can be registered separately after its native validation.
    public IReadOnlyList<ParameterMutationDescriptor> Descriptors { get; } = Array.AsReadOnly(new[]
        { new ParameterMutationDescriptor(OperationKind.CreateThroughHole, EditableParameter.HoleDiameter, "diameterMm") });
    public bool CanExecute(OperationNode owner, EditableParameter parameter) => owner.Kind == OperationKind.CreateThroughHole;
    public void ValidateTransition(OperationNode before, OperationNode after, EditableParameter parameter) { }
    public FullValidationReason ValidationReasons(OperationNode owner, EditableParameter parameter) => FullValidationReason.None;
    public double Read(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) => NativeHoleProfile.Read(context, owner.SemanticId!).RadiusMeters * 2000;
    private sealed record Rollback(string Owner, double DiameterMm);
    public object Capture(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) =>
        new Rollback(owner.SemanticId!, Read(context, owner, parameter));
    public void Apply(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) =>
        NativeHoleProfile.SetDiameter(context, owner.SemanticId!, owner.Parameter<LengthParameter>("diameterMm").Millimeters);
    public void Restore(SolidWorksExecutionContext context, object rollback)
    { var original = (Rollback)rollback; NativeHoleProfile.SetDiameter(context, original.Owner, original.DiameterMm); }
    public void ValidateNative(SolidWorksExecutionContext context, CadProgram expected, string target, EditableParameter parameter) =>
        NativeHoleInstanceVerifier.Verify(context, expected, new[] { target });
}

// Reads faces produced by the dirty seed/pattern features, not unrelated body
// faces. Instance radii, positions and circular boundary heights establish that
// native propagation happened; a seed sketch getter alone cannot prove it.
internal static class NativeHoleInstanceVerifier
{
    internal static void Verify(SolidWorksExecutionContext context, CadProgram expected, IEnumerable<string> affected)
    {
        var dirty = affected.ToHashSet(StringComparer.Ordinal);
        foreach (var hole in expected.Operations.Where(o => o.Kind == OperationKind.CreateThroughHole && dirty.Contains(o.SemanticId!)))
        {
            var seedPosition = hole.Parameter<PlacementParameter>("placement").Value;
            var positions = new List<Point2D> { seedPosition };
            var produced = new List<IFace2>(NativeTopology.FeatureFaces(context.DirectFeature(hole.SemanticId!)));
            foreach (var pattern in expected.Operations.Where(o => o.Input("seed")?.References[0].SemanticId == hole.SemanticId))
            {
                var d = LinearPatternHandler.Dimensions(pattern);
                var x = LinearPatternHandler.Axis(context.DirectDirection(pattern, d.Swap ? 1 : 0));
                if (LinearPatternHandler.IsReversed(x)) for (var k = 0; k < 3; k++) x[k] = -x[k];
                var y = d.Y > 1 ? LinearPatternHandler.Axis(context.DirectDirection(pattern, 1)) : new double[3];
                if (LinearPatternHandler.IsReversed(y)) for (var k = 0; k < 3; k++) y[k] = -y[k];
                for (var i = 0; i < d.X; i++) for (var j = 0; j < d.Y; j++)
                    if (i != 0 || j != 0) positions.Add(new(seedPosition.XMm + i * d.SpacingX * x[0] + j * d.SpacingY * y[0],
                        seedPosition.YMm + i * d.SpacingX * x[1] + j * d.SpacingY * y[1]));
                produced.AddRange(NativeTopology.FeatureFaces(context.DirectFeature(pattern.SemanticId!)));
            }
            var walls = produced.Where(f => f.GetSurface() is ISurface s && s.IsCylinder())
                .GroupBy(f => PersistentReferenceAdapter.Capture(context, f)).Select(g => g.First()).ToArray();
            if (walls.Length != positions.Count) throw new StateException("PARAMETER_NOT_APPLIED", "Dirty native hole instance count differs.");
            var host = expected.Operations.Single(o => hole.Input("host")!.References[0].SemanticId == o.SemanticId + ".top_face");
            var depth = host.Parameter<LengthParameter>("depthMm").Millimeters;
            var radius = hole.Parameter<LengthParameter>("diameterMm").Millimeters / 2;
            foreach (var position in positions)
            {
                var face = NativeTopology.Unique(walls.Where(f =>
                {
                    var c = NativeGeometry.Doubles(((ISurface)f.GetSurface()).CylinderParams);
                    return Math.Abs(c[0] * 1000 - position.XMm) <= GeometryMath.ToleranceMm && Math.Abs(c[1] * 1000 - position.YMm) <= GeometryMath.ToleranceMm;
                }), "dirty native hole instance");
                var cylinder = NativeGeometry.Doubles(((ISurface)face.GetSurface()).CylinderParams);
                RelationNativeReadback.Near(cylinder[6] * 1000, radius, "Native patterned hole radius differs.");
                RelationNativeReadback.Near(Math.Abs(cylinder[5]), 1, "Native patterned hole direction differs.");
                var levels = NativeTopology.Objects<IEdge>(face.GetEdges()).Select(e => (ICurve)e.GetCurve())
                    .Where(c => c.IsCircle()).Select(c => NativeGeometry.Doubles(c.CircleParams)[2] * 1000).Distinct().OrderBy(z => z).ToArray();
                if (levels.Length != 2) throw new StateException("PARAMETER_NOT_APPLIED", "Through-hole instance lacks two circular boundaries.");
                RelationNativeReadback.Near(levels[0], 0, "Native hole lower boundary differs.");
                RelationNativeReadback.Near(levels[1], depth, "Native hole upper boundary differs after depth mutation.");
            }
        }
    }
}
