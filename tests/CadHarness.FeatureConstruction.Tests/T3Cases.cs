using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;

internal static class T3Cases
{
    internal static ConstructionProgram Cross(double height = 40)
    {
        var top = Cases.Top with { Placement = Cases.Top.Placement! with { Offset = new(8) } };
        var upright = Cases.Sketch("upright_profile", Cases.Rectangle(0, -20, 8, 20), 8);
        var yz = new SpatialPlacement(new("world.yz", SemanticType.ReferencePlane), null,
            new(new(0, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0)), new(0));
        return Cases.Program(Cases.Sketch("base_profile", Cases.Rectangle(0, -20, 60, 20)),
            Cases.Extrude("base", "base_profile", ConstructionKind.CreateExtrude, 8), top, upright,
            Cases.Extrude("upright", "upright_profile", ConstructionKind.CreateAdditiveBoss, height, host: "base"),
            Cases.Sketch("hole_a_profile", Cases.Circle(30, -10, 3)), Cases.Extrude("hole_a", "hole_a_profile", ConstructionKind.CreateExtrudedCut, null, host: "upright"),
            Cases.Sketch("hole_b_profile", Cases.Circle(30, 10, 3)), Cases.Extrude("hole_b", "hole_b_profile", ConstructionKind.CreateExtrudedCut, null, host: "hole_a"),
            Cases.Sketch("wall_hole_profile", Cases.Circle(0, 30, 3)) with { Placement = yz },
            Cases.Extrude("wall_hole", "wall_hole_profile", ConstructionKind.CreateExtrudedCut, null, host: "hole_b"));
    }
    internal static ConstructionProgram Changed(ConstructionProgram p, string id, Func<ConstructionOperation, ConstructionOperation> change) =>
        p with { Operations = p.Operations.Select(o => o.SemanticId == id ? change(o) : o).ToArray() };
    internal static ConstructionProgram Full(double height = 40) => Cross(height) with { Operations = Cross(height).Operations.Append(
        new ConstructionOperation("corner_op", ConstructionKind.ApplyFillet, "corner", null, null, null, null, new("wall_hole.body",SemanticType.BodyRef),
            null, null, null, null, null) { EdgeTreatment=new(1,new("upright",SemanticType.FeatureRef),PrincipalAxis.Y,BoundarySide.Minimum,BoundarySide.Maximum,new(1)) }).ToArray() };
}
