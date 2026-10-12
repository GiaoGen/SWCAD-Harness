using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CadHarness.Ir.V03;

namespace CadHarness.Planning;

public sealed record ExternalRadialRecess(string Id,string HostRadius,string Start,string End,string FloorRadius,string Depth);
public sealed record SemanticRadialDefinitionV2(IReadOnlyList<SemanticRadialRange> BaseOuter,IReadOnlyList<SemanticRadialRange> BaseInner,
    IReadOnlyList<ExternalRadialRecess> ExternalRecesses,IReadOnlyList<DeclaredRadialDependency> Dependencies);
public sealed record SemanticProfileV2(SemanticProfileKind Kind,SemanticRectangle? Rectangle,SemanticCircle? Circle,
    SemanticCapsule? Capsule,SemanticRadialDefinitionV2? Radial);
public sealed record SemanticConstructionNodeV2(string Id,ConstructionKind Kind,SemanticPlacement? Placement,SemanticProfileV2? Profile,
    string? Host,EndCondition? EndCondition,Direction? Direction,string? Depth,string? Angle,SemanticEdge? Edge);
public sealed record SemanticConstructionDraftV2(int Version,PlanOutcome Outcome,IReadOnlyList<SemanticConstructionNodeV2> Nodes,
    IReadOnlyList<string> Questions,string? Reason);
public sealed record ExpandedRadialSpan(string Start,string End,string? Radius,int BaseIndex,string? RecessId);
public sealed record CompiledRadialExpansion(string Owner,IReadOnlyList<ExpandedRadialSpan> Outer);
public sealed record RadialExpansionMap(string RuleVersion,IReadOnlyList<CompiledRadialExpansion> Profiles);

public static class SemanticConstructionV2
{
    public const string CompilerVersion="2";
    public const string ExpansionRule="external_recess_split_v1";
    private static SemanticConstructionNode Legacy(SemanticConstructionNodeV2 n,SemanticRadialSection? radial)=>new(n.Id,n.Kind,n.Placement,
        n.Profile is null?null:new(n.Profile.Kind,n.Profile.Rectangle,n.Profile.Circle,n.Profile.Capsule,radial),n.Host,n.EndCondition,n.Direction,n.Depth,n.Angle,n.Edge);
    public static void ValidateDraft(SemanticConstructionDraftV2 draft)
    {
        ContractValidation.Require(draft.Version==2,"Semantic v2 draft version differs.");
        SemanticConstructionCompiler.ValidateDraft(new(1,draft.Outcome,draft.Nodes.Select(n=>Legacy(n,null)).ToArray(),draft.Questions,draft.Reason));
    }
    public static SemanticCompilationResult Compile(ConstructionPlanRequest request,SemanticConstructionDraftV2 draft)
    {
        ContractValidation.Require(request.SemanticDraftVersion==2,"Semantic v2 request version differs.");ValidateDraft(draft);
        ContractValidation.Require(draft.Outcome==PlanOutcome.Planned,"Only planned v2 drafts can be compiled.");
        var facts=ConstructionRequirements.Normalize(request);
        RadialTaskAdmission.VerifyDraft(request,draft);
        var maps=new List<CompiledRadialExpansion>();var nodes=new List<SemanticConstructionNode>();
        foreach(var node in draft.Nodes)
        {
            SemanticRadialSection? section=null;
            if(node.Profile?.Radial is { } radial)
            {
                ContractValidation.Require(node.Kind==ConstructionKind.CreateRevolvedBoss && node.Profile.Kind==SemanticProfileKind.RadialSection,"Radial modifier requires a qualified radial revolve.");
                var rows=Expand(radial,facts,request.DesignDependencies??Array.Empty<DeclaredRadialDependency>());
                maps.Add(new(node.Id,rows));section=new(rows.Select(r=>new SemanticRadialRange(r.Start,r.End,r.Radius)).ToArray(),radial.BaseInner,radial.Dependencies);
            }
            nodes.Add(Legacy(node,section));
        }
        var expanded=new SemanticConstructionDraft(1,draft.Outcome,nodes,draft.Questions,draft.Reason);
        var result=SemanticConstructionCompiler.CompileExpanded(request,expanded);
        RadialTaskAdmission.VerifyProgram(request,result.Response.Program!);
        foreach(var role in result.Evidence.EditTargets)
        {
            if(role.Parameter!=ParameterKey.RevolveRadius)continue;
            var owner=result.Response.Program!.Operations.Single(o=>o.SemanticId==role.Owner);
            var sketch=result.Response.Program.Operations.Single(o=>o.SemanticId+".profile"==owner.Profile!.SemanticId);
            var changed=RadialProfilePolicy.Edit(sketch.Sketch!,role.ParameterSemanticId[(role.Owner+".radius.").Length..],role.Before,role.After);
            var program=result.Response.Program with {Operations=result.Response.Program.Operations.Select(o=>o.SemanticId==sketch.SemanticId?o with {Sketch=changed}:o).ToArray()};
            RadialTaskAdmission.VerifyProgram(request,program,new Dictionary<string,double>{{request.EditabilityRequirements!.Single(e=>e.Id==role.RequirementId).SourceKey,role.After}});
        }
        return new(result.Response,new(CompilerVersion,null,result.Evidence.EditTargets){DraftV2=draft,SourceMap=new(ExpansionRule,maps)});
    }
    public static IReadOnlyList<ExpandedRadialSpan> Expand(SemanticRadialDefinitionV2 radial,RequirementRecord facts,IReadOnlyList<DeclaredRadialDependency> declared)
    {
        var values=facts.Requirements.ToDictionary(r=>r.SemanticKey);
        double V(string key){ContractValidation.Require(values.TryGetValue(key,out var r) && r.Value is not null && !r.Ambiguous && r.Unit==ScalarUnit.Millimeter,"Radial source unresolved/wrong unit: "+key);return r!.Value!.Value;}
        ContractValidation.Require(radial.BaseOuter.Count is >0 and <=24 && radial.BaseInner.Count is >0 and <=24 && radial.ExternalRecesses.Count<=12,"Radial v2 bounds exceeded.");
        void Base(IReadOnlyList<SemanticRadialRange> ranges,bool outer)
        {
            foreach(var row in ranges)ContractValidation.Require(V(row.Start)<V(row.End) && (row.Radius is null?!outer:V(row.Radius)>0),"Invalid radial base interval.");
            for(var i=1;i<ranges.Count;i++)ContractValidation.Require(ranges[i-1].End==ranges[i].Start,"Radial base gap/overlap or inconsistent boundary source.");
        }
        Base(radial.BaseOuter,true);Base(radial.BaseInner,false);
        ContractValidation.Require(V(radial.BaseOuter[0].Start)==V(radial.BaseInner[0].Start) && V(radial.BaseOuter[^1].End)==V(radial.BaseInner[^1].End),"Radial base domains differ.");
        ContractValidation.Unique(radial.ExternalRecesses.Select(r=>r.Id),"external recess");
        var sorted=radial.ExternalRecesses.OrderBy(r=>V(r.Start)).ToArray();
        for(var i=1;i<sorted.Length;i++)ContractValidation.Require(V(sorted[i-1].End)<V(sorted[i].Start),"External recess overlap/touching requires unqualified precedence or boundary ownership.");
        var hosts=new Dictionary<string,int>();
        foreach(var recess in sorted)
        {
            ContractValidation.Require(ContractValidation.Id(recess.Id) && V(recess.Start)<V(recess.End),"Invalid external recess identity/span: "+recess.Id);
            var candidates=radial.BaseOuter.Select((row,index)=>(row,index)).Where(x=>x.row.Radius==recess.HostRadius && V(x.row.Start)<V(recess.Start) && V(recess.End)<V(x.row.End)).ToArray();
            ContractValidation.Require(candidates.Length==1,"External recess host/source is not unique or span crosses/touches a base step: "+recess.Id);
            ContractValidation.Require(V(recess.FloorRadius)>0 && V(recess.FloorRadius)<V(recess.HostRadius) && V(recess.Depth)>0,"External recess floor/depth is invalid: "+recess.Id);
            var relation=new DeclaredRadialDependency(recess.HostRadius,recess.FloorRadius,recess.Depth);
            ContractValidation.Require(declared.Contains(relation) && radial.Dependencies.Contains(relation),"External recess lacks its exact declared floor/depth dependency: "+recess.Id);
            foreach(var inner in radial.BaseInner.Where(row=>V(row.Start)<V(recess.End) && V(row.End)>V(recess.Start)))
                ContractValidation.Require(inner.Radius is null || V(inner.Radius)<V(recess.FloorRadius),"External recess collapses bore wall: "+recess.Id);
            hosts.Add(recess.Id,candidates[0].index);
        }
        var rows=new List<ExpandedRadialSpan>();
        for(var i=0;i<radial.BaseOuter.Count;i++)
        {
            var basis=radial.BaseOuter[i];var start=basis.Start;
            foreach(var recess in sorted.Where(r=>hosts[r.Id]==i))
            {
                rows.Add(new(start,recess.Start,basis.Radius,i,null));rows.Add(new(recess.Start,recess.End,recess.FloorRadius,i,recess.Id));start=recess.End;
            }
            rows.Add(new(start,basis.End,basis.Radius,i,null));
        }
        ContractValidation.Require(rows.Count<=24,"Expanded radial interval bound exceeded.");return rows;
    }
    public static void VerifyCompilation(ConstructionPlanRequest request,ConstructionPlanResponse response,ConstructionCompilation evidence)
    {
        ContractValidation.Require(evidence.Version==CompilerVersion && evidence.Draft is null && evidence.DraftV2 is not null && evidence.SourceMap is not null,"Invalid semantic v2 compilation version/envelope.");
        var actual=Compile(request,evidence.DraftV2!);
        ContractValidation.Require(ContractJson.Write(actual.Response,ConstructionRequirements.ValidateResponse)==ContractJson.Write(response,ConstructionRequirements.ValidateResponse) &&
            JsonSerializer.Serialize(actual.Evidence)==JsonSerializer.Serialize(evidence),"Semantic v2 modifier/source map/expanded IR/edit role drift.");
    }
    public static string ResponseSchema(ConstructionPlanRequest request,IConstructionPlanningRuntime runtime)
    {
        var root=JsonNode.Parse(SemanticConstructionCompiler.ShapeSchema(JsonNode.Parse(ContractJson.Schema<SemanticConstructionDraftV2>())!.AsObject(),request,runtime,2))!.AsObject();
        var variants=root["properties"]!["nodes"]!["items"]!["anyOf"]!.AsArray();var projected=new JsonArray();
        var facts=ConstructionRequirements.Normalize(request).Requirements;
        foreach(var variant in variants)
        {
            var shape=variant!.DeepClone();var p=shape["properties"]!;var kind=p["kind"]!["enum"]![0]!.ToString();
            if(request.GeometryRequirements?.Radial.SingleOrDefault() is { } radial)
            {
                if(kind!="create_revolved_boss")continue;
                var placements=new JsonArray();
                foreach(var plane in Enum.GetValues<SemanticPlane>())foreach(var angle in facts.Where(r=>r.Unit==ScalarUnit.Degree && r.Value is >=-180 and <=180 && !r.Ambiguous))
                {
                    var x=plane==SemanticPlane.Yz?new Vector3(0,1,0):new Vector3(1,0,0);var y=plane==SemanticPlane.Xy?new Vector3(0,1,0):plane==SemanticPlane.Xz?new Vector3(0,0,-1):new Vector3(0,0,1);
                    var radians=angle.Value!.Value*Math.PI/180;var axis=SketchFrames.Add(SketchFrames.Scale(x,Math.Cos(radians)),SketchFrames.Scale(y,Math.Sin(radians)));
                    var wanted=radial.Layout.Axis switch{PrincipalAxis.X=>new Vector3(1,0,0),PrincipalAxis.Y=>new Vector3(0,1,0),_=>new Vector3(0,0,1)};
                    if(Math.Abs(axis.X-wanted.X)+Math.Abs(axis.Y-wanted.Y)+Math.Abs(axis.Z-wanted.Z)>1e-8)continue;
                    var placement=p["placement"]!.DeepClone();var fields=placement["properties"]!;
                    fields["plane"]!["enum"]=new JsonArray(plane.ToString().ToLowerInvariant());fields["angle"]!["enum"]=new JsonArray(angle.SemanticKey);
                    fields["originX"]!["enum"]=new JsonArray(radial.Layout.OriginX);fields["originY"]!["enum"]=new JsonArray(radial.Layout.OriginY);fields["originZ"]!["enum"]=new JsonArray(radial.Layout.OriginZ);placements.Add(placement);
                }
                ContractValidation.Require(placements.Count>0,"No accepted plane/angle source satisfies the requested world axis.");p["placement"]=new JsonObject{["anyOf"]=placements};
            }
            var roles=(request.EditabilityRequirements??Array.Empty<EditabilityRequirement>()).Where(r=>r.SweepAxis is not null && r.AnchorSourceKey is null && kind==(r.Parameter==ParameterKey.AdditiveBossDepth?"create_additive_boss":"create_extruded_cut")).ToArray();
            foreach(var role in roles)
            {
                var specific=shape.DeepClone();var fields=specific["properties"]!;fields["depth"]=new JsonObject{["type"]="string",["enum"]=new JsonArray(role.SourceKey)};
                fields["placement"]!["properties"]!["plane"]!["enum"]=new JsonArray(role.SweepAxis==PrincipalAxis.X?"yz":role.SweepAxis==PrincipalAxis.Y?"xz":"xy");
                fields["direction"]!["enum"]=new JsonArray(role.SweepDirection!.Value.ToString().ToLowerInvariant());projected.Add(specific);
            }
            if(roles.Length>0)
            {
                var keys=facts.Where(r=>r.Unit==ScalarUnit.Millimeter && r.Value is not null && !r.Ambiguous && !roles.Any(role=>role.SourceKey==r.SemanticKey)).Select(r=>r.SemanticKey).ToArray();
                if(keys.Length==0)continue;
                var depth=p["depth"]!["type"]?.ToString()=="string"?p["depth"]!:p["depth"]!["anyOf"]![0]!;depth["enum"]=new JsonArray(keys.Select(k=>(JsonNode?)JsonValue.Create(k)).ToArray());
            }
            projected.Add(shape);
        }
        root["properties"]!["nodes"]!["items"]!["anyOf"]=projected;var json=root.ToJsonString();ProviderSchemaCompatibility.RequireCompatible(json);return json;
    }
    public static string Instructions(ConstructionPlanRequest request,IConstructionPlanningRuntime runtime)=>
        SemanticConstructionCompiler.InstructionsCore(request,runtime,2)+
        "\nSEMANTIC V2: radial has baseOuter, baseInner, externalRecesses, dependencies, not final outer/inner. Base ranges are continuous unmodified stock envelope/bore ranges. " +
        "External recesses are modifiers with id, hostRadius, start, end, floorRadius, depth source keys; do NOT append recess ranges to baseOuter or put them in baseInner. " +
        "The compiler splits the uniquely selected base interval. Recesses are strictly internal to one base interval and strictly separated; crossing/touching steps or modifiers are unsupported. " +
        "Use only exact declared source identities, including the independent bore endpoint source. Match all typed obligations below without copying a feature tree. " +
        "Typed structure/layout obligations (not an oracle or expanded outline):\n"+(request.GeometryRequirements is null?"none":ContractJson.Write(request.GeometryRequirements,_=>{}));
}
