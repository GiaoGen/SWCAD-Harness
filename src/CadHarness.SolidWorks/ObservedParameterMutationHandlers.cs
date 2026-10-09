using CadHarness.Ir.V03;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public sealed record ObservedSetterResult(string Api, int ReturnCode, bool Succeeded);

public interface IObservedParameterMutationHandler
{
    bool Supports(NativeSubtype subtype, ParameterKey key);
    double ReadObserved(IModelDoc2 document, IFeature feature, ParameterKey key);
    ObservedSetterResult ApplyObserved(IModelDoc2 document, IFeature feature, ScalarEdit edit);
}
public sealed partial class ExtrusionDepthMutationHandler : IObservedParameterMutationHandler
{
    public bool Supports(NativeSubtype subtype, ParameterKey key) => subtype == NativeSubtype.StraightBlindBossExtrude && key == ParameterKey.ExtrusionDepth;
    public double ReadObserved(IModelDoc2 document, IFeature feature, ParameterKey key) => ((IExtrudeFeatureData2)feature.GetDefinition()).GetDepth(true) * 1000;
    public ObservedSetterResult ApplyObserved(IModelDoc2 document, IFeature feature, ScalarEdit edit)
    {
        var data = feature.GetDefinition() as IExtrudeFeatureData2 ?? throw new StateException("STALE_REFERENCE", "Extrusion interface changed.");
        var accessed = false; var modified = false;
        try
        {
            accessed = data.AccessSelections(document, null);
            if (!accessed || data.GetEndCondition(true) != (int)swEndConditions_e.swEndCondBlind || data.IsThinFeature() || data.BothDirections)
                throw new StateException(V03FailureCodes.UnsupportedNativeSubtype, "Extrusion subtype changed.");
            data.SetDepth(true, edit.Value / 1000);
            modified = feature.ModifyDefinition(data, document, null);
            return new("IFeature.ModifyDefinition (Boolean)", modified ? 1 : 0, modified);
        }
        finally { if (accessed && !modified) data.ReleaseSelectionAccess(); document.ClearSelection2(true); }
    }
}
public sealed partial class PatternScalarMutationHandler : IObservedParameterMutationHandler
{
    public bool Supports(NativeSubtype subtype, ParameterKey key) => subtype == NativeSubtype.SingleDirectionLinearPattern && key is ParameterKey.PatternCount or ParameterKey.PatternSpacing;
    public double ReadObserved(IModelDoc2 document, IFeature feature, ParameterKey key)
    { var data = (ILinearPatternFeatureData)feature.GetDefinition(); return key == ParameterKey.PatternCount ? data.D1TotalInstances : data.D1Spacing * 1000; }
    public ObservedSetterResult ApplyObserved(IModelDoc2 document, IFeature feature, ScalarEdit edit)
    {
        var data = feature.GetDefinition() as ILinearPatternFeatureData ?? throw new StateException("STALE_REFERENCE", "Pattern interface changed.");
        var accessed = false; var modified = false;
        try
        {
            accessed = data.AccessSelections(document, null);
            if (!accessed || data.IsDirection2Specified() || data.D1EndCondition != 0 || data.VarySketch || data.GetSkippedItemCount() != 0)
                throw new StateException(V03FailureCodes.UnsupportedNativeSubtype, "Pattern subtype changed.");
            if (edit.Parameter == ParameterKey.PatternCount) data.D1TotalInstances = checked((int)edit.Value); else data.D1Spacing = edit.Value / 1000;
            modified = feature.ModifyDefinition(data, document, null);
            return new("IFeature.ModifyDefinition (Boolean)", modified ? 1 : 0, modified);
        }
        finally { if (accessed && !modified) data.ReleaseSelectionAccess(); document.ClearSelection2(true); }
    }
}
public sealed partial class HoleDiameterMutationHandler : IObservedParameterMutationHandler
{
    public bool Supports(NativeSubtype subtype, ParameterKey key) => subtype == NativeSubtype.SingleCircleThroughAllCut && key == ParameterKey.HoleDiameter;
    public double ReadObserved(IModelDoc2 document, IFeature feature, ParameterKey key)
    {
        // Dimension writes are immediate; sketch/topology caches update at the transaction rebuild.
        var dimension = ExternalNativeQualification.HoleDimension(feature, out var diameter, false);
        return dimension.SystemValue * (diameter ? 1000 : 2000);
    }
    public ObservedSetterResult ApplyObserved(IModelDoc2 document, IFeature feature, ScalarEdit edit)
    {
        var dimension = ExternalNativeQualification.HoleDimension(feature, out var diameter);
        var result = dimension.SetSystemValue3(edit.Value / (diameter ? 1000 : 2000), (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
        return new("IDimension.SetSystemValue3 (swSetValueReturnStatus_e)", result,
            result == (int)swSetValueReturnStatus_e.swSetValue_Successful);
    }
}
