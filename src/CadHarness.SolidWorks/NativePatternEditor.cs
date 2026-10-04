using CadHarness.Ir;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

internal static class NativePatternEditor
{
    internal static void Apply(SolidWorksExecutionContext context, OperationNode operation, EditableParameter? only = null)
    {
        var feature = context.DirectFeature(operation.SemanticId!);
        var d = LinearPatternHandler.Dimensions(operation);
        // M5 edits scalar parameters of an existing pattern. Preserve native
        // seed/direction selections instead of assigning optional null axes.
        if (feature.GetDefinition() is not ILinearPatternFeatureData data)
            throw new NativeOperationException("STALE_REFERENCE", "Bound pattern has no linear pattern definition.");
        var accessed = false; var modified = false;
        try
        {
            accessed = data.AccessSelections(context.Document, null);
            if (!accessed) throw new NativeOperationException(FailureCodes.PreconditionFailed, "Cannot access native pattern selections.");
            if (only is null || only == EditableParameter.PatternCount ||
                (!d.Swap && only == EditableParameter.PatternCountX) || (d.Swap && only == EditableParameter.PatternCountY)) data.D1TotalInstances = d.X;
            if (only is null || only == EditableParameter.PatternSpacing ||
                (!d.Swap && only == EditableParameter.PatternSpacingX) || (d.Swap && only == EditableParameter.PatternSpacingY)) data.D1Spacing = d.SpacingX / 1000;
            if (d.Y > 1)
            {
                if (only is null || only == EditableParameter.PatternCountY) data.D2TotalInstances = d.Y;
                if (only is null || only == EditableParameter.PatternSpacingY) data.D2Spacing = d.SpacingY / 1000;
            }
            modified = feature.ModifyDefinition(data, context.Document, null);
            if (!modified) throw new NativeOperationException("PARAMETER_NOT_APPLIED", "Native pattern ModifyDefinition failed.");
        }
        finally
        {
            if (accessed && !modified) data.ReleaseSelectionAccess();
            context.Document.ClearSelection2(true);
        }
    }
}
