using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;

internal sealed record T4Case(string Id,ConstructionPlanRequest Request,ConstructionPlanResponse Response);
internal static class T4Cases
{
    private sealed class Input(string intent)
    {
        internal readonly List<Requirement> Given=new();internal readonly List<RequirementRule> Rules=new();internal readonly List<GeometryRequirementBinding> Bindings=new();
        internal void G(string key,double value,ScalarUnit unit=ScalarUnit.Millimeter)=>Given.Add(new(key,value,unit,RequirementSource.Given,null,null,1,false,true));
        internal void R(string key,string rule,params string[] inputs)=>Rules.Add(inputs.Length<=1?new(key,rule,inputs.FirstOrDefault()):new RequirementRule(key,rule,null){Inputs=inputs});
        internal void B(string key,string target,RequirementMetric metric,string? element=null,string? end=null)=>Bindings.Add(new(key,target,metric){Element=element,EndElement=end});
        internal void Placement(string target,string x="zero",string y="zero",string z="zero",string offset="zero",string angle="zero_angle")
        {B(x,target,RequirementMetric.OriginX);B(y,target,RequirementMetric.OriginY);B(z,target,RequirementMetric.OriginZ);B(offset,target,RequirementMetric.Offset);B(angle,target,RequirementMetric.InPlaneAngle);}
        internal Input Layout(){R("zero","layout_zero");R("zero_angle","layout_zero_angle");return this;}
        internal ConstructionPlanRequest Request()=>new(new("0.3","requirements",0,intent,Given.ToArray()),Rules.ToArray());
        internal T4Case Finish(string id,ConstructionProgram p){var q=Request();return new(id,q,new("0.3",PlanOutcome.Planned,ConstructionRequirements.Normalize(q),p,Array.Empty<string>(),null,Bindings.ToArray()));}
    }
    private static ConstructionOperation Sketch(string id,ClosedSketch s,SpatialPlacement? placement=null)=>new(id+"_op",ConstructionKind.CreateSketch,id,
        placement??new(new("world.xy",SemanticType.ReferencePlane),null,Cases.XY,new(0)),s,null,null,null,null,null,null,null,null);
    private static ClosedSketch Rectangle(double x0,double y0,double x1,double y1)=>new(new[]{new LocalPoint("a",new(x0),new(y0)),new("b",new(x1),new(y0)),new("c",new(x1),new(y1)),new("d",new(x0),new(y1))},
        new[]{new SketchEntity("outline",SketchEntityKind.Polyline,new[]{"a","b","c","d"},null,null,null,null)},new[]{new SketchLoop("outer",false,new[]{"outline"})},Array.Empty<SketchConstraint>(),ConstraintStatus.FullyConstrained);
    private static ClosedSketch Circle(double x,double y)=>Cases.Circle(3) with { Points=new[]{new LocalPoint("c",new(x),new(y))} };
    private static ConstructionOperation Sweep(string id,string profile,ConstructionKind kind,double? depth,string? host=null,bool reverse=false)=>new(id+"_op",kind,id,null,null,new(profile+".profile",SemanticType.SketchProfile),null,
        host is null?null:new(host+".body",SemanticType.BodyRef),depth is null?EndCondition.ThroughAll:EndCondition.Blind,reverse?Direction.Reverse:Direction.Forward,depth is null?null:new(depth.Value),null,
        kind==ConstructionKind.CreateExtrude?BodyRule.NewSingleBody:kind==ConstructionKind.CreateAdditiveBoss?BodyRule.MergeWithHost:BodyRule.RemoveFromHost);
    private static ConstructionProgram Program(params ConstructionOperation[] ops)=>new("0.3",ModelOrigin.Harness,RequestMode.CreateModel,"requirements",ops);
    internal static T4Case Radial(bool sleeve,string intent)
    {
        var q=new Input(intent).Layout();q.R("turn","full_revolution");
        var drivers=sleeve?new[]{("outside",32d),("flange",48d),("small_bore",20d),("large_bore",24d)}:new[]{("head",30d),("middle",24d),("tip",18d)};
        foreach(var (id,d) in drivers){q.G(id+"_diameter",d);q.R(id+"_radius","half_dimension",id+"_diameter");}
        (double X,string Driver,string Station)[] rows;
        if(sleeve)
        {
            q.G("sleeve_length",30);q.G("flange_thickness",6);q.G("first_bore_span",20);q.G("second_bore_span",16);
            q.R("overall","sum_dimensions","sleeve_length","flange_thickness");q.R("bore_end","sum_dimensions","first_bore_span","second_bore_span");
            rows=new[]{(0d,"small_bore","zero"),(20d,"small_bore","first_bore_span"),(20d,"large_bore","first_bore_span"),(36d,"large_bore","bore_end"),(36d,"flange","overall"),(30d,"flange","sleeve_length"),(30d,"outside","sleeve_length"),(0d,"outside","zero")};
        }
        else
        {
            q.G("head_length",20);q.G("middle_length",40);q.G("tip_length",20);q.G("groove_start",35);q.G("groove_width",4);q.G("groove_depth",2);
            q.R("middle_end","sum_dimensions","head_length","middle_length");q.R("overall","sum_dimensions","middle_end","tip_length");q.R("groove_end","sum_dimensions","groove_start","groove_width");q.R("groove_radius","difference_dimensions","middle_radius","groove_depth");
            rows=new[]{(0d,"zero","zero"),(80d,"zero","overall"),(80d,"tip","overall"),(60d,"tip","middle_end"),(60d,"middle","middle_end"),(39d,"middle","groove_end"),(39d,"groove","groove_end"),(35d,"groove","groove_start"),(35d,"middle","groove_start"),(20d,"middle","head_length"),(20d,"head","head_length"),(0d,"head","zero")};
        }
        var normalized=ConstructionRequirements.Normalize(q.Request()).Requirements.ToDictionary(r=>r.SemanticKey,r=>r.Value!.Value);
        var radii=drivers.Select(d=>new RadialDriver(d.Item1,new(normalized[d.Item1+"_radius"]))).ToList();if(!sleeve)radii.Add(new("groove",new(normalized["groove_radius"])));
        var points=rows.Select((r,i)=>new LocalPoint("v"+i,new(r.X),new(r.Driver=="zero"?0:normalized[r.Driver+"_radius"]))).ToArray();
        var section=new ClosedSketch(points,new[]{new SketchEntity("outline",SketchEntityKind.Polyline,points.Select(p=>p.Id).ToArray(),null,null,null,null)},new[]{new SketchLoop("outer",false,new[]{"outline"})},Array.Empty<SketchConstraint>(),ConstraintStatus.FullyConstrained)
        {RadialProfile=new(1,radii,rows.Select((r,i)=>new RadialPointBinding("v"+i,r.Driver=="zero"?null:r.Driver)).ToArray(),sleeve?Array.Empty<RadialOffsetRelation>():new[]{new RadialOffsetRelation("groove_relation","middle","groove",new(2))})};
        q.Placement("section");for(var i=0;i<rows.Length;i++)q.B(rows[i].Station,"section",RequirementMetric.AxialStation,"v"+i);
        foreach(var d in radii)q.B(d.Id+"_radius","section",RequirementMetric.RadialDriverRadius,d.Id);
        if(!sleeve)q.B("groove_depth","section",RequirementMetric.RadialOffset,"groove_relation");else q.B("second_bore_span","section",RequirementMetric.AxialSpan,"v2","v3");
        q.B("turn","rotation",RequirementMetric.Angle);
        return q.Finish(sleeve?"sleeve":"shaft",Program(Sketch("section",section),new("rotation_op",ConstructionKind.CreateRevolvedBoss,"rotation",null,null,new("section.profile",SemanticType.SketchProfile),new("section.axis_x",SemanticType.ReferenceAxis),null,EndCondition.FullRevolution,Direction.Forward,null,360,BodyRule.NewSingleBody)));
    }
    internal static T4Case Bracket(string intent)
    {
        var q=new Input(intent).Layout();foreach(var (key,value) in new[]{("base_min_x",0d),("base_max_x",60d),("min_y",-20d),("max_y",20d),("thickness",8d),("leg_width",8d),("leg_height",40d),("hole_diameter",6d),("hole_a_x",30d),("hole_a_y",-10d),("hole_b_x",30d),("hole_b_y",10d),("wall_y",0d),("wall_z",30d)})q.G(key,value);
        q.R("base_width","difference_dimensions","base_max_x","base_min_x");q.R("height","difference_dimensions","max_y","min_y");q.R("base_sum_x","sum_dimensions","base_min_x","base_max_x");q.R("base_center_x","half_dimension","base_sum_x");q.R("sum_y","sum_dimensions","min_y","max_y");q.R("center_y","half_dimension","sum_y");q.R("leg_center_x","half_dimension","leg_width");q.R("hole_radius","half_dimension","hole_diameter");q.R("edge_radius","bracket_cosmetic_corner");
        void Rect(string id,string width,string centerX){q.B(width,id,RequirementMetric.ProfileWidth);q.B("height",id,RequirementMetric.ProfileHeight);q.B(centerX,id,RequirementMetric.ProfileCenterX);q.B("center_y",id,RequirementMetric.ProfileCenterY);}
        Rect("base_profile","base_width","base_center_x");q.Placement("base_profile");q.B("thickness","base",RequirementMetric.Depth);
        q.Placement("top",offset:"thickness");Rect("leg_profile","leg_width","leg_center_x");q.Placement("leg_profile",z:"thickness");q.B("leg_height","leg",RequirementMetric.Depth);
        var top=new ConstructionOperation("top_op",ConstructionKind.CreateDatumPlane,"top",new(new("world.xy",SemanticType.ReferencePlane),null,Cases.XY,new(8)),null,null,null,null,null,null,null,null,null);
        var atTop=new SpatialPlacement(new("top.plane",SemanticType.ReferencePlane),null,Cases.XY with {OriginMm=new(0,0,8)},new(0));
        var ops=new List<ConstructionOperation>{Sketch("base_profile",Rectangle(0,-20,60,20)),Sweep("base","base_profile",ConstructionKind.CreateExtrude,8),top,Sketch("leg_profile",Rectangle(0,-20,8,20),atTop),Sweep("leg","leg_profile",ConstructionKind.CreateAdditiveBoss,40,"base")};
        var yz=new SpatialPlacement(new("world.yz",SemanticType.ReferencePlane),null,new(new(0,0,0),new(0,1,0),new(0,0,1),new(1,0,0)),new(0));
        foreach(var (id,x,y,xkey,ykey) in new[]{("hole_a",30d,-10d,"hole_a_x","hole_a_y"),("hole_b",30d,10d,"hole_b_x","hole_b_y"),("wall",0d,30d,"wall_y","wall_z")})
        {
            var profile=id+"_profile";q.Placement(profile);q.B(xkey,profile,RequirementMetric.ProfileCenterX);q.B(ykey,profile,RequirementMetric.ProfileCenterY);q.B("hole_radius",profile,RequirementMetric.CircleRadius);q.B("hole_diameter",profile,RequirementMetric.CircleDiameter);
            ops.Add(Sketch(profile,Circle(x,y),id=="wall"?yz:null));ops.Add(Sweep(id,profile,ConstructionKind.CreateExtrudedCut,null,id=="hole_a"?"leg":id=="hole_b"?"hole_a":"hole_b"));
        }
        ops.Add(new("corner_op",ConstructionKind.ApplyFillet,"corner",null,null,null,null,new("wall.body",SemanticType.BodyRef),null,null,null,null,null){EdgeTreatment=new(1,new("leg",SemanticType.FeatureRef),PrincipalAxis.Y,BoundarySide.Minimum,BoundarySide.Maximum,new(1))});q.B("edge_radius","corner",RequirementMetric.FilletRadius);
        return q.Finish("bracket",Program(ops.ToArray()));
    }
    internal static T4Case Slots(string intent)
    {
        var q=new Input(intent).Layout();foreach(var (key,v) in new[]{("width",100d),("height",60d),("thickness",8d),("slot_width",6d),("slot_length",20d),("slot_depth",3d)})q.G(key,v);q.G("slot_angle",90,ScalarUnit.Degree);q.R("slot_end_radius","capsule_end_radius","slot_width");
        q.B("width","base_profile",RequirementMetric.ProfileWidth);q.B("height","base_profile",RequirementMetric.ProfileHeight);q.B("zero","base_profile",RequirementMetric.ProfileCenterX);q.B("zero","base_profile",RequirementMetric.ProfileCenterY);q.Placement("base_profile");q.B("thickness","base",RequirementMetric.Depth);q.Placement("top",offset:"thickness");
        var ops=new List<ConstructionOperation>{Sketch("base_profile",Rectangle(-50,-30,50,30)),Sweep("base","base_profile",ConstructionKind.CreateExtrude,8),new("top_op",ConstructionKind.CreateDatumPlane,"top",new(new("world.xy",SemanticType.ReferencePlane),null,Cases.XY,new(8)),null,null,null,null,null,null,null,null,null)};
        for(var i=0;i<3;i++)
        {
            q.G("slot"+i+"_x",(i-1)*30);q.G("slot"+i+"_y",0);var id="slot"+i;var profile=id+"_profile";
            var sketch=new ClosedSketch(new[]{new LocalPoint("a",new(-7),new(0)),new("b",new(7),new(0))},new[]{new SketchEntity("slot",SketchEntityKind.Slot,new[]{"a","b"},null,null,new(6),new(20))},new[]{new SketchLoop("outer",false,new[]{"slot"})},Array.Empty<SketchConstraint>(),ConstraintStatus.FullyConstrained);
            var frame=new LocalFrame(new((i-1)*30,0,8),new(0,1,0),new(-1,0,0),new(0,0,1));ops.Add(Sketch(profile,sketch,new(new("top.plane",SemanticType.ReferencePlane),null,frame,new(0))));ops.Add(Sweep(id,profile,ConstructionKind.CreateExtrudedCut,3,i==0?"base":"slot"+(i-1),true));
            q.Placement(profile,"slot"+i+"_x","slot"+i+"_y","thickness",angle:"slot_angle");q.B("zero",profile,RequirementMetric.ProfileCenterX);q.B("zero",profile,RequirementMetric.ProfileCenterY);q.B("slot_width",profile,RequirementMetric.SlotWidth);q.B("slot_length",profile,RequirementMetric.SlotLength);q.B("slot_end_radius",profile,RequirementMetric.SlotEndRadius);q.B("slot_depth",id,RequirementMetric.Depth);
        }
        return q.Finish("slots",Program(ops.ToArray()));
    }
    internal static T4Case[] All(string root)
    {
        using var doc=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root,"tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json")));
        var intents=doc.RootElement.GetProperty("development").EnumerateArray().Select(r=>r.GetProperty("intent").GetString()!).ToArray();
        return new[]{Radial(false,intents[0]),Radial(true,intents[1]),Bracket(intents[2]),Slots(intents[3])};
    }
}
