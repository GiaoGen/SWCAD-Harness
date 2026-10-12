using System;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.Ir;

internal static class T2Cases
{
    internal static ClosedSketch Section(bool sleeve)
    {
        var points = sleeve ? new[] { (0d, "small_bore"), (20d, "small_bore"), (20d, "large_bore"), (36d, "large_bore"), (36d, "flange"), (30d, "flange"), (30d, "outside"), (0d, "outside") } :
            new[] { (0d, "zero"), (80d, "zero"), (80d, "tip"), (60d, "tip"), (60d, "middle"), (39d, "middle"), (39d, "groove"), (35d, "groove"), (35d, "middle"), (20d, "middle"), (20d, "head"), (0d, "head") };
        var drivers = sleeve ? new[] { new RadialDriver("small_bore", new(10)), new("large_bore", new(12)), new("flange", new(24)), new("outside", new(16)) } :
            new[] { new RadialDriver("head", new(15)), new("middle", new(12)), new("groove", new(10)), new("tip", new(9)) };
        var values = drivers.ToDictionary(d => d.Id, d => d.Radius.Millimeters);
        var p = points.Select((v, i) => new LocalPoint("vertex" + i, new(v.Item1), new(v.Item2 == "zero" ? 0 : values[v.Item2]))).ToArray();
        return new(p, new[] { new SketchEntity("outline", SketchEntityKind.Polyline, p.Select(p => p.Id).ToArray(), null, null, null, null) },
            new[] { new SketchLoop("outer", false, new[] { "outline" }) }, Array.Empty<SketchConstraint>(), ConstraintStatus.FullyConstrained)
        { RadialProfile = new(1, drivers, points.Select((v, i) => new RadialPointBinding("vertex" + i, v.Item2 == "zero" ? null : v.Item2)).ToArray(),
            sleeve ? Array.Empty<RadialOffsetRelation>() : new[] { new RadialOffsetRelation("groove_depth", "middle", "groove", new(2)) }) };
    }
    internal static ConstructionProgram Program(bool sleeve) => new("0.3", ModelOrigin.Harness, RequestMode.CreateModel, "development_requirements", new[] {
        new ConstructionOperation("section_op", ConstructionKind.CreateSketch, "section", new(new("world.xy", SemanticType.ReferencePlane), null, Cases.XY, new(0)), Section(sleeve), null,null,null,null,null,null,null,null),
        new("rotation_op", ConstructionKind.CreateRevolvedBoss, "rotation", null,null,new("section.profile",SemanticType.SketchProfile),new("section.axis_x",SemanticType.ReferenceAxis),null,EndCondition.FullRevolution,Direction.Forward,null,360,BodyRule.NewSingleBody)
    });
}
