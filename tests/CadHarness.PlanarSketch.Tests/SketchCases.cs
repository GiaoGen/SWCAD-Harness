using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;

internal static class SketchCases
{
    internal static readonly LocalFrame XY = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
    internal static SpatialPlacement Placement => new(new("world.xy", SemanticType.ReferencePlane), null, XY, new(0));
    internal static LocalPoint Point(string id, double x, double y) => new(id, new(x), new(y));
    internal static ConstructionOperation Operation(string id, ClosedSketch sketch, SpatialPlacement? placement = null) =>
        new(id + "_op", ConstructionKind.CreateSketch, id, placement ?? Placement, sketch, null, null, null, null, null, null, null, null);
    internal static ClosedSketch Circle(double x = -8, double y = 6, double radius = 3) => new(new[] { Point("center", x, y) },
        new[] { new SketchEntity("circle", SketchEntityKind.Circle, new[] { "center" }, new(radius), null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "circle" }) },
        new[] { new SketchConstraint("radial", ConstraintKind.Radius, new[] { "circle" }, new(radius)) }, ConstraintStatus.FullyConstrained);
    internal static ClosedSketch Composite => new(
        new[] { Point("a", -30, -20), Point("b", 30, -20), Point("c", 30, 20), Point("d", -30, 20), Point("hole", -12, 3), Point("cap_a", 4, -5), Point("cap_b", 16, -5) },
        new[] {
            new SketchEntity("polygon", SketchEntityKind.Polyline, new[] { "a", "b", "c", "d" }, null, null, null, null),
            new SketchEntity("circle", SketchEntityKind.Circle, new[] { "hole" }, new(4), null, null, null),
            new SketchEntity("slot", SketchEntityKind.Slot, new[] { "cap_a", "cap_b" }, null, null, new(6), new(18)) },
        new[] { new SketchLoop("outer", false, new[] { "polygon" }), new SketchLoop("inner_circle", true, new[] { "circle" }), new SketchLoop("inner_slot", true, new[] { "slot" }) },
        new[] { new SketchConstraint("diameter", ConstraintKind.Diameter, new[] { "circle" }, new(8)) }, ConstraintStatus.FullyConstrained);
    internal static ClosedSketch Arcs => new(new[] { Point("o", 0, 0), Point("a", -8, 0), Point("b", 8, 0) },
        new[] { new SketchEntity("arc", SketchEntityKind.Arc, new[] { "o", "b", "a" }, new(8), 180, null, null),
            new SketchEntity("line", SketchEntityKind.Line, new[] { "a", "b" }, null, null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "arc", "line" }) },
        new[] { new SketchConstraint("horizontal", ConstraintKind.Horizontal, new[] { "line" }, null), new SketchConstraint("length", ConstraintKind.Distance, new[] { "line" }, new(16)),
            new SketchConstraint("radius", ConstraintKind.Radius, new[] { "arc" }, new(8)) }, ConstraintStatus.FullyConstrained);
    internal static ClosedSketch Rectangle => new(new[] { Point("a", -10, -5), Point("b", 10, -5), Point("c", 10, 5), Point("d", -10, 5) },
        new[] { new SketchEntity("bottom", SketchEntityKind.Line, new[] { "a", "b" }, null, null, null, null), new SketchEntity("right", SketchEntityKind.Line, new[] { "b", "c" }, null, null, null, null),
            new SketchEntity("top", SketchEntityKind.Line, new[] { "c", "d" }, null, null, null, null), new SketchEntity("left", SketchEntityKind.Line, new[] { "d", "a" }, null, null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "bottom", "right", "top", "left" }) },
        new[] { new SketchConstraint("horizontal", ConstraintKind.Horizontal, new[] { "bottom" }, null), new SketchConstraint("vertical", ConstraintKind.Vertical, new[] { "right" }, null),
            new SketchConstraint("parallel", ConstraintKind.Parallel, new[] { "bottom", "top" }, null), new SketchConstraint("perpendicular", ConstraintKind.Perpendicular, new[] { "bottom", "right" }, null),
            new SketchConstraint("equal", ConstraintKind.Equal, new[] { "right", "left" }, null), new SketchConstraint("distance", ConstraintKind.Distance, new[] { "a", "b" }, new(20)) }, ConstraintStatus.FullyConstrained);
    internal static ClosedSketch Rings => new(new[] { Point("a", 0, 0), Point("b", 0, 0) },
        new[] { new SketchEntity("outer_circle", SketchEntityKind.Circle, new[] { "a" }, new(8), null, null, null), new SketchEntity("inner_circle", SketchEntityKind.Circle, new[] { "b" }, new(3), null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "outer_circle" }), new SketchLoop("inner", true, new[] { "inner_circle" }) },
        new[] { new SketchConstraint("radius_a", ConstraintKind.Radius, new[] { "outer_circle" }, new(8)), new SketchConstraint("radius_b", ConstraintKind.Radius, new[] { "inner_circle" }, new(3)),
            new SketchConstraint("concentric", ConstraintKind.Concentric, new[] { "outer_circle", "inner_circle" }, null), new SketchConstraint("coincident", ConstraintKind.Coincident, new[] { "a", "b" }, null) }, ConstraintStatus.FullyConstrained);
}
