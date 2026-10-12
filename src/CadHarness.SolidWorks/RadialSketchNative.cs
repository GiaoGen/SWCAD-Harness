using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using V = CadHarness.Ir.V03.Vector3;

namespace CadHarness.SolidWorks;

internal static class RadialSketchNative
{
    internal static PlanarSketchBinding Create(SolidWorksExecutionContext c, string id, SpatialPlacement placement, PreparedPlanarSketch prepared, object plane)
    {
        RadialProfilePolicy.Validate(prepared.Specification);
        var doc = c.Document; var manager = (ISketchManager)doc.SketchManager;
        var app = c.Application!; var input = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
        var add = manager.AddToDB; var display = manager.DisplayWhenAdded;
        var planeRef = PersistentReferenceAdapter.Capture(c, plane);
        var lines = new List<ISketchLine>(); var dimensions = new List<NativeRadialDimension>();
        ISketch? sketch = null; ISketchPoint? origin = null;
        try
        {
            app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            if (app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate)) throw new StateException("OPERATION_PRECONDITION_FAILED", "Cannot disable dimension dialog.");
            doc.ClearSelection2(true); NativeTopology.Select(doc, plane, false, 0); manager.InsertSketch(false);
            sketch = (ISketch)manager.ActiveSketch;
            var matrix = NativeGeometry.Doubles(sketch.ModelToSketchTransform.ArrayData);
            V Local(SketchPoint2 p) => PlanarSketchNative.Transform(SketchFrames.Scale(SketchFrames.World(placement, p), .001), matrix);
            var zero = Local(new(0, 0)); var x = SketchFrames.Subtract(Local(new(1, 0)), zero); var y = SketchFrames.Subtract(Local(new(0, 1)), zero);
            var radialVertical = Math.Abs(y.Y) > .000999;
            if (Math.Abs(zero.Z) > 1e-8 || !(radialVertical ? Math.Abs(x.X) > .000999 : Math.Abs(x.Y) > .000999 && Math.Abs(y.X) > .000999))
                throw new StateException("SPATIAL_FRAME_MISMATCH", "Radial driver frame must align with native sketch axes.");
            manager.AddToDB = true; manager.DisplayWhenAdded = false;
            origin = manager.CreatePoint(zero.X, zero.Y, 0);
            foreach (var p in prepared.Primitives)
            {
                var a = Local(p.Start); var b = Local(p.End);
                lines.Add((ISketchLine)(manager.CreateLine(a.X, a.Y, 0, b.X, b.Y, 0) ?? throw new StateException("GEOMETRY_INVALID", "Native radial line failed.")));
            }
            manager.AddToDB = false; manager.DisplayWhenAdded = true;
            void Relation(string kind, params object[] entities)
            {
                if (kind == "sgCOINCIDENT")
                {
                    var first = PersistentReferenceAdapter.Capture(c, entities[0]); var second = PersistentReferenceAdapter.Capture(c, entities[1]);
                    c.FeatureTrace(new("coincidence-identities", id, first.Base64 + "/" + second.Base64, Success: first == second));
                    // CreateLine may share an endpoint even with AddToDB set.
                    // A shared native point needs no redundant coincidence.
                    if (first == second) return;
                }
                var type = kind switch
                {
                    "sgFIXED" => swConstraintType_e.swConstraintType_FIXED,
                    "sgCOINCIDENT" => swConstraintType_e.swConstraintType_COINCIDENT,
                    "sgHORIZONTAL" => entities.All(e => e is ISketchPoint) ? swConstraintType_e.swConstraintType_HORIZPOINTS : swConstraintType_e.swConstraintType_HORIZONTAL,
                    "sgVERTICAL" => entities.All(e => e is ISketchPoint) ? swConstraintType_e.swConstraintType_VERTPOINTS : swConstraintType_e.swConstraintType_VERTICAL,
                    _ => throw new InvalidOperationException("Unsupported radial relation.")
                };
                var token = type switch
                {
                    swConstraintType_e.swConstraintType_HORIZONTAL => "sgHORIZONTAL2D",
                    swConstraintType_e.swConstraintType_VERTICAL => "sgVERTICAL2D",
                    swConstraintType_e.swConstraintType_HORIZPOINTS => "sgHORIZONTALPOINTS2D",
                    swConstraintType_e.swConstraintType_VERTPOINTS => "sgVERTICALPOINTS2D",
                    _ => kind
                };
                doc.ClearSelection2(true);
                foreach (var e in entities)
                {
                    var ok = e is ISketchPoint point ? point.Select4(true, null!) : ((ISketchSegment)e).Select4(true, null!);
                    if (!ok) throw new StateException("BINDING_UNRESOLVED", "Radial constraint selection failed.");
                }
                c.SketchTrace("before-relation", "IModelDoc2.SketchAddConstraints/" + token, id);
                doc.SketchAddConstraints(token);
                var created = entities.SelectMany(e => NativeTopology.Objects<ISketchRelation>(e is ISketchPoint p ? p.GetRelations() : ((ISketchSegment)e).GetRelations()))
                    .Any(r => r.GetRelationType() == (int)type);
                c.SketchTrace("relation", "IModelDoc2.SketchAddConstraints/" + token, id, status: created ? (int)type : -1);
                if (!created) throw new StateException("GEOMETRY_INVALID", "Native radial relation was not created.");
            }
            Relation("sgFIXED", origin);
            for (var i = 0; i < lines.Count; i++)
            {
                Relation("sgCOINCIDENT", lines[i].GetEndPoint2(), lines[(i + 1) % lines.Count].GetStartPoint2());
                var horizontal = Math.Abs(prepared.Primitives[i].Start.Y - prepared.Primitives[i].End.Y) < 1e-7;
                Relation(horizontal == radialVertical ? "sgHORIZONTAL" : "sgVERTICAL", lines[i]);
            }
            void Dimension(int ordinal, string identity, bool radial, double value)
            {
                var point = (ISketchPoint)lines[ordinal].GetStartPoint2();
                var vertical = radial ? radialVertical : !radialVertical;
                if (value == 0) { Relation(vertical ? "sgHORIZONTAL" : "sgVERTICAL", origin, point); return; }
                doc.ClearSelection2(true);
                if (!origin.Select4(false, null!) || !point.Select4(true, null!)) throw new StateException("BINDING_UNRESOLVED", "Radial dimension selection failed.");
                var p = Local(prepared.Primitives[ordinal].Start);
                var displayDim = (IDisplayDimension?)(vertical ? doc.AddVerticalDimension2(p.X + .005, p.Y, 0) : doc.AddHorizontalDimension2(p.X, p.Y + .005, 0));
                var dim = displayDim?.GetDimension2(0) ?? throw new StateException("GEOMETRY_INVALID", "Native orthogonal driving dimension failed.");
                var status = dim.SetSystemValue3(Math.Abs(value) / 1000, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null!);
                c.SketchTrace("create-driver", "IDimension.SetSystemValue3", identity, dim.SystemValue * 1000, status);
                if (status != 0 || dim.DrivenState != (int)swDimensionDrivenState_e.swDimensionDriving) throw new StateException("UNSUPPORTED_PARAMETER_DRIVER", "Native dimension is not independently driving.");
                dimensions.Add(new(identity, radial, ordinal, displayDim!.Type2, PersistentReferenceAdapter.Capture(c, point)));
            }
            var bindings = prepared.Specification.RadialProfile!.Points.ToDictionary(p => p.PointId, p => p.DriverId);
            var ordered = prepared.Specification.Entities.Single().Points;
            var rails = Enumerable.Range(0, lines.Count).Where(i => Math.Abs(prepared.Primitives[i].Start.Y - prepared.Primitives[i].End.Y) < 1e-7);
            foreach (var group in rails.GroupBy(i => bindings[ordered[i]]))
            {
                var primary = group.First(); var point = (ISketchPoint)lines[primary].GetStartPoint2();
                foreach (var other in group.Skip(1)) Relation(radialVertical ? "sgHORIZONTAL" : "sgVERTICAL", point, lines[other].GetStartPoint2());
                Dimension(primary, group.Key ?? "baseline", true, prepared.Primitives[primary].Start.Y);
            }
            var risers = Enumerable.Range(0, lines.Count).Where(i => Math.Abs(prepared.Primitives[i].Start.X - prepared.Primitives[i].End.X) < 1e-7);
            foreach (var group in risers.GroupBy(i => prepared.Primitives[i].Start.X))
            {
                var primary = group.First(); var point = (ISketchPoint)lines[primary].GetStartPoint2();
                foreach (var other in group.Skip(1)) Relation(radialVertical ? "sgVERTICAL" : "sgHORIZONTAL", point, lines[other].GetStartPoint2());
                Dimension(primary, "station_" + primary, false, group.Key);
            }
        }
        finally
        {
            manager.AddToDB = add; manager.DisplayWhenAdded = display;
            try { if (manager.ActiveSketch is not null) manager.InsertSketch(false); doc.ClearSelection2(true); }
            finally { app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, input); }
        }
        c.RebuildSketch(); var feature = CenteredRectangleProfileBackend.FeatureForSketch(doc, sketch!);
        c.SketchTrace("radial-solver", "ISketch.GetConstrainedStatus", id, status: sketch!.GetConstrainedStatus());
        return new(c.ConstructionDocumentId, doc.ConfigurationManager.ActiveConfiguration.Name, id, placement, prepared.Specification,
            planeRef, PersistentReferenceAdapter.Capture(c, feature), prepared.Primitives.Select((p, i) => new NativeSketchPrimitive(p, PersistentReferenceAdapter.Capture(c, lines[i]))).ToArray(),
            Array.Empty<NativeSketchDimension>(), NativeGeometry.Doubles(sketch.ModelToSketchTransform.ArrayData), 0)
        { Radial = new(PersistentReferenceAdapter.Capture(c, origin!), dimensions) };
    }
    internal static IDimension ResolveDimension(SolidWorksExecutionContext c, PlanarSketchBinding binding, NativeRadialDimension d)
    {
        var matrix = binding.NativeModelToSketchTransform.ToArray();
        var zero = PlanarSketchNative.Transform(SketchFrames.Scale(SketchFrames.World(binding.Placement, new(0, 0)), .001), matrix);
        var radialVector = SketchFrames.Subtract(PlanarSketchNative.Transform(SketchFrames.Scale(SketchFrames.World(binding.Placement, new(0, 1)), .001), matrix), zero);
        var vertical = d.Radial ? Math.Abs(radialVector.Y) > .000999 : Math.Abs(radialVector.X) > .000999;
        if (d.NativeDimensionType != (int)(vertical ? swDimensionType_e.swVertLinearDimension : swDimensionType_e.swHorLinearDimension))
            throw new StateException("UNSUPPORTED_PARAMETER_DRIVER", "Radial dimension orientation differs from the exact declared frame.");
        var point = PersistentReferenceAdapter.Resolve<ISketchPoint>(c, d.Point).NativeObject ?? throw new StateException("STALE_REFERENCE", "Radial driving point is stale.");
        var origin = PersistentReferenceAdapter.Resolve<ISketchPoint>(c, binding.Radial!.Origin).NativeObject ?? throw new StateException("STALE_REFERENCE", "Radial origin is stale.");
        var primitive = (ISketchLine)PersistentReferenceAdapter.Resolve<ISketchSegment>(c, binding.Primitives.Single(p => p.Geometry.Ordinal == d.PrimitiveOrdinal).Reference).NativeObject!;
        if (PersistentReferenceAdapter.Capture(c, primitive.GetStartPoint2()) != d.Point ||
            PersistentReferenceAdapter.Capture(c, CenteredRectangleProfileBackend.FeatureForSketch(c.Document, point.GetSketch())) != binding.Profile ||
            PersistentReferenceAdapter.Capture(c, CenteredRectangleProfileBackend.FeatureForSketch(c.Document, origin.GetSketch())) != binding.Profile)
            throw new StateException("BINDING_AMBIGUOUS", "Radial point/profile ownership differs.");
        var dimensions = NativeTopology.Objects<ISketchRelation>(point.GetRelations()).Where(r =>
            NativeTopology.Objects<ISketchPoint>(r.GetEntities()).Any(p => PersistentReferenceAdapter.Capture(c, p) == binding.Radial.Origin))
            .Select(r => r.GetDisplayDimension() as IDisplayDimension).Where(dimension => dimension?.Type2 == d.NativeDimensionType).ToArray();
        if (dimensions.Length != 1) throw new StateException("AMBIGUOUS_NATIVE_DIMENSION", "Radial point/anchor needs one exact native dimension relation.");
        var dimension = (IDimension)dimensions[0]!.GetDimension2(0);
        if (dimension.ReadOnly || dimension.IsDesignTableDimension() || dimension.DrivenState != (int)swDimensionDrivenState_e.swDimensionDriving)
            throw new StateException("UNSUPPORTED_PARAMETER_DRIVER", "Radial/axial dimension is not an independent native driver.");
        return dimension;
    }
    internal static IReadOnlyDictionary<string, double> Read(SolidWorksExecutionContext c, PlanarSketchBinding binding)
    {
        GenericRevisionValidation.ValidateRadialImage(binding.Specification, binding.Radial!);
        var origin = PersistentReferenceAdapter.Resolve<ISketchPoint>(c, binding.Radial!.Origin).NativeObject!;
        var inv = NativeGeometry.Doubles(origin.GetSketch().ModelToSketchTransform.IInverse().ArrayData);
        var world = SketchFrames.Scale(PlanarSketchNative.Transform(new(origin.X, origin.Y, origin.Z), inv), 1000);
        if (Math.Sqrt(SketchFrames.Dot(SketchFrames.Subtract(world, SketchFrames.World(binding.Placement, new(0, 0))), SketchFrames.Subtract(world, SketchFrames.World(binding.Placement, new(0, 0))))) > ContractLimits.LinearToleranceMm)
            throw new StateException("STATE_DRIFT_DETECTED", "Radial origin drifted.");
        var result = new Dictionary<string, double>();
        foreach (var d in binding.Radial.Dimensions)
        {
            var expected = d.Radial ? binding.Specification.RadialProfile!.Drivers.Single(p => p.Id == d.Id).Radius.Millimeters :
                Math.Abs(binding.Primitives.Single(p => p.Geometry.Ordinal == d.PrimitiveOrdinal).Geometry.Start.X);
            var value = ResolveDimension(c, binding, d).SystemValue * 1000;
            if (Math.Abs(value - expected) > ContractLimits.LinearToleranceMm) throw new StateException("STATE_DRIFT_DETECTED", "Radial/station native driver differs.");
            if (d.Radial) result.Add(d.Id, value);
        }
        return result;
    }
    internal static PlanarSketchBinding Edit(SolidWorksExecutionContext c, PlanarSketchBinding binding, ClosedSketch changed)
    {
        _ = c.ReadPlanarSketch(binding); var prepared = PlanarSketchPreflight.Prepare(changed);
        var updated = binding with { Specification = changed, Primitives = binding.Primitives.Select(p => p with { Geometry = prepared.Primitives.Single(q => q.Ordinal == p.Geometry.Ordinal) }).ToArray(), Revision = binding.Revision + 1 };
        foreach (var id in RadialProfilePolicy.NativeOrder(binding.Specification, changed))
        {
            var driver = changed.RadialProfile!.Drivers.Single(d => d.Id == id);
            var old = binding.Specification.RadialProfile!.Drivers.Single(d => d.Id == driver.Id).Radius.Millimeters;
            if (old == driver.Radius.Millimeters) continue;
            var dimension = ResolveDimension(c, binding, binding.Radial!.Dimensions.Single(d => d.Radial && d.Id == driver.Id));
            var status = dimension.SetSystemValue3(driver.Radius.Millimeters / 1000, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null!);
            c.SketchTrace("radial-setter-return", "IDimension.SetSystemValue3", driver.Id, dimension.SystemValue * 1000, status);
            if (status != 0) throw new StateException("PARAMETER_NOT_APPLIED", "Radial native driver Setter rejected.");
            c.FeatureTrace(new("radial-driver-applied", driver.Id, "IDimension.SetSystemValue3", old, driver.Radius.Millimeters, true));
        }
        c.RebuildSketch(); _ = c.ReadPlanarSketch(updated); return updated;
    }
    internal static void Restore(SolidWorksExecutionContext c, PlanarSketchBinding binding, ClosedSketch original, IReadOnlyDictionary<string, double> savedValues)
    {
        var live = original.RadialProfile!.Drivers.ToDictionary(d => d.Id, d => ResolveDimension(c, binding,
            binding.Radial!.Dimensions.Single(p => p.Radial && p.Id == d.Id)).SystemValue * 1000);
        // Remove rule assertions only in this temporary rollback shape proof;
        // the authoritative program and its rules are restored by checkpoint.
        var owners = original.RadialProfile.Points.ToDictionary(p => p.PointId, p => p.DriverId);
        var current = original with { Points = original.Points.Select(p => p with { Y = new(owners[p.Id] is { } id ? live[id] : 0) }).ToArray(),
            RadialProfile = original.RadialProfile with { Drivers = original.RadialProfile.Drivers.Select(d => d with { Radius = new(live[d.Id]) }).ToArray(), Relations = Array.Empty<RadialOffsetRelation>() } };
        var restored = original with { Points = original.Points.Select(p => p with { Y = new(owners[p.Id] is { } id ? savedValues[id] : 0) }).ToArray(),
            RadialProfile = original.RadialProfile with { Drivers = original.RadialProfile.Drivers.Select(d => d with { Radius = new(savedValues[d.Id]) }).ToArray() } };
        foreach (var id in RadialProfilePolicy.NativeOrder(current, restored))
        {
            var d = ResolveDimension(c, binding, binding.Radial!.Dimensions.Single(d => d.Radial && d.Id == id));
            var value = savedValues[id];
            var status = d.SetSystemValue3(value / 1000, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null!);
            c.SketchTrace("radial-rollback-setter", "IDimension.SetSystemValue3", id, d.SystemValue * 1000, status);
            if (status != 0) throw new StateException("ROLLBACK_FAILED", "Radial restoration Setter failed.");
        }
    }
}
