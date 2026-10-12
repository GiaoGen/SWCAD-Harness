using System;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.Planning;

internal sealed record T4CCase(string Id,ConstructionPlanRequest Request,SemanticConstructionDraftV2 Draft,T4BCase Previous);
internal static class T4CCases
{
    internal static T4CCase[] All(string root)=>T4BCases.All(root).Select((s,index)=>
    {
        var recess=index==0?new[]{new ExternalRadialRecess("external_recess_required","middle_radius","groove_start","groove_end","groove_radius","groove_depth")}:Array.Empty<ExternalRadialRecess>();
        SemanticRadialDefinitionV2? radial=null;
        if(index<2)
        {
            var old=s.Draft.Nodes[0].Profile!.Radial!;
            var stock=index==0?new[]{new SemanticRadialRange("zero","head_length","head_radius"),new("head_length","middle_end","middle_radius"),new("middle_end","overall","tip_radius")}:old.Outer;
            radial=new(stock,old.Inner,recess,old.Dependencies);
        }
        var geometry=radial is null?null:new ConstructionGeometryRequirements(1,new[]{new RadialGeometryObligation("radial_body",radial.BaseOuter,radial.BaseInner,recess,
            new(PrincipalAxis.X,RequirementSource.Defaulted,"principal_axis_x_layout","1","zero","zero","zero"))});
        var edits=s.Request.EditabilityRequirements!.Select(e=>index==2?e with {SweepAxis=PrincipalAxis.Z,SweepDirection=Direction.Forward}:index==3?e with {SweepAxis=PrincipalAxis.Z,SweepDirection=Direction.Reverse}:e).ToArray();
        var q=s.Request with {SemanticDraftVersion=2,GeometryRequirements=geometry,EditabilityRequirements=edits};
        var nodes=s.Draft.Nodes.Select(n=>new SemanticConstructionNodeV2(n.Id,n.Kind,n.Placement,n.Profile is null?null:new SemanticProfileV2(n.Profile.Kind,n.Profile.Rectangle,n.Profile.Circle,n.Profile.Capsule,radial),n.Host,n.EndCondition,n.Direction,n.Depth,n.Angle,n.Edge)).ToArray();
        return new T4CCase(s.Id,q,new(2,PlanOutcome.Planned,nodes,Array.Empty<string>(),null),s);
    }).ToArray();
}
