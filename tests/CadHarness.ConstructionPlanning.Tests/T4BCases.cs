using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;

internal sealed record T4BCase(string Id,ConstructionPlanRequest Request,SemanticConstructionDraft Draft,T4Case Expected);
internal static class T4BCases
{
    internal static T4BCase[] All(string root)
    {
        using var tasks=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json")));
        var frozen=tasks.RootElement.GetProperty("development").EnumerateArray().ToArray();var originals=T4Cases.All(root);
        return originals.Select((s,index)=>
        {
            var key=index==0?"middle_radius":index==1?"large_bore_radius":index==2?"leg_height":"slot_depth";
            var parameter=index<2?ParameterKey.RevolveRadius:index==2?ParameterKey.AdditiveBossDepth:ParameterKey.CutDepth;
            var preserved=index==0?new[]{"head_radius","tip_radius","groove_depth"}:index==1?new[]{"small_bore_radius","outside_radius","flange_radius"}:index==2?new[]{"thickness","hole_radius","wall_z","edge_radius"}:new[]{"thickness","slot_width","slot_length"};
            var role=new EditabilityRequirement("primary",key,parameter,frozen[index].GetProperty("postCreationEdit").GetProperty("valueMm").GetDouble(),true,index==3?"slot0_x":null,preserved);
            var dependencies=index==0?new[]{new DeclaredRadialDependency("middle_radius","groove_radius","groove_depth")}:Array.Empty<DeclaredRadialDependency>();
            var q=s.Request with {SemanticDraftVersion=1,EditabilityRequirements=new[]{role},DesignDependencies=dependencies};
            SemanticConstructionNode[] nodes;
            if(index<2)
            {
                SemanticRadialRange R(string a,string b,string? r)=>new(a,b,r);
                var outer=index==0?new[]{R("zero","head_length","head_radius"),R("head_length","groove_start","middle_radius"),R("groove_start","groove_end","groove_radius"),R("groove_end","middle_end","middle_radius"),R("middle_end","overall","tip_radius")}:
                    new[]{R("zero","sleeve_length","outside_radius"),R("sleeve_length","overall","flange_radius")};
                var inner=index==0?new[]{R("zero","overall",null)}:new[]{R("zero","first_bore_span","small_bore_radius"),R("first_bore_span","bore_end","large_bore_radius")};
                nodes=new[]{new SemanticConstructionNode("body_shape",ConstructionKind.CreateRevolvedBoss,new(SemanticPlane.Xy,"zero","zero","zero","zero_angle"),new(SemanticProfileKind.RadialSection,null,null,null,new(outer,inner,dependencies)),null,EndCondition.FullRevolution,Direction.Forward,null,"turn",null)};
            }
            else
            {
                GeometryRequirementBinding Binding(string id,RequirementMetric metric)=>s.Response.Bindings.Single(b=>b.Target==id && b.Metric==metric);
                string Key(string id,RequirementMetric metric)=>Binding(id,metric).SemanticKey;
                nodes=s.Response.Program!.Operations.Where(o=>o.Profile is not null || o.EdgeTreatment is not null).Select(o=>
                {
                    if(o.EdgeTreatment is { } edge)return new SemanticConstructionNode(o.SemanticId,o.Kind,null,null,o.HostBody!.SemanticId[..^5],null,null,null,null,new(edge.HostFeature.SemanticId,edge.EdgeAxis,edge.FirstSide,edge.SecondSide,Key(o.SemanticId,RequirementMetric.FilletRadius)));
                    var sketch=s.Response.Program.Operations.Single(p=>p.SemanticId+".profile"==o.Profile!.SemanticId);var id=sketch.SemanticId;var plane=sketch.Placement!.Frame.ZAxis.X==1?SemanticPlane.Yz:SemanticPlane.Xy;
                    var placement=new SemanticPlacement(plane,Key(id,RequirementMetric.OriginX),Key(id,RequirementMetric.OriginY),Key(id,RequirementMetric.OriginZ),Key(id,RequirementMetric.InPlaneAngle));
                    var entity=sketch.Sketch!.Entities.Single();SemanticProfile profile;
                    if(entity.Kind==SketchEntityKind.Circle)profile=new(SemanticProfileKind.Circle,null,new(Key(id,RequirementMetric.ProfileCenterX),Key(id,RequirementMetric.ProfileCenterY),Key(id,RequirementMetric.CircleDiameter)),null,null);
                    else if(entity.Kind==SketchEntityKind.Slot)profile=new(SemanticProfileKind.Capsule,null,null,new(Key(id,RequirementMetric.ProfileCenterX),Key(id,RequirementMetric.ProfileCenterY),Key(id,RequirementMetric.SlotWidth),Key(id,RequirementMetric.SlotLength)),null);
                    else profile=new(SemanticProfileKind.Rectangle,new(Key(id,RequirementMetric.ProfileCenterX),Key(id,RequirementMetric.ProfileCenterY),Key(id,RequirementMetric.ProfileWidth),Key(id,RequirementMetric.ProfileHeight)),null,null,null);
                    return new SemanticConstructionNode(o.SemanticId,o.Kind,placement,profile,o.HostBody?.SemanticId[..^5],o.EndCondition,o.Direction,o.Depth is null?null:Key(o.SemanticId,RequirementMetric.Depth),null,null);
                }).ToArray();
            }
            return new T4BCase(s.Id,q,new(1,PlanOutcome.Planned,nodes,Array.Empty<string>(),null),s);
        }).ToArray();
    }
}
