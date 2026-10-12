using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir.V03;

namespace CadHarness.Planning;

public sealed record AxialLayoutRequirement(PrincipalAxis Axis,RequirementSource Source,string? RuleId,string? RuleVersion,
    string OriginX,string OriginY,string OriginZ);
public sealed record RadialGeometryObligation(string Id,IReadOnlyList<SemanticRadialRange> BaseOuter,IReadOnlyList<SemanticRadialRange> BaseInner,
    IReadOnlyList<ExternalRadialRecess> ExternalRecesses,AxialLayoutRequirement Layout);
public sealed record ConstructionGeometryRequirements(int Version,IReadOnlyList<RadialGeometryObligation> Radial);
public sealed record RadialSourceOverride(string Target,string? ParameterSemanticId,double Value);

// Checks requested structure against the emitted polygon, not against the
// lowerer's source map. Numeric overrides are separate from immutable Given.
public static class RadialTaskAdmission
{
    private const double Tolerance=PlanarSketchPreflight.ToleranceMm;
    public static void ValidateRequest(ConstructionPlanRequest request,RequirementRecord facts)
    {
        if(request.GeometryRequirements is not { } geometry)return;
        ContractValidation.Require(geometry.Version==1 && geometry.Radial.Count<=1,"Invalid geometry obligation version/single-body bound.");
        ContractValidation.Unique(geometry.Radial.Select(r=>r.Id),"radial obligation");var values=facts.Requirements.ToDictionary(r=>r.SemanticKey);
        foreach(var role in geometry.Radial)
        {
            var l=role.Layout;
            ContractValidation.Require(ContractValidation.Id(role.Id) && Enum.IsDefined(l.Axis) &&
                (l.Source==RequirementSource.Given?l.RuleId is null && l.RuleVersion is null:l.Source==RequirementSource.Defaulted && l.RuleId=="principal_axis_x_layout" && l.RuleVersion=="1" && l.Axis==PrincipalAxis.X),
                "STRUCTURE_LAYOUT: axis must be explicitly given or the disclosed positive-X layout rule: "+role.Id);
            foreach(var key in new[]{l.OriginX,l.OriginY,l.OriginZ})ContractValidation.Require(values.TryGetValue(key,out var r) && r.Value is not null && !r.Ambiguous && r.Unit==ScalarUnit.Millimeter,"STRUCTURE_LAYOUT: unresolved origin source: "+key);
            _=SemanticConstructionV2.Expand(new(role.BaseOuter,role.BaseInner,role.ExternalRecesses,request.DesignDependencies??Array.Empty<DeclaredRadialDependency>()),facts,request.DesignDependencies??Array.Empty<DeclaredRadialDependency>());
        }
    }
    public static void VerifyDraft(ConstructionPlanRequest request,SemanticConstructionDraftV2 draft)
    {
        foreach(var role in request.GeometryRequirements?.Radial??Array.Empty<RadialGeometryObligation>())
        {
            var candidates=draft.Nodes.Where(n=>n.Kind==ConstructionKind.CreateRevolvedBoss && n.Profile?.Radial is not null).ToArray();
            ContractValidation.Require(candidates.Length==1,"STRUCTURE_OWNER: expected one radial owner: "+role.Id);
            var actual=candidates[0].Profile!.Radial!;
            var placement=candidates[0].Placement;
            ContractValidation.Require(placement is not null && placement.OriginX==role.Layout.OriginX && placement.OriginY==role.Layout.OriginY && placement.OriginZ==role.Layout.OriginZ,"STRUCTURE_LAYOUT_SOURCE: world origin source identities differ: "+role.Id);
            ContractValidation.Require(actual.BaseInner.SequenceEqual(role.BaseInner),"STRUCTURE_INNER: solid/bore boundary differs; an internal cavity cannot substitute an external recess: "+role.Id);
            ContractValidation.Require(actual.BaseOuter.SequenceEqual(role.BaseOuter),"STRUCTURE_BASE_OUTER: base interval/source responsibilities differ: "+role.Id);
            ContractValidation.Require(actual.ExternalRecesses.Count==role.ExternalRecesses.Count && role.ExternalRecesses.All(r=>actual.ExternalRecesses.Contains(r)),
                "STRUCTURE_EXTERNAL_RECESS: missing/wrong modifier ID, host, span, floor or depth source: "+role.Id);
        }
    }
    public static Dictionary<string,double> ResolvedValues(ConstructionPlanRequest request,IReadOnlyDictionary<string,double>? overrides=null)
    {
        var values=ConstructionRequirements.Normalize(request).Requirements.Where(r=>r.Value is not null).ToDictionary(r=>r.SemanticKey,r=>r.Value!.Value);
        if(overrides is not null)foreach(var row in overrides){ContractValidation.Require(values.ContainsKey(row.Key),"Unknown explicit geometry override source.");values[row.Key]=row.Value;}
        var pending=(request.DesignDependencies??Array.Empty<DeclaredRadialDependency>()).ToList();
        while(pending.Count>0)
        {
            var ready=pending.Where(r=>!pending.Any(other=>other.TargetKey==r.SourceKey)).ToArray();ContractValidation.Require(ready.Length>0,"Cyclic geometry override dependency.");
            foreach(var r in ready){values[r.TargetKey]=values[r.SourceKey]-values[r.OffsetKey];pending.Remove(r);}
        }
        return values;
    }
    public static void VerifyProgram(ConstructionPlanRequest request,ConstructionProgram program,IReadOnlyDictionary<string,double>? overrides=null)
    {
        if(request.GeometryRequirements is not {Radial.Count:>0})return;
        var values=ResolvedValues(request,overrides);double V(string key)=>values[key];
        foreach(var role in request.GeometryRequirements.Radial)
        {
            var owners=program.Operations.Where(o=>o.Kind==ConstructionKind.CreateRevolvedBoss).ToArray();
            ContractValidation.Require(owners.Length==1,"STRUCTURE_OWNER: one emitted revolve required: "+role.Id);
            var owner=owners[0];var sketch=program.Operations.Single(o=>o.SemanticId+".profile"==owner.Profile!.SemanticId);
            var axis=role.Layout.Axis switch{PrincipalAxis.X=>new Vector3(1,0,0),PrincipalAxis.Y=>new Vector3(0,1,0),_=>new Vector3(0,0,1)};
            var origin=new Vector3(V(role.Layout.OriginX),V(role.Layout.OriginY),V(role.Layout.OriginZ));
            ContractValidation.Require(sketch.Placement!.Frame.XAxis==axis && sketch.Placement.Frame.OriginMm==origin && sketch.Placement.Offset.Millimeters==0,"STRUCTURE_LAYOUT: emitted world axis/origin differs: "+role.Id);
            var polygon=sketch.Sketch!.Points;
            ContractValidation.Require(Math.Abs(polygon.Min(p=>p.X.Millimeters)-V(role.BaseOuter[0].Start))<=Tolerance && Math.Abs(polygon.Max(p=>p.X.Millimeters)-V(role.BaseOuter[^1].End))<=Tolerance,"STRUCTURE_AXIAL_DOMAIN: emitted axial span differs: "+role.Id);
            var breaks=role.BaseOuter.Concat(role.BaseInner).SelectMany(r=>new[]{V(r.Start),V(r.End)})
                .Concat(role.ExternalRecesses.SelectMany(r=>new[]{V(r.Start),V(r.End)})).Concat(polygon.Select(p=>p.X.Millimeters)).Distinct().OrderBy(x=>x).ToArray();
            for(var i=1;i<breaks.Length;i++)
            {
                var x=(breaks[i-1]+breaks[i])/2;
                var outer=role.BaseOuter.Single(r=>V(r.Start)<x && x<V(r.End));var inner=role.BaseInner.Single(r=>V(r.Start)<x && x<V(r.End));
                var recess=role.ExternalRecesses.SingleOrDefault(r=>V(r.Start)<x && x<V(r.End));var expectedOuter=V(recess?.FloorRadius??outer.Radius!);var expectedInner=inner.Radius is null?0:V(inner.Radius);
                var intersections=new List<double>();
                for(var p=0;p<polygon.Count;p++)
                {
                    var a=polygon[p];var b=polygon[(p+1)%polygon.Count];
                    if(Math.Min(a.X.Millimeters,b.X.Millimeters)<x && x<Math.Max(a.X.Millimeters,b.X.Millimeters))
                    {
                        ContractValidation.Require(a.Y==b.Y,"STRUCTURE_PROFILE: nonorthogonal emitted boundary.");intersections.Add(a.Y.Millimeters);
                    }
                }
                intersections.Sort();
                ContractValidation.Require(intersections.Count==2 && Math.Abs(intersections[0]-expectedInner)<=Tolerance && Math.Abs(intersections[1]-expectedOuter)<=Tolerance && expectedOuter>expectedInner+Tolerance,
                    "STRUCTURE_BOUNDARY: outer/inner mismatch at axial "+x+" for "+role.Id+"; expected="+expectedInner+".."+expectedOuter+", actual="+string.Join(",",intersections));
            }
        }
    }
    public static void VerifyOverrides(ConstructionPlanRequest request,ConstructionPlanResponse accepted,ConstructionProgram program,IReadOnlyList<RadialSourceOverride> edits)
    {
        if(request.GeometryRequirements is not {Radial.Count:>0})return;
        var overrides=new Dictionary<string,double>();
        foreach(var edit in edits)
        {
            var owner=accepted.Program!.Operations.Single(o=>o.SemanticId==edit.Target);var sketch=owner.Profile!.SemanticId[..^8];
            var prefix=edit.Target+".radius.";ContractValidation.Require(edit.ParameterSemanticId?.StartsWith(prefix,StringComparison.Ordinal)==true,"Explicit radial override identity is missing.");
            var binding=accepted.Bindings.Single(b=>b.Target==sketch && b.Metric==RequirementMetric.RadialDriverRadius && b.Element==edit.ParameterSemanticId![prefix.Length..]);overrides[binding.SemanticKey]=edit.Value;
        }
        VerifyProgram(request,program,overrides);
    }
}
