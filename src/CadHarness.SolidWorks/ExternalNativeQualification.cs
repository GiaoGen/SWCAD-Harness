using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

internal static class ExternalNativeQualification
{
    internal static IFeature Resolve(IModelDoc2 document, ObservedFeature expected)
    {
        if (expected.NativeReference is null) Fail("Missing binding reference.", "STALE_REFERENCE");
        var native = document.Extension.GetObjectByPersistReference3(StateValidation.DecodeReference(expected.NativeReference!), out var error) as IFeature;
        if (error != 0 || native is null || native.IsSuppressed() || native.GetTypeName2() != expected.NativeType ||
            document.Extension.GetPersistReference3(native) is not byte[] bytes || Convert.ToBase64String(bytes) != expected.NativeReference!.Base64)
            Fail("Persistent reference did not resolve to the exact active feature/type.", "STALE_REFERENCE");
        if (native!.GetErrorCode2(out var warning) != 0 || warning) Fail("Native feature has an error/warning.", "FEATURE_REBUILD_FAILED");
        return native;
    }
    internal static IFeature SketchFeature(IFeature feature)
    {
        var parents = Objects<IFeature>(feature.GetParents()).Where(p => ExternalProfileOwnership.IsConsumingProfile(p.GetTypeName2()) && p.GetSpecificFeature2() is ISketch).ToArray();
        if (parents.Length != 1) Fail("Expected one unique native sketch owner.");
        return parents[0];
    }
    internal static ISketch Sketch(IFeature feature) => (ISketch)SketchFeature(feature).GetSpecificFeature2();
    internal static IDimension HoleDimension(IFeature feature, out bool diameter, bool requireProfileAgreement = true)
    {
        var sketchFeature = SketchFeature(feature); var radius = NativeHoleProfile.Circle((ISketch)sketchFeature.GetSpecificFeature2()).GetRadius();
        var matches = Dimensions(sketchFeature).Where(d => d.Display.Type2 is (int)swDimensionType_e.swDiameterDimension or (int)swDimensionType_e.swRadialDimension)
            .ToArray();
        if (matches.Length != 1) Fail("Hole radius needs one verified native driving radius/diameter dimension; none/ambiguous is unsupported.", "AMBIGUOUS_NATIVE_DIMENSION");
        VerifyDimension(matches[0].Dimension); diameter = matches[0].Display.Type2 == (int)swDimensionType_e.swDiameterDimension;
        if (requireProfileAgreement && Math.Abs(matches[0].Dimension.SystemValue - radius * (diameter ? 2 : 1)) >= 1e-9)
            Fail("Driving circle dimension and rebuilt native profile disagree.", "STATE_DRIFT_DETECTED");
        return matches[0].Dimension;
    }
    private static IEnumerable<(IDisplayDimension Display, IDimension Dimension)> Dimensions(IFeature feature)
    {
        var next = feature.GetFirstDisplayDimension() as IDisplayDimension; var count = 0;
        while (next is not null)
        {
            if (++count > 64) Fail("Dimension enumeration exceeded bound.");
            yield return (next, (IDimension)next.GetDimension2(0)); next = feature.GetNextDisplayDimension(next) as IDisplayDimension;
        }
    }
    private static void VerifyDimension(IDimension d)
    {
        if (d.ReadOnly || d.IsDesignTableDimension() || d.DrivenState != (int)swDimensionDrivenState_e.swDimensionDriving)
            Fail("Read-only/driven/design-table/unknown dimension cannot be mutated.", "UNSUPPORTED_PARAMETER_DRIVER");
    }
    internal static void VerifyDrivers(IModelDoc2 document, ObservedModel observation)
    {
        var configs = document.GetConfigurationCount();
        // Row/column queries require Attach (which activates Excel). Use the official
        // document presence query without activating or editing any design table.
        var hasTable = document.Extension.HasDesignTable() ||
            observation.Features.Any(f => f.NativeType.Contains("DesignTable", StringComparison.OrdinalIgnoreCase));
        var equations = document.GetEquationMgr() as IEquationMgr; var count = equations?.GetCount();
        var external = document.ListExternalFileReferencesCount(false);
        if (configs != 1 || hasTable || equations is null || count != 0 || equations.LinkToFile || external != 0)
            Fail($"Unsupported/unknown driver facts: configurations={configs}, designTable={hasTable}, equationCount={count?.ToString() ?? "unknown"}, linkedEquationFile={equations?.LinkToFile}, externalReferenceCount={external}.", "UNSUPPORTED_PARAMETER_DRIVER");
        foreach (var observed in observation.Features.Where(f => f.NativeReference is not null && f.Health == ObservationHealth.Healthy))
        {
            var feature = Resolve(document, observed);
            if (feature.ListExternalFileReferencesCount() != 0) Fail("External native feature references are unsupported.", "UNSUPPORTED_PARAMETER_DRIVER");
            foreach (var dimension in Dimensions(feature))
                if (dimension.Dimension.ReadOnly || dimension.Dimension.IsDesignTableDimension()) Fail("Read-only/design-table driving history.", "UNSUPPORTED_PARAMETER_DRIVER");
            if (feature.GetSpecificFeature2() is ISketch sketch &&
                (sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swExternal) != 0 ||
                 sketch.RelationManager.GetRelationsCount((int)swSketchRelationFilterType_e.swDefinedInContext) != 0))
                Fail("Externally driven/in-context sketch is unsupported.", "UNSUPPORTED_PARAMETER_DRIVER");
        }
    }
    internal static ExternalEditState Qualify(IModelDoc2 document, PartSelection selection)
    {
        var source = new ExternalPartInspection.NativeSource(document, true);
        var observed = ExternalObservation.Capture(selection, source).Model;
        if (!observed.InventoryComplete) Fail("Incomplete inventory cannot qualify.", V03FailureCodes.ObservationLimitExceeded);
        VerifyDrivers(document, observed);
        var physical = observed.Features.Where(f => f.Subtype is NativeSubtype.StraightBlindBossExtrude or NativeSubtype.SingleCircleThroughAllCut or NativeSubtype.SingleDirectionLinearPattern).ToArray();
        var neutral = new HashSet<string>(StringComparer.Ordinal) { "HistoryFolder", "CommentsFolder", "FavoriteFolder", "SelectionSetFolder",
            "SensorFolder", "DocsFolder", "DetailCabinet", "NotesAreaFtrFolder", "SurfaceBodyFolder", "SolidBodyFolder", "MaterialFolder", "RefPlane", "OriginProfileFeature", "ProfileFeature",
            "EnvFolder", "AmbientLight", "DirectionLight", "InkMarkupFolder", "EqnFolder",
            "AnnotationViewFeat", "FtrFolder", "ConfigTableFolder", "NativeConfigurationTableFeature" };
        foreach (var f in observed.Features.Except(physical))
        {
            if (!neutral.Contains(f.NativeType) || f.DependencyCompleteness != EvidenceCompleteness.Known)
                Fail("Unqualified native history remains read-only: " + f.NativeType + "/" + f.Subtype, V03FailureCodes.UnsupportedNativeSubtype);
            if (f.NativeType is "AnnotationViewFeat" or "FtrFolder" or "ConfigTableFolder" or "NativeConfigurationTableFeature")
            {
                // These native UI nodes have no driving dimensions or material faces.
                // The separate driver guard still rejects tables, equations and multiple configurations.
                var ui = ResolveInventoryNode(document, f);
                if (ui.GetFirstDisplayDimension() is not null || Objects<IFace2>(ui.GetFaces()).Any())
                    Fail("Nonphysical UI inventory has unexpected dimensions/material faces.");
            }
        }
        if (physical.Any(f => f.Health != ObservationHealth.Healthy || f.DependencyCompleteness != EvidenceCompleteness.Known)) Fail("Stale/suppressed/unknown physical node.");
        var roots = physical.Where(f => f.Subtype == NativeSubtype.StraightBlindBossExtrude).ToArray();
        if (roots.Length != 1) Fail("Finite external qualification supports one rectangular host extrusion.");
        var root = roots[0]; var rootNative = Resolve(document, root); var rootData = (IExtrudeFeatureData2)rootNative.GetDefinition();
        if (rootData.ReverseDirection || rootData.FromType != 0 || rootData.LinkToThickness || rootData.GetDraftWhileExtruding(true)) Fail("Unsupported extrusion start/direction/driver.");
        var sketchRoot = Sketch(rootNative); var segments = Objects<ISketchSegment>(sketchRoot.GetSketchSegments()).Where(s => !s.ConstructionGeometry).ToArray();
        if (segments.Length != 4 || segments.Any(s => s is not ISketchLine)) Fail("External host is not a verified four-line rectangular prism.");
        var transform = (IMathTransform)sketchRoot.ModelToSketchTransform.Inverse();
        var points = segments.Cast<ISketchLine>().SelectMany(l => new[] { (ISketchPoint)l.GetStartPoint2(), (ISketchPoint)l.GetEndPoint2() })
            .Select(p => NativeHoleProfile.Transform(new(p.X, p.Y, p.Z), transform, 1000)).ToArray();
        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X); var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y); var bottom = points[0].Z;
        foreach (var p in points)
            if (Math.Abs(p.Z - bottom) > 0.001 || Math.Min(Math.Abs(p.X - minX), Math.Abs(p.X - maxX)) > 0.001 ||
                Math.Min(Math.Abs(p.Y - minY), Math.Abs(p.Y - maxY)) > 0.001) Fail("Host profile is not world-XY axis-aligned rectangular geometry.");
        var holes = new List<ExternalHoleContract>();
        foreach (var f in physical.Where(f => f.Subtype == NativeSubtype.SingleCircleThroughAllCut))
        {
            var native = Resolve(document, f); var data = (IExtrudeFeatureData2)native.GetDefinition();
            if (data.FlipSideToCut || data.FromType != 0 || data.LinkToThickness) Fail("Unsupported through-cut direction/start/driver.");
            var sketch = Sketch(native); _ = HoleDimension(native, out _);
            var arc = NativeHoleProfile.Circle(sketch); var center = (ISketchPoint)arc.GetCenterPoint2();
            var world = NativeHoleProfile.Transform(new(center.X, center.Y, center.Z), (IMathTransform)sketch.ModelToSketchTransform.Inverse(), 1000);
            holes.Add(new(f.SemanticId, world.X, world.Y, arc.GetRadius() * 2000));
        }
        var patterns = new List<ExternalPatternContract>();
        foreach (var f in physical.Where(f => f.Subtype == NativeSubtype.SingleDirectionLinearPattern))
        {
            var native = Resolve(document, f); var data = (ILinearPatternFeatureData)native.GetDefinition();
            if (!data.AccessSelections(document, null)) Fail("Cannot qualify native pattern selections.");
            try
            {
                if (data.D1EndCondition != 0 || data.VarySketch || data.GetSkippedItemCount() != 0 || data.GetPatternFeatureCount() != 1 ||
                    data.GetPatternBodyCount() != 0 || data.GetPatternFaceCount() != 0) Fail("Only one-seed, fixed-instance feature pattern is qualified.");
                var seeds = Objects<IFeature>(data.PatternFeatureArray).ToArray();
                if (seeds.Length != 1) Fail("Pattern seed is ambiguous.");
                var seedReference = document.Extension.GetPersistReference3(seeds[0]) as byte[];
                var seed = physical.SingleOrDefault(p => p.NativeReference?.Base64 == (seedReference is null ? null : Convert.ToBase64String(seedReference)));
                if (seed is null || !holes.Any(h => h.Target == seed.SemanticId)) Fail("Pattern seed is not a qualified single-circle through-hole.");
                double[] axis;
                if (data.D1Axis is IEdge edge && edge.GetCurve() is ICurve curve && curve.IsLine()) axis = (double[])curve.LineParams;
                else Fail("Pattern direction needs one verified straight edge.");
                axis = (double[])((ICurve)((IEdge)data.D1Axis).GetCurve()).LineParams;
                var sign = data.D1ReverseDirection ? -1 : 1;
                if (Math.Abs(axis[5]) > 1e-8) Fail("Pattern direction is not in the measured host plane.");
                patterns.Add(new(f.SemanticId, seed!.SemanticId, data.D1TotalInstances, data.D1Spacing * 1000, axis[3] * sign, axis[4] * sign));
            }
            finally { data.ReleaseSelectionAccess(); document.ClearSelection2(true); }
        }
        var qualifiedIds = physical.Select(f => f.SemanticId).ToHashSet();
        observed = observed with { Features = observed.Features.Select(f => qualifiedIds.Contains(f.SemanticId) ? f with {
            EditSupport = EditSupport.Editable, SupportReason = "M14 native subtype, driver and full independent plate geometry qualified." } :
            f with { SupportReason = f.NativeType is "ProfileFeature" or "RefPlane" or "OriginProfileFeature" ? "M14_VERIFIED_SUPPORT_NODE" : "M14_NONPHYSICAL_INVENTORY" }).ToArray() };
        var state = new ExternalEditState("0.3", observed, new(root.SemanticId, minX, minY, maxX, maxY, bottom, rootData.GetDepth(true) * 1000, holes, patterns));
        ExternalEditPlanning.Validate(state); Verify(document, state); return state;
    }
    internal static void Verify(IModelDoc2 document, ExternalEditState expected)
    {
        ExternalEditPlanning.Validate(expected);
        if (document.ConfigurationManager.ActiveConfiguration.Name != expected.Observation.Selection.ConfigurationName || document.GetActiveSketch2() is not null || document.Extension.NeedsRebuild2 != 0)
            Fail("Configuration, active sketch or native rebuild state changed.", "FEATURE_REBUILD_FAILED");
        var current = ExternalObservation.Capture(expected.Observation.Selection, new ExternalPartInspection.NativeSource(document, true)).Model;
        if (!ExternalInventoryIdentity.Matches(current, expected.Observation))
            Fail("Complete native inventory/dependency identity drifted.", "STATE_DRIFT_DETECTED");
        VerifyDrivers(document, current);
        foreach (var f in expected.Observation.Features.Where(f => f.EditSupport == EditSupport.Editable))
        {
            var native = Resolve(document, f); var actual = current.Features.Single(c => c.SemanticId == f.SemanticId);
            if (actual.Subtype != f.Subtype) Fail("Native subtype drifted.");
            foreach (var p in f.Parameters)
                ExternalEditPlanning.Near(ParameterMutationRegistry.Default.GetObserved(f.Subtype, p.Key).ReadObserved(document, native, p.Key), p.Value, p.Key);
            if (f.Subtype == NativeSubtype.SingleCircleThroughAllCut)
            {
                _ = HoleDimension(native, out _);
                var sketch = Sketch(native); var circle = NativeHoleProfile.Circle(sketch); var p = (ISketchPoint)circle.GetCenterPoint2();
                var center = NativeHoleProfile.Transform(new(p.X, p.Y, p.Z), (IMathTransform)sketch.ModelToSketchTransform.Inverse(), 1000);
                var contract = expected.Geometry.Holes.Single(h => h.Target == f.SemanticId);
                Near(center.X, contract.XMm, 0.001, "Owning hole sketch center X drifted.");
                Near(center.Y, contract.YMm, 0.001, "Owning hole sketch center Y drifted.");
            }
            if (f.Subtype == NativeSubtype.SingleDirectionLinearPattern)
            {
                var data = (ILinearPatternFeatureData)native.GetDefinition();
                if (!data.AccessSelections(document, null)) Fail("Pattern selections cannot be verified.");
                try
                {
                    var contract = expected.Geometry.Patterns.Single(p => p.Target == f.SemanticId);
                    var seed = expected.Observation.Features.Single(p => p.SemanticId == contract.Seed);
                    var seeds = Objects<IFeature>(data.PatternFeatureArray).ToArray();
                    if (data.IsDirection2Specified() || data.D1EndCondition != 0 || data.VarySketch || data.GeometryPattern || data.GetSkippedItemCount() != 0 || seeds.Length != 1 ||
                        document.Extension.GetPersistReference3(seeds[0]) is not byte[] reference || Convert.ToBase64String(reference) != seed.NativeReference!.Base64)
                        Fail("Pattern seed/subtype/driver drifted.");
                    if (data.D1Axis is not IEdge edge || edge.GetCurve() is not ICurve curve || !curve.IsLine()) Fail("Pattern axis is no longer a straight edge.");
                    var axis = (double[])((ICurve)((IEdge)data.D1Axis).GetCurve()).LineParams; var sign = data.D1ReverseDirection ? -1 : 1;
                    Near(axis[3] * sign, contract.DirectionX, 1e-8, "Pattern direction X changed.");
                    Near(axis[4] * sign, contract.DirectionY, 1e-8, "Pattern direction Y changed."); Near(axis[5], 0, 1e-8, "Pattern direction left the host plane.");
                }
                finally { data.ReleaseSelectionAccess(); document.ClearSelection2(true); }
            }
        }
        var bodies = Objects<IBody2>(((IPartDoc)document).GetBodies2((int)swBodyType_e.swAllBodies, false)).ToArray();
        if (bodies.Length != 1 || bodies[0].GetType() != (int)swBodyType_e.swSolidBody) Fail("Native body topology differs from single solid contract.");
        var g = expected.Geometry; var mass = (double[])bodies[0].GetMassProperties(1); var volume = mass[3] * 1e9;
        Near(volume, ExternalEditPlanning.ExpectedVolume(g), Math.Max(0.01, Math.Abs(volume) * 1e-7), "Native material volume differs from analytic final model.");
        var box = (double[])bodies[0].GetBodyBox(); var bounds = new[] { g.MinimumX, g.MinimumY, g.BottomZ, g.MaximumX, g.MaximumY, g.BottomZ + g.DepthMm };
        for (var i = 0; i < 6; i++) Near(box[i] * 1000, bounds[i], 0.01, "Native host envelope differs.");
        var faces = Objects<IFace2>(bodies[0].GetFaces()).ToArray();
        if (faces.Any(f => f.GetSurface() is not ISurface s || !s.IsPlane() && !s.IsCylinder())) Fail("Unsupported face geometry in full oracle.");
        var cylinders = faces.Where(f => ((ISurface)f.GetSurface()).IsCylinder()).ToArray(); var instances = ExternalEditPlanning.Instances(g);
        if (cylinders.Length != instances.Count) Fail("Full native cylinder count differs from complete hole/pattern contract.");
        var consumed = new HashSet<int>();
        foreach (var instance in instances)
        {
            var matches = cylinders.Select((f, i) => (Face: f, Index: i, Params: (double[])((ISurface)f.GetSurface()).CylinderParams))
                .Where(c => Math.Abs(c.Params[0] * 1000 - instance.X) < 0.001 && Math.Abs(c.Params[1] * 1000 - instance.Y) < 0.001).ToArray();
            if (matches.Length != 1 || !consumed.Add(matches[0].Index)) Fail("Ambiguous or missing native hole instance.");
            var cylinder = matches[0]; Near(cylinder.Params[6] * 1000, instance.Radius, 0.001, "Native instance radius differs.");
            Near(Math.Abs(cylinder.Params[5]), 1, 1e-8, "Native instance axis differs.");
            var levels = Objects<IEdge>(cylinder.Face.GetEdges()).Select(e => (ICurve)e.GetCurve()).Where(c => c.IsCircle())
                .Select(c => ((double[])c.CircleParams)[2] * 1000).OrderBy(z => z).ToArray();
            if (levels.Length != 2) Fail("Native hole lacks exactly two full circular boundaries.");
            Near(levels[0], g.BottomZ, 0.001, "Through-hole lower boundary differs.");
            Near(levels[1], g.BottomZ + g.DepthMm, 0.001, "Through-hole upper boundary differs.");
        }
    }
    private static IEnumerable<T> Objects<T>(object? value) => value is Array a ? a.Cast<object>().OfType<T>() : Array.Empty<T>();
    private static IFeature ResolveInventoryNode(IModelDoc2 document, ObservedFeature feature)
    {
        // Some nonphysical configuration-table nodes do not expose persistent references.
        // They are never edit targets; require one exact native UI type/name inventory match.
        if (feature.NativeReference is not null)
        {
            var value = document.Extension.GetObjectByPersistReference3(Convert.FromBase64String(feature.NativeReference.Base64), out var status) as IFeature;
            if (status == 0 && value is not null && value.GetTypeName2() == feature.NativeType) return value;
            Fail("Nonphysical inventory reference changed.");
        }
        var matches = new List<IFeature>(); var visited = new HashSet<long>(); var visits = 0;
        void Walk(IFeature? f, bool sub)
        {
            while (f is not null)
            {
                if (++visits > ContractLimits.Dependencies) Fail("UI inventory traversal exceeded bound.");
                var pointer = Marshal.GetIUnknownForObject(f); long identity;
                try { identity = pointer.ToInt64(); } finally { Marshal.Release(pointer); }
                if (visited.Add(identity))
                {
                    if (f.GetTypeName2() == feature.NativeType && f.Name == feature.DisplayName) matches.Add(f);
                    Walk(f.GetFirstSubFeature() as IFeature, true);
                    if (visited.Count > ContractLimits.Features) Fail("UI inventory traversal exceeded bound.");
                }
                f = (sub ? f.GetNextSubFeature() : f.GetNextFeature()) as IFeature;
            }
        }
        Walk(document.FirstFeature() as IFeature, false);
        if (matches.Count != 1) Fail("Nonphysical native inventory match is ambiguous.");
        return matches[0];
    }
    private static void Near(double actual, double expected, double tolerance, string message)
    { if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance) Fail(message, "NATIVE_GEOMETRY_MISMATCH"); }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string message, string code = V03FailureCodes.UnsupportedNativeSubtype) => throw new StateException(code, message);
}
