using System;
using System.Linq;
using System.Runtime.InteropServices;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public sealed class CreateExtrudeHandler : IOperationBackendHandler
{
    public static System.Collections.Generic.IReadOnlyList<ProfileKind> SupportedProfiles { get; } =
        Array.AsReadOnly(new[] { ProfileKind.CenteredRectangle, ProfileKind.Circle });
    public OperationKind Kind => OperationKind.CreateExtrude;

    public PreflightResult Preflight(OperationNode operation)
    {
        var validation = new ProgramValidator().Validate(new("0.2", new[] { operation }, Array.Empty<DesignRelation>()));
        if (!validation.IsValid)
        {
            var issue = validation.Issues[0];
            return new(false, issue.Code, issue.Path + ": " + issue.Message);
        }
        if (operation.Kind != Kind || !SupportedProfiles.Contains(operation.Parameter<ProfileParameter>("profile").Value.Kind))
            return new(false, FailureCodes.OperationUnsupported, "Extrusion requires a supported centered profile.");
        try
        {
            switch (operation.Parameter<ProfileParameter>("profile").Value)
            {
                case CenteredRectangleProfile rectangle:
                    Millimeters.ToMeters(rectangle.WidthMm); Millimeters.ToMeters(rectangle.HeightMm); break;
                case CircleProfile circle: Millimeters.ToMeters(circle.DiameterMm); break;
            }
            Millimeters.ToMeters(operation.Parameter<LengthParameter>("depthMm").Millimeters);
        }
        catch (ArgumentOutOfRangeException)
        { return new(false, FailureCodes.PreconditionFailed, "Dimensions cannot be represented in native units."); }
        return PreflightResult.Success;
    }

    public OperationExecutionResult Execute(SolidWorksExecutionContext context, OperationNode operation)
    {
        var preflight = Preflight(operation);
        if (!preflight.IsValid) return new(false, preflight.FailureCode, preflight.Message, operation.SemanticId, false, false);
        context.CheckThread();
        var document = context.Document;
        var mutationStarted = false;
        try
        {
            if (document.GetType() != (int)swDocumentTypes_e.swDocPART || context.CreatedFeature is not null ||
                NativeGeometry.SolidBodies(document).Count != 0 || document.GetActiveSketch2() is not null)
                return new(false, FailureCodes.PreconditionFailed, "An empty Part outside sketch editing is required.", operation.SemanticId, false, false);
            var profile = operation.Parameter<ProfileParameter>("profile").Value;
            var depth = Millimeters.ToMeters(operation.Parameter<LengthParameter>("depthMm").Millimeters);
            mutationStarted = true;
            var sketch = profile switch
            {
                CenteredRectangleProfile rectangle => CenteredRectangleProfileBackend.Create(document,
                    Millimeters.ToMeters(rectangle.WidthMm), Millimeters.ToMeters(rectangle.HeightMm)),
                CircleProfile circle => CircleProfileBackend.Create(document, Millimeters.ToMeters(circle.DiameterMm) / 2),
                _ => throw new NativeOperationException(FailureCodes.OperationUnsupported, "Unsupported extrusion profile.")
            };
            if (!sketch.Select2(false, 0)) throw new NativeOperationException("GEOMETRY_INVALID", "Cannot select the created profile.");
            var manager = (IFeatureManager)document.FeatureManager;
            var native = manager.FeatureExtrusion3(
                true, false, false,
                (int)swEndConditions_e.swEndCondBlind, (int)swEndConditions_e.swEndCondBlind,
                depth, 0,
                false, false, false, false, 0, 0,
                false, false, false, false,
                true, true, true,
                (int)swStartConditions_e.swStartSketchPlane, 0, false);
            if (native is null) throw new NativeOperationException("GEOMETRY_INVALID", "Native extrusion returned no feature.");
            context.CreatedFeature = (IFeature)native;
            var rebuilt = document.ForceRebuild3(false);
            var error = context.CreatedFeature.GetErrorCode2(out var warning);
            if (!rebuilt || error != 0 || warning || document.Extension.NeedsRebuild2 != 0)
                return new(false, "FEATURE_REBUILD_FAILED", "Extrusion rebuild or native feature status failed.", operation.SemanticId, true, false);
            context.RegisterExtrude(operation, context.CreatedFeature);
            return new(true, null, "Native extrusion created and rebuilt.", operation.SemanticId, true, true);
        }
        catch (NativeOperationException error)
        { return new(false, error.Code, error.Message, operation.SemanticId, mutationStarted, false); }
        catch (StateException error)
        { return new(false, error.Code, error.Message, operation.SemanticId, mutationStarted, false); }
        catch (COMException error)
        { return new(false, "GEOMETRY_INVALID", $"SOLIDWORKS COM failure 0x{error.HResult:X8}: {error.Message}", operation.SemanticId, mutationStarted, false); }
        finally { document.ClearSelection2(true); }
    }
}
