using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.Planning;

internal sealed record TaskDefinition(string Id,string Family,string Intent,ConstructionProgram Program,
    ConstructionPlanRequest Request,double OldValue,double NewValue,double ColdValue,string DraftJson)
{
    public CapsuleGeometry[]? Expected {get;init;}
}
internal sealed record GeometryPhase(long Revision,double Value,double Volume,double[] Bounds,double[][] Cylinders);

internal static class Cases
{
    internal static readonly LocalFrame XY=new(new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1));
    internal static ClosedSketch Circle(double radius)=>new(new[]{new LocalPoint("c",new(0),new(0))},
        new[]{new SketchEntity("circle",SketchEntityKind.Circle,new[]{"c"},new(radius),null,null,null)},
        new[]{new SketchLoop("outer",false,new[]{"circle"})},new[]{new SketchConstraint("radius",ConstraintKind.Radius,new[]{"circle"},new(radius))},ConstraintStatus.FullyConstrained);
}
internal static class Tasks
{
    internal static TaskDefinition[] Load(string root)=>T4CCases.All(root).Select((c,i)=>
    {
        var compiled=SemanticConstructionV2.Compile(c.Request,c.Draft);
        var role=compiled.Evidence.EditTargets.Single();
        return new TaskDefinition("C"+(i+1),"C"+(i+1),c.Request.Input.Intent,compiled.Response.Program!,c.Request,
            role.Before,role.After,i==0?12.5:i==1?11:i==2?42:3.5,ContractJson.Write(c.Draft,SemanticConstructionV2.ValidateDraft));
    }).ToArray();
    internal static Requirement Given(string key,double? value)=>new(key,value,ScalarUnit.Millimeter,
        value is null?RequirementSource.Unresolved:RequirementSource.Given,null,null,1,false,true);
    internal static ConstructionPlanRequest Request(string intent,IEnumerable<Requirement> inputs)=>new(new("0.3","requirements",0,intent,inputs.ToArray()),Array.Empty<RequirementRule>());
    internal static GeometryPhase Geometry(TaskDefinition task,int phase)
    {
        var v=phase==0?task.OldValue:phase==1?task.NewValue:task.ColdValue;
        if(task.Expected is { } explicitExpected)return CapsuleExpected(explicitExpected[phase],phase+1,v);
        return task.Family switch
        {
            "C1"=>new(phase+1,v,Math.PI*(4500+v*v*36+(v-2)*(v-2)*4+1620),new[]{0d,-15,-15,80,15,15},
                new[]{new[]{15d,0,20},new[]{v,20,35},new[]{v-2,35,39},new[]{v,39,60},new[]{9d,60,80}}),
            "C2"=>new(phase+1,v,Math.PI*(16*16*30+24*24*6-10*10*20-v*v*16),new[]{0d,-24,-24,36,24,24},
                new[]{new[]{16d,0,30},new[]{24d,30,36},new[]{10d,0,20},new[]{v,20,36}}),
            "C3"=>new(phase+1,v,60*40*8+8*40*v-216*Math.PI-(1-Math.PI/4)*40,new[]{0d,-20,0,60,20,8+v},Array.Empty<double[]>()),
            "C4"=>new(phase+1,v,100*60*8-(6+v)*(84+9*Math.PI),new[]{-50d,-30,0,50,30,8},
                Enumerable.Range(0,3).SelectMany(i=>new[]{new[]{3d,(i-1)*30d,-7d,8-(i==0?v:3),8},new[]{3d,(i-1)*30d,7d,8-(i==0?v:3),8}}).ToArray()),
            _=>throw new InvalidOperationException("No frozen independent geometry for task")
        };
    }
    internal static GeometryPhase CapsuleExpected(CapsuleGeometry g,long revision,double value)=>new(revision,value,
        g.Width*g.Height*g.Thickness-g.Slots.Sum(s=>(s.Width*(s.Length-s.Width)+Math.PI*s.Width*s.Width/4)*s.Depth),
        new[]{-g.Width/2,-g.Height/2,0,g.Width/2,g.Height/2,g.Thickness},
        g.Slots.SelectMany(s=>new[]{-1,1}.Select(sign=>
        {
            var a=s.Angle*Math.PI/180;var h=(s.Length-s.Width)/2;
            return new[]{s.Width/2,s.X-sign*Math.Sin(a)*h,s.Y+sign*Math.Cos(a)*h,g.Thickness-s.Depth,g.Thickness};
        })).ToArray());
}
