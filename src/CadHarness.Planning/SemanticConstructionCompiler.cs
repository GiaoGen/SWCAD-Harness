using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CadHarness.Ir;
using CadHarness.Ir.V03;

namespace CadHarness.Planning;

public sealed record EditabilityRequirement(string Id,string SourceKey,ParameterKey Parameter,double TestValue,bool Independent,
    string? AnchorSourceKey,IReadOnlyList<string> PreservedSources);
public sealed record DeclaredRadialDependency(string SourceKey,string TargetKey,string OffsetKey);
public enum SemanticProfileKind { Rectangle,Circle,Capsule,RadialSection }
public enum SemanticPlane { Xy,Xz,Yz }
public sealed record SemanticPlacement(SemanticPlane Plane,string OriginX,string OriginY,string OriginZ,string Angle);
public sealed record SemanticRectangle(string CenterX,string CenterY,string Width,string Height);
public sealed record SemanticCircle(string CenterX,string CenterY,string Diameter);
public sealed record SemanticCapsule(string CenterX,string CenterY,string Width,string Length);
public sealed record SemanticRadialRange(string Start,string End,string? Radius);
public sealed record SemanticRadialSection(IReadOnlyList<SemanticRadialRange> Outer,IReadOnlyList<SemanticRadialRange> Inner,
    IReadOnlyList<DeclaredRadialDependency> Dependencies);
public sealed record SemanticProfile(SemanticProfileKind Kind,SemanticRectangle? Rectangle,SemanticCircle? Circle,
    SemanticCapsule? Capsule,SemanticRadialSection? Radial);
public sealed record SemanticEdge(string Host,PrincipalAxis Axis,BoundarySide FirstSide,BoundarySide SecondSide,string Radius);
public sealed record SemanticConstructionNode(string Id,ConstructionKind Kind,SemanticPlacement? Placement,SemanticProfile? Profile,
    string? Host,EndCondition? EndCondition,Direction? Direction,string? Depth,string? Angle,SemanticEdge? Edge);
public sealed record SemanticConstructionDraft(int Version,PlanOutcome Outcome,IReadOnlyList<SemanticConstructionNode> Nodes,
    IReadOnlyList<string> Questions,string? Reason);
public sealed record CompiledEditTarget(string RequirementId,string Owner,string ParameterSemanticId,ParameterKey Parameter,double Before,double After);
public sealed record ConstructionCompilation(string Version,SemanticConstructionDraft Draft,IReadOnlyList<CompiledEditTarget> EditTargets);
public sealed record SemanticCompilationResult(ConstructionPlanResponse Response,ConstructionCompilation Evidence);

// This lowerer owns representation, not part design. All values refer to the
// accepted fact graph; native execution still uses the unchanged IR/backend.
public static class SemanticConstructionCompiler
{
    public const string Version="1";
    public static void ValidateRequestRoles(ConstructionPlanRequest q,RequirementRecord facts)
    {
        var values=facts.Requirements.ToDictionary(r=>r.SemanticKey);
        bool Source(string k)=>values.TryGetValue(k,out var v) && v.Value is not null && !v.Ambiguous && v.Unit==ScalarUnit.Millimeter;
        var edits=q.EditabilityRequirements??Array.Empty<EditabilityRequirement>();
        ContractValidation.Require(edits.Count<=16,"Editability role bound exceeded.");ContractValidation.Unique(edits.Select(e=>e.Id),"editability role");
        foreach(var e in edits)
        {
            ContractValidation.Require(ContractValidation.Id(e.Id) && Source(e.SourceKey) && e.Parameter is ParameterKey.RevolveRadius or ParameterKey.AdditiveBossDepth or ParameterKey.CutDepth &&
                e.Independent && double.IsFinite(e.TestValue) && e.TestValue>0 && e.TestValue!=values[e.SourceKey].Value &&
                (e.AnchorSourceKey is null || Source(e.AnchorSourceKey)) && e.PreservedSources.Count<=128 && e.PreservedSources.All(Source),"Invalid or unqualified editability role.");
        }
        var relations=q.DesignDependencies??Array.Empty<DeclaredRadialDependency>();
        ContractValidation.Require(relations.Count<=24,"Declared dependency bound exceeded.");ContractValidation.Unique(relations.Select(r=>r.TargetKey),"declared derived target");
        foreach(var r in relations)ContractValidation.Require(Source(r.SourceKey) && Source(r.TargetKey) && Source(r.OffsetKey) &&
            values[r.OffsetKey].Value>0 && Math.Abs(values[r.TargetKey].Value!.Value-values[r.SourceKey].Value!.Value+values[r.OffsetKey].Value!.Value)<=PlanarSketchPreflight.ToleranceMm,
            "Declared design offset contradicts accepted facts.");
        var pending=relations.ToList();var known=relations.Select(r=>r.SourceKey).Except(relations.Select(r=>r.TargetKey)).ToHashSet();
        while(pending.Count>0){var ready=pending.Where(r=>known.Contains(r.SourceKey)).ToArray();ContractValidation.Require(ready.Length>0,"Declared design dependencies are cyclic.");foreach(var r in ready){known.Add(r.TargetKey);pending.Remove(r);}}
    }
    public static void ValidateDraft(SemanticConstructionDraft d)
    {
        ContractValidation.Require(d.Version==1 && Enum.IsDefined(d.Outcome) && d.Nodes.Count<=12 && d.Questions.Count<=8,"Invalid semantic draft version/bounds.");
        ContractValidation.Unique(d.Nodes.Select(n=>n.Id),"semantic node");
        ContractValidation.Require(d.Outcome switch{
            PlanOutcome.Planned=>d.Nodes.Count>0 && d.Questions.Count==0 && d.Reason is null,
            PlanOutcome.NeedsClarification=>d.Nodes.Count==0 && d.Questions.Count>0 && d.Reason is null,
            PlanOutcome.Unsupported=>d.Nodes.Count==0 && d.Questions.Count==0 && !string.IsNullOrWhiteSpace(d.Reason),_=>false},"Invalid semantic outcome envelope.");
        foreach(var n in d.Nodes)ContractValidation.Require(ContractValidation.Id(n.Id) && !n.Id.StartsWith("world.",StringComparison.Ordinal) &&
            n.Kind is ConstructionKind.CreateExtrude or ConstructionKind.CreateRevolvedBoss or ConstructionKind.CreateAdditiveBoss or ConstructionKind.CreateExtrudedCut or ConstructionKind.ApplyFillet,
            "Unqualified semantic node kind/identity.");
    }
    public static SemanticCompilationResult Compile(ConstructionPlanRequest request,SemanticConstructionDraft draft)
    {
        ContractValidation.Require(request.SemanticDraftVersion==1,"Semantic draft requires explicit request version.");ValidateDraft(draft);
        var facts=ConstructionRequirements.Normalize(request);
        ContractValidation.Require(draft.Outcome==PlanOutcome.Planned,"Only a planned draft can be lowered.");
        var builder=new Builder(request,facts);var response=builder.Build(draft);
        var targets=VerifyEditability(request,response,draft);
        return new(response,new(Version,draft,targets));
    }
    public static void VerifyCompilation(ConstructionPlanRequest request,ConstructionPlanResponse response,ConstructionCompilation? compilation)
    {
        ContractValidation.Require((request.SemanticDraftVersion is not null)==(compilation is not null),"Planning representation tag differs.");
        if(compilation is null){VerifyEditability(request,response,null);return;}
        ContractValidation.Require(compilation.Version==Version,"Compiler version changed; fresh plan required.");
        var actual=Compile(request,compilation.Draft);
        ContractValidation.Require(ContractJson.Write(actual.Response,ConstructionRequirements.ValidateResponse)==ContractJson.Write(response,ConstructionRequirements.ValidateResponse) &&
            JsonSerializer.Serialize(actual.Evidence.EditTargets)==JsonSerializer.Serialize(compilation.EditTargets),"Compiled program/source map/edit roles differ from accepted draft.");
    }
    public static IReadOnlyList<CompiledEditTarget> VerifyEditability(ConstructionPlanRequest request,ConstructionPlanResponse response,SemanticConstructionDraft? draft)
    {
        var targets=new List<CompiledEditTarget>();var program=response.Program!;
        var facts=response.Requirements.Requirements.ToDictionary(r=>r.SemanticKey);
        foreach(var role in request.EditabilityRequirements??Array.Empty<EditabilityRequirement>())
        {
            var metric=role.Parameter==ParameterKey.RevolveRadius?RequirementMetric.RadialDriverRadius:RequirementMetric.Depth;
            var matches=response.Bindings.Where(b=>b.SemanticKey==role.SourceKey && b.Metric==metric).ToArray();
            if(role.AnchorSourceKey is not null)matches=matches.Where(b=>{
                var node=draft?.Nodes.SingleOrDefault(n=>n.Id==b.Target);return node is not null && Sources(node).Contains(role.AnchorSourceKey);
            }).ToArray();
            ContractValidation.Require(matches.Length==1,"Editable source must resolve one exact qualified driver/owner.");var binding=matches[0];
            ConstructionOperation owner;string identity;ConstructionProgram edited;
            if(role.Parameter==ParameterKey.RevolveRadius)
            {
                var sketch=program.Operations.Single(o=>o.SemanticId==binding.Target);
                owner=program.Operations.Single(o=>o.Profile?.SemanticId==sketch.SemanticId+".profile");
                ContractValidation.Require(owner.Kind==ConstructionKind.CreateRevolvedBoss && sketch.Sketch?.RadialProfile is not null &&
                    !sketch.Sketch.RadialProfile.Relations.Any(r=>r.TargetDriver==binding.Element),"Required radius is derived or has an unqualified owner.");
                var changed=RadialProfilePolicy.Edit(sketch.Sketch!,binding.Element!,facts[role.SourceKey].Value!.Value,role.TestValue);
                edited=program with {Operations=program.Operations.Select(o=>o.SemanticId==sketch.SemanticId?o with {Sketch=changed}:o).ToArray()};
                identity=owner.SemanticId+".radius."+binding.Element;
                foreach(var key in role.PreservedSources)
                    foreach(var b in response.Bindings.Where(b=>b.SemanticKey==key && b.Target==sketch.SemanticId))
                        ContractValidation.Require(b.Metric switch{
                            RequirementMetric.RadialDriverRadius=>changed.RadialProfile!.Drivers.Single(d=>d.Id==b.Element).Radius.Millimeters==sketch.Sketch!.RadialProfile!.Drivers.Single(d=>d.Id==b.Element).Radius.Millimeters,
                            RequirementMetric.RadialOffset=>changed.RadialProfile!.Relations.Single(r=>r.Id==b.Element).Offset==sketch.Sketch!.RadialProfile!.Relations.Single(r=>r.Id==b.Element).Offset,
                            _=>true},"Required edit changes a preserved radial source.");
            }
            else
            {
                owner=program.Operations.Single(o=>o.SemanticId==binding.Target);
                ContractValidation.Require(owner.Kind==(role.Parameter==ParameterKey.AdditiveBossDepth?ConstructionKind.CreateAdditiveBoss:ConstructionKind.CreateExtrudedCut),"Required depth has wrong qualified owner.");
                edited=program with {Operations=program.Operations.Select(o=>o.SemanticId==owner.SemanticId?o with {Depth=new(role.TestValue)}:o).ToArray()};identity=owner.SemanticId+".depth";
                ContractValidation.Require(!role.PreservedSources.Contains(role.SourceKey),"Editable dimension cannot also be preserved.");
            }
            foreach(var key in role.PreservedSources)ContractValidation.Require(response.Bindings.Any(b=>b.SemanticKey==key),"Preserved source has no exact geometry binding.");
            _=ConstructionPreflight.EvaluateGeometry(edited);
            targets.Add(new(role.Id,owner.SemanticId,identity,role.Parameter,facts[role.SourceKey].Value!.Value,role.TestValue));
        }
        return targets;
    }
    private static IEnumerable<string> Sources(SemanticConstructionNode n)
    {
        if(n.Placement is { } p)foreach(var key in new[]{p.OriginX,p.OriginY,p.OriginZ,p.Angle})yield return key;
        if(n.Profile?.Rectangle is { } r)foreach(var key in new[]{r.CenterX,r.CenterY,r.Width,r.Height})yield return key;
        if(n.Profile?.Circle is { } c)foreach(var key in new[]{c.CenterX,c.CenterY,c.Diameter})yield return key;
        if(n.Profile?.Capsule is { } s)foreach(var key in new[]{s.CenterX,s.CenterY,s.Width,s.Length})yield return key;
    }
    private sealed class Builder(ConstructionPlanRequest request,RequirementRecord facts)
    {
        private readonly Dictionary<string,Requirement> values=facts.Requirements.ToDictionary(r=>r.SemanticKey);
        private readonly List<ConstructionOperation> operations=new();private readonly List<GeometryRequirementBinding> bindings=new();
        private readonly Dictionary<string,string> datums=new();private readonly HashSet<string> solids=new();
        private double V(string key,ScalarUnit unit=ScalarUnit.Millimeter)
        {ContractValidation.Require(values.TryGetValue(key,out var r) && r.Value is not null && !r.Ambiguous && r.Unit==unit,"Unknown/unresolved or wrong-unit source: "+key);return r!.Value!.Value;}
        private string Rule(string id,params string[] inputs)
        {
            var candidates=request.Rules.Where(r=>r.RuleId==id && ConstructionRequirements.InputKeys(r).SequenceEqual(inputs)).ToArray();
            ContractValidation.Require(candidates.Length==1,"Required unique disclosed representation rule missing: "+id);return candidates[0].SemanticKey;
        }
        private void B(string key,string target,RequirementMetric metric,string? element=null)=>bindings.Add(new(key,target,metric){Element=element});
        private void PlaceBindings(string id,SemanticPlacement p,string offset)
        {B(p.OriginX,id,RequirementMetric.OriginX);B(p.OriginY,id,RequirementMetric.OriginY);B(p.OriginZ,id,RequirementMetric.OriginZ);B(p.Angle,id,RequirementMetric.InPlaneAngle);B(offset,id,RequirementMetric.Offset);}
        private static LocalFrame Basis(SemanticPlane plane)=>plane switch{
            SemanticPlane.Xy=>new(new(0,0,0),new(1,0,0),new(0,1,0),new(0,0,1)),
            SemanticPlane.Xz=>new(new(0,0,0),new(1,0,0),new(0,0,-1),new(0,1,0)),
            SemanticPlane.Yz=>new(new(0,0,0),new(0,1,0),new(0,0,1),new(1,0,0)),
            _=>throw new ContractException(V03FailureCodes.ContractInvalid,"Unknown principal plane.")};
        private SpatialPlacement Placement(string id,SemanticPlacement p)
        {
            var zero=Rule("layout_zero");var zeroAngle=Rule("layout_zero_angle");var frame=Basis(p.Plane);var plane="world."+p.Plane.ToString().ToLowerInvariant();
            var origin=new Vector3(V(p.OriginX),V(p.OriginY),V(p.OriginZ));var angle=V(p.Angle,ScalarUnit.Degree);
            ContractValidation.Require(angle is >=-180 and <=180,"Principal rotation out of bounds.");var radians=angle*Math.PI/180;
            var rotated=frame with {OriginMm=origin,XAxis=SketchFrames.Add(SketchFrames.Scale(frame.XAxis,Math.Cos(radians)),SketchFrames.Scale(frame.YAxis,Math.Sin(radians))),
                YAxis=SketchFrames.Add(SketchFrames.Scale(frame.YAxis,Math.Cos(radians)),SketchFrames.Scale(frame.XAxis,-Math.Sin(radians)))};
            var normalKey=p.Plane==SemanticPlane.Xy?p.OriginZ:p.Plane==SemanticPlane.Xz?p.OriginY:p.OriginX;
            if(V(normalKey)!=0)
            {
                var cache=plane+":"+normalKey;
                if(!datums.TryGetValue(cache,out var datum))
                {
                    datum=id+"_datum";operations.Add(new(datum+"_op",ConstructionKind.CreateDatumPlane,datum,new(new(plane,SemanticType.ReferencePlane),null,frame,new(V(normalKey))),null,null,null,null,null,null,null,null,null));
                    PlaceBindings(datum,new(p.Plane,zero,zero,zero,zeroAngle),normalKey);datums.Add(cache,datum);
                }
                plane=datum+".plane";
            }
            PlaceBindings(id,p,zero);return new(new(plane,SemanticType.ReferencePlane),null,rotated,new(0));
        }
        internal ConstructionPlanResponse Build(SemanticConstructionDraft draft)
        {
            foreach(var n in draft.Nodes)
            {
                ContractValidation.Require(n.Host is null || solids.Contains(n.Host),"Unknown/forward/non-body host.");
                if(n.Kind==ConstructionKind.ApplyFillet)
                {
                    ContractValidation.Require(n.Profile is null && n.Placement is null && n.Depth is null && n.Angle is null && n.Direction is null && n.EndCondition is null && n.Edge is not null && n.Host is not null && solids.Contains(n.Edge.Host),"Invalid semantic edge operation.");
                    var edge=n.Edge!;operations.Add(new(n.Id+"_op",n.Kind,n.Id,null,null,null,null,new(n.Host!+".body",SemanticType.BodyRef),null,null,null,null,null){EdgeTreatment=new(1,new(edge.Host,SemanticType.FeatureRef),edge.Axis,edge.FirstSide,edge.SecondSide,new(V(edge.Radius)))});B(edge.Radius,n.Id,RequirementMetric.FilletRadius);solids.Add(n.Id);continue;
                }
                ContractValidation.Require(n.Profile is not null && n.Placement is not null && n.Edge is null && n.Direction is not null && n.EndCondition is not null,"Semantic sweep fields incomplete.");
                var id=n.Id+"_profile";var placement=Placement(id,n.Placement!);var sketch=Profile(id,n.Profile!);
                operations.Add(new(id+"_op",ConstructionKind.CreateSketch,id,placement,sketch,null,null,null,null,null,null,null,null));
                var revolve=n.Kind==ConstructionKind.CreateRevolvedBoss;
                ContractValidation.Require(revolve?n.Depth is null && n.Angle is not null:n.Angle is null,"Depth/angle representation tag differs.");
                var op=new ConstructionOperation(n.Id+"_op",n.Kind,n.Id,null,null,new(id+".profile",SemanticType.SketchProfile),revolve?new(id+".axis_x",SemanticType.ReferenceAxis):null,
                    n.Host is null?null:new(n.Host+".body",SemanticType.BodyRef),n.EndCondition,n.Direction,n.Depth is null?null:new(V(n.Depth)),n.Angle is null?null:V(n.Angle,ScalarUnit.Degree),
                    n.Kind is ConstructionKind.CreateExtrude or ConstructionKind.CreateRevolvedBoss?BodyRule.NewSingleBody:n.Kind==ConstructionKind.CreateAdditiveBoss?BodyRule.MergeWithHost:BodyRule.RemoveFromHost);
                operations.Add(op);if(n.Depth is not null)B(n.Depth,n.Id,RequirementMetric.Depth);if(n.Angle is not null)B(n.Angle,n.Id,RequirementMetric.Angle);solids.Add(n.Id);
            }
            var program=new ConstructionProgram("0.3",ModelOrigin.Harness,RequestMode.CreateModel,facts.RecordId,operations.ToArray());
            var result=new ConstructionPlanResponse("0.3",PlanOutcome.Planned,facts,program,Array.Empty<string>(),null,bindings.ToArray());
            ConstructionRequirements.ValidateResponse(result);_=ConstructionPreflight.EvaluateGeometry(program);ConstructionRequirements.VerifyGeometry(result,Array.Empty<ConstructionOperation>(),request);return result;
        }
        private ClosedSketch Profile(string id,SemanticProfile profile)
        {
            ContractValidation.Require(new object?[]{profile.Rectangle,profile.Circle,profile.Capsule,profile.Radial}.Count(x=>x is not null)==1,"Profile must contain exactly one typed shape.");
            if(profile.Kind==SemanticProfileKind.RadialSection){ContractValidation.Require(profile.Radial is not null,"Wrong radial shape tag.");return Radial(id,profile.Radial!);}
            string x,y;LocalPoint[] points;SketchEntity entity;SketchConstraint[] constraints=Array.Empty<SketchConstraint>();
            if(profile.Kind==SemanticProfileKind.Rectangle)
            {
                ContractValidation.Require(profile.Rectangle is not null,"Wrong rectangle tag.");var r=profile.Rectangle!;var cx=V(r.CenterX);var cy=V(r.CenterY);var w=V(r.Width);var h=V(r.Height);x=r.CenterX;y=r.CenterY;
                points=new[]{new LocalPoint("a",new(cx-w/2),new(cy-h/2)),new("b",new(cx+w/2),new(cy-h/2)),new("c",new(cx+w/2),new(cy+h/2)),new("d",new(cx-w/2),new(cy+h/2))};
                entity=new("outline",SketchEntityKind.Polyline,new[]{"a","b","c","d"},null,null,null,null);B(r.Width,id,RequirementMetric.ProfileWidth);B(r.Height,id,RequirementMetric.ProfileHeight);
            }
            else if(profile.Kind==SemanticProfileKind.Circle)
            {
                ContractValidation.Require(profile.Circle is not null,"Wrong circle tag.");var c=profile.Circle!;x=c.CenterX;y=c.CenterY;var radius=Rule("half_dimension",c.Diameter);
                points=new[]{new LocalPoint("center",new(V(x)),new(V(y)))};entity=new("circle",SketchEntityKind.Circle,new[]{"center"},new(V(radius)),null,null,null);
                constraints=new[]{new SketchConstraint("driver",ConstraintKind.Radius,new[]{"circle"},new(V(radius)))};B(c.Diameter,id,RequirementMetric.CircleDiameter);B(radius,id,RequirementMetric.CircleRadius);
            }
            else
            {
                ContractValidation.Require(profile.Kind==SemanticProfileKind.Capsule && profile.Capsule is not null,"Wrong capsule tag.");var s=profile.Capsule!;x=s.CenterX;y=s.CenterY;var w=V(s.Width);var length=V(s.Length);var half=(length-w)/2;
                ContractValidation.Require(half>0,"Capsule length must exceed width.");points=new[]{new LocalPoint("a",new(V(x)-half),new(V(y))),new("b",new(V(x)+half),new(V(y)))};
                entity=new("slot",SketchEntityKind.Slot,new[]{"a","b"},null,null,new(w),new(length));B(s.Width,id,RequirementMetric.SlotWidth);B(s.Length,id,RequirementMetric.SlotLength);B(Rule("capsule_end_radius",s.Width),id,RequirementMetric.SlotEndRadius);
            }
            B(x,id,RequirementMetric.ProfileCenterX);B(y,id,RequirementMetric.ProfileCenterY);return new(points,new[]{entity},new[]{new SketchLoop("outer",false,new[]{entity.Id})},constraints,ConstraintStatus.FullyConstrained);
        }
        private ClosedSketch Radial(string id,SemanticRadialSection r)
        {
            ContractValidation.Require(r.Outer.Count is >0 and <=24 && r.Inner.Count is >0 and <=24 && r.Dependencies.Count<=24,"Radial interval bound exceeded.");
            void Boundary(IReadOnlyList<SemanticRadialRange> ranges,bool outer)
            {
                foreach(var range in ranges)ContractValidation.Require(V(range.End)>V(range.Start) && (range.Radius is not null?V(range.Radius)>0:!outer),"Invalid radial region/zero outer radius.");
                for(var i=1;i<ranges.Count;i++)ContractValidation.Require(ranges[i-1].End==ranges[i].Start,"Radial intervals must be explicitly contiguous and ordered by source identity.");
            }
            Boundary(r.Outer,true);Boundary(r.Inner,false);
            ContractValidation.Require(V(r.Outer[0].Start)==V(r.Inner[0].Start) && V(r.Outer[^1].End)==V(r.Inner[^1].End),"Explicit inner/outer domain coordinates differ.");
            var declared=request.DesignDependencies??Array.Empty<DeclaredRadialDependency>();
            ContractValidation.Require(r.Dependencies.All(d=>declared.Contains(d)),"Undeclared native design dependency; numeric differences do not authorize linkage.");
            var keys=r.Outer.Concat(r.Inner).Select(x=>x.Radius).OfType<string>().Distinct().ToArray();
            ContractValidation.Require(declared.Where(d=>keys.Contains(d.SourceKey)||keys.Contains(d.TargetKey)).All(r.Dependencies.Contains),"Required design dependency omitted from its profile.");
            var points=new List<LocalPoint>();var pointBindings=new List<RadialPointBinding>();var stations=new List<string>();
            void Point(string station,string? radius)
            {
                var px=V(station);var py=radius is null?0:V(radius);
                if(points.Count>0 && points[^1].X.Millimeters==px && points[^1].Y.Millimeters==py){ContractValidation.Require(stations[^1]==station && pointBindings[^1].DriverId==(radius is null?null:"d_"+radius),"Coincident coordinates have inconsistent source ownership.");return;}
                var key="v"+points.Count;points.Add(new(key,new(px),new(py)));stations.Add(station);pointBindings.Add(new(key,radius is null?null:"d_"+radius));
            }
            foreach(var row in r.Inner){Point(row.Start,row.Radius);Point(row.End,row.Radius);}
            foreach(var row in r.Outer.Reverse()){Point(row.End,row.Radius);Point(row.Start,row.Radius);}
            for(var i=0;i<points.Count;i++)B(stations[i],id,RequirementMetric.AxialStation,points[i].Id);
            var drivers=keys.Select(k=>new RadialDriver("d_"+k,new(V(k)))).ToArray();foreach(var k in keys)B(k,id,RequirementMetric.RadialDriverRadius,"d_"+k);
            var relations=r.Dependencies.Select((d,i)=>{B(d.OffsetKey,id,RequirementMetric.RadialOffset,"relation_"+i);return new RadialOffsetRelation("relation_"+i,"d_"+d.SourceKey,"d_"+d.TargetKey,new(V(d.OffsetKey)));}).ToArray();
            return new(points,new[]{new SketchEntity("outline",SketchEntityKind.Polyline,points.Select(p=>p.Id).ToArray(),null,null,null,null)},new[]{new SketchLoop("outer",false,new[]{"outline"})},Array.Empty<SketchConstraint>(),ConstraintStatus.FullyConstrained)
                {RadialProfile=new(1,drivers,pointBindings,relations)};
        }
    }
    public static string ResponseSchema(ConstructionPlanRequest request,IConstructionPlanningRuntime runtime)
    {
        var root=JsonNode.Parse(ContractJson.Schema<SemanticConstructionDraft>())!.AsObject();root.Remove("$comment");root.Remove("$id");root.Remove("$schema");root["properties"]!["version"]!["enum"]=new JsonArray(1);
        var node=root["properties"]!["nodes"]!["items"]!;var variants=new JsonArray();
        foreach(var kind in runtime.ExecutableKinds.Where(k=>k is ConstructionKind.CreateExtrude or ConstructionKind.CreateRevolvedBoss or ConstructionKind.CreateAdditiveBoss or ConstructionKind.CreateExtrudedCut or ConstructionKind.ApplyFillet))
        {
            var shape=node.DeepClone();var p=shape["properties"]!;p["kind"]!["enum"]=new JsonArray(JsonNamingPolicy.SnakeCaseLower.ConvertName(kind.ToString()));
            void Null(string key)=>p[key]=new JsonObject{["type"]="null"};void NonNull(string key)=>p[key]=p[key]!["anyOf"]![0]!.DeepClone();
            if(kind==ConstructionKind.ApplyFillet){foreach(var k in new[]{"placement","profile","depth","angle","direction","endCondition"})Null(k);NonNull("host");NonNull("edge");}
            else
            {
                Null("edge");NonNull("placement");NonNull("profile");NonNull("direction");NonNull("endCondition");
                if(kind is ConstructionKind.CreateExtrude or ConstructionKind.CreateRevolvedBoss)Null("host");else NonNull("host");
                if(kind==ConstructionKind.CreateRevolvedBoss){Null("depth");NonNull("angle");p["endCondition"]!["enum"]=new JsonArray("full_revolution");}else {Null("angle");if(kind!=ConstructionKind.CreateExtrudedCut)NonNull("depth");p["endCondition"]!["enum"]=kind==ConstructionKind.CreateExtrudedCut?new JsonArray("blind","through_all"):new JsonArray("blind");}
                var profile=p["profile"]!.DeepClone();var shapes=new JsonArray();
                foreach(var tag in kind==ConstructionKind.CreateRevolvedBoss?new[]{"radial_section"}:new[]{"rectangle","circle","capsule"})
                {
                    var v=profile.DeepClone();v["properties"]!["kind"]!["enum"]=new JsonArray(tag);foreach(var field in new[]{"rectangle","circle","capsule","radial"})v["properties"]![field]=field==(tag=="radial_section"?"radial":tag)?v["properties"]![field]!["anyOf"]![0]!.DeepClone():new JsonObject{["type"]="null"};shapes.Add(v);
                }
                p["profile"]=new JsonObject{["anyOf"]=shapes};
            }
            variants.Add(shape);
        }
        root["properties"]!["nodes"]!["items"]=new JsonObject{["anyOf"]=variants};root["properties"]!["nodes"]!["maxItems"]=12;
        // Source references are enums, never model-generated numbers/expressions.
        var facts=ConstructionRequirements.Normalize(request).Requirements;var mm=facts.Where(r=>r.Value is not null && !r.Ambiguous && r.Unit==ScalarUnit.Millimeter).Select(r=>r.SemanticKey).ToArray();var degrees=facts.Where(r=>r.Value is not null && r.Unit==ScalarUnit.Degree).Select(r=>r.SemanticKey).ToArray();
        void Visit(JsonNode? n)
        {
            if(n is JsonObject o)
            {
                if(o["properties"] is JsonObject properties)foreach(var pair in properties.ToArray())
                {
                    var name=pair.Key;var source=name is "originX" or "originY" or "originZ" or "centerX" or "centerY" or "width" or "height" or "diameter" or "length" or "start" or "end" or "radius" or "depth" or "sourceKey" or "targetKey" or "offsetKey" or "angle";
                    if(source && pair.Value is JsonObject value){var strings=value["type"]?.ToString()=="string"?value:value["anyOf"] is JsonArray array?array.OfType<JsonObject>().SingleOrDefault(x=>x["type"]?.ToString()=="string"):null;if(strings is not null)strings["enum"]=new JsonArray((name=="angle"?degrees:mm).Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray());}
                }
                if(o["properties"] is JsonObject fields && fields["outer"] is not null){fields["outer"]!["minItems"]=1;fields["outer"]!["maxItems"]=24;fields["inner"]!["minItems"]=1;fields["inner"]!["maxItems"]=24;fields["dependencies"]!["maxItems"]=24;}
                foreach(var child in o.ToArray())Visit(child.Value);
            }
            else if(n is JsonArray a)foreach(var child in a)Visit(child);
        }
        Visit(root);var json=root.ToJsonString();ProviderSchemaCompatibility.RequireCompatible(json);return json;
    }
    public static string Instructions(ConstructionPlanRequest request,IConstructionPlanningRuntime runtime)
    {
        return "Produce ONE complete version=1 semantic construction plan. No CAD execution, API calls, code, expressions or retries. " +
            "Use only the qualified kinds in schema. Values are accepted source keys, never numbers. Select the geometry composition and operation order; the compiler only expands primitives. " +
            "Each node has ONE unique id. host/edge.host reference an earlier solid node id, not native names, .body or .profile strings. " +
            "planned: nodes nonempty, questions=[], reason=null. needs_clarification: nodes=[], questions nonempty, reason=null, ONLY for unresolved critical user inputs. unsupported: nodes=[],questions=[],reason nonempty. " +
            "Placement origin is WORLD coordinates. Plane chooses xy/xz/yz normal +Z/+Y/+X; angle rotates within its canonical right-handed basis. Off-plane origins are lowered to tracked datums. " +
            "Profiles are LOCAL to placement: rectangle is center+width/height; circle is center+diameter; capsule is center+width/overall length along LOCAL X. World-Y capsules on xy need the given 90-degree angle. " +
            "radial_section outer/inner list ordered contiguous axial intervals. Radius=null is the zero inner baseline only. All steps and radial notches belong in this one section; split intervals where a radius changes. No later cuts on a revolve. " +
            "Outer is the external material envelope; an external annular groove reduces OUTER radius over its given span. Inner is an internal bore/void boundary; a solid shaft has null inner radius throughout. Do not exchange an external notch for an internal cavity. " +
            "Use only explicitly declared design dependencies, target=source-offset. Independent Given radii must not be linked because of numeric differences. Copy each relevant declared dependency exactly; do not invent any. " +
            "Every critical/Given input must fund geometry through the disclosed value rules. Required editable sources must be independent drivers of the specified native kind, not fixed sketch coordinates. " +
            "Additive depth editing requires choosing the extrusion axis/section so that the required source is depth. Other dimensions/relations listed as preserved must not move. " +
            "Cuts start at the selected world origin and sweep forward/reverse along the principal normal. A fillet is terminal, hostFeature is a positive extrusion node; axis/sides are root-local, same semantics as capability. " +
            "No hidden dimensions, source substitutions, fit/material/strength assumptions, external-model construction or unsupported parameter types.\nCapabilities:\n"+CapabilityProjection(runtime)+
            "\nAccepted facts:\n"+ContractJson.Write(ConstructionRequirements.Normalize(request),ContractValidation.Requirements)+
            "\nValue derivation rules:\n"+ContractJson.Write<IReadOnlyList<RequirementRule>>(request.Rules,_=>{})+
            "\nRequired editable sources and preserved invariants:\n"+ContractJson.Write<IReadOnlyList<EditabilityRequirement>>(request.EditabilityRequirements??Array.Empty<EditabilityRequirement>(),_=>{})+
            "\nDeclared native design dependencies:\n"+ContractJson.Write<IReadOnlyList<DeclaredRadialDependency>>(request.DesignDependencies??Array.Empty<DeclaredRadialDependency>(),_=>{});
    }
    public static string CapabilityProjection(IConstructionPlanningRuntime runtime)
    {
        // Direct IR reference syntax must not contradict the semantic boundary.
        var capability=JsonNode.Parse(runtime.CapabilityJson)!.AsObject();
        capability.Remove("outputRules");capability.Remove("featureRules");
        capability["semanticReferenceRules"]=new JsonArray(
            "host and edge.host are exact earlier node IDs. Do not append .body, .profile or other IR suffixes; the compiler owns typed outputs.",
            "A revolve is the entire rotational history. All steps, inner bores and radial notches must appear in its outer/inner intervals; no later solid nodes.",
            "Prismatic additions precede cuts. Each host is the most recent solid node to preserve the full material history; a fillet is terminal.",
            "Fillet edge.host is the positive extrusion node owning the edge, not a body output. Axis/sides remain root-local.");
        return capability.ToJsonString();
    }
}
