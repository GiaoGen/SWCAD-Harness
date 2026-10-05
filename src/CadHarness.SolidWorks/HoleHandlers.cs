using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public sealed class CreateThroughHoleHandler : HoleHandler
{ public override OperationKind Kind => OperationKind.CreateThroughHole; }
public sealed class CreateBlindHoleHandler : HoleHandler
{ public override OperationKind Kind => OperationKind.CreateBlindHole; }

public abstract class HoleHandler : NativeFeatureHandler
{
    public override PreflightResult Preflight(OperationNode operation)
    {
        var check = base.Preflight(operation);
        if (!check.IsValid) return check;
        if (operation.Input("host")!.References[0].Type != SemanticType.PlanarFace)
            return FeaturePreflight.Failure("M4 holes require a constructed planar host face.");
        if (operation.Parameters.TryGetValue("placement", out var p) && p is PlacementParameter placement &&
            (!double.IsFinite(placement.Value.XMm / 1000) || !double.IsFinite(placement.Value.YMm / 1000)))
            return FeaturePreflight.Failure("Placement cannot be represented in native units.");
        return check;
    }
    protected override void ValidateInputs(SolidWorksExecutionContext context, OperationNode operation)
    {
        var host = operation.Input("host")!.References[0];
        var face = context.Resolve<IFace2>(host);
        var root = context.Operation(context.Owner(host.SemanticId));
        if (root.Kind != OperationKind.CreateExtrude || host.SemanticId != root.SemanticId + ".top_face")
            throw new NativeOperationException(FailureCodes.PreconditionFailed, "M4 hole placement uses the initial extrusion's XY top face and local origin.");
        var thickness = root.Parameter<LengthParameter>("depthMm").Millimeters;
        if (!NativeTopology.IsZPlane(face, thickness / 1000))
            throw new NativeOperationException(FailureCodes.PreconditionFailed, "Hole host is no longer the supported XY top face.");
        var profile = root.Parameter<ProfileParameter>("profile").Value;
        var placement = Placement(operation);
        var radius = operation.Parameter<LengthParameter>("diameterMm").Millimeters / 2;
        if (!ProfileGeometry.ContainsHole(profile, placement, radius))
            throw new NativeOperationException(FailureCodes.PreconditionFailed, "Hole circle must lie strictly inside the host profile.");
        if (Kind == OperationKind.CreateBlindHole && operation.Parameter<LengthParameter>("depthMm").Millimeters >= thickness)
            throw new NativeOperationException(FailureCodes.PreconditionFailed, "Blind-hole depth must be less than the host thickness.");
    }
    protected override object? CreateNative(SolidWorksExecutionContext context, OperationNode operation)
    {
        var doc = context.Document;
        var host = operation.Input("host")!.References[0];
        NativeTopology.Select(doc, context.Resolve<IFace2>(host), false, 0);
        var manager = (ISketchManager)doc.SketchManager;
        var addToDb = manager.AddToDB;
        var display = manager.DisplayWhenAdded;
        ISketch? sketch = null;
        try
        {
            manager.InsertSketch(false);
            sketch = (ISketch?)manager.ActiveSketch ?? throw new NativeOperationException("GEOMETRY_INVALID", "Circle sketch creation failed.");
            manager.AddToDB = true; manager.DisplayWhenAdded = false;
            var placement = Placement(operation);
            var z = context.Operation(context.Owner(host.SemanticId)).Parameter<LengthParameter>("depthMm").Millimeters / 1000;
            var matrix = NativeGeometry.Doubles(sketch.ModelToSketchTransform.ArrayData);
            var x = placement.XMm / 1000; var y = placement.YMm / 1000;
            var sx = x * matrix[0] + y * matrix[3] + z * matrix[6] + matrix[9];
            var sy = x * matrix[1] + y * matrix[4] + z * matrix[7] + matrix[10];
            var sz = x * matrix[2] + y * matrix[5] + z * matrix[8] + matrix[11];
            if (!NativeTopology.Near(matrix[12], 1) || !NativeTopology.Near(sz, 0))
                throw new NativeOperationException(FailureCodes.PreconditionFailed, "Unsupported host sketch transform.");
            if (manager.CreateCircleByRadius(sx, sy, 0, Millimeters.ToMeters(operation.Parameter<LengthParameter>("diameterMm").Millimeters) / 2) is null)
                throw new NativeOperationException("GEOMETRY_INVALID", "Native circle creation failed.");
        }
        finally
        {
            manager.DisplayWhenAdded = display; manager.AddToDB = addToDb;
            if (manager.ActiveSketch is not null) manager.InsertSketch(false);
            doc.ClearSelection2(true);
        }
        var profile = CenteredRectangleProfileBackend.FeatureForSketch(doc, sketch!);
        context.RegisterHoleProfile(operation.SemanticId!, profile);
        NativeTopology.Select(doc, profile, false, 0);
        var end = Kind == OperationKind.CreateThroughHole ? swEndConditions_e.swEndCondThroughAll : swEndConditions_e.swEndCondBlind;
        var depth = Kind == OperationKind.CreateBlindHole ? Millimeters.ToMeters(operation.Parameter<LengthParameter>("depthMm").Millimeters) : 0;
        return ((IFeatureManager)doc.FeatureManager).FeatureCut4(true, false, false, (int)end, (int)swEndConditions_e.swEndCondBlind,
            depth, 0, false, false, false, false, 0, 0, false, false, false, false, false,
            true, true, false, false, false, (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
    }
    protected override void ValidateFeature(SolidWorksExecutionContext context, OperationNode operation, object native)
    {
        var feature = (IFeature)native;
        Require(feature.GetDefinition() is IExtrudeFeatureData2, "Cut definition is missing.");
        var data = (IExtrudeFeatureData2)feature.GetDefinition();
        var end = Kind == OperationKind.CreateThroughHole ? swEndConditions_e.swEndCondThroughAll : swEndConditions_e.swEndCondBlind;
        Require(data.GetEndCondition(true) == (int)end, "Hole end condition differs from the operation.");
        if (Kind == OperationKind.CreateBlindHole)
            Require(Matches(data.GetDepth(true), operation.Parameter<LengthParameter>("depthMm").Millimeters), "Blind depth differs.");
        var walls = NativeTopology.FeatureFaces(feature).Where(f => f.GetSurface() is ISurface s && s.IsCylinder()).ToArray();
        Require(walls.Length == 1, "Hole must create one cylindrical wall.");
        var cylinder = NativeGeometry.Doubles(((ISurface)walls[0].GetSurface()).CylinderParams);
        Require(Matches(cylinder[6] * 2, operation.Parameter<LengthParameter>("diameterMm").Millimeters), "Native hole diameter differs.");
        var p = Placement(operation);
        Require(Matches(cylinder[0], p.XMm) && Matches(cylinder[1], p.YMm) && NativeTopology.Near(Math.Abs(cylinder[5]), 1), "Native hole axis or placement differs.");
    }
    protected override void Register(SolidWorksExecutionContext context, OperationNode operation, object feature) => context.RegisterHole(operation, (IFeature)feature);
    private static Point2D Placement(OperationNode operation) => operation.Parameters.TryGetValue("placement", out var value) ? ((PlacementParameter)value).Value : new(0, 0);
}
