using System;
using System.Linq;
using System.Runtime.InteropServices;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

// A design direction is a datum line in the extrusion frame. Its native source
// is the intersection of origin planes, never topology of the resulting solid.
internal static class NativePatternDirection
{
    internal static IFeature Create(IModelDoc2 document, int axis)
    {
        var planes = Features(document).Where(f => f.GetTypeName2() == "RefPlane").ToArray();
        IFeature Plane(int normalAxis) => NativeTopology.Unique(planes.Where(f =>
        {
            var m = NativeGeometry.Doubles(((IRefPlane)f.GetSpecificFeature2()).Transform.ArrayData);
            return m.Length >= 13 && Enumerable.Range(0, 3).All(i => NativeTopology.Near(Math.Abs(m[6 + i]), i == normalAxis ? 1 : 0)) &&
                Enumerable.Range(9, 3).All(i => NativeTopology.Near(m[i], 0));
        }), "origin construction plane for a datum direction");
        var before = Features(document).Where(f => f.GetTypeName2() == "RefAxis").Select(f => Identity(f)).ToHashSet();
        document.ClearSelection2(true);
        try
        {
            if (!Plane(2).Select2(false, 0) || !Plane(axis == 0 ? 1 : 0).Select2(true, 0) || !document.InsertAxis2(true))
                throw new NativeOperationException("GEOMETRY_INVALID", "Native local-frame datum axis creation failed.");
            var created = NativeTopology.Unique(Features(document).Where(f => f.GetTypeName2() == "RefAxis" && !before.Contains(Identity(f))), "new datum axis");
            Read(created, axis); return created;
        }
        finally { document.ClearSelection2(true); }
    }
    internal static SemanticGeometry Read(IFeature feature, int axis)
    {
        var p = Parameters(feature); var direction = Vector(feature);
        var sign = direction[axis] < 0 ? -1 : 1;
        for (var i = 0; i < 3; i++)
            if (!NativeTopology.Near(direction[i] * sign, i == axis ? 1 : 0))
                throw new NativeOperationException("RELATION_VIOLATED", "Native datum is not parallel to its declared local-frame direction.");
        var projection = p[0] * direction[0] + p[1] * direction[1] + p[2] * direction[2];
        for (var i = 0; i < 3; i++)
            if (!NativeTopology.Near(p[i] - projection * direction[i], 0))
                throw new NativeOperationException("RELATION_VIOLATED", "Native datum does not pass through the local-frame origin.");
        return new(new Point3(0, 0, 0), axis == 0 ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0));
    }
    internal static double[] Vector(IFeature feature)
    {
        var p = Parameters(feature); var v = new[] { p[3] - p[0], p[4] - p[1], p[5] - p[2] };
        var length = Math.Sqrt(v.Sum(x => x * x));
        if (!double.IsFinite(length) || length < 1e-12) throw new NativeOperationException("RELATION_VIOLATED", "Native datum axis is degenerate.");
        return v.Select(x => x / length).ToArray();
    }
    private static double[] Parameters(IFeature feature)
    {
        if (feature.GetErrorCode2(out var warning) != 0 || warning)
            throw new NativeOperationException("FEATURE_REBUILD_FAILED", "Native pattern datum has an error or warning.");
        if (feature.GetSpecificFeature2() is not IRefAxis axis || axis.IsTempAxis())
            throw new NativeOperationException("RELATION_VIOLATED", "Pattern direction requires a persistent native reference-axis feature.");
        var p = NativeGeometry.Doubles(axis.GetRefAxisParams());
        if (p.Length != 6 || p.Any(v => !double.IsFinite(v))) throw new NativeOperationException("RELATION_VIOLATED", "Native datum axis parameters are invalid.");
        return p;
    }
    internal static IFeature CanonicalFeature(SolidWorksExecutionContext context, object native)
    {
        if (native is IFeature feature && feature.GetSpecificFeature2() is IRefAxis) return feature;
        if (native is not IRefAxis) throw new NativeOperationException("RELATION_VIOLATED", "Pattern native axis is not a reference datum.");
        // Convert the returned specific interface to its owning feature before
        // persistent-reference comparison. COM identity is only interface lookup.
        return NativeTopology.Unique(Features(context.Document).Where(f => f.GetTypeName2() == "RefAxis" && Identity(f.GetSpecificFeature2()) == Identity(native)), "owner of the native pattern datum");
    }
    private static IFeature[] Features(IModelDoc2 document)
    {
        var result = new System.Collections.Generic.List<IFeature>(); var feature = (IFeature?)document.FirstFeature();
        for (var i = 0; feature is not null; i++, feature = (IFeature?)feature.GetNextFeature())
        { if (i >= 256) throw new NativeOperationException(FailureCodes.PreconditionFailed, "Datum inventory exceeds its bound."); result.Add(feature); }
        return result.ToArray();
    }
    private static long Identity(object native)
    { var pointer = Marshal.GetIUnknownForObject(native); try { return pointer.ToInt64(); } finally { Marshal.Release(pointer); } }
}
