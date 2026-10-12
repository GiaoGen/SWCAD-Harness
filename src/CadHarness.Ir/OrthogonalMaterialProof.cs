using System;
using System.Collections.Generic;
using System.Linq;

namespace CadHarness.Ir.V03;

public sealed record MaterialBox(string Id, double[] Low, double[] High);
public sealed record MaterialBore(string Id, int Axis, double[] Center, double Radius, double Start, double End);
public sealed record OrthogonalMaterial(IReadOnlyList<MaterialBox> Boxes, IReadOnlyList<MaterialBore> Bores);

// Exact box union and disjoint cylindrical removals in the root's local frame.
// Bounding-square containment is conservative: an unproved intersection refuses
// before native mutation rather than estimating a boolean volume.
public static class OrthogonalMaterialProof
{
    private const double E = 1e-7;
    private static void Require(bool condition, string message) => ContractValidation.Require(condition, message, V03FailureCodes.UnsupportedBodyTopology);
    public static double[] Local(Vector3 p, LocalFrame frame)
    {
        var d = SketchFrames.Subtract(p, frame.OriginMm);
        return new[] { SketchFrames.Dot(d, frame.XAxis), SketchFrames.Dot(d, frame.YAxis), SketchFrames.Dot(d, frame.ZAxis) };
    }
    private static int Axis(Vector3 normal, LocalFrame frame)
    {
        var components = new[] { SketchFrames.Dot(normal, frame.XAxis), SketchFrames.Dot(normal, frame.YAxis), SketchFrames.Dot(normal, frame.ZAxis) };
        var axes = Enumerable.Range(0, 3).Where(i => Math.Abs(Math.Abs(components[i]) - 1) < E).ToArray();
        Require(axes.Length == 1, "Orthogonal material requires root-frame principal sweep axes."); return axes[0];
    }
    public static FeatureSolidExpectation Evaluate(IReadOnlyList<PreparedFeatureProfile> features)
    {
        Require(features.Count > 0 && features[0].Operation.Kind == ConstructionKind.CreateExtrude, "Orthogonal proof requires a rectangular initial body.");
        var frame = features[0].Placement.Frame with { OriginMm = SketchFrames.World(features[0].Placement, new(0, 0)) };
        var boxes = new List<MaterialBox>(); var bores = new List<MaterialBore>();
        foreach (var f in features)
        {
            var o = f.Operation; var axis = Axis(f.Placement.Frame.ZAxis, frame);
            var transverse = Enumerable.Range(0, 3).Where(i => i != axis).ToArray();
            var origin = Local(SketchFrames.World(f.Placement, new(0, 0)), frame);
            var sign = SketchFrames.Dot(f.Placement.Frame.ZAxis, new[] { frame.XAxis, frame.YAxis, frame.ZAxis }[axis]) * (o.Direction == Direction.Reverse ? -1 : 1);
            if (o.Kind is ConstructionKind.CreateExtrude or ConstructionKind.CreateAdditiveBoss)
            {
                Require(bores.Count == 0 && boxes.Count < 16 && o.EndCondition == EndCondition.Blind && o.Depth is not null, "Orthogonal additions must precede cuts and use bounded blind depth.");
                var p = f.Sketch.Primitives;
                Require(f.Sketch.Loops.Count == 1 && !f.Sketch.Loops[0].Inner && p.Count == 4 && p.All(x => x.Center is null), "Orthogonal positive material requires one four-edge rectangle.");
                var points = p.Select(x => Local(SketchFrames.World(f.Placement, x.Start), frame)).ToArray();
                var lo = Enumerable.Range(0, 3).Select(i => points.Min(x => x[i])).ToArray();
                var hi = Enumerable.Range(0, 3).Select(i => points.Max(x => x[i])).ToArray();
                Require(transverse.All(i => hi[i] - lo[i] > E) && points.All(x => transverse.All(i => Math.Abs(x[i] - lo[i]) < E || Math.Abs(x[i] - hi[i]) < E)), "Positive footprint must be an axis-aligned rectangle in the root frame.");
                foreach (var edge in p)
                {
                    var a = Local(SketchFrames.World(f.Placement, edge.Start), frame); var b = Local(SketchFrames.World(f.Placement, edge.End), frame);
                    Require(Enumerable.Range(0, 3).Count(i => Math.Abs(a[i] - b[i]) > E) == 1, "Rectangle edges must follow principal axes.");
                }
                lo[axis] = Math.Min(origin[axis], origin[axis] + sign * o.Depth!.Millimeters);
                hi[axis] = Math.Max(origin[axis], origin[axis] + sign * o.Depth.Millimeters);
                var box = new MaterialBox(o.SemanticId, lo, hi);
                Require(boxes.Count == 0 || boxes.Any(b => Attached(b, box)), "Detached orthogonal addition.");
                var before = UnionVolume(boxes); boxes.Add(box);
                Require(UnionVolume(boxes) > before + E, "Addition must introduce retained material.");
            }
            else
            {
                Require(o.Kind == ConstructionKind.CreateExtrudedCut && boxes.Count > 0 && bores.Count < 32 && f.Sketch.Primitives.Count == 1,
                    "Orthogonal removals support one principal-axis circular profile only.");
                var circle = f.Sketch.Primitives.Single();
                Require(circle.Center is not null && Math.Abs(Math.Abs(circle.SweepRadians) - 2 * Math.PI) < E, "Cross-axis cuts require a complete circle.");
                var center = Local(SketchFrames.World(f.Placement, circle.Center!.Value), frame);
                var end = o.EndCondition == EndCondition.ThroughAll ? sign > 0 ? boxes.Max(b => b.High[axis]) : boxes.Min(b => b.Low[axis]) : origin[axis] + sign * o.Depth!.Millimeters;
                var start = Math.Min(origin[axis], end); end = Math.Max(origin[axis], end);
                Require(end - start > E, "Cut has no directed span.");
                var intervals = new List<(double Low, double High)>();
                foreach (var b in boxes)
                {
                    var low = Math.Max(start, b.Low[axis]); var high = Math.Min(end, b.High[axis]);
                    if (high - low <= E || transverse.Any(i => center[i] + circle.RadiusMm <= b.Low[i] + E || center[i] - circle.RadiusMm >= b.High[i] - E)) continue;
                    Require(transverse.All(i => center[i] - circle.RadiusMm > b.Low[i] + E && center[i] + circle.RadiusMm < b.High[i] - E),
                        "Partial disk/box intersection or support-bearing boundary cut lacks a proof.");
                    intervals.Add((low, high));
                }
                Require(intervals.Count > 0, "Cut misses actual host material.");
                double newlyRemoved = 0;
                foreach (var span in Merge(intervals))
                {
                    var bore = new MaterialBore(o.SemanticId, axis, center, circle.RadiusMm, span.Low, span.High);
                    var covered = Merge(bores.Where(old => old.Axis == axis && transverse.All(i => old.Center[i] == center[i]) && old.Radius == bore.Radius)
                        .Select(old => (Low: Math.Max(old.Start, span.Low), High: Math.Min(old.End, span.High))).Where(s => s.High > s.Low)).Sum(s => s.High - s.Low);
                    newlyRemoved += Math.PI * bore.Radius * bore.Radius * (span.High - span.Low - covered);
                    foreach (var old in bores)
                    {
                        if (old.Axis == axis && transverse.All(i => old.Center[i] == center[i]) && old.Radius == bore.Radius) continue;
                        Require(Disjoint(old, bore), "Intersecting or partially overlapping bores lack an exact qualified proof.");
                    }
                    bores.Add(bore);
                }
                Require(newlyRemoved > E, "Cut misses retained material: its whole span was already removed.");
            }
        }
        foreach (var a in boxes)
            foreach (var b in boxes.Where(b => string.CompareOrdinal(a.Id, b.Id) < 0 && Attached(a, b)))
            {
                var low = Enumerable.Range(0, 3).Select(i => Math.Max(a.Low[i], b.Low[i])).ToArray();
                var high = Enumerable.Range(0, 3).Select(i => Math.Min(a.High[i], b.High[i])).ToArray();
                Require(bores.All(c => Enumerable.Range(0, 3).Any(i => BoreHigh(c, i) < low[i] - E || BoreLow(c, i) > high[i] + E)),
                    "A bore intersects a box attachment; single-body connectivity is not proved.");
            }
        var unique = new List<MaterialBore>();
        foreach (var group in bores.GroupBy(c => (c.Axis, c.Center[(c.Axis + 1) % 3], c.Center[(c.Axis + 2) % 3], c.Radius)))
            foreach (var span in Merge(group.Select(c => (c.Start, c.End)))) unique.Add(group.First() with { Start = span.Low, End = span.High });
        var volume = UnionVolume(boxes) - unique.Sum(c => Math.PI * c.Radius * c.Radius * (c.End - c.Start));
        Require(volume > E, "Empty orthogonal material.");
        var min = Enumerable.Range(0, 3).Select(i => boxes.Min(b => b.Low[i])).ToArray();
        var max = Enumerable.Range(0, 3).Select(i => boxes.Max(b => b.High[i])).ToArray();
        return new(volume, frame, new(min[0], min[1], min[2]), new(max[0], max[1], max[2]), Array.Empty<PrismLayer>(), null, null)
        { Orthogonal = new(boxes, unique) };
    }
    private static bool Attached(MaterialBox a, MaterialBox b)
    {
        var spans = Enumerable.Range(0, 3).Select(i => Math.Min(a.High[i], b.High[i]) - Math.Max(a.Low[i], b.Low[i])).ToArray();
        return spans.All(x => x >= -E) && spans.Count(x => x > E) >= 2;
    }
    private static double BoreLow(MaterialBore b, int i) => i == b.Axis ? b.Start : b.Center[i] - b.Radius;
    private static double BoreHigh(MaterialBore b, int i) => i == b.Axis ? b.End : b.Center[i] + b.Radius;
    private static bool Disjoint(MaterialBore a, MaterialBore b)
    {
        if (a.Axis != b.Axis) return Enumerable.Range(0, 3).Any(i => BoreHigh(a, i) <= BoreLow(b, i) - E || BoreHigh(b, i) <= BoreLow(a, i) - E);
        if (a.End <= b.Start + E || b.End <= a.Start + E) return true;
        return Math.Sqrt(Enumerable.Range(0, 3).Where(i => i != a.Axis).Sum(i => Math.Pow(a.Center[i] - b.Center[i], 2))) > a.Radius + b.Radius + E;
    }
    private static IEnumerable<(double Low, double High)> Merge(IEnumerable<(double Low, double High)> intervals)
    {
        var ordered = intervals.OrderBy(s => s.Low).ToArray(); if (ordered.Length == 0) yield break;
        var span = ordered[0];
        foreach (var next in ordered.Skip(1)) { if (next.Low <= span.High + E) span.High = Math.Max(span.High, next.High); else { yield return span; span = next; } }
        yield return span;
    }
    private static double UnionVolume(IReadOnlyList<MaterialBox> boxes)
    {
        if (boxes.Count == 0) return 0;
        var levels = Enumerable.Range(0, 3).Select(i => boxes.SelectMany(b => new[] { b.Low[i], b.High[i] }).Distinct().OrderBy(v => v).ToArray()).ToArray();
        double volume = 0;
        for (var x = 1; x < levels[0].Length; x++) for (var y = 1; y < levels[1].Length; y++) for (var z = 1; z < levels[2].Length; z++)
        {
            var indices = new[] { x, y, z };
            var p = Enumerable.Range(0, 3).Select(i => (levels[i][indices[i] - 1] + levels[i][indices[i]]) / 2).ToArray();
            if (boxes.Any(b => Enumerable.Range(0, 3).All(i => p[i] > b.Low[i] && p[i] < b.High[i])))
                volume += Enumerable.Range(0, 3).Select(i => levels[i][indices[i]] - levels[i][indices[i] - 1]).Aggregate(1d, (a, b) => a * b);
        }
        return volume;
    }
}
