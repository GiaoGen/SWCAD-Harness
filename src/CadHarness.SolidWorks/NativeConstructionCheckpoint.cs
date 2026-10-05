using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

// Session-local native feature identities delimit exactly the topology created
// by this program. They are never used to bind semantic entities or recover a
// stale persisted reference. No operation/case names participate in rollback.
internal sealed class NativeConstructionCheckpoint
{
    private sealed record FeatureImage(long Identity, IFeature Feature, string Type, int Error, bool Warning);
    private sealed record PropertyImage(string Name, int Type, string Value);
    private sealed record FaceImage(NativePersistentReference Reference, int SurfaceKind, int Edges, double Area, double[] Bounds, double[] Sample);
    private sealed record BodyImage(NativePersistentReference Reference, int Edges, double[] Mass, double[] Extremes, IReadOnlyList<FaceImage> Faces);
    private readonly IReadOnlyList<FeatureImage> features;
    private readonly HashSet<long> featureIds;
    private readonly IReadOnlyList<BodyImage> bodies;
    private readonly IReadOnlyList<PropertyImage> documentProperties;
    private readonly IReadOnlyList<PropertyImage> configurationProperties;
    private readonly string configuration;
    private readonly object session;

    private NativeConstructionCheckpoint(SolidWorksExecutionContext context)
    {
        features = Inventory(context.Document); featureIds = features.Select(f => f.Identity).ToHashSet();
        bodies = CaptureBodies(context); configuration = context.Document.ConfigurationManager.ActiveConfiguration.Name;
        documentProperties = Properties((ICustomPropertyManager)context.Document.Extension.CustomPropertyManager[""]);
        configurationProperties = Properties((ICustomPropertyManager)context.Document.Extension.CustomPropertyManager[configuration]);
        session = context.SnapshotConstructionSession();
    }
    internal static NativeConstructionCheckpoint Capture(SolidWorksExecutionContext context) => new(context);
    internal void Restore(SolidWorksExecutionContext context)
    {
        var doc = context.Document;
        if (doc.ConfigurationManager.ActiveConfiguration.Name != configuration)
            throw new StateException("ROLLBACK_FAILED", "Active configuration changed during construction.");
        var sketch = (ISketchManager)doc.SketchManager;
        if (sketch.ActiveSketch is not null) sketch.InsertSketch(false);
        // Remove newest introduced features first, including their absorbed
        // sketches. Re-enumerate after each delete because topology can change.
        for (var attempt = 0; attempt < 256; attempt++)
        {
            var current = Inventory(doc);
            var introduced = current.Where(f => !featureIds.Contains(f.Identity)).LastOrDefault();
            if (introduced is null) break;
            doc.ClearSelection2(true);
            if (!introduced.Feature.Select2(false, 0) || !doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed))
                throw new StateException("ROLLBACK_FAILED", "Cannot delete an introduced construction feature.");
            if (attempt == 255) throw new StateException("ROLLBACK_FAILED", "Construction deletion exceeded its finite bound.");
        }
        RestoreProperties((ICustomPropertyManager)doc.Extension.CustomPropertyManager[""], documentProperties);
        RestoreProperties((ICustomPropertyManager)doc.Extension.CustomPropertyManager[configuration], configurationProperties);
        context.RestoreConstructionSession(session); doc.ClearSelection2(true);
    }
    internal void Verify(SolidWorksExecutionContext context)
    {
        var current = Inventory(context.Document);
        if (!features.Select(f => (f.Identity, f.Type, f.Error, f.Warning)).SequenceEqual(current.Select(f => (f.Identity, f.Type, f.Error, f.Warning))))
            throw new StateException("ROLLBACK_VALIDATION_FAILED", "Original feature inventory/status differs after rollback.");
        if (!documentProperties.SequenceEqual(Properties((ICustomPropertyManager)context.Document.Extension.CustomPropertyManager[""])) ||
            !configurationProperties.SequenceEqual(Properties((ICustomPropertyManager)context.Document.Extension.CustomPropertyManager[configuration])))
            throw new StateException("ROLLBACK_VALIDATION_FAILED", "Native custom properties differ after rollback.");
        var restored = CaptureBodies(context);
        if (bodies.Count != restored.Count) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Solid body count differs after rollback.");
        foreach (var before in bodies)
        {
            var after = restored.SingleOrDefault(b => b.Reference == before.Reference) ?? throw new StateException("ROLLBACK_VALIDATION_FAILED", "Original body reference was not restored.");
            if (before.Edges != after.Edges || before.Faces.Count != after.Faces.Count) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Original body topology differs.");
            Compare(before.Mass, after.Mass); Compare(before.Extremes, after.Extremes);
            foreach (var face in before.Faces)
            {
                var observed = after.Faces.SingleOrDefault(f => f.Reference == face.Reference) ?? throw new StateException("ROLLBACK_VALIDATION_FAILED", "Original face reference was not restored.");
                if (face.SurfaceKind != observed.SurfaceKind || face.Edges != observed.Edges) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Original face topology differs.");
                Compare(new[] { face.Area }, new[] { observed.Area }); Compare(face.Bounds, observed.Bounds); Compare(face.Sample, observed.Sample);
            }
        }
    }
    private static void Compare(double[] expected, double[] actual)
    {
        if (expected.Length != actual.Length || expected.Where((value, i) => !double.IsFinite(actual[i]) || Math.Abs(value - actual[i]) > Math.Max(1e-12, Math.Abs(value) * 1e-8)).Any())
            throw new StateException("ROLLBACK_VALIDATION_FAILED", "Native body/surface geometry differs after rollback.");
    }
    private static IReadOnlyList<FeatureImage> Inventory(IModelDoc2 doc)
    {
        var items = new List<FeatureImage>(); var seen = new HashSet<long>();
        var feature = (IFeature?)doc.FirstFeature();
        for (var i = 0; feature is not null; i++, feature = (IFeature?)feature.GetNextFeature())
        {
            if (i >= 256) throw new StateException("OPERATION_PRECONDITION_FAILED", "Native feature inventory exceeds 256 entries.");
            var pointer = Marshal.GetIUnknownForObject(feature); long identity;
            try { identity = pointer.ToInt64(); } finally { Marshal.Release(pointer); }
            if (!seen.Add(identity)) throw new StateException("OPERATION_PRECONDITION_FAILED", "Native feature inventory repeats an object.");
            var error = feature.GetErrorCode2(out var warning);
            items.Add(new(identity, feature, feature.GetTypeName2(), error, warning));
        }
        return items;
    }
    private static IReadOnlyList<BodyImage> CaptureBodies(SolidWorksExecutionContext context) => NativeGeometry.SolidBodies(context.Document).Select(body =>
    {
        var extremes = new List<double>();
        foreach (var d in new[] { (1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0), (-1.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, -1.0) })
        {
            if (!body.GetExtremePoint(d.Item1, d.Item2, d.Item3, out var x, out var y, out var z)) throw new StateException("GEOMETRY_INVALID", "Cannot capture body support point.");
            extremes.AddRange(new[] { x, y, z });
        }
        var faces = NativeTopology.Faces(body).Select(face =>
        {
            var surface = (ISurface)face.GetSurface(); var uv = NativeGeometry.Doubles(face.GetUVBounds());
            var sample = NativeGeometry.Doubles(surface.Evaluate((uv[0] + uv[1]) / 2, (uv[2] + uv[3]) / 2, 1, 1));
            return new FaceImage(PersistentReferenceAdapter.Capture(context, face), surface.Identity(), face.GetEdgeCount(), face.GetArea(), uv, sample);
        }).ToArray();
        return new BodyImage(PersistentReferenceAdapter.Capture(context, body), body.GetEdgeCount(), NativeGeometry.Doubles(body.GetMassProperties(1)), extremes.ToArray(), faces);
    }).ToArray();
    private static IReadOnlyList<PropertyImage> Properties(ICustomPropertyManager properties) => NativeTopology.Objects<string>(properties.GetNames())
        .OrderBy(n => n, StringComparer.Ordinal).Select(name =>
        {
            properties.Get6(name, false, out var value, out _, out _, out _); return new PropertyImage(name, properties.GetType2(name), value);
        }).ToArray();
    private static void RestoreProperties(ICustomPropertyManager properties, IReadOnlyList<PropertyImage> before)
    {
        var expected = before.ToDictionary(p => p.Name, StringComparer.Ordinal);
        foreach (var current in Properties(properties))
            if (!expected.ContainsKey(current.Name)) properties.Delete2(current.Name);
        foreach (var old in before)
        {
            var current = Properties(properties).SingleOrDefault(p => p.Name == old.Name);
            if (current != old) properties.Add3(old.Name, old.Type, old.Value, (int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
        }
        if (!before.SequenceEqual(Properties(properties))) throw new StateException("ROLLBACK_FAILED", "Native property restoration failed.");
    }
}
