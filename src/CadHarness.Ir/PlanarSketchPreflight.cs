using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir.V03;

namespace CadHarness.Ir.V03;

public readonly record struct SketchPoint2(double X, double Y)
{
    public static SketchPoint2 operator +(SketchPoint2 a, SketchPoint2 b) => new(a.X + b.X, a.Y + b.Y);
    public static SketchPoint2 operator -(SketchPoint2 a, SketchPoint2 b) => new(a.X - b.X, a.Y - b.Y);
    public static SketchPoint2 operator *(SketchPoint2 a, double b) => new(a.X * b, a.Y * b);
    public double Length => Math.Sqrt(X * X + Y * Y);
}
public sealed record SketchPrimitive(string EntityId, int Ordinal, SketchPoint2 Start, SketchPoint2 End,
    SketchPoint2? Center, double RadiusMm, double SweepRadians)
{
    public bool Circular => Center is not null;
    public bool FullCircle => Math.Abs(SweepRadians) >= 2 * Math.PI - 1e-10;
    public SketchPoint2 At(double fraction)
    {
        if (Center is not { } c) return Start + (End - Start) * fraction;
        var a = Math.Atan2(Start.Y - c.Y, Start.X - c.X) + SweepRadians * fraction;
        return c + new SketchPoint2(Math.Cos(a), Math.Sin(a)) * RadiusMm;
    }
}
public sealed record PreparedSketchLoop(string Id, bool Inner, IReadOnlyList<SketchPrimitive> Primitives, double SignedAreaMm2);
public sealed record PreparedPlanarSketch(ClosedSketch Specification, IReadOnlyList<PreparedSketchLoop> Loops,
    IReadOnlyList<SketchPrimitive> Primitives, string CoordinateDriverPolicy);

// Finite, already-dimensioned geometry, not an arbitrary symbolic solver. All
// supplied local point coordinates are authoritative driving positions.
public static class PlanarSketchPreflight
{
    public const double ToleranceMm = 1e-7;
    private const double Tau = 2 * Math.PI;
    private static void Check(bool value, string message, string code = V03FailureCodes.InvalidSketch) => ContractValidation.Require(value, message, code);
    private static double Cross(SketchPoint2 a, SketchPoint2 b) => a.X * b.Y - a.Y * b.X;
    private static double Dot(SketchPoint2 a, SketchPoint2 b) => a.X * b.X + a.Y * b.Y;
    private static bool Near(SketchPoint2 a, SketchPoint2 b) => (a - b).Length <= ToleranceMm;
    private static double PositiveAngle(double angle) => (angle % Tau + Tau) % Tau;
    public static PreparedPlanarSketch Prepare(ClosedSketch sketch)
    {
        ContractValidation.Sketch(sketch);
        var points = sketch.Points.ToDictionary(p => p.Id, p => new SketchPoint2(p.X.Millimeters, p.Y.Millimeters), StringComparer.Ordinal);
        var entities = sketch.Entities.ToDictionary(e => e.Id, StringComparer.Ordinal);
        Check(points.Keys.All(p => sketch.Entities.Any(e => e.Points.Contains(p))), "Unconsumed driving point.");
        var loops = new List<PreparedSketchLoop>();
        foreach (var loop in sketch.Loops)
        {
            var primitives = new List<SketchPrimitive>();
            foreach (var id in loop.Entities)
            {
                var e = entities[id]; var p = e.Points.Select(id2 => points[id2]).ToArray();
                switch (e.Kind)
                {
                    case SketchEntityKind.Line: primitives.Add(Line(e.Id, 0, p[0], p[1])); break;
                    case SketchEntityKind.Polyline:
                        for (var i = 1; i < p.Length; i++) primitives.Add(Line(e.Id, i - 1, p[i - 1], p[i]));
                        if (loop.Entities.Count == 1) primitives.Add(Line(e.Id, p.Length - 1, p[^1], p[0]));
                        break;
                    case SketchEntityKind.Arc:
                        var r = e.Radius!.Millimeters; var sweep = e.SweepDegrees!.Value * Math.PI / 180;
                        Check(Math.Abs((p[1] - p[0]).Length - r) <= ToleranceMm && Math.Abs((p[2] - p[0]).Length - r) <= ToleranceMm, "Arc endpoints disagree with driving radius.");
                        var arc = new SketchPrimitive(e.Id, 0, p[1], p[2], p[0], r, sweep);
                        Check(Near(arc.At(1), p[2]), "Arc endpoint disagrees with signed sweep."); primitives.Add(arc); break;
                    case SketchEntityKind.Circle:
                        r = e.Radius!.Millimeters;
                        var radial = sketch.Constraints.Where(c => c.Kind is ConstraintKind.Radius or ConstraintKind.Diameter && c.References.Count == 1 && c.References[0] == e.Id).ToArray();
                        Check(radial.Length == 1, "Circle needs exactly one declared driving radius/diameter dimension.", V03FailureCodes.UnderConstrainedSketch);
                        primitives.Add(new(e.Id, 0, p[0] + new SketchPoint2(r, 0), p[0] + new SketchPoint2(r, 0), p[0], r, loop.Inner ? -Tau : Tau)); break;
                    case SketchEntityKind.Slot:
                        var delta = p[1] - p[0]; var span = delta.Length; var width = e.Width!.Millimeters;
                        Check(span > ToleranceMm && Math.Abs(span + width - e.Length!.Millimeters) <= ToleranceMm, "Slot points are cap centers; length must equal center distance plus width.");
                        var normal = new SketchPoint2(-delta.Y / span, delta.X / span) * (width / 2);
                        var a = p[0] - normal; var b = p[1] - normal; var c = p[1] + normal; var d = p[0] + normal;
                        primitives.Add(Line(e.Id, 0, a, b)); primitives.Add(new(e.Id, 1, b, c, p[1], width / 2, Math.PI));
                        primitives.Add(Line(e.Id, 2, c, d)); primitives.Add(new(e.Id, 3, d, a, p[0], width / 2, Math.PI));
                        if (loop.Inner) primitives = primitives.Select(v => v with { Start = v.End, End = v.Start, SweepRadians = -v.SweepRadians }).Reverse().ToList();
                        Check(loop.Entities.Count == 1, "A compact slot is a complete loop."); break;
                }
            }
            Check(primitives.Count is > 0 and <= ContractLimits.SketchEntities, "Lowered primitive bound exceeded.");
            for (var i = 0; i < primitives.Count; i++) Check(Near(primitives[i].End, primitives[(i + 1) % primitives.Count].Start), "Declared loop is open or incorrectly ordered.");
            var area = primitives.Sum(Area);
            Check(Math.Abs(area) > ToleranceMm * ToleranceMm, "Zero-area loop.");
            Check(loop.Inner ? area < 0 : area > 0, "Outer winding must be CCW and inner winding CW.");
            for (var i = 0; i < primitives.Count; i++)
                for (var j = i + 1; j < primitives.Count; j++)
                {
                    var hits = Intersections(primitives[i], primitives[j]);
                    var adjacent = j == i + 1 || i == 0 && j == primitives.Count - 1;
                    Check(!hits.Overlap && hits.Points.All(h => adjacent &&
                        (Near(h, primitives[i].Start) || Near(h, primitives[i].End)) &&
                        (Near(h, primitives[j].Start) || Near(h, primitives[j].End))), "Self-intersecting or coincident-ambiguous loop.");
                }
            loops.Add(new(loop.Id, loop.Inner, primitives, area));
        }
        var flat = loops.SelectMany(l => l.Primitives).ToArray();
        Check(flat.Length <= ContractLimits.SketchEntities, "Total lowered primitive bound exceeded.");
        for (var i = 0; i < loops.Count; i++)
            for (var j = i + 1; j < loops.Count; j++)
                foreach (var a in loops[i].Primitives)
                    foreach (var b in loops[j].Primitives)
                    { var hits = Intersections(a, b); Check(!hits.Overlap && hits.Points.Count == 0, "Loops intersect or touch."); }
        foreach (var inner in loops.Where(l => l.Inner))
        {
            Check(loops.Count(l => !l.Inner && Contains(l, inner.Primitives[0].Start)) == 1, "Inner loop needs one containing outer loop.");
            Check(!loops.Any(l => l != inner && l.Inner && Contains(l, inner.Primitives[0].Start)), "Nested inner loops are not subtractive profiles.");
        }
        foreach (var outer in loops.Where(l => !l.Inner))
            Check(!loops.Any(l => l != outer && !l.Inner && Contains(l, outer.Primitives[0].Start)), "Nested outer loops are ambiguous.");
        ValidateConstraints(sketch, points, entities);
        return new(sketch, loops, flat, "Explicit point coordinates drive placement; noncircle primitives are fixed to that supplied geometry; circle centers are fixed and only declared radial dimensions drive radius. No inferred dimensions/default positions.");
    }
    private static SketchPrimitive Line(string id, int ordinal, SketchPoint2 a, SketchPoint2 b)
    { Check((b - a).Length > ToleranceMm, "Zero-length primitive."); return new(id, ordinal, a, b, null, 0, 0); }
    private static double Area(SketchPrimitive p)
    {
        if (p.Center is not { } c) return Cross(p.Start, p.End) / 2;
        return (Cross(c, p.End - p.Start) + p.RadiusMm * p.RadiusMm * p.SweepRadians) / 2;
    }
    private static bool On(SketchPrimitive p, SketchPoint2 q)
    {
        if (p.Center is not { } c) return Math.Abs(Cross(p.End - p.Start, q - p.Start)) <= ToleranceMm * (p.End - p.Start).Length && Dot(q - p.Start, q - p.End) <= ToleranceMm * ToleranceMm;
        if (Math.Abs((q - c).Length - p.RadiusMm) > ToleranceMm) return false;
        if (p.FullCircle) return true;
        var a = Math.Atan2(q.Y - c.Y, q.X - c.X) - Math.Atan2(p.Start.Y - c.Y, p.Start.X - c.X);
        return PositiveAngle(p.SweepRadians > 0 ? a : -a) <= Math.Abs(p.SweepRadians) + 1e-10 || Near(q, p.Start);
    }
    private sealed record Hits(bool Overlap, IReadOnlyList<SketchPoint2> Points);
    private static Hits Intersections(SketchPrimitive a, SketchPrimitive b)
    {
        var hits = new List<SketchPoint2>();
        if (!a.Circular && !b.Circular)
        {
            var u = a.End - a.Start; var v = b.End - b.Start; var den = Cross(u, v);
            if (Math.Abs(den) <= 1e-12 * u.Length * v.Length)
            {
                if (Math.Abs(Cross(b.Start - a.Start, u)) > ToleranceMm * u.Length) return new(false, hits);
                foreach (var p in new[] { a.Start, a.End, b.Start, b.End }) if (On(a, p) && On(b, p) && !hits.Any(h => Near(h, p))) hits.Add(p);
                return new(hits.Count > 1, hits);
            }
            var t = Cross(b.Start - a.Start, v) / den; var q = a.Start + u * t;
            if (On(a, q) && On(b, q)) hits.Add(q); return new(false, hits);
        }
        if (!a.Circular || !b.Circular)
        {
            var line = a.Circular ? b : a; var circle = a.Circular ? a : b;
            var u = line.End - line.Start; var w = line.Start - circle.Center!.Value;
            var aa = Dot(u, u); var bb = 2 * Dot(w, u); var cc = Dot(w, w) - circle.RadiusMm * circle.RadiusMm;
            var disc = bb * bb - 4 * aa * cc;
            if (disc >= -1e-12)
                foreach (var sign in new[] { -1, 1 })
                { var q = line.Start + u * ((-bb + sign * Math.Sqrt(Math.Max(0, disc))) / (2 * aa)); if (On(line, q) && On(circle, q) && !hits.Any(h => Near(h, q))) hits.Add(q); }
            return new(false, hits);
        }
        var ca = a.Center!.Value; var cb = b.Center!.Value; var delta = cb - ca; var distance = delta.Length;
        if (distance <= ToleranceMm && Math.Abs(a.RadiusMm - b.RadiusMm) <= ToleranceMm)
        {
            var overlapping = a.FullCircle || b.FullCircle || On(b, a.At(0.5)) || On(a, b.At(0.5));
            foreach (var p in new[] { a.Start, a.End, b.Start, b.End }) if (On(a, p) && On(b, p) && !hits.Any(h => Near(h, p))) hits.Add(p);
            return new(overlapping, hits);
        }
        if (distance < ToleranceMm || distance > a.RadiusMm + b.RadiusMm + ToleranceMm || distance < Math.Abs(a.RadiusMm - b.RadiusMm) - ToleranceMm) return new(false, hits);
        var x = (a.RadiusMm * a.RadiusMm - b.RadiusMm * b.RadiusMm + distance * distance) / (2 * distance);
        var h2 = a.RadiusMm * a.RadiusMm - x * x; if (h2 < -1e-10) return new(false, hits);
        var center = ca + delta * (x / distance); var perp = new SketchPoint2(-delta.Y, delta.X) * (Math.Sqrt(Math.Max(0, h2)) / distance);
        foreach (var p in new[] { center + perp, center - perp }) if (On(a, p) && On(b, p) && !hits.Any(h => Near(h, p))) hits.Add(p);
        return new(false, hits);
    }
    public static bool Contains(PreparedSketchLoop loop, SketchPoint2 q)
    {
        // Analytic horizontal-ray crossings; split arcs at vertical extrema so
        // half-open endpoint counting is identical for lines and circular arcs.
        var crossings = 0;
        foreach (var p in loop.Primitives)
        {
            if (On(p, q)) return false;
            if (p.Center is not { } c)
            {
                if ((p.Start.Y > q.Y) != (p.End.Y > q.Y) && p.Start.X + (q.Y - p.Start.Y) * (p.End.X - p.Start.X) / (p.End.Y - p.Start.Y) > q.X) crossings++;
                continue;
            }
            var pieces = new List<double> { 0, 1 }; var start = Math.Atan2(p.Start.Y - c.Y, p.Start.X - c.X);
            foreach (var angle in new[] { Math.PI / 2, 3 * Math.PI / 2 })
            {
                var t = PositiveAngle(p.SweepRadians > 0 ? angle - start : start - angle) / Math.Abs(p.SweepRadians);
                if (t > 1e-10 && t < 1 - 1e-10) pieces.Add(t);
            }
            pieces.Sort();
            for (var i = 1; i < pieces.Count; i++)
            {
                var a = p.At(pieces[i - 1]); var b = p.At(pieces[i]);
                if ((a.Y > q.Y) == (b.Y > q.Y)) continue;
                var dx = Math.Sqrt(Math.Max(0, p.RadiusMm * p.RadiusMm - (q.Y - c.Y) * (q.Y - c.Y)));
                var x = p.At((pieces[i - 1] + pieces[i]) / 2).X >= c.X ? c.X + dx : c.X - dx;
                if (x > q.X) crossings++;
            }
        }
        return crossings % 2 == 1;
    }
    private static void ValidateConstraints(ClosedSketch sketch, Dictionary<string, SketchPoint2> points, Dictionary<string, SketchEntity> entities)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in sketch.Constraints)
        {
            Check(declared.Add(c.Kind + ":" + string.Join("|", c.References)), "Repeated constraint is redundant/overconstrained.", V03FailureCodes.OverConstrainedSketch);
            SketchEntity Entity(int i) { Check(i < c.References.Count && entities.ContainsKey(c.References[i]), "Constraint requires an entity reference."); return entities[c.References[i]]; }
            SketchPoint2 Point(int i) { Check(i < c.References.Count && points.ContainsKey(c.References[i]), "Constraint requires a point reference."); return points[c.References[i]]; }
            SketchPoint2 Vector(int i) { var e = Entity(i); Check(e.Kind == SketchEntityKind.Line, "Orientation constraint requires a line."); return points[e.Points[1]] - points[e.Points[0]]; }
            double Radius(int i) { var e = Entity(i); Check(e.Kind is SketchEntityKind.Arc or SketchEntityKind.Circle, "Radial constraint requires a circle/arc."); return e.Radius!.Millimeters; }
            void Arity(int n) => Check(c.References.Count == n, "Constraint arity mismatch.");
            double actual = 0; bool valid;
            switch (c.Kind)
            {
                case ConstraintKind.Coincident: Arity(2); valid = Near(Point(0), Point(1)); break;
                case ConstraintKind.Horizontal: Arity(1); valid = Math.Abs(Vector(0).Y) <= ToleranceMm; break;
                case ConstraintKind.Vertical: Arity(1); valid = Math.Abs(Vector(0).X) <= ToleranceMm; break;
                case ConstraintKind.Parallel: Arity(2); valid = Math.Abs(Cross(Vector(0), Vector(1))) <= ToleranceMm * Vector(0).Length * Vector(1).Length; break;
                case ConstraintKind.Perpendicular: Arity(2); valid = Math.Abs(Dot(Vector(0), Vector(1))) <= ToleranceMm * Vector(0).Length * Vector(1).Length; break;
                case ConstraintKind.Concentric: Arity(2); _ = Radius(0); _ = Radius(1); valid = Near(points[Entity(0).Points[0]], points[Entity(1).Points[0]]); break;
                case ConstraintKind.Equal:
                    Arity(2); var a = Entity(0); var b = Entity(1);
                    valid = a.Kind == b.Kind && Math.Abs(a.Kind == SketchEntityKind.Line ? Vector(0).Length - Vector(1).Length : Radius(0) - Radius(1)) <= ToleranceMm; break;
                case ConstraintKind.Distance:
                    Check(c.References.Count is 1 or 2, "Distance requires one line or two points."); actual = c.References.Count == 1 ? Vector(0).Length : (Point(1) - Point(0)).Length;
                    valid = Math.Abs(actual - c.Dimension!.Millimeters) <= ToleranceMm; break;
                case ConstraintKind.Radius: Arity(1); actual = Radius(0); valid = Math.Abs(actual - c.Dimension!.Millimeters) <= ToleranceMm; break;
                case ConstraintKind.Diameter: Arity(1); actual = Radius(0) * 2; valid = Math.Abs(actual - c.Dimension!.Millimeters) <= ToleranceMm; break;
                default: valid = false; break;
            }
            Check(valid, "Declared constraint disagrees with dimensioned geometry: " + c.Id, V03FailureCodes.OverConstrainedSketch);
        }
    }
}

public static class SketchFrames
{
    public static Vector3 Add(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3 Scale(Vector3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vector3 Subtract(Vector3 a, Vector3 b) => Add(a, Scale(b, -1));
    public static double Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static Vector3 World(SpatialPlacement p, SketchPoint2 point) => Add(Add(p.Frame.OriginMm, Scale(p.Frame.ZAxis, p.Offset.Millimeters)), Add(Scale(p.Frame.XAxis, point.X), Scale(p.Frame.YAxis, point.Y)));
    public static void VerifyPlane(SpatialPlacement p, Vector3 planePointMm, Vector3 planeNormal, Vector3? direction)
    {
        ContractValidation.Placement(p);
        ContractValidation.Require(Dot(p.Frame.ZAxis, planeNormal) >= 1 - 1e-8, "Native plane normal is reversed or inconsistent.", "SPATIAL_FRAME_MISMATCH");
        ContractValidation.Require(Math.Abs(Dot(Subtract(p.Frame.OriginMm, planePointMm), planeNormal)) <= ContractLimits.LinearToleranceMm, "Frame origin is not on its bound reference plane.", "SPATIAL_FRAME_MISMATCH");
        if (direction is { } axis) ContractValidation.Require(Dot(axis, p.Frame.XAxis) >= 1 - 1e-8 && Math.Abs(Dot(axis, planeNormal)) <= 1e-8, "Bound in-plane direction disagrees with frame.", "SPATIAL_FRAME_MISMATCH");
    }
}
