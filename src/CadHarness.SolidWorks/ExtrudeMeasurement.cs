using System;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed record ExtrudeMeasurement(int SolidBodyCount, double WidthMm, double HeightMm, double DepthMm);

public static class ExtrudeMeasurementReader
{
    public static ExtrudeMeasurement Read(SolidWorksExecutionContext context)
    {
        context.CheckThread();
        var bodies = NativeGeometry.SolidBodies(context.Document);
        if (bodies.Count != 1) return new(bodies.Count, 0, 0, 0);
        var body = bodies[0];
        // Native exact support points, not approximate BodyBox values. The M2
        // construction frame is XY-aligned; these measure independent geometry.
        return new(1, Extent(body, 1, 0, 0), Extent(body, 0, 1, 0), Extent(body, 0, 0, 1));
    }

    private static double Extent(IBody2 body, double dx, double dy, double dz)
    {
        if (!body.GetExtremePoint(dx, dy, dz, out var maxX, out var maxY, out var maxZ) ||
            !body.GetExtremePoint(-dx, -dy, -dz, out var minX, out var minY, out var minZ))
            throw new InvalidOperationException("Native body extent measurement failed.");
        var value = ((maxX - minX) * dx + (maxY - minY) * dy + (maxZ - minZ) * dz) * 1000;
        if (!double.IsFinite(value) || value <= 0) throw new InvalidOperationException("Native body extent is invalid.");
        return value;
    }
}
