using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;

namespace CadHarness.Generalization.Tests;

// Evaluation fixtures only. Production handlers never see a case name.
internal sealed record ExpectedHole(double X, double Y, double Diameter, double Bottom, double Top);
internal sealed record ExpectedModel(double Width, double Height, double Thickness, double Volume,
    IReadOnlyList<ExpectedHole> Holes, bool Disk = false, double Fillet = 0, double Chamfer = 0);
internal sealed record CaseDefinition(string Name, string Intent, CadProgram? Program, ExpectedModel? Expected,
    string Outcome = "supported");

internal static class CaseData
{
    internal static OperationNode Extrude(SketchProfile profile, double depth) => new("base", OperationKind.CreateExtrude, "stock",
        Array.Empty<OperationInput>(), new Dictionary<string, OperationParameter> { ["profile"] = new ProfileParameter(profile), ["depthMm"] = new LengthParameter(depth) });
    internal static OperationNode Hole(string id, double diameter, double x, double y, double? depth = null) => new(id,
        depth is null ? OperationKind.CreateThroughHole : OperationKind.CreateBlindHole, id,
        new[] { Input("host", "stock.top_face", SemanticType.PlanarFace) }, HoleParameters(diameter, x, y, depth));
    private static Dictionary<string, OperationParameter> HoleParameters(double diameter, double x, double y, double? depth)
    {
        var p = new Dictionary<string, OperationParameter> { ["diameterMm"] = new LengthParameter(diameter), ["placement"] = new PlacementParameter(new(x, y)) };
        if (depth is not null) p["depthMm"] = new LengthParameter(depth.Value);
        return p;
    }
    internal static OperationInput Input(string slot, string id, SemanticType type) => new(slot, new[] { new SemanticReference(id, type) });
    private static OperationNode Linear(int count, double spacing) => new("repeat", OperationKind.CreateLinearPattern, "layout",
        new[] { Input("seed", "seed", SemanticType.FeatureRef), Input("direction", "stock.direction_x", SemanticType.LinearEdge) },
        new Dictionary<string, OperationParameter> { ["count"] = new CountParameter(count), ["spacingMm"] = new LengthParameter(spacing) });
    private static OperationNode Rectangular(int x, int y, double sx, double sy) => new("repeat", OperationKind.CreateRectangularPattern, "layout",
        new[] { Input("seed", "seed", SemanticType.FeatureRef), Input("directionX", "stock.direction_x", SemanticType.LinearEdge), Input("directionY", "stock.direction_y", SemanticType.LinearEdge) },
        new Dictionary<string, OperationParameter> { ["countX"] = new CountParameter(x), ["countY"] = new CountParameter(y), ["spacingXMm"] = new LengthParameter(sx), ["spacingYMm"] = new LengthParameter(sy) });
    internal static OperationNode Treatment(bool chamfer, double size) => new("treat", chamfer ? OperationKind.ApplyChamfer : OperationKind.ApplyFillet, "corners",
        new[] { new OperationInput("edges", Enumerable.Range(1, 4).Select(i => new SemanticReference("stock.outer_edge_" + i, SemanticType.LinearEdge)).ToArray()) },
        new Dictionary<string, OperationParameter> { [chamfer ? "distanceMm" : "radiusMm"] = new LengthParameter(size) });
    private static IReadOnlyList<DesignRelation> LayoutRelations(bool circular = false) => new[]
    {
        new DesignRelation(RelationKind.HostedOn, "seed", "stock.top_face"),
        new DesignRelation(RelationKind.PatternSeed, "layout", "seed"),
        new DesignRelation(RelationKind.EqualSpacing, "layout", "seed")
    }.Concat(circular ? Array.Empty<DesignRelation>() : new[] { new DesignRelation(RelationKind.CenteredAbout, "layout", "stock.local_frame") }).ToArray();
    private static CaseDefinition Plate(string name, double w, double h, double t, double d, int nx, int ny, double sx, double sy,
        double fillet = 0, double chamfer = 0)
    {
        var ops = new List<OperationNode> { Extrude(new CenteredRectangleProfile(w, h), t), Hole("seed", d, 0, 0), ny == 1 ? Linear(nx, sx) : Rectangular(nx, ny, sx, sy) };
        if (fillet > 0 || chamfer > 0) ops.Add(Treatment(chamfer > 0, Math.Max(fillet, chamfer)));
        var holes = Enumerable.Range(0, nx).SelectMany(x => Enumerable.Range(0, ny).Select(y => new ExpectedHole((x - (nx - 1) / 2.0) * sx, (y - (ny - 1) / 2.0) * sy, d, 0, t))).ToArray();
        var volume = (w * h - holes.Length * Math.PI * d * d / 4 - (4 - Math.PI) * fillet * fillet - 2 * chamfer * chamfer) * t;
        return new(name, $"{w}x{h}x{t} plate, {nx}x{ny} centered diameter {d} through holes, spacing {sx}/{sy}; outer R{fillet}, chamfer {chamfer}.",
            new("0.2", ops, LayoutRelations()), new(w, h, t, volume, holes, Fillet: fillet, Chamfer: chamfer));
    }
    internal static IReadOnlyList<CaseDefinition> All()
    {
        var g4holes = new[] { new ExpectedHole(0, 0, 20, 0, 12) }.Concat(Enumerable.Range(0, 6).Select(i => new ExpectedHole(35 * Math.Cos(i * Math.PI / 3), 35 * Math.Sin(i * Math.PI / 3), 8, 0, 12))).ToArray();
        var g4 = new CadProgram("0.2", new[] { Extrude(new CircleProfile(100), 12), Hole("bore", 20, 0, 0), Hole("seed", 8, 35, 0),
            new OperationNode("ring", OperationKind.CreateCircularPattern, "layout", new[] { Input("seed", "seed", SemanticType.FeatureRef), Input("axis", "stock.rotational_reference", SemanticType.CylindricalFace) },
                new Dictionary<string, OperationParameter> { ["count"] = new CountParameter(6) }) },
            LayoutRelations(true).Append(new DesignRelation(RelationKind.HostedOn, "bore", "stock.top_face")).ToArray());
        var baseline = new CadProgram("0.2", new[] { Extrude(new CenteredRectangleProfile(83, 57), 9), Hole("seed", 6, 0, 0) },
            new[] { new DesignRelation(RelationKind.HostedOn, "seed", "stock.top_face") });
        var held = Plate("HeldOut", 137, 91, 13, 7, 3, 1, 29, 0);
        held = held with { Intent = "137x91x13 plate; three centered diameter7 through holes along X at 29 spacing; diameter11 blind hole depth4 at (0,27). Later enlarge through holes to9.",
            Program = held.Program! with { Operations = held.Program!.Operations.Append(Hole("pocket", 11, 0, 27, 4)).ToArray(),
                Relations = held.Program.Relations.Append(new DesignRelation(RelationKind.HostedOn, "pocket", "stock.top_face")).ToArray() },
            Expected = held.Expected! with { Volume = held.Expected!.Volume - Math.PI * 121 / 4 * 4, Holes = held.Expected.Holes.Append(new ExpectedHole(0, 27, 11, 9, 13)).ToArray() } };
        return new[]
        {
            Plate("G1", 80, 50, 10, 8, 2, 1, 40, 0, fillet: 3),
            Plate("G2", 100, 60, 8, 6, 2, 2, 60, 30),
            Plate("G3", 120, 80, 10, 8, 2, 3, 70, 25, fillet: 5),
            new CaseDefinition("G4", "Diameter100 disk thickness12; center diameter20 through bore; six diameter8 through bolt holes on diameter70 PCD.", g4,
                new(100, 100, 12, Math.PI * (2500 - 100 - 6 * 16) * 12, g4holes, Disk: true)),
            Plate("G5", 90, 70, 6, 6, 2, 2, 50, 40, chamfer: 2),
            new CaseDefinition("G6_Fillet", "83x57x9 plate with diameter6 hole; impossible R1000 outer fillet.", baseline with { Operations = baseline.Operations.Append(Treatment(false, 1000)).ToArray() }, null, "native_failure"),
            new CaseDefinition("G6_Pattern", "83x57x9 plate; invalid 1025-instance pattern.", baseline with { Operations = baseline.Operations.Append(Linear(1025, 10)).ToArray() }, null, "preflight_failure"),
            new CaseDefinition("G6_Placement", "83x57x9 plate; diameter6 hole outside the host at (60,40).", baseline with { Operations = new[] { baseline.Operations[0], Hole("seed", 6, 60, 40) } }, null, "preflight_failure"),
            new CaseDefinition("G7", "Optimized turbine blade with internal cooling passages.", null, null, "unsupported"),
            held
        };
    }
    internal static OperationNode Edit(string target, EditableParameter parameter, double value) => new("edit", OperationKind.EditParameter, null,
        new[] { Input("target", target, SemanticType.FeatureRef) }, new Dictionary<string, OperationParameter>
        { ["parameter"] = new ParameterNameParameter(parameter), ["value"] = new EditValueParameter(EditableParameters.Contract(parameter).Kind == ParameterKind.Count ? new CountParameter((int)value) : new LengthParameter(value)) });
}
