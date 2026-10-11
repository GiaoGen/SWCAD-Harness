using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;

internal static class Cases
{
    internal static readonly LocalFrame XY = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
    internal static SpatialPlacement Plane(double z = 0) => new(new(z == 0 ? "world.xy" : "top.plane", SemanticType.ReferencePlane), null, XY with { OriginMm = new(0, 0, z) }, new(0));
    internal static LocalPoint P(string id, double x, double y) => new(id, new(x), new(y));
    internal static ClosedSketch Circle(double x, double y, double radius) => new(new[] { P("c", x, y) },
        new[] { new SketchEntity("circle", SketchEntityKind.Circle, new[] { "c" }, new(radius), null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "circle" }) }, new[] { new SketchConstraint("radius", ConstraintKind.Radius, new[] { "circle" }, new(radius)) }, ConstraintStatus.FullyConstrained);
    internal static ClosedSketch Rectangle(double x0, double y0, double x1, double y1) => new(new[] { P("a", x0, y0), P("b", x1, y0), P("c", x1, y1), P("d", x0, y1) },
        new[] { new SketchEntity("outline", SketchEntityKind.Polyline, new[] { "a", "b", "c", "d" }, null, null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "outline" }) }, Array.Empty<SketchConstraint>(), ConstraintStatus.FullyConstrained);
    internal static ClosedSketch Slot => new(new[] { P("a", -22, 0), P("b", -14, 0) },
        new[] { new SketchEntity("slot", SketchEntityKind.Slot, new[] { "a", "b" }, null, null, new(4), new(12)) },
        new[] { new SketchLoop("outer", false, new[] { "slot" }) }, Array.Empty<SketchConstraint>(), ConstraintStatus.FullyConstrained);
    internal static ConstructionOperation Sketch(string id, ClosedSketch sketch, double z = 0) => new(id + "_op", ConstructionKind.CreateSketch, id, Plane(z), sketch, null, null, null, null, null, null, null, null);
    internal static ConstructionOperation Extrude(string id, string profile, ConstructionKind kind, double? depth, bool reverse = false, string? host = null) => new(id + "_op", kind, id, null, null,
        new(profile + ".profile", SemanticType.SketchProfile), null, host is null ? null : new(host + ".body", SemanticType.BodyRef), depth is null ? EndCondition.ThroughAll : EndCondition.Blind,
        reverse ? Direction.Reverse : Direction.Forward, depth is null ? null : new(depth.Value), null,
        kind == ConstructionKind.CreateExtrude ? BodyRule.NewSingleBody : kind == ConstructionKind.CreateAdditiveBoss ? BodyRule.MergeWithHost : BodyRule.RemoveFromHost);
    internal static ConstructionOperation Top => new("top_op", ConstructionKind.CreateDatumPlane, "top", new(new("world.xy", SemanticType.ReferencePlane), null, XY, new(10)), null, null, null, null, null, null, null, null, null);
    internal static ConstructionProgram Program(params ConstructionOperation[] operations) => new("0.3", ModelOrigin.Harness, RequestMode.CreateModel, "requirements", operations);
    internal static ConstructionProgram Torus => Program(Sketch("radial", Circle(0, 15, 3)),
        new("revolve_op", ConstructionKind.CreateRevolvedBoss, "revolve", null, null, new("radial.profile", SemanticType.SketchProfile), new("radial.axis_x", SemanticType.ReferenceAxis), null,
            EndCondition.FullRevolution, Direction.Forward, null, 360, BodyRule.NewSingleBody));
    internal static ConstructionProgram Quarter => Program(Sketch("section", Rectangle(0, 0, 20, 10)),
        new("quarter_op", ConstructionKind.CreateRevolvedBoss, "quarter", null, null, new("section.profile", SemanticType.SketchProfile), new("section.axis_x", SemanticType.ReferenceAxis), null,
            EndCondition.FiniteAngle, Direction.Forward, null, 90, BodyRule.NewSingleBody));
    internal static ConstructionProgram Root => Program(Sketch("base_sketch", Rectangle(-40, -30, 40, 30)), Extrude("base", "base_sketch", ConstructionKind.CreateExtrude, 10));
    internal static ConstructionProgram Boss => Root with { Operations = Root.Operations.Concat(new[] { Top, Sketch("boss_sketch", Circle(18, 0, 5), 10), Extrude("boss", "boss_sketch", ConstructionKind.CreateAdditiveBoss, 6, host: "base") }).ToArray() };
    internal static ConstructionProgram Cut => Root with { Operations = Root.Operations.Concat(new[] { Top, Sketch("pocket_sketch", Slot, 10), Extrude("pocket", "pocket_sketch", ConstructionKind.CreateExtrudedCut, 4, true, "base") }).ToArray() };
    internal static ConstructionProgram Composition => Boss with { Operations = Boss.Operations.Concat(new[] {
        Sketch("pocket_sketch", Slot, 10), Extrude("pocket", "pocket_sketch", ConstructionKind.CreateExtrudedCut, 4, true, "boss"),
        Sketch("through_sketch", Circle(-5, 20, 3), 10), Extrude("through", "through_sketch", ConstructionKind.CreateExtrudedCut, null, true, "pocket") }).ToArray() };
    internal static ConstructionProgram LateFailure => Boss with { Operations = Boss.Operations.Concat(Composition.Operations.Skip(Boss.Operations.Count).Take(2)).ToArray() };
}
