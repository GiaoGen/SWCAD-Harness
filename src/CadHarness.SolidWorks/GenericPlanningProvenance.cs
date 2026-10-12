using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.State;

[assembly: InternalsVisibleTo("CadHarness.ConstructionPlanning.Tests")]

namespace CadHarness.SolidWorks;

public sealed record PersistedConstructionPlan(ConstructionPlanRequest Request,ConstructionPlanResponse Response,string CapabilityJson,string ContextIdentity,string Sha256,long BeforeRevision,int PrefixCount)
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public ConstructionCompilation? Compilation {get;init;}
    public ConstructionPlanResponse ReadResponse()=>Response;
    internal AcceptedConstructionPlan ToAccepted()=>new AcceptedConstructionPlan(ContractJson.Write(Request,r=>{_=ConstructionRequirements.Normalize(r);}),ContractJson.Write(Response,ConstructionRequirements.ValidateResponse),CapabilityJson,ContextIdentity,Sha256,BeforeRevision,PrefixCount){Compilation=Compilation};
    internal static PersistedConstructionPlan Capture(AcceptedConstructionPlan p)=>new PersistedConstructionPlan(ContractJson.Read<ConstructionPlanRequest>(p.RequestJson,r=>{_=ConstructionRequirements.Normalize(r);}),p.ReadResponse(),p.CapabilityJson,p.ContextIdentity,p.Sha256,p.BeforeRevision,p.PrefixCount){Compilation=p.Compilation};
}
public sealed record PlanningHistoryStep(long BeforeRevision,PersistedConstructionPlan? Plan,GenericScalarCommand? Edit);
public sealed record PlanningHistory(int Version,long BaselineRevision,ConstructionProgram Baseline,IReadOnlyList<PlanningHistoryStep> Steps);

// Accepted inputs remain immutable. Explicit scalar overrides are distinct
// revision events, not retroactive changes to the initial Given dimensions.
internal static class GenericPlanningProvenance
{
    internal const string Kind="construction_planning";
    private static PlanningHistory Read(GenericProvenance p)
    {
        ContractValidation.Require(p.Version=="1" && p.Kind==Kind,"Unsupported planning provenance version/tag.");
        return ContractJson.Read<PlanningHistory>(p.Payload,Validate,65536);
    }
    private static void Validate(PlanningHistory h)
    {
        ContractValidation.Require(h.Version==1 && h.BaselineRevision>=0 && h.Steps.Count is >0 and <=64 && h.Baseline.Origin==ModelOrigin.Harness,"Invalid planning history version/baseline.");
        if(h.Baseline.Operations.Count>0)ContractValidation.Program(h.Baseline);
        var program=h.Baseline;var revision=h.BaselineRevision;
        foreach(var step in h.Steps)
        {
            ContractValidation.Require(step.BeforeRevision==revision && (step.Plan is null)!=(step.Edit is null),"Planning history revision or event tag differs.");
            if(step.Plan is { } saved)
            {
                var plan=saved.ToAccepted();
                ContractValidation.Require(plan.BeforeRevision==revision && plan.PrefixCount==program.Operations.Count,"Approved plan prefix/revision differs.");
                var request=ContractJson.Read<ConstructionPlanRequest>(plan.RequestJson,r=>{_=ConstructionRequirements.Normalize(r);});var response=plan.ReadResponse();
                ContractValidation.Require(new AcceptedRequirements(response.Requirements).Json==new AcceptedRequirements(ConstructionRequirements.Normalize(request)).Json,"Accepted request/response requirements differ.");
                _=ConstructionPreflight.Evaluate(response.Program!,program.Operations);ConstructionRequirements.VerifyGeometry(response,program.Operations,request);
                SemanticConstructionCompiler.VerifyCompilation(request,response,plan.Compilation);
                var compilation=plan.Compilation is null?null:ContractJson.Write(plan.Compilation,_=>{});
                var hash=AcceptedConstructionPlan.ComputeHash(plan.ContextIdentity,plan.BeforeRevision,plan.CapabilityJson,plan.RequestJson,plan.ResponseJson,compilation);
                ContractValidation.Require(hash==plan.Sha256,"Accepted immutable plan hash differs.");program=response.Program!;
            }
            else program=ReplayEdit(program,step.Edit!,revision);
            revision++;
        }
    }
    private static ConstructionProgram ReplayEdit(ConstructionProgram p,GenericScalarCommand edit,long revision)
    {
        ContractValidation.Require(edit.ExpectedRevision==revision && edit.Value!=edit.ExpectedOldValue,"Explicit override revision/value differs.");
        var f=p.Operations.SingleOrDefault(o=>o.SemanticId==edit.Target);ContractValidation.Require(f is not null && GenericFeatureCapabilities.IsQualified(f.Kind,edit.Parameter),"Unqualified override in planning history.");
        ContractValidation.Scalar(edit.Parameter,ScalarUnit.Millimeter,edit.Value);
        if(edit.Parameter==ParameterKey.RevolveRadius)
        {
            var sketch=p.Operations.Single(o=>o.SemanticId+".profile"==f!.Profile!.SemanticId);var s=sketch.Sketch!;ClosedSketch changed;
            if(s.RadialProfile is not null)
            {
                var prefix=f!.SemanticId+".radius.";ContractValidation.Require(edit.ParameterSemanticId?.StartsWith(prefix,StringComparison.Ordinal)==true,"Radial override needs exact parameter identity.");
                changed=RadialProfilePolicy.Edit(s,edit.ParameterSemanticId![prefix.Length..],edit.ExpectedOldValue,edit.Value);
            }
            else
            {
                ContractValidation.Require(s.Entities.Count==1 && s.Entities[0].Kind==SketchEntityKind.Circle && Math.Abs(s.Entities[0].Radius!.Millimeters-edit.ExpectedOldValue)<=PlanarSketchPreflight.ToleranceMm,"Circle override differs.");
                changed=s with {Entities=s.Entities.Select(e=>e with {Radius=new(edit.Value)}).ToArray(),Constraints=s.Constraints.Select(c=>c.Kind is ConstraintKind.Radius or ConstraintKind.Diameter?c with {Dimension=new(edit.Value*(c.Kind==ConstraintKind.Diameter?2:1))}:c).ToArray()};
            }
            p=p with {Operations=p.Operations.Select(o=>o.SemanticId==sketch.SemanticId?o with {Sketch=changed}:o).ToArray()};
        }
        else
        {
            ContractValidation.Require(f!.Depth is not null && Math.Abs(f.Depth.Millimeters-edit.ExpectedOldValue)<=PlanarSketchPreflight.ToleranceMm && (edit.ParameterSemanticId is null || edit.ParameterSemanticId==f.SemanticId+".depth"),"Depth override differs.");
            p=p with {Operations=p.Operations.Select(o=>o.SemanticId==edit.Target?o with {Depth=new(edit.Value)}:o).ToArray()};
        }
        _=ConstructionPreflight.EvaluateGeometry(p);return p;
    }
    private static GenericProvenance Write(PlanningHistory h)
    {
        // Whitespace is not provenance. Preserve every field while avoiding
        // multiplying pretty-print overhead in the bounded metadata container.
        using var json=JsonDocument.Parse(ContractJson.Write(h,Validate));
        var payload=JsonSerializer.Serialize(json.RootElement);
        ContractValidation.Require(payload.Length<=65536,"Planning history exceeds the existing provenance bound.");
        return new("1",Kind,payload);
    }
    internal static GenericProvenance AppendPlan(GenericProvenance? previous,GenericConstructionState before,AcceptedConstructionPlan plan)
    {
        var h=previous?.Kind==Kind?Read(previous):new PlanningHistory(1,before.Revision,before.Program,Array.Empty<PlanningHistoryStep>());
        return Write(h with {Steps=h.Steps.Append(new(before.Revision,PersistedConstructionPlan.Capture(plan),null)).ToArray()});
    }
    internal static GenericProvenance? AppendEdit(GenericProvenance? previous,GenericConstructionState before,GenericScalarCommand edit)
    {
        if(previous?.Kind!=Kind)return previous;
        var h=Read(previous);return Write(h with {Steps=h.Steps.Append(new(before.Revision,null,edit)).ToArray()});
    }
    internal static void Verify(GenericProvenance? provenance,GenericConstructionState state)
    {
        if(provenance?.Kind!=Kind)return;
        var h=Read(provenance);var p=h.Baseline;var r=h.BaselineRevision;
        foreach(var step in h.Steps){p=step.Plan is { } plan?plan.ReadResponse().Program!:ReplayEdit(p,step.Edit!,r);r++;}
        ContractValidation.Require(r==state.Revision && JsonSerializer.Serialize(p)==JsonSerializer.Serialize(state.Program),"Planning provenance does not reproduce authoritative program/revision.");
    }
}
