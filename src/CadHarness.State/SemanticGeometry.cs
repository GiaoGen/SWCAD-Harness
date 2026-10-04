using System;

namespace CadHarness.State;

public sealed record Point3(double X, double Y, double Z);
public sealed record Vector3(double X, double Y, double Z);
public sealed record LocalFrameGeometry(Point3 OriginMm, Vector3 XAxis, Vector3 YAxis, Vector3 ZAxis);
public sealed record SemanticGeometry(Point3 OriginMm, Vector3? Direction = null,
    LocalFrameGeometry? Frame = null, double? RadiusMm = null);

public static class GeometryMath
{
    public const double ToleranceMm = 1e-6;
    public static bool Finite(Point3 p) => p is not null && double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
    public static bool Unit(Vector3 v) => v is not null && double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z) && Math.Abs(Dot(v, v) - 1) < 1e-8;
    public static double Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static bool Near(Point3 a, Point3 b, double tolerance = ToleranceMm) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Z - b.Z) <= tolerance;
    public static bool Parallel(Vector3 a, Vector3 b) => Math.Abs(Math.Abs(Dot(a, b)) - 1) <= 1e-8;
    public static bool Valid(SemanticGeometry geometry)
    {
        if (!Finite(geometry.OriginMm) || (geometry.Direction is not null && !Unit(geometry.Direction)) ||
            (geometry.RadiusMm is double radius && (!double.IsFinite(radius) || radius <= 0))) return false;
        if (geometry.Frame is not { } f) return true;
        return Finite(f.OriginMm) && Unit(f.XAxis) && Unit(f.YAxis) && Unit(f.ZAxis) &&
            Math.Abs(Dot(f.XAxis, f.YAxis)) < 1e-8 && Math.Abs(Dot(f.XAxis, f.ZAxis)) < 1e-8 && Math.Abs(Dot(f.YAxis, f.ZAxis)) < 1e-8 &&
            Math.Abs((f.XAxis.Y * f.YAxis.Z - f.XAxis.Z * f.YAxis.Y) * f.ZAxis.X +
                (f.XAxis.Z * f.YAxis.X - f.XAxis.X * f.YAxis.Z) * f.ZAxis.Y +
                (f.XAxis.X * f.YAxis.Y - f.XAxis.Y * f.YAxis.X) * f.ZAxis.Z - 1) < 1e-8;
    }
}
