using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;

internal static class Cases
{
    internal static LocalFrame XY = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
    internal static Requirement Given(string key, double? value, bool critical = true) => new(key, value, ScalarUnit.Millimeter,
        value is null ? RequirementSource.Unresolved : RequirementSource.Given, null, null, 1, false, critical);
    internal static ConstructionPlanRequest Request(string intent, long revision, IEnumerable<Requirement> given, bool circle = false)
    {
        var rules = new List<RequirementRule>();
        if (circle) rules.Add(new("radius", "half_dimension", "diameter"));
        foreach (var key in new[] { "center_x", "center_y", "origin_x", "origin_y", "origin_z", "offset" }) rules.Add(new(key, "layout_zero", null));
        rules.Add(new("frame_angle", "layout_zero_angle", null));
        return new(new("0.3", "requirements", revision, intent, given.ToArray()), rules);
    }
    internal static ConstructionPlanRequest Plate(double width = 62, double height = 46, double depth = 9) => Request(
        $"Create a rectangular prism {width} mm wide, {height} mm high and {depth} mm deep, nominal geometry only. Omitted placement may use the disclosed zero layout defaults.",
        0, new[] { Given("width", width), Given("height", height), Given("depth", depth) });
    internal static ConstructionPlanRequest Cylinder => Request("Create a cylinder, diameter 18 mm, depth 14 mm, nominal geometry only. Disclose derived radius and zero placement.", 0,
        new[] { Given("diameter", 18), Given("depth", 14) }, true);
    internal static ConstructionPlanRequest Hole(long revision) => Request("Continue the exact existing Part with one centered through-all circular cut, diameter 6 mm, forward from XY. Preserve all existing operations. Disclose radius and zero placement.",
        revision, new[] { Given("diameter", 6) }, true);
    internal static ClosedSketch Rectangle(double width, double height) => new(new[] {
        new LocalPoint("a", new(-width/2), new(-height/2)), new LocalPoint("b", new(width/2), new(-height/2)),
        new LocalPoint("c", new(width/2), new(height/2)), new LocalPoint("d", new(-width/2), new(height/2)) },
        new[] { new SketchEntity("outline", SketchEntityKind.Polyline, new[] { "a", "b", "c", "d" }, null, null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "outline" }) }, Array.Empty<SketchConstraint>(), ConstraintStatus.FullyConstrained);
    internal static ClosedSketch Circle(double radius) => new(new[] { new LocalPoint("c", new(0), new(0)) },
        new[] { new SketchEntity("circle", SketchEntityKind.Circle, new[] { "c" }, new(radius), null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "circle" }) },
        new[] { new SketchConstraint("radius", ConstraintKind.Radius, new[] { "circle" }, new(radius)) }, ConstraintStatus.FullyConstrained);
    internal static ConstructionProgram Program(ClosedSketch sketch, double? depth, IReadOnlyList<ConstructionOperation>? prefix = null)
    {
        prefix ??= Array.Empty<ConstructionOperation>();
        var continuation = prefix.Count != 0;
        var s = new ConstructionOperation("sketch_op", ConstructionKind.CreateSketch, "section", new(new("world.xy", SemanticType.ReferencePlane), null, XY, new(0)),
            sketch, null, null, null, null, null, null, null, null);
        if (continuation) s = s with { Id = "cut_sketch_op", SemanticId = "cut_section" };
        var f = new ConstructionOperation(continuation ? "cut_op" : "base_op", continuation ? ConstructionKind.CreateExtrudedCut : ConstructionKind.CreateExtrude,
            continuation ? "cut" : "base", null, null, new(s.SemanticId + ".profile", SemanticType.SketchProfile), null,
            continuation ? new("base.body", SemanticType.BodyRef) : null, depth is null ? EndCondition.ThroughAll : EndCondition.Blind,
            Direction.Forward, depth is null ? null : new(depth.Value), null, continuation ? BodyRule.RemoveFromHost : BodyRule.NewSingleBody);
        return new("0.3", ModelOrigin.Harness, RequestMode.CreateModel, "requirements", prefix.Concat(new[] { s, f }).ToArray());
    }
    internal static ConstructionPlanResponse Response(ConstructionPlanRequest request, ConstructionProgram program, int prefix = 0)
    {
        var s = program.Operations[prefix]; var f = program.Operations[prefix + 1];
        var bindings = new List<GeometryRequirementBinding>();
        void Add(string key, string target, RequirementMetric metric) => bindings.Add(new(key, target, metric));
        if (s.Sketch!.Entities[0].Kind == SketchEntityKind.Circle)
        { Add("radius", s.SemanticId, RequirementMetric.CircleRadius); Add("diameter", s.SemanticId, RequirementMetric.CircleDiameter); }
        else { Add("width", s.SemanticId, RequirementMetric.ProfileWidth); Add("height", s.SemanticId, RequirementMetric.ProfileHeight); }
        Add("center_x", s.SemanticId, RequirementMetric.ProfileCenterX); Add("center_y", s.SemanticId, RequirementMetric.ProfileCenterY);
        Add("origin_x", s.SemanticId, RequirementMetric.OriginX); Add("origin_y", s.SemanticId, RequirementMetric.OriginY); Add("origin_z", s.SemanticId, RequirementMetric.OriginZ);
        Add("offset", s.SemanticId, RequirementMetric.Offset);
        Add("frame_angle", s.SemanticId, RequirementMetric.InPlaneAngle);
        if (f.Depth is not null) Add("depth", f.SemanticId, RequirementMetric.Depth);
        return new("0.3", PlanOutcome.Planned, ConstructionRequirements.Normalize(request), program, Array.Empty<string>(), null, bindings);
    }
    internal static string Wire(ConstructionPlanResponse r) => ContractJson.Write(r, _ => { });
}
