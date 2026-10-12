using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

internal static partial class T4Tests
{
    internal static int Pure(string root,string run)
    {
        var output=Path.Combine(root,"artifacts/m20-preparation/T4",run);var results=new List<object>();var failed=0;
        void Test(string name,Action action){try{action();results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.ToString()});Console.WriteLine("FAIL "+name+" "+e.Message);}}
        void Bad(Action action){try{action();}catch(ContractException){return;}throw new Exception("Unsafe planning accepted");}
        var scenes=T4Cases.All(root);
        ConstructionPlanningResult Plan(T4Case scene,ConstructionPlanResponse? response=null)=>new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),new RecordingSource(Cases.Wire(response??scene.Response))).PlanAsync(scene.Request).GetAwaiter().GetResult();
        foreach(var scene in scenes)Test("full_"+scene.Id,()=>{var r=Plan(scene);Program.Write(Path.Combine(output,scene.Id+"-plan.json"),new{scene.Request,scene.Response,r});Program.Check(r.Preview is not null,r.Message);});
        Test("strict_provider_schema",()=>Program.Check(ProviderSchemaCompatibility.Audit(ConstructionPlanner.ResponseSchema(SolidWorksConstructionPlanningRuntime.ForNewModel())).Count==0,"Schema incompatible"));
        Test("circle_schema_requires_one_point",()=>{
            using var d=JsonDocument.Parse(ConstructionPlanner.ResponseSchema(SolidWorksConstructionPlanningRuntime.ForNewModel()));var op=d.RootElement.GetProperty("properties").GetProperty("program").GetProperty("anyOf")[0].GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("anyOf").EnumerateArray().Single(a=>a.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString()=="create_sketch");
            var circle=op.GetProperty("properties").GetProperty("sketch").GetProperty("properties").GetProperty("entities").GetProperty("items").GetProperty("anyOf")[0].GetProperty("properties");Program.Check(circle.GetProperty("points").GetProperty("minItems").GetInt32()==1 && circle.GetProperty("points").GetProperty("maxItems").GetInt32()==1,"Empty circle permitted");});
        Test("derived_chain_covers_original_diameters_and_lengths",()=>ConstructionRequirements.VerifyGeometry(scenes[0].Response,Array.Empty<ConstructionOperation>(),scenes[0].Request));
        Test("unused_explicit_fact_refuses",()=>{var s=scenes[0];var q=s.Request with {Input=s.Request.Input with {Requirements=s.Request.Input.Requirements.Append(Cases.Given("unused_strength",12)).ToArray()}};Bad(()=>ConstructionRequirements.VerifyGeometry(s.Response with {Requirements=ConstructionRequirements.Normalize(q)},Array.Empty<ConstructionOperation>(),q));});
        Test("missing_driver_binding_refuses",()=>Program.Check(Plan(scenes[0],scenes[0].Response with {Bindings=scenes[0].Response.Bindings.Where(b=>b.Element!="middle").ToArray()}).Preview is null,"Missing driver accepted"));
        Test("wrong_axial_station_refuses",()=>{var s=scenes[0];var p=s.Response.Program!;var o=p.Operations[0];var sketch=o.Sketch!;var changed=sketch with {Points=sketch.Points.Select(v=>v.Id is "v7" or "v8"?v with {X=new(34)}:v).ToArray()};Program.Check(Plan(s,s.Response with {Program=p with {Operations=new[]{o with {Sketch=changed},p.Operations[1]}}}).Preview is null,"Wrong groove station accepted");});
        Test("wrong_selector_refuses",()=>Program.Check(Plan(scenes[1],scenes[1].Response with {Bindings=scenes[1].Response.Bindings.Select(b=>b.Metric==RequirementMetric.RadialDriverRadius?b with {Element="missing"}:b).ToArray()}).Preview is null,"Unknown driver accepted"));
        Test("wrong_unit_refuses",()=>{var s=scenes[0];Bad(()=>ConstructionRequirements.VerifyGeometry(s.Response with {Requirements=s.Response.Requirements with {Requirements=s.Response.Requirements.Requirements.Select(r=>r.SemanticKey=="head_radius"?r with {Unit=ScalarUnit.Degree}:r).ToArray()}},Array.Empty<ConstructionOperation>(),s.Request));});
        Test("forward_rule_or_cycle_refuses",()=>Bad(()=>ConstructionRequirements.Normalize(scenes[0].Request with {Rules=new[]{new RequirementRule("one","copy_dimension","two"),new("two","copy_dimension","one")}})));
        Test("source_graph_rejects_cycles_before_traversal",()=>Bad(()=>ConstructionRequirements.VerifyGeometry(scenes[0].Response,Array.Empty<ConstructionOperation>(),scenes[0].Request with {Rules=new[]{new RequirementRule("one","copy_dimension","two"),new("two","copy_dimension","one")}})));
        Test("shared_derivation_graph_is_bounded",()=>{
            var s=scenes[0];var rules=s.Request.Rules.ToList();var key="zero";
            for(var i=0;i<32;i++){var next="shared_"+i;rules.Add(new RequirementRule(next,"sum_dimensions",null){Inputs=Enumerable.Repeat(key,16).ToArray()});key=next;}
            var q=s.Request with {Rules=rules};var response=s.Response with {Requirements=ConstructionRequirements.Normalize(q),Bindings=s.Response.Bindings.Select(b=>b.SemanticKey=="zero"?b with {SemanticKey=key}:b).ToArray()};
            ConstructionRequirements.VerifyGeometry(response,Array.Empty<ConstructionOperation>(),q);
        });
        Test("missing_critical_input_cannot_be_funded_by_rule",()=>Bad(()=>ConstructionRequirements.Normalize(scenes[0].Request with {Input=scenes[0].Request.Input with {Requirements=scenes[0].Request.Input.Requirements.Select(r=>r.SemanticKey=="middle_diameter"?Cases.Given(r.SemanticKey,null):r).ToArray()}})));
        Test("cosmetic_default_cannot_fund_hole_size",()=>{var s=scenes[2];Program.Check(Plan(s,s.Response with {Bindings=s.Response.Bindings.Select(b=>b.Metric==RequirementMetric.CircleRadius?b with {SemanticKey="edge_radius"}:b).ToArray()}).Preview is null,"Default-funded hole accepted");});
        Test("cosmetic_default_cannot_fund_depth",()=>{var s=scenes[2];Program.Check(Plan(s,s.Response with {Bindings=s.Response.Bindings.Select(b=>b.Target=="leg"?b with {SemanticKey="edge_radius"}:b).ToArray()}).Preview is null,"Default-funded depth accepted");});
        foreach(var throughCopy in new[]{false,true})Test("matching_size_cosmetic_default_refuses_"+throughCopy,()=>{
            var s=scenes[2];var q=s.Request with {Input=s.Request.Input with {Requirements=s.Request.Input.Requirements.Select(r=>r.SemanticKey=="hole_diameter"?r with {Value=2}:r).ToArray()}};
            if(throughCopy)q=q with {Rules=q.Rules.Append(new("copied_cosmetic","copy_dimension","edge_radius")).ToArray()};
            var program=s.Response.Program! with {Operations=s.Response.Program!.Operations.Select(o=>o.Sketch?.Entities.Single().Kind==SketchEntityKind.Circle?o with {Sketch=o.Sketch with {Entities=o.Sketch.Entities.Select(e=>e with {Radius=new(1)}).ToArray(),Constraints=o.Sketch.Constraints.Select(c=>c with {Dimension=new(1)}).ToArray()}}:o).ToArray()};
            var response=s.Response with {Program=program,Requirements=ConstructionRequirements.Normalize(q),Bindings=s.Response.Bindings.Select(b=>b.Metric==RequirementMetric.CircleRadius?b with {SemanticKey=throughCopy?"copied_cosmetic":"edge_radius"}:b).ToArray()};
            var result=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),new RecordingSource(Cases.Wire(response))).PlanAsync(q).GetAwaiter().GetResult();
            Program.Check(result.Preview is null && result.Message.Contains("defaults",StringComparison.OrdinalIgnoreCase),"Matching value/derived copy bypassed functional-default boundary: "+result.Message);
        });
        Test("body_is_not_a_sketch_plane",()=>{var s=scenes[2];Program.Check(Plan(s,s.Response with {Program=s.Response.Program! with {Operations=s.Response.Program!.Operations.Select(o=>o.SemanticId=="leg_profile"?o with {Placement=o.Placement! with {Plane=new("base.body",CadHarness.Ir.SemanticType.BodyRef)}}:o).ToArray()}}).Preview is null,"Body plane accepted");});
        Test("wrong_output_suffix_refuses",()=>{var s=scenes[0];Program.Check(Plan(s,s.Response with {Program=s.Response.Program! with {Operations=s.Response.Program!.Operations.Select(o=>o.Profile is null?o:o with {Profile=o.Profile with {SemanticId="section"}}).ToArray()}}).Preview is null,"Unproduced profile accepted");});
        Test("implicit_top_offset_refuses",()=>{var s=scenes[3];Program.Check(Plan(s,s.Response with {Program=s.Response.Program! with {Operations=s.Response.Program!.Operations.Select(o=>o.SemanticId=="slot0_profile"?o with {Placement=o.Placement! with {Plane=new("world.xy",CadHarness.Ir.SemanticType.ReferencePlane)}}:o).ToArray()}}).Preview is null,"Unowned top plane accepted");});
        Test("approval_reuse_and_context_refuse",()=>{var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();var s=scenes[0];var result=new ConstructionPlanner(runtime,new RecordingSource(Cases.Wire(s.Response))).PlanAsync(s.Request).GetAwaiter().GetResult();var a=result.Preview!.Approve(result.Preview.Sha256);a.Consume(runtime);Bad(()=>a.Consume(runtime));Bad(()=>result.Preview.Approve(result.Preview.Sha256).Consume(SolidWorksConstructionPlanningRuntime.ForNewModel()));});
        Test("capability_drift_refuses",()=>{var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();var s=scenes[2];var result=new ConstructionPlanner(runtime,new RecordingSource(Cases.Wire(s.Response))).PlanAsync(s.Request).GetAwaiter().GetResult();Bad(()=>result.Preview!.Approve(result.Preview.Sha256).Consume(new ChangedRuntime(runtime)));});
        Test("unqualified_operation_refuses",()=>{var r=SolidWorksConstructionPlanningRuntime.ForNewModel();var result=new ConstructionPlanner(new NoFilletRuntime(r),new RecordingSource(Cases.Wire(scenes[2].Response))).PlanAsync(scenes[2].Request).GetAwaiter().GetResult();Program.Check(result.Preview is null && result.FailureCode==V03FailureCodes.CapabilityUnavailable,"Unqualified fillet advertised/accepted");});
        Test("sleeve_continuation_does_not_advertise_append",()=>{var s=scenes[1];var p=s.Response.Program!;var state=new CadHarness.State.GenericConstructionState(new(Guid.NewGuid(),Guid.NewGuid(),"Default","D:\\owned.SLDPRT"),1,p,p.Operations.Select(o=>new CadHarness.State.GenericNativeFeature(o.SemanticId,o.Kind,"tracked",new("AQ=="),Array.Empty<CadHarness.State.NativePersistentReference>())).ToArray(),Array.Empty<CadHarness.State.GenericNativeSketch>(),Array.Empty<CadHarness.State.GenericNativeParameter>(),Array.Empty<CadHarness.State.DependencyEdge>(),null);var r=SolidWorksConstructionPlanningRuntime.ForSnapshotPreview(state);Program.Check(r.ExecutableKinds.Count==0,"Arbitrary revolve continuation advertised");ProviderSchemaCompatibility.RequireCompatible(ConstructionPlanner.ResponseSchema(r));});
        GenericConstructionState State(ConstructionProgram p,long revision)=>new(new(Guid.NewGuid(),Guid.NewGuid(),"Default","D:\\owned.SLDPRT"),revision,p,Array.Empty<GenericNativeFeature>(),Array.Empty<GenericNativeSketch>(),Array.Empty<GenericNativeParameter>(),Array.Empty<DependencyEdge>(),null);
        GenericProvenance History(T4Case s)
        {
            var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();
            var preview=new ConstructionPlanner(runtime,new RecordingSource(Cases.Wire(s.Response))).PlanAsync(s.Request).GetAwaiter().GetResult().Preview!;
            var accepted=preview.Approve(preview.Sha256).ConsumeAccepted(runtime);
            return GenericPlanningProvenance.AppendPlan(null,State(s.Response.Program! with {Operations=Array.Empty<ConstructionOperation>()},0),accepted);
        }
        foreach(var s in scenes)Test("durable_history_"+s.Id,()=>{
            var p=History(s);GenericPlanningProvenance.Verify(p,State(s.Response.Program!,1));
            Program.Check(ContractJson.Read<GenericProvenance>(ContractJson.Write(p,_=>{}),_=>{}).Payload==p.Payload,"Outer package string bound differs from explicit provenance contract");
            Program.Write(Path.Combine(output,s.Id+"-provenance.json"),new{characters=p.Payload.Length,withinExistingBound=p.Payload.Length<=65536,record=p});
        });
        Test("provenance_size_bound_stays_finite",()=>{Bad(()=>ContractJson.Write(new GenericProvenance("1","construction_planning",new string('x',65537)),_=>{}));using var d=JsonDocument.Parse(ContractJson.Schema<GenericProvenance>());Program.Check(d.RootElement.GetProperty("properties").GetProperty("payload").GetProperty("maxLength").GetInt32()==65536,"Schema/parser bounds differ");Bad(()=>ContractJson.Write(new RequirementRule(new string('x',10925),"layout_zero",null),_=>{}));});
        Test("explicit_override_preserves_original_given",()=>{
            var s=scenes[0];var first=History(s);var state=State(s.Response.Program!,1);
            var command=new GenericScalarCommand("rotation",ParameterKey.RevolveRadius,12,13,1,"rotation.radius.middle");
            var updated=GenericPlanningProvenance.AppendEdit(first,state,command)!;
            var sketch=state.Program.Operations[0];var program=state.Program with {Operations=new[]{sketch with {Sketch=RadialProfilePolicy.Edit(sketch.Sketch!,"middle",12,13)},state.Program.Operations[1]}};
            GenericPlanningProvenance.Verify(updated,state with {Program=program,Revision=2});
            var h=ContractJson.Read<PlanningHistory>(updated.Payload,_=>{});var original=h.Steps[0].Plan!.Request;
            Program.Check(original.Input.Requirements.Single(r=>r.SemanticKey=="middle_diameter").Value==24 && h.Steps[1].Edit==command,"Retroactive Given rewrite");
            Bad(()=>GenericPlanningProvenance.Verify(updated,state with {Revision=2}));
        });
        Test("tampered_accepted_binding_refuses",()=>{
            var s=scenes[0];var p=History(s);var h=ContractJson.Read<PlanningHistory>(p.Payload,_=>{});var step=h.Steps[0];var plan=step.Plan!;
            var altered=plan with {Response=plan.ReadResponse() with {Bindings=plan.ReadResponse().Bindings.Where(b=>b.Element!="middle").ToArray()}};
            var damaged=p with {Payload=ContractJson.Write(h with {Steps=new[]{step with {Plan=altered}}},_=>{})};
            Bad(()=>GenericPlanningProvenance.Verify(damaged,State(s.Response.Program!,1)));
        });
        Test("missing_history_event_and_revision_refuse",()=>{var s=scenes[0];var p=History(s);Bad(()=>GenericPlanningProvenance.Verify(p,State(s.Response.Program!,2)));var h=ContractJson.Read<PlanningHistory>(p.Payload,_=>{});Bad(()=>GenericPlanningProvenance.Verify(p with {Payload=ContractJson.Write(h with {Steps=Array.Empty<PlanningHistoryStep>()},_=>{})},State(s.Response.Program!,1)));});
        Test("accepted_hash_and_version_refuse_tampering",()=>{var s=scenes[0];var p=History(s);var state=State(s.Response.Program!,1);Bad(()=>GenericPlanningProvenance.Verify(p with {Version="2"},state));var h=ContractJson.Read<PlanningHistory>(p.Payload,_=>{});var step=h.Steps[0];Bad(()=>GenericPlanningProvenance.Verify(p with {Payload=ContractJson.Write(h with {Steps=new[]{step with {Plan=step.Plan! with {CapabilityJson=step.Plan.CapabilityJson+" "}}}},_=>{})},state));});
        Test("unrecorded_construction_refuses_before_mutation",()=>{var s=scenes[0];Bad(()=>GenericPlanningProvenance.Verify(History(s),State(s.Response.Program!,2)));});
        Test("override_wrong_driver_or_old_value_refuses",()=>{var s=scenes[0];var p=History(s);var state=State(s.Response.Program!,1);Bad(()=>GenericPlanningProvenance.AppendEdit(p,state,new("rotation",ParameterKey.RevolveRadius,11,13,1,"rotation.radius.middle")));Bad(()=>GenericPlanningProvenance.AppendEdit(p,state,new("rotation",ParameterKey.RevolveRadius,12,13,1,"rotation.radius.not_found")));});
        // Replay preserved bad provider output exactly, with its own immutable
        // request. Do not patch JSON into the deterministic answers above.
        var legacy=Path.Combine(root,"artifacts/milestone20/acceptance_v3");
        foreach(var id in new[]{"C2","C4"})
            Test("legacy_bad_"+id,()=>{
                var path=Path.Combine(legacy,"slots",id+"-real-1","provider-response.json");
                using var raw=JsonDocument.Parse(File.ReadAllText(path));var json=raw.RootElement.GetProperty("response").GetProperty("json").GetString()!;
                string? code=null,message=null;try{var response=ContractJson.Read<ConstructionPlanResponse>(json,ConstructionRequirements.ValidateResponse);ConstructionPreflight.EvaluateGeometry(response.Program!);ConstructionRequirements.VerifyGeometry(response,Array.Empty<ConstructionOperation>());}catch(ContractException e){code=e.Code;message=e.Message;}
                Program.Write(Path.Combine(output,"legacy-"+id+"-replay.json"),new{path,sha256=CadHarness.State.ManagedRevisionStore.Hash(path),refused=code is not null,code,message,unmodifiedResponse=true,nativeMutation=false});Program.Check(code is not null,"Old bad response silently accepted");
            });
        Program.Write(Path.Combine(output,"result.json"),new{passed=failed==0,total=results.Count,failed,results,nativeOpens=0,providerCalls=0});return failed==0?0:1;
    }
    private sealed class ChangedRuntime(IConstructionPlanningRuntime original):IConstructionPlanningRuntime
    {public string ContextIdentity=>original.ContextIdentity;public long Revision=>original.Revision;public string CapabilityJson=>original.CapabilityJson+" ";public IReadOnlyList<ConstructionOperation> Prefix=>original.Prefix;public IReadOnlyList<ConstructionKind> ExecutableKinds=>original.ExecutableKinds;public FeatureSolidExpectation Preflight(ConstructionProgram p)=>original.Preflight(p);}
    private sealed class NoFilletRuntime(IConstructionPlanningRuntime original):IConstructionPlanningRuntime
    {public string ContextIdentity=>original.ContextIdentity;public long Revision=>original.Revision;public string CapabilityJson=>original.CapabilityJson;public IReadOnlyList<ConstructionOperation> Prefix=>original.Prefix;public IReadOnlyList<ConstructionKind> ExecutableKinds=>original.ExecutableKinds.Where(k=>k!=ConstructionKind.ApplyFillet).ToArray();public FeatureSolidExpectation Preflight(ConstructionProgram p)=>original.Preflight(p);}
}
