using CadHarness.Ir;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public abstract class EdgeTreatmentHandler : NativeFeatureHandler
{
    protected override void ValidateInputs(SolidWorksExecutionContext context, OperationNode operation)
    {
        foreach (var reference in operation.Input("edges")!.References)
        {
            var edge = context.Resolve<IEdge>(reference);
            if (edge.GetCurve() is not ICurve curve ||
                (reference.Type == SemanticType.LinearEdge && !curve.IsLine()) ||
                (reference.Type == SemanticType.CircularEdge && !curve.IsCircle()))
                throw new NativeOperationException(FailureCodes.PreconditionFailed, "Native edge geometry does not match the declared semantic type.");
        }
    }
    protected void SelectEdges(SolidWorksExecutionContext context, OperationNode operation, int mark)
    {
        var append = false;
        foreach (var reference in operation.Input("edges")!.References)
        {
            NativeTopology.Select(context.Document, context.Resolve<IEdge>(reference), append, mark);
            append = true;
        }
    }
}

public sealed class ApplyFilletHandler : EdgeTreatmentHandler
{
    public override OperationKind Kind => OperationKind.ApplyFillet;
    protected override object? CreateNative(SolidWorksExecutionContext context, OperationNode operation)
    {
        SelectEdges(context, operation, 1);
        return ((IFeatureManager)context.Document.FeatureManager).FeatureFillet3(
            (int)swFeatureFilletOptions_e.swFeatureFilletUniformRadius,
            Millimeters.ToMeters(operation.Parameter<LengthParameter>("radiusMm").Millimeters), 0, 0,
            (int)swFeatureFilletType_e.swFeatureFilletType_Simple, 0, 0,
            null, null, null, null, null, null, null);
    }
    protected override void ValidateFeature(SolidWorksExecutionContext context, OperationNode operation, object native)
    {
        Require(((IFeature)native).GetDefinition() is ISimpleFilletFeatureData2, "Fillet definition is missing.");
        var data = (ISimpleFilletFeatureData2)((IFeature)native).GetDefinition();
        Require(Matches(data.DefaultRadius, operation.Parameter<LengthParameter>("radiusMm").Millimeters), "Native fillet radius differs.");
    }
}

public sealed class ApplyChamferHandler : EdgeTreatmentHandler
{
    public override OperationKind Kind => OperationKind.ApplyChamfer;
    protected override object? CreateNative(SolidWorksExecutionContext context, OperationNode operation)
    {
        SelectEdges(context, operation, 0);
        var distance = Millimeters.ToMeters(operation.Parameter<LengthParameter>("distanceMm").Millimeters);
        return ((IFeatureManager)context.Document.FeatureManager).InsertFeatureChamfer(0,
            (int)swChamferType_e.swChamferDistanceDistance | (int)swChamferType_e.swChamferEqualDistance,
            distance, 0, distance, 0, 0, 0);
    }
    protected override void ValidateFeature(SolidWorksExecutionContext context, OperationNode operation, object native)
    {
        Require(((IFeature)native).GetDefinition() is IChamferFeatureData2, "Chamfer definition is missing.");
        var data = (IChamferFeatureData2)((IFeature)native).GetDefinition();
        Require(Matches(data.GetEdgeChamferDistance(0), operation.Parameter<LengthParameter>("distanceMm").Millimeters) &&
            Matches(data.GetEdgeChamferDistance(1), operation.Parameter<LengthParameter>("distanceMm").Millimeters), "Native chamfer distances differ.");
    }
}
