using System;
using System.Runtime.InteropServices;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public abstract class NativeFeatureHandler : IOperationBackendHandler
{
    public abstract OperationKind Kind { get; }
    public virtual PreflightResult Preflight(OperationNode operation) => FeaturePreflight.Validate(operation, Kind);
    protected abstract void ValidateInputs(SolidWorksExecutionContext context, OperationNode operation);
    protected abstract object? CreateNative(SolidWorksExecutionContext context, OperationNode operation);
    protected abstract void ValidateFeature(SolidWorksExecutionContext context, OperationNode operation, object feature);
    protected virtual void Register(SolidWorksExecutionContext context, OperationNode operation, object feature) =>
        context.RegisterFeature(operation, (IFeature)feature);

    public OperationExecutionResult Execute(SolidWorksExecutionContext context, OperationNode operation)
    {
        var check = Preflight(operation);
        if (!check.IsValid) return new(false, check.FailureCode, check.Message, operation.SemanticId, false, false);
        context.CheckThread();
        var doc = context.Document;
        var started = false;
        var rebuilt = false;
        try
        {
            if (doc.GetType() != (int)swDocumentTypes_e.swDocPART || doc.GetActiveSketch2() is not null ||
                context.ConstructionRoot is null || context.ContainsFeature(operation.SemanticId!))
                throw new NativeOperationException(FailureCodes.PreconditionFailed, "A managed construction Part outside sketch editing with a new semantic ID is required.");
            doc.ClearSelection2(true);
            context.BindOperationInputs(operation);
            ValidateInputs(context, operation);
            started = true;
            if (CreateNative(context, operation) is not IFeature feature)
                throw new NativeOperationException("GEOMETRY_IMPOSSIBLE", "Native feature creation returned no feature.");
            rebuilt = doc.ForceRebuild3(false);
            var error = feature.GetErrorCode2(out var warning);
            if (!rebuilt || error != 0 || warning || doc.Extension.NeedsRebuild2 != 0)
            { rebuilt = false; throw new NativeOperationException("FEATURE_REBUILD_FAILED", "Native feature status or rebuild failed."); }
            ValidateFeature(context, operation, feature);
            Register(context, operation, feature);
            return new(true, null, "Native feature created, rebuilt and checked against operation parameters.", operation.SemanticId, true, true);
        }
        catch (NativeOperationException error) { return new(false, error.Code, error.Message, operation.SemanticId, started, rebuilt); }
        catch (StateException error) { return new(false, error.Code, error.Message, operation.SemanticId, started, rebuilt); }
        catch (COMException error) { return new(false, "GEOMETRY_INVALID", $"SOLIDWORKS COM failure 0x{error.HResult:X8}: {error.Message}", operation.SemanticId, started, rebuilt); }
        finally { doc.ClearSelection2(true); }
    }
    protected static void Require(bool condition, string message)
    { if (!condition) throw new NativeOperationException("PARAMETER_NOT_APPLIED", message); }
    protected static bool Matches(double meters, double mm) => double.IsFinite(meters) && Math.Abs(meters * 1000 - mm) <= 1e-6;
}
