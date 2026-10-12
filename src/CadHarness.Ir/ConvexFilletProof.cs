using System;
using System.Collections.Generic;
using System.Linq;

namespace CadHarness.Ir.V03;

public sealed record ConvexFilletGeometry(string Id, string Host, int Axis, int[] Transverse, double[] Start, double[] End,
    double[] Center, double[] Low, double[] High, double Radius, double RemovedVolume);
public static class ConvexFilletProof
{
    private const double E = 1e-7;
    private static void Require(bool condition, string message) => ContractValidation.Require(condition, message, V03FailureCodes.UnsupportedBodyTopology);
    public static void RequireUniqueCandidateCount(int count) => ContractValidation.Require(count == 1,
        $"Convex edge selector resolved {count} exact owned linear edges.", V03FailureCodes.AmbiguousTargetSet);
    public static IReadOnlyList<ConvexFilletGeometry> Describe(ConstructionProgram program, FeatureSolidExpectation material)
    {
        var treatments = program.Operations.Where(o => o.EdgeTreatment is not null).ToArray();
        if (treatments.Length == 0) return Array.Empty<ConvexFilletGeometry>();
        Require(material.Orthogonal is not null && treatments.Length <= 4 && program.Operations.TakeLast(treatments.Length).SequenceEqual(treatments),
            "Simple convex fillets must terminate an orthogonal extrusion/circular-cut history.");
        var proof = material.Orthogonal!; var result = new List<ConvexFilletGeometry>();
        foreach (var o in treatments)
        {
            var e = o.EdgeTreatment!; var host = proof.Boxes.Single(b => b.Id == e.HostFeature.SemanticId);
            var axis = (int)e.EdgeAxis; var t = Enumerable.Range(0, 3).Where(i => i != axis).ToArray(); var radius = e.Radius.Millimeters;
            Require(t.All(i => radius * 2 < host.High[i] - host.Low[i]), "Convex radius consumes a support-bearing transverse span.");
            var start = host.Low.ToArray(); var end = host.High.ToArray(); var center = host.Low.ToArray(); var low = host.Low.ToArray(); var high = host.High.ToArray();
            var sides = new[] { e.FirstSide, e.SecondSide };
            for (var k = 0; k < 2; k++)
            {
                var i = t[k]; var maximum = sides[k] == BoundarySide.Maximum; var corner = maximum ? host.High[i] : host.Low[i];
                start[i] = end[i] = corner; center[i] = corner + (maximum ? -radius : radius);
                low[i] = maximum ? corner - radius : corner; high[i] = maximum ? corner : corner + radius;
            }
            bool Touches(double[] a, double[] b) => Enumerable.Range(0, 3).All(i => b[i] >= low[i] - E && a[i] <= high[i] + E);
            Require(proof.Boxes.Where(b => b.Id != host.Id).All(b => !Touches(b.Low, b.High)), "Selected corner is covered by another positive body or attachment.");
            foreach (var b in proof.Bores)
            {
                var a = Enumerable.Range(0, 3).Select(i => i == b.Axis ? b.Start : b.Center[i] - b.Radius).ToArray();
                var z = Enumerable.Range(0, 3).Select(i => i == b.Axis ? b.End : b.Center[i] + b.Radius).ToArray();
                Require(!Touches(a, z), "Bore intersects the finite convex fillet region.");
            }
            Require(result.All(f => !Touches(f.Low, f.High)), "Intersecting fillet regions lack an exact material proof.");
            result.Add(new(o.SemanticId, host.Id, axis, t, start, end, center, low, high, radius,
                (1 - Math.PI / 4) * radius * radius * (host.High[axis] - host.Low[axis])));
        }
        return result;
    }
    public static FeatureSolidExpectation Apply(ConstructionProgram program, FeatureSolidExpectation material) =>
        material with { VolumeMm3 = material.VolumeMm3 - Describe(program, material).Sum(f => f.RemovedVolume) };
}
