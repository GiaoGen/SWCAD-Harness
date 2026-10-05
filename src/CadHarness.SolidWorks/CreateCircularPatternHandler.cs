using System;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed class CreateCircularPatternHandler : NativeFeatureHandler
{
    public override OperationKind Kind => OperationKind.CreateCircularPattern;
    public override PreflightResult Preflight(OperationNode operation)
    {
        var check = base.Preflight(operation);
        if (!check.IsValid) return check;
        if (operation.Parameter<CountParameter>("count").Value > FeaturePreflight.MaximumPatternInstances)
            return FeaturePreflight.Failure("Circular pattern exceeds 1024 instances.");
        if (operation.Input("seed")!.References[0].Type != SemanticType.FeatureRef || operation.Input("axis")!.References[0].Type != SemanticType.CylindricalFace)
            return FeaturePreflight.Failure("Circular pattern requires a feature seed and native cylindrical face axis.");
        return check;
    }
    protected override void ValidateInputs(SolidWorksExecutionContext context, OperationNode operation)
    {
        var seed = context.Operation(PatternGeometry.Seed(operation));
        if (seed.Kind is not (OperationKind.CreateThroughHole or OperationKind.CreateBlindHole))
            throw new NativeOperationException(FailureCodes.OperationUnsupported, "Circular pattern supports managed hole feature seeds.");
        context.Resolve<IFeature>(operation.Input("seed")!.References[0]);
        var axis = operation.Input("axis")!.References[0];
        var root = context.Operation(context.Owner(axis.SemanticId));
        if (root.Kind != OperationKind.CreateExtrude || root.Parameter<ProfileParameter>("profile").Value is not CircleProfile ||
            axis.SemanticId != root.SemanticId + ".rotational_reference" || seed.Input("host")!.References[0].SemanticId != root.SemanticId + ".top_face")
            throw new NativeOperationException(FailureCodes.OperationUnsupported, "Circular axis must be the circle host's outer cylindrical face.");
        Reverse(context.Resolve<IFace2>(axis));
    }
    protected override object? CreateNative(SolidWorksExecutionContext context, OperationNode operation)
    {
        var axis = context.Resolve<IFace2>(operation.Input("axis")!.References[0]);
        var reverse = Reverse(axis);
        NativeTopology.Select(context.Document, axis, false, 1);
        NativeTopology.Select(context.Document, context.Resolve<IFeature>(operation.Input("seed")!.References[0]), true, 4);
        return ((IFeatureManager)context.Document.FeatureManager).FeatureCircularPattern4(
            operation.Parameter<CountParameter>("count").Value, PatternGeometry.AngleDegrees(operation) * Math.PI / 180,
            reverse, "", false, true, false);
    }
    protected override void ValidateFeature(SolidWorksExecutionContext context, OperationNode operation, object feature) => VerifyDefinition(context, operation, (IFeature)feature);
    internal static bool Reverse(IFace2 face)
    {
        if (face.GetSurface() is not ISurface surface || !surface.IsCylinder())
            throw new NativeOperationException(FailureCodes.PreconditionFailed, "Rotational reference is not a native cylinder.");
        var c = NativeGeometry.Doubles(surface.CylinderParams);
        if (c.Length < 7 || !NativeTopology.Near(c[0], 0) || !NativeTopology.Near(c[1], 0) || !NativeTopology.Near(Math.Abs(c[5]), 1))
            throw new NativeOperationException(FailureCodes.PreconditionFailed, "Rotational reference must be centered on local Z.");
        return c[5] < 0;
    }
    internal static void VerifyDefinition(SolidWorksExecutionContext context, OperationNode operation, IFeature feature)
    {
        Require(feature.GetDefinition() is ICircularPatternFeatureData, "Circular native definition missing.");
        var data = (ICircularPatternFeatureData)feature.GetDefinition();
        Require(data.TotalInstances == operation.Parameter<CountParameter>("count").Value && data.EqualSpacing && !data.VarySketch &&
            !data.GeometryPattern && !data.Direction2 && !data.BodyPattern, "Circular layout count or native mode differs.");
        RelationNativeReadback.Near(data.Spacing * 180 / Math.PI, PatternGeometry.AngleDegrees(operation), "Circular native angular span differs.");
        var axis = context.DirectFace(operation.Input("axis")!.References[0].SemanticId);
        var axisRef = PersistentReferenceAdapter.Capture(context, axis);
        var seedRef = PersistentReferenceAdapter.Capture(context, context.DirectFeature(PatternGeometry.Seed(operation)));
        var reverse = Reverse(axis);
        if (!data.AccessSelections(context.Document, null)) throw new NativeOperationException("RELATION_VIOLATED", "Cannot access native circular selections.");
        try
        {
            var seeds = NativeTopology.Objects<object>(data.PatternFeatureArray);
            Require(seeds.Count == 1 && PersistentReferenceAdapter.Capture(context, seeds[0]) == seedRef, "Circular native seed reference differs.");
            Require(data.Axis is IFace2 && PersistentReferenceAdapter.Capture(context, data.Axis) == axisRef && data.ReverseDirection == reverse,
                "Circular native axis reference or orientation differs.");
            Require(data.GetSkippedItemCount() == 0 && data.GetPatternFaceCount() == 0 && data.GetPatternBodyCount() == 0,
                "Circular layout has skipped instances or uses face/body pattern inputs.");
        }
        finally { data.ReleaseSelectionAccess(); context.Document.ClearSelection2(true); }
    }
}
