using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using V = CadHarness.Ir.V03.Vector3;

namespace CadHarness.SolidWorks;

public sealed record NativeSketchPrimitive(SketchPrimitive Geometry, NativePersistentReference Reference);
public sealed record NativeSketchDimension(string EntityId, ConstraintKind Kind, NativePersistentReference CircleReference);
public sealed record PlanarSketchBinding(Guid SessionId, string Configuration, string SemanticId, SpatialPlacement Placement,
    ClosedSketch Specification, NativePersistentReference Plane, NativePersistentReference Profile,
    IReadOnlyList<NativeSketchPrimitive> Primitives, IReadOnlyList<NativeSketchDimension> Dimensions, IReadOnlyList<double> NativeModelToSketchTransform, long Revision);
public sealed record SketchPrimitiveReadback(string EntityId, int Ordinal, V StartMm, V EndMm, V? CenterMm, double RadiusMm, double LengthMm);
public sealed record PlanarSketchReadback(int SolverStatus, IReadOnlyList<SketchPrimitiveReadback> Primitives,
    IReadOnlyDictionary<string, double> DimensionsMm, bool ReferencesStable);
public sealed record PlanarSketchTrace(string Stage, string Api, string? EntityId = null, double? Value = null, int? Status = null);

public sealed partial class SolidWorksExecutionContext
{
    public Action<PlanarSketchTrace>? SketchEvidenceSink { get; set; }
    internal void SketchTrace(string stage, string api, string? entity = null, double? value = null, int? status = null) => SketchEvidenceSink?.Invoke(new(stage, api, entity, value, status));
    public PlanarSketchBinding CreatePlanarSketch(ConstructionOperation operation)
    {
        CheckSketchSession();
        V03ContractCapabilities.RequirePlanarOperation(operation.Kind);
        ContractValidation.Require(operation.Kind == ConstructionKind.CreateSketch && ContractValidation.Id(operation.Id) && ContractValidation.Id(operation.SemanticId) &&
            !operation.SemanticId.StartsWith("world.", StringComparison.Ordinal) && operation.Placement is not null && operation.Sketch is not null &&
            operation.Profile is null && operation.Axis is null && operation.HostBody is null && operation.EndCondition is null && operation.Direction is null &&
            operation.Depth is null && operation.AngleDegrees is null && operation.BodyRule is null, "Expected a typed sketch operation without feature fields.");
        var prepared = PlanarSketchPreflight.Prepare(operation.Sketch!);
        ContractValidation.Placement(operation.Placement!);
        if (outputs.ContainsKey(operation.SemanticId) || outputs.ContainsKey(operation.SemanticId + ".profile")) throw new StateException("BINDING_AMBIGUOUS", "Sketch semantic identity already exists.");
        var plane = ResolveSketchPlane(operation.Placement!);
        // All decidable sketch, identity and frame checks precede checkpoint.
        var checkpoint = NativeConstructionCheckpoint.Capture(this);
        try
        {
            if (Math.Abs(operation.Placement!.Offset.Millimeters) > 1e-9)
                plane = CreateNativeOffsetPlane(plane, operation.Placement);
            var binding = PlanarSketchNative.Create(this, operation.SemanticId, operation.Placement, prepared, plane);
            _ = ReadPlanarSketch(binding);
            Register(operation.SemanticId, SemanticType.FeatureRef, operation.SemanticId, ResolveSketchFeature(binding));
            Register(operation.SemanticId + ".profile", SemanticType.SketchProfile, operation.SemanticId, ResolveSketchFeature(binding));
            var origin = SketchFrames.World(operation.Placement, new(0, 0));
            Register(operation.SemanticId + ".axis_x", SemanticType.ReferenceAxis, operation.SemanticId, ResolveSketchFeature(binding),
                new(new Point3(origin.X, origin.Y, origin.Z), new CadHarness.State.Vector3(operation.Placement.Frame.XAxis.X, operation.Placement.Frame.XAxis.Y, operation.Placement.Frame.XAxis.Z)));
            return binding;
        }
        catch
        {
            try { checkpoint.Restore(this); RebuildSketch(); checkpoint.Verify(this); }
            catch { RelationContextUsable = false; throw new StateException("ROLLBACK_FAILED", "Planar construction rollback failed; context quarantined."); }
            throw;
        }
        finally { Document.ClearSelection2(true); }
    }

    public NativePersistentReference CreateOffsetDatum(ConstructionOperation operation)
    {
        CheckSketchSession();
        V03ContractCapabilities.RequirePlanarOperation(operation.Kind);
        ContractValidation.Require(operation.Kind == ConstructionKind.CreateDatumPlane && ContractValidation.Id(operation.Id) && ContractValidation.Id(operation.SemanticId) &&
            !operation.SemanticId.StartsWith("world.", StringComparison.Ordinal) && operation.Placement is not null &&
            operation.Sketch is null && operation.Profile is null && operation.Axis is null && operation.HostBody is null && operation.Depth is null && operation.AngleDegrees is null && operation.Direction is null && operation.EndCondition is null && operation.BodyRule is null, "Expected a typed datum-plane operation.");
        var placement = operation.Placement!; ContractValidation.Placement(placement);
        ContractValidation.Require(Math.Abs(placement.Offset.Millimeters) > 1e-9, "Datum needs a nonzero explicit signed offset.");
        if (outputs.ContainsKey(operation.SemanticId + ".plane")) throw new StateException("BINDING_AMBIGUOUS", "Datum identity exists.");
        var plane = ResolveSketchPlane(placement); var checkpoint = NativeConstructionCheckpoint.Capture(this);
        try
        {
            var created = CreateNativeOffsetPlane(plane, placement);
            Register(operation.SemanticId, SemanticType.FeatureRef, operation.SemanticId, created);
            var origin = SketchFrames.World(placement, new(0, 0));
            Register(operation.SemanticId + ".plane", SemanticType.ReferencePlane, operation.SemanticId, created,
                new(new Point3(origin.X, origin.Y, origin.Z), new CadHarness.State.Vector3(placement.Frame.ZAxis.X, placement.Frame.ZAxis.Y, placement.Frame.ZAxis.Z)));
            return PersistentReferenceAdapter.Capture(this, created);
        }
        catch
        {
            try { checkpoint.Restore(this); RebuildSketch(); checkpoint.Verify(this); }
            catch { RelationContextUsable = false; throw new StateException("ROLLBACK_FAILED", "Datum rollback failed; context quarantined."); }
            throw;
        }
        finally { Document.ClearSelection2(true); }
    }

    public PlanarSketchReadback ReadPlanarSketch(PlanarSketchBinding binding)
    {
        CheckSketchBinding(binding); return PlanarSketchNative.Read(this, binding);
    }

    public PlanarSketchBinding EditSketchRadius(PlanarSketchBinding binding, string entityId, PositiveLength expectedRadius, PositiveLength radius)
    {
        CheckSketchSession(); CheckSketchBinding(binding); _ = ReadPlanarSketch(binding);
        ContractValidation.Require(ContractValidation.Length(radius.Millimeters) && ContractValidation.Length(expectedRadius.Millimeters), "Radius bounds invalid.");
        var entity = binding.Specification.Entities.SingleOrDefault(e => e.Id == entityId);
        ContractValidation.Require(entity?.Kind == SketchEntityKind.Circle && Math.Abs(entity.Radius!.Millimeters - expectedRadius.Millimeters) <= PlanarSketchPreflight.ToleranceMm, "Only an exact declared independent circle radius is editable.");
        var dimBinding = binding.Dimensions.Single(d => d.EntityId == entityId);
        var specification = binding.Specification with
        {
            Entities = binding.Specification.Entities.Select(e => e.Id == entityId ? e with { Radius = radius } : e).ToArray(),
            Constraints = binding.Specification.Constraints.Select(c => c.References.Count == 1 && c.References[0] == entityId && c.Kind is ConstraintKind.Radius or ConstraintKind.Diameter ?
                c with { Dimension = new(radius.Millimeters * (c.Kind == ConstraintKind.Diameter ? 2 : 1)) } : c).ToArray()
        };
        var prepared = PlanarSketchPreflight.Prepare(specification);
        var changed = binding with { Specification = specification, Primitives = binding.Primitives.Select(p => p with { Geometry = prepared.Primitives.Single(q => q.EntityId == p.Geometry.EntityId && q.Ordinal == p.Geometry.Ordinal) }).ToArray(), Revision = checked(binding.Revision + 1) };
        var dimension = PlanarSketchNative.Dimension(this, dimBinding);
        var old = dimension.SystemValue;
        try
        {
            var status = dimension.SetSystemValue3(radius.Millimeters * (dimBinding.Kind == ConstraintKind.Diameter ? 2 : 1) / 1000, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null!);
            SketchTrace("setter-return", "IDimension.SetSystemValue3", entityId, dimension.SystemValue * 1000, status);
            if (status != 0) throw new StateException("PARAMETER_NOT_APPLIED", "Sketch radial Setter status: " + status);
            RebuildSketch(); _ = ReadPlanarSketch(changed); return changed;
        }
        catch
        {
            try
            {
                var restoredDimension = PlanarSketchNative.Dimension(this, dimBinding);
                if (restoredDimension.SetSystemValue3(old, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null!) != 0)
                    throw new StateException("ROLLBACK_FAILED", "Sketch radius restore Setter failed.");
                RebuildSketch(); _ = ReadPlanarSketch(binding);
            }
            catch { RelationContextUsable = false; throw new StateException("ROLLBACK_FAILED", "Sketch radius restoration did not validate."); }
            throw;
        }
    }
    private void CheckSketchSession()
    {
        CheckThread();
        if (!RelationContextUsable || MutationInProgress || Document.GetActiveSketch2() is not null || Document.GetType() != (int)swDocumentTypes_e.swDocPART)
            throw new StateException("TRANSACTION_BUSY", "Planar construction needs a usable Part outside another transaction/sketch edit.");
    }
    private void CheckSketchBinding(PlanarSketchBinding binding)
    {
        CheckThread();
        if (!RelationContextUsable || binding.SessionId != ConstructionDocumentId || binding.Configuration != Document.ConfigurationManager.ActiveConfiguration.Name)
            throw new StateException("CONFIGURATION_MISMATCH", "Sketch binding does not belong to this session/configuration.");
        _ = ResolveSketchFeature(binding);
        var plane = PersistentReferenceAdapter.Resolve<object>(this, binding.Plane).NativeObject ?? throw new StateException("STALE_REFERENCE", "Sketch plane is stale.");
        var effective = binding.Placement with { Frame = binding.Placement.Frame with { OriginMm = SketchFrames.World(binding.Placement, new(0, 0)) }, Offset = new(0) };
        PlanarSketchNative.VerifyPlane(plane, effective, null);
    }
    private IFeature ResolveSketchFeature(PlanarSketchBinding binding) => PersistentReferenceAdapter.Resolve<IFeature>(this, binding.Profile).NativeObject ?? throw new StateException("STALE_REFERENCE", "Sketch profile is stale.");
    internal void RebuildSketch()
    {
        if (!Document.ForceRebuild3(false) || Document.Extension.NeedsRebuild2 != 0) throw new StateException("FEATURE_REBUILD_FAILED", "Planar rebuild failed.");
    }
    private object ResolveSketchPlane(SpatialPlacement placement)
    {
        ContractValidation.Placement(placement);
        object plane;
        if (placement.Plane.SemanticId.StartsWith("world.", StringComparison.Ordinal))
        {
            ContractValidation.Require(placement.Plane.Type == SemanticType.ReferencePlane && placement.Plane.SemanticId is "world.xy" or "world.xz" or "world.yz", "Unknown principal plane.");
            var axis = placement.Plane.SemanticId == "world.xy" ? 2 : placement.Plane.SemanticId == "world.xz" ? 1 : 0;
            var canonicalNormal = axis == 2 ? new V(0, 0, 1) : axis == 1 ? new V(0, 1, 0) : new V(1, 0, 0);
            SketchFrames.VerifyPlane(placement, new(0, 0, 0), canonicalNormal, null);
            var candidates = new List<IFeature>(); var f = (IFeature?)Document.FirstFeature();
            for (var i = 0; f is not null; i++, f = (IFeature?)f.GetNextFeature())
            {
                if (i >= 256) throw new StateException("OBSERVATION_LIMIT_EXCEEDED", "Plane inventory bound.");
                if (f.GetTypeName2() != "RefPlane") continue;
                var m = NativeGeometry.Doubles(((IRefPlane)f.GetSpecificFeature2()).Transform.ArrayData);
                if (Enumerable.Range(0, 3).All(j => Math.Abs(Math.Abs(m[6 + j]) - (j == axis ? 1 : 0)) < 1e-8) && Enumerable.Range(9, 3).All(j => Math.Abs(m[j]) < 1e-9)) candidates.Add(f);
            }
            plane = NativeTopology.Unique(candidates, "principal plane");
        }
        else
        {
            plane = Resolve<object>(placement.Plane);
            if (placement.Plane.Type == SemanticType.ReferencePlane && outputs.TryGetValue(placement.Plane.SemanticId, out var output) && output.LogicalGeometry?.Direction is { } normal)
                ContractValidation.Require(SketchFrames.Dot(placement.Frame.ZAxis, new(normal.X, normal.Y, normal.Z)) >= 1 - 1e-8, "Bound datum's signed normal changed.", "SPATIAL_FRAME_MISMATCH");
        }
        V? direction = null;
        if (placement.InPlaneDirection is { } reference)
        {
            var native = Resolve<object>(reference); double[] vector;
            if (native is IFeature feature && feature.GetSpecificFeature2() is IRefAxis) vector = NativePatternDirection.Vector(feature);
            else if (native is IEdge edge && edge.GetCurve() is ICurve curve && curve.IsLine() && NativeTopology.Endpoints(edge) is { } ends) vector = ends[1].Zip(ends[0], (a, b) => a - b).ToArray();
            else throw new StateException("BINDING_UNRESOLVED", "In-plane direction must be a healthy bound straight edge or datum axis.");
            var length = Math.Sqrt(vector.Sum(x => x * x)); if (length < 1e-12) throw new StateException("BINDING_UNRESOLVED", "Zero direction.");
            direction = new(vector[0] / length, vector[1] / length, vector[2] / length);
            if (SketchFrames.Dot(direction, placement.Frame.XAxis) < 0) direction = SketchFrames.Scale(direction, -1); // Native edge/axis is undirected; the explicit frame chooses its sign.
        }
        PlanarSketchNative.VerifyPlane(plane, placement, direction); return plane;
    }
    private IFeature CreateNativeOffsetPlane(object plane, SpatialPlacement placement)
    {
        NativeTopology.Select(Document, plane, false, 0);
        var options = (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance;
        if (placement.Offset.Millimeters < 0) options |= (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip;
        var created = (IFeature?)((IFeatureManager)Document.FeatureManager).InsertRefPlane(options, Math.Abs(placement.Offset.Millimeters) / 1000, 0, 0, 0, 0)
            ?? throw new StateException("GEOMETRY_INVALID", "Native offset plane creation failed.");
        RebuildSketch();
        var effective = placement with { Frame = placement.Frame with { OriginMm = SketchFrames.World(placement, new(0, 0)) }, Offset = new(0) };
        PlanarSketchNative.VerifyPlane(created, effective, null); return created;
    }
}

internal static class PlanarSketchNative
{
    internal static void VerifyPlane(object plane, SpatialPlacement placement, V? direction)
    {
        V normal; V point;
        if (plane is IFeature feature && feature.GetSpecificFeature2() is IRefPlane datum)
        {
            if (feature.GetErrorCode2(out var warning) != 0 || warning || feature.IsSuppressed()) throw new StateException("STALE_REFERENCE", "Datum is unhealthy.");
            var m = NativeGeometry.Doubles(datum.Transform.ArrayData);
            normal = new(m[6], m[7], m[8]); point = new(m[9] * 1000, m[10] * 1000, m[11] * 1000);
            // Reference planes are undirected. Principal/datum sign is supplied
            // explicitly by the typed right-handed frame, never by camera state.
            if (SketchFrames.Dot(normal, placement.Frame.ZAxis) < 0) normal = SketchFrames.Scale(normal, -1);
        }
        else if (plane is IFace2 face && face.GetSurface() is ISurface surface && surface.IsPlane())
        {
            var p = NativeGeometry.Doubles(surface.PlaneParams); normal = new(p[0], p[1], p[2]);
            if (face.FaceInSurfaceSense()) normal = SketchFrames.Scale(normal, -1);
            point = new(p[3] * 1000, p[4] * 1000, p[5] * 1000);
        }
        else throw new StateException("BINDING_UNRESOLVED", "Only native reference planes and planar managed faces are sketch planes.");
        SketchFrames.VerifyPlane(placement, point, normal, direction);
    }
    internal static V Transform(V point, double[] m) => new((point.X * m[0] + point.Y * m[3] + point.Z * m[6]) * m[12] + m[9],
        (point.X * m[1] + point.Y * m[4] + point.Z * m[7]) * m[12] + m[10], (point.X * m[2] + point.Y * m[5] + point.Z * m[8]) * m[12] + m[11]);
    internal static PlanarSketchBinding Create(SolidWorksExecutionContext context, string id, SpatialPlacement placement, PreparedPlanarSketch prepared, object plane)
    {
        var doc = context.Document; var manager = (ISketchManager)doc.SketchManager;
        // A rebuild can invalidate the original face COM handle. Freeze its
        // identity before mutation and resolve that exact reference afterward.
        var planeReference = PersistentReferenceAdapter.Capture(context, plane);
        var app = context.Application ?? throw new StateException("CAPABILITY_UNAVAILABLE", "Planar creation requires the STA connection's application context for scoped native dimension preferences.");
        var inputDimension = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
        var add = manager.AddToDB; var display = manager.DisplayWhenAdded; ISketch? sketch = null;
        var native = new List<(SketchPrimitive Geometry, ISketchSegment Native)>(); var dimensions = new List<(string EntityId, ConstraintKind Kind)>();
        try
        {
            app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            if (app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate)) throw new StateException("OPERATION_PRECONDITION_FAILED", "Cannot disable modal dimension input for scoped native creation.");
            context.SketchTrace("before", "ISketchManager.InsertSketch", id);
            doc.ClearSelection2(true); NativeTopology.Select(doc, plane, false, 0); manager.InsertSketch(false);
            sketch = (ISketch?)manager.ActiveSketch ?? throw new StateException("GEOMETRY_INVALID", "Native sketch was not entered.");
            var transform = NativeGeometry.Doubles(sketch.ModelToSketchTransform.ArrayData);
            V Local(SketchPoint2 point) { var p = Transform(SketchFrames.Scale(SketchFrames.World(placement, point), 0.001), transform); if (Math.Abs(p.Z) > 1e-8) throw new StateException("SPATIAL_FRAME_MISMATCH", "Requested geometry is not on the actual sketch plane."); return p; }
            manager.AddToDB = true; manager.DisplayWhenAdded = false;
            foreach (var p in prepared.Primitives)
            {
                context.SketchTrace("before", "ISketchManager.CreatePrimitive", p.EntityId);
                var a = Local(p.Start); var b = Local(p.End); ISketchSegment? segment;
                if (p.Center is { } center)
                {
                    var c = Local(center);
                    var nativeNormal = Transform(SketchFrames.Add(SketchFrames.Scale(placement.Frame.ZAxis, 0.001), SketchFrames.Scale(SketchFrames.World(placement, new(0, 0)), 0.001)), transform).Z;
                    var sense = Math.Sign(p.SweepRadians) * Math.Sign(nativeNormal);
                    segment = p.FullCircle ? manager.CreateCircleByRadius(c.X, c.Y, 0, p.RadiusMm / 1000) : manager.CreateArc(c.X, c.Y, 0, a.X, a.Y, 0, b.X, b.Y, 0, (short)sense);
                }
                else segment = manager.CreateLine(a.X, a.Y, 0, b.X, b.Y, 0);
                native.Add((p, segment ?? throw new StateException("GEOMETRY_INVALID", "Native primitive creation failed.")));
                context.SketchTrace("after", "ISketchManager.CreatePrimitive", p.EntityId);
            }
            manager.AddToDB = false; manager.DisplayWhenAdded = true;
            foreach (var item in native)
            {
                doc.ClearSelection2(true);
                var entity = prepared.Specification.Entities.Single(e => e.Id == item.Geometry.EntityId);
                if (entity.Kind == SketchEntityKind.Circle)
                {
                    var circle = (ISketchArc)item.Native;
                    context.SketchTrace("before", "IModelDoc2.SketchAddConstraints(center fixed)", entity.Id);
                    if (!((ISketchPoint)circle.GetCenterPoint2()).Select4(false, null!)) throw new StateException("BINDING_UNRESOLVED", "Circle center selection failed.");
                    doc.SketchAddConstraints("sgFIXED"); doc.ClearSelection2(true);
                    if (!item.Native.Select4(false, null!)) throw new StateException("BINDING_UNRESOLVED", "Circle dimension selection failed.");
                    var constraint = prepared.Specification.Constraints.Single(c => c.References.Count == 1 && c.References[0] == entity.Id && c.Kind is ConstraintKind.Radius or ConstraintKind.Diameter);
                    var center = Local(item.Geometry.Center!.Value);
                    context.SketchTrace("before", "IModelDoc2.AddRadialOrDiameterDimension2", entity.Id);
                    var displayDimension = (IDisplayDimension?)(constraint.Kind == ConstraintKind.Diameter ? doc.AddDiameterDimension2(center.X + item.Geometry.RadiusMm / 1000, center.Y, 0) : doc.AddRadialDimension2(center.X + item.Geometry.RadiusMm / 1000, center.Y, 0));
                    var dimension = displayDimension?.GetDimension2(0) ?? throw new StateException("GEOMETRY_INVALID", "Declared radial dimension creation failed.");
                    context.SketchTrace("after", "IModelDoc2.AddRadialOrDiameterDimension2", entity.Id, dimension.SystemValue * 1000);
                    if (dimension.SetSystemValue3(constraint.Dimension!.Millimeters / 1000, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null!) != 0) throw new StateException("PARAMETER_NOT_APPLIED", "Declared radial driving dimension rejected.");
                    // IDimension itself is not a persistent entity in this API.
                    // Resolve its native ownership through the persistent circle
                    // and dimension relation, never by name or scalar value.
                    dimensions.Add((entity.Id, constraint.Kind));
                }
                else
                {
                    context.SketchTrace("before", "IModelDoc2.SketchAddConstraints(coordinate fixed)", entity.Id);
                    if (!item.Native.Select4(false, null!)) throw new StateException("BINDING_UNRESOLVED", "Coordinate-driver selection failed.");
                    doc.SketchAddConstraints("sgFIXED");
                    context.SketchTrace("after", "IModelDoc2.SketchAddConstraints(coordinate fixed)", entity.Id);
                }
            }
        }
        finally
        {
            manager.AddToDB = add; manager.DisplayWhenAdded = display;
            try { if (manager.ActiveSketch is not null) manager.InsertSketch(false); doc.ClearSelection2(true); }
            finally { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, inputDimension); }
        }
        context.RebuildSketch();
        context.SketchTrace("rebuilt", "ISketch.GetConstrainedStatus", id, status: sketch!.GetConstrainedStatus());
        var feature = CenteredRectangleProfileBackend.FeatureForSketch(doc, sketch!);
        var resolvedPlane = PersistentReferenceAdapter.Resolve<object>(context, planeReference).NativeObject
            ?? throw new StateException("STALE_REFERENCE", "Sketch host reference changed during creation.");
        var effective = placement with { Frame = placement.Frame with { OriginMm = SketchFrames.World(placement, new(0, 0)) }, Offset = new(0) };
        VerifyPlane(resolvedPlane, effective, null);
        return new(context.ConstructionDocumentId, doc.ConfigurationManager.ActiveConfiguration.Name, id, placement, prepared.Specification,
            planeReference, PersistentReferenceAdapter.Capture(context, feature),
            native.Select(p => new NativeSketchPrimitive(p.Geometry, PersistentReferenceAdapter.Capture(context, p.Native))).ToArray(),
            dimensions.Select(d => new NativeSketchDimension(d.EntityId, d.Kind, PersistentReferenceAdapter.Capture(context, native.Single(p => p.Geometry.EntityId == d.EntityId).Native))).ToArray(), NativeGeometry.Doubles(sketch!.ModelToSketchTransform.ArrayData), 0);
    }
    internal static IDimension Dimension(SolidWorksExecutionContext context, NativeSketchDimension binding)
    {
        var circle = PersistentReferenceAdapter.Resolve<ISketchSegment>(context, binding.CircleReference).NativeObject;
        if (circle is not ISketchArc arc || arc.IsCircle() != 1) throw new StateException("STALE_REFERENCE", "Dimension's exact native circle is unresolved.");
        var type = binding.Kind == ConstraintKind.Diameter ? (int)swDimensionType_e.swDiameterDimension : (int)swDimensionType_e.swRadialDimension;
        var matches = NativeTopology.Objects<ISketchRelation>(circle.GetRelations()).Select(r => r.GetDisplayDimension() as IDisplayDimension).Where(d => d is not null && d.Type2 == type).ToArray();
        if (matches.Length != 1) throw new StateException("AMBIGUOUS_NATIVE_DIMENSION", "Persistent circle must have one exact native driving radial relation.");
        var dimension = (IDimension)matches[0]!.GetDimension2(0);
        if (dimension.ReadOnly || dimension.IsDesignTableDimension() || dimension.DrivenState != (int)swDimensionDrivenState_e.swDimensionDriving) throw new StateException("UNSUPPORTED_PARAMETER_DRIVER", "Radial dimension is not an independent driving dimension.");
        return dimension;
    }
    internal static PlanarSketchReadback Read(SolidWorksExecutionContext context, PlanarSketchBinding binding)
    {
        var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, binding.Profile).NativeObject!;
        if (feature.GetTypeName2() != "ProfileFeature" || feature.GetErrorCode2(out var warning) != 0 || warning || feature.IsSuppressed()) throw new StateException("STALE_REFERENCE", "Profile changed type/health.");
        var sketch = (ISketch)feature.GetSpecificFeature2(); var status = sketch.GetConstrainedStatus();
        var transform = NativeGeometry.Doubles(sketch.ModelToSketchTransform.ArrayData);
        if (transform.Length != binding.NativeModelToSketchTransform.Count || transform.Where((v, i) => Math.Abs(v - binding.NativeModelToSketchTransform[i]) > 1e-8).Any()) throw new StateException("SPATIAL_FRAME_MISMATCH", "Native sketch transform changed sign/orientation/placement.");
        if (status != (int)swConstrainedStatus_e.swFullyConstrained) throw new StateException(status == (int)swConstrainedStatus_e.swUnderConstrained ? V03FailureCodes.UnderConstrainedSketch : V03FailureCodes.OverConstrainedSketch, "Native solver status: " + status);
        var inverse = NativeGeometry.Doubles(sketch.ModelToSketchTransform.IInverse().ArrayData);
        V World(ISketchPoint point) => SketchFrames.Scale(Transform(new(point.X, point.Y, point.Z), inverse), 1000);
        void Near(V a, V b) { if (Math.Sqrt(SketchFrames.Dot(SketchFrames.Subtract(a, b), SketchFrames.Subtract(a, b))) > ContractLimits.LinearToleranceMm) throw new StateException("STATE_DRIFT_DETECTED", "Native transformed world geometry differs."); }
        var observed = new List<SketchPrimitiveReadback>();
        foreach (var p in binding.Primitives)
        {
            var native = PersistentReferenceAdapter.Resolve<ISketchSegment>(context, p.Reference).NativeObject ?? throw new StateException("STALE_REFERENCE", "Primitive reference did not resolve.");
            if (PersistentReferenceAdapter.Capture(context, native) != p.Reference || PersistentReferenceAdapter.Capture(context, CenteredRectangleProfileBackend.FeatureForSketch(context.Document, native.GetSketch())) != binding.Profile) throw new StateException("BINDING_AMBIGUOUS", "Primitive ownership/reference changed.");
            V a; V b; V? c = null; var radius = 0.0;
            if (p.Geometry.Circular && native is ISketchArc arc)
            {
                c = World((ISketchPoint)arc.GetCenterPoint2()); radius = arc.GetRadius() * 1000;
                Near(c, SketchFrames.World(binding.Placement, p.Geometry.Center!.Value));
                if (Math.Abs(radius - p.Geometry.RadiusMm) > ContractLimits.LinearToleranceMm) throw new StateException("STATE_DRIFT_DETECTED", "Native radius differs.");
                if (p.Geometry.FullCircle) { if (arc.IsCircle() != 1) throw new StateException("STATE_DRIFT_DETECTED", "Circle became arc."); a = b = SketchFrames.World(binding.Placement, p.Geometry.Start); }
                else
                {
                    a = World((ISketchPoint)arc.GetStartPoint2()); b = World((ISketchPoint)arc.GetEndPoint2());
                    var normalInSketch = Transform(SketchFrames.Scale(binding.Placement.Frame.ZAxis, 0.001), transform).Z - transform[11];
                    var expectedDirection = Math.Sign(p.Geometry.SweepRadians) * Math.Sign(normalInSketch);
                    if (arc.GetRotationDir() != expectedDirection) throw new StateException("STATE_DRIFT_DETECTED", "Native arc traverses the opposite signed sweep.");
                }
            }
            else if (!p.Geometry.Circular && native is ISketchLine line) { a = World((ISketchPoint)line.GetStartPoint2()); b = World((ISketchPoint)line.GetEndPoint2()); }
            else throw new StateException("STATE_DRIFT_DETECTED", "Primitive native subtype changed.");
            if (!p.Geometry.FullCircle)
            {
                Near(a, SketchFrames.World(binding.Placement, p.Geometry.Start)); Near(b, SketchFrames.World(binding.Placement, p.Geometry.End));
            }
            var expectedLength = p.Geometry.Circular ? p.Geometry.RadiusMm * Math.Abs(p.Geometry.SweepRadians) : (p.Geometry.End - p.Geometry.Start).Length;
            var length = native.GetLength() * 1000;
            if (Math.Abs(length - expectedLength) > ContractLimits.LinearToleranceMm) throw new StateException("STATE_DRIFT_DETECTED", "Native primitive length/sweep differs.");
            observed.Add(new(p.Geometry.EntityId, p.Geometry.Ordinal, a, b, c, radius, length));
        }
        var segments = NativeTopology.Objects<ISketchSegment>(sketch.GetSketchSegments());
        if (segments.Count != binding.Primitives.Count) throw new StateException("STATE_DRIFT_DETECTED", "Unexpected native sketch geometry.");
        var dims = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var d in binding.Dimensions)
        {
            var dimension = Dimension(context, d);
            var value = dimension.SystemValue * 1000;
            var expected = binding.Specification.Constraints.Single(c => c.References.Count == 1 && c.References[0] == d.EntityId && c.Kind == d.Kind).Dimension!.Millimeters;
            if (Math.Abs(value - expected) > ContractLimits.LinearToleranceMm) throw new StateException("STATE_DRIFT_DETECTED", "Driving dimension differs.");
            dims.Add(d.EntityId, value);
        }
        return new(status, observed, dims, true);
    }
}
