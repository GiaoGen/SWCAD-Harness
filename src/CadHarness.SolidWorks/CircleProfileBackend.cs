using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

internal static class CircleProfileBackend
{
    internal static IFeature Create(IModelDoc2 document, double radiusMeters)
    {
        document.ClearSelection2(true);
        if (!CenteredRectangleProfileBackend.FindConstructionPlane(document).Select2(false, 0))
            throw new NativeOperationException("OPERATION_PRECONDITION_FAILED", "Cannot select the XY construction plane.");
        var manager = (ISketchManager)document.SketchManager;
        var add = manager.AddToDB; var display = manager.DisplayWhenAdded;
        ISketch? sketch = null;
        try
        {
            manager.InsertSketch(false);
            sketch = (ISketch?)manager.ActiveSketch ?? throw new NativeOperationException("GEOMETRY_INVALID", "Circle profile sketch creation failed.");
            manager.AddToDB = true; manager.DisplayWhenAdded = false;
            if (manager.CreateCircleByRadius(0, 0, 0, radiusMeters) is null)
                throw new NativeOperationException("GEOMETRY_INVALID", "Circle profile creation failed.");
        }
        finally
        {
            manager.DisplayWhenAdded = display; manager.AddToDB = add;
            if (manager.ActiveSketch is not null) manager.InsertSketch(false);
            document.ClearSelection2(true);
        }
        return CenteredRectangleProfileBackend.FeatureForSketch(document, sketch!);
    }
}
