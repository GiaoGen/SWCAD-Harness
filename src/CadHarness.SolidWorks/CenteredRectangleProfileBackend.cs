using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

internal static class CenteredRectangleProfileBackend
{
    internal static IFeature Create(IModelDoc2 document, double widthMeters, double heightMeters)
    {
        document.ClearSelection2(true);
        var plane = FindConstructionPlane(document);
        if (!plane.Select2(false, 0)) throw new NativeOperationException("OPERATION_PRECONDITION_FAILED", "Cannot select the XY construction plane.");
        var manager = (ISketchManager)document.SketchManager;
        var originalAddToDb = manager.AddToDB;
        var originalDisplay = manager.DisplayWhenAdded;
        ISketch? sketch = null;
        try
        {
            manager.InsertSketch(false);
            sketch = (ISketch?)manager.ActiveSketch
                ?? throw new NativeOperationException("GEOMETRY_INVALID", "Native sketch creation failed.");
            manager.AddToDB = true;
            manager.DisplayWhenAdded = false;
            var segments = manager.CreateCenterRectangle(0, 0, 0, widthMeters / 2, heightMeters / 2, 0) as Array;
            if (segments is null || segments.Length < 4)
                throw new NativeOperationException("GEOMETRY_INVALID", "Native centered rectangle creation failed.");
        }
        finally
        {
            manager.DisplayWhenAdded = originalDisplay;
            manager.AddToDB = originalAddToDb;
            if (manager.ActiveSketch is not null) manager.InsertSketch(false);
            document.ClearSelection2(true);
        }
        // Match the sketch created in this API sequence by its ephemeral native
        // handle. This is not semantic binding or persistent-reference recovery.
        return FeatureForSketch(document, sketch!);
    }

    internal static IFeature FeatureForSketch(IModelDoc2 document, ISketch sketch)
    {
        var feature = (IFeature?)document.FirstFeature();
        for (var i = 0; feature is not null && i < 128; i++, feature = (IFeature?)feature.GetNextFeature())
            if (feature.GetTypeName2() == "ProfileFeature" && SameNativeObject(feature.GetSpecificFeature2(), sketch))
                return feature;
        throw new NativeOperationException("GEOMETRY_INVALID", "Cannot obtain the feature for the newly created sketch.");
    }

    private static IFeature FindConstructionPlane(IModelDoc2 document)
    {
        // Narrow, locale-independent construction-frame selection in a fresh
        // Part. The first-stage backend supports XY-aligned default templates.
        var feature = (IFeature?)document.FirstFeature();
        for (var i = 0; feature is not null && i < 128; i++, feature = (IFeature?)feature.GetNextFeature())
        {
            if (feature.GetTypeName2() != "RefPlane") continue;
            var plane = (IRefPlane)feature.GetSpecificFeature2();
            var matrix = NativeGeometry.Doubles(plane.Transform.ArrayData);
            if (matrix.Length >= 13 && Near(Math.Abs(matrix[0]), 1) && Near(Math.Abs(matrix[4]), 1) &&
                Near(Math.Abs(matrix[8]), 1) && Near(matrix[9], 0) && Near(matrix[10], 0) && Near(matrix[11], 0))
                return feature;
        }
        throw new NativeOperationException("OPERATION_PRECONDITION_FAILED", "Template has no origin-centered XY-aligned construction plane.");
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1e-9;
    private static bool SameNativeObject(object a, object b)
    {
        var first = Marshal.GetIUnknownForObject(a);
        try
        {
            var second = Marshal.GetIUnknownForObject(b);
            try { return first == second; }
            finally { Marshal.Release(second); }
        }
        finally { Marshal.Release(first); }
    }
}
