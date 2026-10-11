using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;

internal sealed record TaskDefinition(string Id, string Family, string Intent, ConstructionProgram? Program,
    ConstructionPlanRequest Request, double BeforeVolume, double AfterVolume, double[] Bounds,
    string EditTarget, ParameterKey EditParameter, double OldValue, double NewValue, double[][] Cylinders,
    string? CapabilityGap, bool HeldOut);

internal static class Tasks
{
    internal static readonly LocalFrame XY = new(new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1));
    private static LocalPoint P(string id,double x,double y)=>new(id,new(x),new(y));
    private static ClosedSketch Polygon(params double[][] coordinates)
    {
        var points=coordinates.Select((v,i)=>P("p"+i,v[0],v[1])).ToArray();
        return new(points,new[]{new SketchEntity("outline",SketchEntityKind.Polyline,points.Select(p=>p.Id).ToArray(),null,null,null,null)},
            new[]{new SketchLoop("outer",false,new[]{"outline"})},Array.Empty<SketchConstraint>(),ConstraintStatus.FullyConstrained);
    }
    internal static ConstructionOperation Sketch(string id,ClosedSketch sketch,SpatialPlacement placement)=>new(id+"_op",ConstructionKind.CreateSketch,id,placement,sketch,null,null,null,null,null,null,null,null);
    internal static ConstructionOperation Sweep(string id,string profile,ConstructionKind kind,double depth,string? host=null)=>new(id+"_op",kind,id,null,null,
        new(profile+".profile",SemanticType.SketchProfile),null,host is null?null:new(host+".body",SemanticType.BodyRef),EndCondition.Blind,
        kind==ConstructionKind.CreateExtrudedCut?Direction.Reverse:Direction.Forward,new(depth),null,
        kind==ConstructionKind.CreateExtrude?BodyRule.NewSingleBody:kind==ConstructionKind.CreateAdditiveBoss?BodyRule.MergeWithHost:BodyRule.RemoveFromHost);
    private static ConstructionProgram ProgramOf(params ConstructionOperation[] operations)=>new("0.3",ModelOrigin.Harness,RequestMode.CreateModel,"requirements",operations);
    private static ConstructionProgram Rotation(ClosedSketch profile)=>ProgramOf(Sketch("section",profile,new(new("world.xy",SemanticType.ReferencePlane),null,XY,new(0))),
        new("revolve_op",ConstructionKind.CreateRevolvedBoss,"revolve",null,null,new("section.profile",SemanticType.SketchProfile),new("section.axis_x",SemanticType.ReferenceAxis),null,EndCondition.FullRevolution,Direction.Forward,null,360,BodyRule.NewSingleBody));
    internal static Requirement Given(string key,double? value,ScalarUnit unit=ScalarUnit.Millimeter,bool critical=true)=>new(key,value,unit,value is null?RequirementSource.Unresolved:RequirementSource.Given,null,null,1,false,critical);
    internal static ConstructionPlanRequest Request(string intent,IEnumerable<Requirement> input,IEnumerable<RequirementRule>? rules=null)=>new(new("0.3","requirements",0,intent,input.ToArray()),rules?.ToArray()??Array.Empty<RequirementRule>());
    internal static TaskDefinition[] Load(JsonElement frozen)
    {
        var rows=frozen.GetProperty("development").EnumerateArray().ToArray();
        TaskDefinition RotationTask(int index,ClosedSketch profile,double[] bounds,double[][] cylinders,string gap)
        {
            var row=rows[index];var anchor=new List<Requirement>();
            void Flatten(JsonElement e,string key)
            {
                if(e.ValueKind==JsonValueKind.Number)anchor.Add(Given(key,e.GetDouble()));
                else if(e.ValueKind==JsonValueKind.Array){var i=0;foreach(var v in e.EnumerateArray())Flatten(v,key+"_"+i++);}
                else foreach(var p in e.EnumerateObject())Flatten(p.Value,JsonNamingPolicy.SnakeCaseLower.ConvertName(p.Name));
            }
            Flatten(row.GetProperty("anchors"),"anchors");
            var oracle=row.GetProperty("oracle");var intent=row.GetProperty("intent").GetString()!;
            return new("C"+(index+1),"C"+(index+1),intent,Rotation(profile),Request(intent,anchor),
                oracle.GetProperty("volumePiCoefficientMm3").GetDouble()*Math.PI,oracle.GetProperty("postEditVolumePiCoefficientMm3").GetDouble()*Math.PI,
                bounds,"revolve",ParameterKey.RevolveRadius,12,13,cylinders,gap,false);
        }
        var shaft=Polygon(new[]{0d,0},new[]{80d,0},new[]{80d,9},new[]{60d,9},new[]{60d,12},new[]{39d,12},new[]{39d,10},new[]{35d,10},new[]{35d,12},new[]{20d,12},new[]{20d,15},new[]{0d,15});
        var sleeve=Polygon(new[]{0d,10},new[]{20d,10},new[]{20d,12},new[]{36d,12},new[]{36d,24},new[]{30d,24},new[]{30d,16},new[]{0d,16});
        var c1=RotationTask(0,shaft,new[]{0d,-15,-15,80,15,15},new[]{new[]{15d,0,20},new[]{12d,20,35},new[]{10d,35,39},new[]{12d,39,60},new[]{9d,60,80}},"Complex polygon revolve has no qualified radius driver or relation-preserving polygon edit; planner profile subset excludes stepped outlines.");
        var c2=RotationTask(1,sleeve,new[]{0d,-24,-24,36,24,24},new[]{new[]{16d,0,30},new[]{24d,30,36},new[]{10d,0,20},new[]{12d,20,36}},"Stepped bore polygon has no qualified radius driver or relation-preserving polygon edit; planner profile subset excludes stepped outlines.");
        var c3row=rows[2];var c3intent=c3row.GetProperty("intent").GetString()!;
        var c3=new TaskDefinition("C3","C3",c3intent,null,Request(c3intent,new[]{Given("base_width",60),Given("base_height",40),Given("base_depth",8),Given("leg_height",40),Given("leg_thickness",8),Given("hole_diameter",6)}),
            31960-206*Math.PI,33240-206*Math.PI,new[]{0d,-20,0,60,20,48},"upright",ParameterKey.AdditiveBossDepth,40,44,Array.Empty<double[]>(),
            "Generic construction IR has no fillet operation; perpendicular cut sweep lacks qualified mixed-axis material proof. Exact C3 cannot be represented; no simplified replacement is executed.",false);
        return new[]{c1,c2,c3,Plate("C4",rows[3].GetProperty("intent").GetString()!,100,60,8,20,6,3,new[]{new[]{-30d,0},new[]{0d,0},new[]{30d,0}},0,4,false),
            Plate("C4-held-out",frozen.GetProperty("heldOut")[0].GetProperty("intent").GetString()!,110,70,10,24,8,4,new[]{new[]{-32d,-4},new[]{0d,6},new[]{32d,-4}},1,5,true)};
    }
    private static TaskDefinition Plate(string id,string intent,double width,double height,double thickness,double length,double slotWidth,double depth,double[][] centers,int edited,double newDepth,bool heldOut)
    {
        var operations=new List<ConstructionOperation>{Sketch("plate_sketch",Polygon(new[]{-width/2,-height/2},new[]{width/2,-height/2},new[]{width/2,height/2},new[]{-width/2,height/2}),new(new("world.xy",SemanticType.ReferencePlane),null,XY,new(0))),Sweep("plate","plate_sketch",ConstructionKind.CreateExtrude,thickness),
            new("top_op",ConstructionKind.CreateDatumPlane,"top",new(new("world.xy",SemanticType.ReferencePlane),null,XY,new(thickness)),null,null,null,null,null,null,null,null,null)};
        for(var i=0;i<3;i++)
        {
            var span=(length-slotWidth)/2;
            var sketch=new ClosedSketch(new[]{P("a",-span,0),P("b",span,0)},new[]{new SketchEntity("slot",SketchEntityKind.Slot,new[]{"a","b"},null,null,new(slotWidth),new(length))},new[]{new SketchLoop("outer",false,new[]{"slot"})},Array.Empty<SketchConstraint>(),ConstraintStatus.FullyConstrained);
            var frame=new LocalFrame(new(centers[i][0],centers[i][1],thickness),new(0,1,0),new(-1,0,0),new(0,0,1));
            operations.Add(Sketch("slot"+i+"_sketch",sketch,new(new("top.plane",SemanticType.ReferencePlane),null,frame,new(0))));
            operations.Add(Sweep("slot"+i,"slot"+i+"_sketch",ConstructionKind.CreateExtrudedCut,depth,i==0?"plate":"slot"+(i-1)));
        }
        var values=new List<Requirement>{Given("width",width),Given("height",height),Given("thickness",thickness),Given("slot_length",length),Given("slot_width",slotWidth),Given("slot_depth",depth),Given("slot_angle",90,ScalarUnit.Degree,false)};
        for(var i=0;i<3;i++){values.Add(Given("slot"+i+"_x",centers[i][0],critical:false));values.Add(Given("slot"+i+"_y",centers[i][1],critical:false));}
        var rules=new[]{new RequirementRule("zero","layout_zero",null),new RequirementRule("zero_angle","layout_zero_angle",null),new RequirementRule("slot_end_radius","half_dimension","slot_width")};
        var area=slotWidth*(length-slotWidth)+Math.PI*slotWidth*slotWidth/4;
        return new(id,"C4",intent,ProgramOf(operations.ToArray()),Request(intent,values,rules),width*height*thickness-3*depth*area,width*height*thickness-(2*depth+newDepth)*area,
            new[]{-width/2,-height/2,0,width/2,height/2,thickness},"slot"+edited,ParameterKey.CutDepth,depth,newDepth,
            centers.SelectMany((p,i)=>new[]{new[]{slotWidth/2,p[0],p[1]-(length-slotWidth)/2,thickness-depth,thickness},new[]{slotWidth/2,p[0],p[1]+(length-slotWidth)/2,thickness-depth,thickness}}).ToArray(),null,heldOut);
    }
}
