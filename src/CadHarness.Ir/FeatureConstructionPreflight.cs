using System;
using System.Collections.Generic;
using System.Linq;

namespace CadHarness.Ir.V03;

public sealed record PreparedFeatureProfile(ConstructionOperation Operation, SpatialPlacement Placement, PreparedPlanarSketch Sketch);
public sealed record PrismLayer(string Id, PreparedPlanarSketch Footprint, double StartMm, double EndMm, bool Remove);
public sealed record FeatureSolidExpectation(double VolumeMm3, LocalFrame Frame, Vector3 MinimumLocalMm, Vector3 MaximumLocalMm,
    IReadOnlyList<PrismLayer> Layers, double? RevolveRadiusMm, double? RevolveAxialSpanMm);

// Analytic, bounded material proof. Crossing footprints and mixed sweep axes are
// deliberately unqualified; native success alone cannot authorize them.
public static class FeatureConstructionPreflight
{
    private const double Epsilon = PlanarSketchPreflight.ToleranceMm;
    public static FeatureSolidExpectation Evaluate(IReadOnlyList<PreparedFeatureProfile> features)
    {
        ContractValidation.Require(features.Count is > 0 and <= ContractLimits.Operations, "Bounded feature history required.");
        var first = features[0];
        if (first.Operation.Kind == ConstructionKind.CreateRevolvedBoss)
        {
            ContractValidation.Require(features.Count == 1, "Mixed rotational/prismatic material proofs are not qualified.", V03FailureCodes.UnsupportedBodyTopology);
            return Revolve(first);
        }
        ContractValidation.Require(first.Operation.Kind == ConstructionKind.CreateExtrude, "Initial feature must create a tracked single body.");
        var frame = first.Placement.Frame with { OriginMm = SketchFrames.World(first.Placement, new(0, 0)) };
        var layers = new List<PrismLayer>();
        foreach (var f in features)
        {
            var o = f.Operation;
            ContractValidation.Require(o.Kind is ConstructionKind.CreateExtrude or ConstructionKind.CreateAdditiveBoss or ConstructionKind.CreateExtrudedCut,
                "Unsupported sweep family.", V03FailureCodes.UnsupportedBodyTopology);
            var alignment = SketchFrames.Dot(f.Placement.Frame.ZAxis, frame.ZAxis);
            ContractValidation.Require(Math.Abs(Math.Abs(alignment) - 1) < 1e-8, "Mixed sweep axes lack an exact material proof.", V03FailureCodes.UnsupportedBodyTopology);
            var prepared = Reframe(f, frame);
            ContractValidation.Require(prepared.Loops.Count(l => !l.Inner) == 1, "Disconnected material islands are not qualified.", V03FailureCodes.UnsupportedBodyTopology);
            var origin = SketchFrames.World(f.Placement, new(0, 0));
            var z = SketchFrames.Dot(SketchFrames.Subtract(origin, frame.OriginMm), frame.ZAxis);
            var sign = alignment * (o.Direction == Direction.Reverse ? -1 : 1);
            var remove = o.Kind == ConstructionKind.CreateExtrudedCut;
            double end;
            if (o.EndCondition == EndCondition.ThroughAll)
            {
                ContractValidation.Require(remove && layers.Count > 0, "Through-all requires a managed host.");
                end = sign > 0 ? layers.Max(l => l.EndMm) : layers.Min(l => l.StartMm);
            }
            else end = z + sign * o.Depth!.Millimeters;
            var layer = new PrismLayer(o.SemanticId, prepared, Math.Min(z, end), Math.Max(z, end), remove);
            ContractValidation.Require(layer.EndMm - layer.StartMm > Epsilon, "Sweep has no measurable extent.");
            if (layers.Count > 0)
            {
                ContractValidation.Require(o.Kind != ConstructionKind.CreateExtrude, "Only one initial body is allowed.", V03FailureCodes.UnsupportedBodyTopology);
                ContractValidation.Require(PlanarSketchPreflight.Relation(prepared, layers[0].Footprint) is SketchRegionRelation.Inside or SketchRegionRelation.Equal,
                    "Sweep footprint must stay inside the supported root material.", V03FailureCodes.UnsupportedBodyTopology);
                foreach (var old in layers)
                    ContractValidation.Require(PlanarSketchPreflight.Relation(prepared, old.Footprint) != SketchRegionRelation.Intersecting,
                        "Partial footprint overlap lacks a qualified material proof.", V03FailureCodes.UnsupportedBodyTopology);
                if (remove)
                    ContractValidation.Require(layers.Where(l => !l.Remove).All(l => PlanarSketchPreflight.Relation(prepared, l.Footprint) is not (SketchRegionRelation.Equal or SketchRegionRelation.Contains)),
                        "Cut could remove an entire support-bearing footprint.", V03FailureCodes.UnsupportedBodyTopology);
            }
            var before = Volume(layers);
            layers.Add(layer);
            var after = Volume(layers);
            ContractValidation.Require(remove ? after < before - Epsilon && after > Epsilon : after > before + Epsilon,
                "Sweep must have the intended positive/negative material change without emptying the body.");
            if (layers.Count > 1 && !remove)
            {
                var prior = layers.Take(layers.Count - 1).ToArray();
                var levels = prior.SelectMany(l => new[] { l.StartMm, l.EndMm }).Append(layer.StartMm).Append(layer.EndMm).Distinct().OrderBy(v => v).ToArray();
                var attached = false;
                for (var i = 1; i < levels.Length; i++)
                {
                    var lo = levels[i - 1]; var hi = levels[i];
                    if (hi < layer.StartMm - Epsilon || lo > layer.EndMm + Epsilon) continue;
                    // Measure actual retained material, not an earlier positive
                    // layer that a subsequent cut may already have removed.
                    var probe = layer with { StartMm = lo, EndMm = hi, Remove = true };
                    if (Volume(prior) - Volume(prior.Append(probe).ToArray()) > Epsilon) { attached = true; break; }
                }
                ContractValidation.Require(attached, "Additive sweep is detached from retained managed material.", V03FailureCodes.UnsupportedBodyTopology);
            }
        }
        var bounds = Bounds(layers[0].Footprint.Primitives);
        return new(Volume(layers), frame, new(bounds.MinX, bounds.MinY, layers.Where(l => !l.Remove).Min(l => l.StartMm)),
            new(bounds.MaxX, bounds.MaxY, layers.Where(l => !l.Remove).Max(l => l.EndMm)), layers, null, null);
    }
    public static double Volume(IReadOnlyList<PrismLayer> layers)
    {
        if (layers.Count == 0) return 0;
        var regions = new List<PreparedPlanarSketch>();
        foreach (var l in layers)
            if (!regions.Any(r => PlanarSketchPreflight.Relation(r, l.Footprint) == SketchRegionRelation.Equal)) regions.Add(l.Footprint);
        var levels = layers.SelectMany(l => new[] { l.StartMm, l.EndMm }).Distinct().OrderBy(x => x).ToArray();
        var volume = 0.0;
        for (var i = 1; i < levels.Length; i++)
        {
            var z = (levels[i - 1] + levels[i]) / 2;
            var area = 0.0;
            foreach (var region in regions)
            {
                var descendants = regions.Where(r => PlanarSketchPreflight.Relation(r, region) == SketchRegionRelation.Inside).ToArray();
                var children = descendants.Where(r => !descendants.Any(other => other != r && PlanarSketchPreflight.Relation(r, other) == SketchRegionRelation.Inside)).ToArray();
                var cellArea = region.Loops.Sum(l => l.SignedAreaMm2) - children.Sum(r => r.Loops.Sum(l => l.SignedAreaMm2));
                var material = false;
                foreach (var l in layers.Where(l => z > l.StartMm && z < l.EndMm))
                    if (PlanarSketchPreflight.Relation(region, l.Footprint) is SketchRegionRelation.Inside or SketchRegionRelation.Equal) material = !l.Remove;
                if (material) area += cellArea;
            }
            volume += area * (levels[i] - levels[i - 1]);
        }
        return volume;
    }
    private static PreparedPlanarSketch Reframe(PreparedFeatureProfile feature, LocalFrame frame)
    {
        var placement = feature.Placement;
        var converted = feature.Sketch.Specification with
        {
            Points = feature.Sketch.Specification.Points.Select(p =>
            {
                var world = SketchFrames.Subtract(SketchFrames.World(placement, new(p.X.Millimeters, p.Y.Millimeters)), frame.OriginMm);
                return p with { X = new(SketchFrames.Dot(world, frame.XAxis)), Y = new(SketchFrames.Dot(world, frame.YAxis)) };
            }).ToArray()
        };
        // Flipped sketch normals reverse winding; do not silently reinterpret
        // an otherwise mirrored profile as the same material region.
        ContractValidation.Require(SketchFrames.Dot(placement.Frame.ZAxis, frame.ZAxis) > 0, "Oppositely oriented profile frames are not qualified.", V03FailureCodes.UnsupportedBodyTopology);
        SketchPoint2 Point(SketchPoint2 local)
        {
            var world = SketchFrames.Subtract(SketchFrames.World(placement, local), frame.OriginMm);
            return new(SketchFrames.Dot(world, frame.XAxis), SketchFrames.Dot(world, frame.YAxis));
        }
        var loops = feature.Sketch.Loops.Select(l => l with { Primitives = l.Primitives.Select(p => p with
            { Start = Point(p.Start), End = Point(p.End), Center = p.Center is { } c ? Point(c) : null }).ToArray() }).ToArray();
        return new(converted, loops, loops.SelectMany(l => l.Primitives).ToArray(), feature.Sketch.CoordinateDriverPolicy);
    }
    private static FeatureSolidExpectation Revolve(PreparedFeatureProfile feature)
    {
        var o = feature.Operation; var sketch = feature.Sketch;
        ContractValidation.Require(o.Axis?.SemanticId == o.Profile!.SemanticId.Replace(".profile", ".axis_x", StringComparison.Ordinal),
            "Revolve currently qualifies the exact profile's declared local X axis.");
        ContractValidation.Require(sketch.Loops.Count(l => !l.Inner) == 1, "Disconnected revolve profiles are unsupported.", V03FailureCodes.UnsupportedBodyTopology);
        var b = Bounds(sketch.Primitives);
        ContractValidation.Require(b.MinY >= -Epsilon && b.MaxY > Epsilon, "Profile crosses or lies behind the declared revolve axis.");
        var moment = sketch.Primitives.Sum(FirstMomentY);
        var angle = o.AngleDegrees!.Value * Math.PI / 180;
        var volume = moment * angle;
        ContractValidation.Require(volume > Epsilon, "Revolve must create positive material.");
        var sign = o.Direction == Direction.Reverse ? -1 : 1;
        var angles = new List<double> { 0, angle * sign };
        for (var k = -4; k <= 4; k++) if (k * Math.PI / 2 >= Math.Min(0, angle * sign) - Epsilon && k * Math.PI / 2 <= Math.Max(0, angle * sign) + Epsilon) angles.Add(k * Math.PI / 2);
        var ys = angles.SelectMany(t => new[] { b.MinY * Math.Cos(t), b.MaxY * Math.Cos(t) }).ToArray();
        var zs = angles.SelectMany(t => new[] { b.MinY * Math.Sin(t), b.MaxY * Math.Sin(t) }).ToArray();
        return new(volume, feature.Placement.Frame with { OriginMm = SketchFrames.World(feature.Placement, new(0, 0)) },
            new(b.MinX, ys.Min(), zs.Min()), new(b.MaxX, ys.Max(), zs.Max()), Array.Empty<PrismLayer>(), b.MaxY, b.MaxX - b.MinX);
    }
    private static double FirstMomentY(SketchPrimitive p)
    {
        if (p.Center is not { } c) return -(p.End.X - p.Start.X) * (p.Start.Y * p.Start.Y + p.Start.Y * p.End.Y + p.End.Y * p.End.Y) / 6;
        var a = Math.Atan2(p.Start.Y - c.Y, p.Start.X - c.X); var b = a + p.SweepRadians;
        var i1 = Math.Cos(a) - Math.Cos(b);
        var i2 = (b - a) / 2 - (Math.Sin(2 * b) - Math.Sin(2 * a)) / 4;
        var i3 = -Math.Cos(b) + Math.Pow(Math.Cos(b), 3) / 3 + Math.Cos(a) - Math.Pow(Math.Cos(a), 3) / 3;
        return p.RadiusMm * (c.Y * c.Y * i1 + 2 * c.Y * p.RadiusMm * i2 + p.RadiusMm * p.RadiusMm * i3) / 2;
    }
    public static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IReadOnlyList<SketchPrimitive> primitives)
    {
        var points = new List<SketchPoint2>();
        foreach (var p in primitives)
        {
            points.Add(p.Start); points.Add(p.End);
            if (p.Center is not { } c) continue;
            var start = Math.Atan2(p.Start.Y - c.Y, p.Start.X - c.X);
            for (var k = -8; k <= 8; k++)
            {
                var fraction = (k * Math.PI / 2 - start) / p.SweepRadians;
                if (fraction >= 0 && fraction <= 1) points.Add(p.At(fraction));
            }
        }
        return (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
    }
}
