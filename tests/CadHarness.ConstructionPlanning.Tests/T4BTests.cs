using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

internal static class T4BTests
{
    internal static int Pure(string root,string run)
    {
        var directory=Path.Combine(root,"artifacts/m20-preparation/T4B",run);var rows=new List<object>();var failed=0;
        void Test(string name,Action a){try{a();rows.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception e){failed++;rows.Add(new{name,passed=false,error=e.ToString()});Console.WriteLine("FAIL "+name+": "+e.Message);}}
        void Bad(Action a){try{a();}catch(ContractException){return;}throw new Exception("Unsafe semantic plan accepted");}
        var scenes=T4BCases.All(root);
        using var frozen=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json")));
        var oracles=frozen.RootElement.GetProperty("development").EnumerateArray().Select(t=>t.GetProperty("oracle")).ToArray();
        foreach(var s in scenes)Test("complete_"+s.Id,()=>{
            var result=SemanticConstructionCompiler.Compile(s.Request,s.Draft);
            var expected=ConstructionPreflight.EvaluateGeometry(s.Expected.Response.Program!);var actual=ConstructionPreflight.EvaluateGeometry(result.Response.Program!);
            Program.Check(Math.Abs(actual.VolumeMm3-expected.VolumeMm3)<.0001 && actual.MinimumLocalMm==expected.MinimumLocalMm && actual.MaximumLocalMm==expected.MaximumLocalMm,"Existing independent task definition differs from compiled shape");
            var oracle=oracles[Array.IndexOf(scenes,s)];var independentVolume=oracle.GetProperty("volumeMm3").GetDouble()+Math.PI*oracle.GetProperty("volumePiCoefficientMm3").GetDouble();
            Program.Check(Math.Abs(actual.VolumeMm3-independentVolume)<.0001,"Independent frozen analytical oracle differs");
            ProviderSchemaCompatibility.RequireCompatible(SemanticConstructionCompiler.ResponseSchema(s.Request,SolidWorksConstructionPlanningRuntime.ForNewModel()));
            var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();var planned=new ConstructionPlanner(runtime,new RecordingSource(ContractJson.Write(s.Draft,SemanticConstructionCompiler.ValidateDraft))).PlanAsync(s.Request).GetAwaiter().GetResult();Program.Check(planned.Preview is not null,planned.Message);
            var accepted=planned.Preview!.Approve(planned.Preview.Sha256).ConsumeAccepted(runtime);
            var empty=new GenericConstructionState(new(Guid.NewGuid(),Guid.NewGuid(),"Default","D:\\owned.SLDPRT"),0,result.Response.Program! with {Operations=Array.Empty<ConstructionOperation>()},Array.Empty<GenericNativeFeature>(),Array.Empty<GenericNativeSketch>(),Array.Empty<GenericNativeParameter>(),Array.Empty<DependencyEdge>(),null);
            var history=GenericPlanningProvenance.AppendPlan(null,empty,accepted);GenericPlanningProvenance.Verify(history,empty with {Program=result.Response.Program!,Revision=1});
            Program.Write(Path.Combine(directory,s.Id+"-plan.json"),new{s.Request,s.Draft,result,planned,provenanceCharacters=history.Payload.Length,history});
        });
        var radial=scenes[0];var node=radial.Draft.Nodes[0];var section=node.Profile!.Radial!;
        Test("semantic_capability_has_no_direct_ir_reference_conflict",()=>{
            var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();using var direct=JsonDocument.Parse(runtime.CapabilityJson);using var semantic=JsonDocument.Parse(SemanticConstructionCompiler.CapabilityProjection(runtime));
            Program.Check(!semantic.RootElement.TryGetProperty("outputRules",out _) && !semantic.RootElement.TryGetProperty("featureRules",out _) && semantic.RootElement.GetProperty("limitations").GetRawText()==direct.RootElement.GetProperty("limitations").GetRawText(),"Projection changed material limits or kept conflicting syntax");
        });
        Test("radial_overlap",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request,radial.Draft with {Nodes=new[]{node with {Profile=node.Profile with {Radial=section with {Outer=section.Outer.Select((r,i)=>i==1?r with {Start="zero"}:r).ToArray()}}}}})));
        Test("suffix_is_not_a_semantic_reference",()=>{var s=scenes[3];Bad(()=>SemanticConstructionCompiler.Compile(s.Request,s.Draft with {Nodes=s.Draft.Nodes.Select(n=>n.Host is null?n:n with {Host=n.Host+".body"}).ToArray()}));});
        Test("provenance_bound_not_silently_enlarged",()=>{
            var s=scenes[2];var q=s.Request with {Input=s.Request.Input with {Intent=new string('"',4096)}};var d=s.Draft;
            var c=SemanticConstructionCompiler.Compile(q,d);var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();var p=new ConstructionPlanner(runtime,new RecordingSource(ContractJson.Write(d,SemanticConstructionCompiler.ValidateDraft))).PlanAsync(q).GetAwaiter().GetResult().Preview!;
            var accepted=p.Approve(p.Sha256).ConsumeAccepted(runtime);
            var empty=new GenericConstructionState(new(Guid.NewGuid(),Guid.NewGuid(),"Default","D:\\owned.SLDPRT"),0,c.Response.Program! with {Operations=Array.Empty<ConstructionOperation>()},Array.Empty<GenericNativeFeature>(),Array.Empty<GenericNativeSketch>(),Array.Empty<GenericNativeParameter>(),Array.Empty<DependencyEdge>(),null);
            try{GenericPlanningProvenance.AppendPlan(null,empty,accepted);throw new Exception("Compiled history bypassed the payload limit");}
            catch(ContractException e){Program.Check(e.Message=="Planning history exceeds the existing provenance bound.","Test hit another guard: "+e.Message);}
        });
        Test("unknown_source",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request,radial.Draft with {Nodes=new[]{node with {Angle="missing"}}})));
        Test("forward_host",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request,radial.Draft with {Nodes=new[]{node with {Host="later"}}})));
        Test("duplicate_node",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request,radial.Draft with {Nodes=new[]{node,node}})));
        Test("region_gap",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request,radial.Draft with {Nodes=new[]{node with {Profile=node.Profile with {Radial=section with {Outer=section.Outer.Where((_,i)=>i!=1).ToArray()}}}}})));
        Test("invented_dependency",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request,radial.Draft with {Nodes=new[]{node with {Profile=node.Profile with {Radial=section with {Dependencies=section.Dependencies.Append(new("head_radius","middle_radius","groove_depth")).ToArray()}}}}})));
        Test("missing_required_relation",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request,radial.Draft with {Nodes=new[]{node with {Profile=node.Profile with {Radial=section with {Dependencies=Array.Empty<DeclaredRadialDependency>()}}}}})));
        Test("derived_driver_cannot_be_primary",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request with {EditabilityRequirements=new[]{radial.Request.EditabilityRequirements![0] with {SourceKey="groove_radius"}}},radial.Draft)));
        Test("missing_editable_depth_source",()=>{var s=scenes[2];Bad(()=>SemanticConstructionCompiler.Compile(s.Request,s.Draft with {Nodes=s.Draft.Nodes.Select(n=>n.Kind==ConstructionKind.CreateAdditiveBoss?n with {Depth="leg_width"}:n).ToArray()}));});
        Test("wrong_role_kind",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request with {EditabilityRequirements=new[]{radial.Request.EditabilityRequirements![0] with {Parameter=ParameterKey.CutDepth}}},radial.Draft)));
        Test("no_slot_parameter_qualification_leak",()=>Bad(()=>ConstructionRequirements.Normalize(radial.Request with {EditabilityRequirements=new[]{radial.Request.EditabilityRequirements![0] with {Parameter=ParameterKey.SlotWidth}}})));
        Test("role_old_request_wire_stays_compatible",()=>{var q=ContractJson.Read<ConstructionPlanRequest>(ContractJson.Write(radial.Expected.Request,_=>{}),_=>{});Program.Check(q.SemanticDraftVersion is null && q.EditabilityRequirements is null,"Old direct request changed mode");});
        Test("compiler_version_drift",()=>{var c=SemanticConstructionCompiler.Compile(radial.Request,radial.Draft);Bad(()=>SemanticConstructionCompiler.VerifyCompilation(radial.Request,c.Response,c.Evidence with {Version="99"}));});
        Test("edited_proof_tamper",()=>{var c=SemanticConstructionCompiler.Compile(radial.Request,radial.Draft);Bad(()=>SemanticConstructionCompiler.VerifyCompilation(radial.Request,c.Response,c.Evidence with {EditTargets=new[]{c.Evidence.EditTargets[0] with {Owner="substitute"}}}));});
        Test("expanded_ir_tamper",()=>{var c=SemanticConstructionCompiler.Compile(radial.Request,radial.Draft);Bad(()=>SemanticConstructionCompiler.VerifyCompilation(radial.Request,c.Response with {Bindings=c.Response.Bindings.Skip(1).ToArray()},c.Evidence));});
        Test("symbols_are_not_fixture_spelling",()=>{foreach(var s in scenes){var d=s.Draft with {Nodes=s.Draft.Nodes.Select(n=>n with {Id="renamed_"+n.Id,Host=n.Host is null?null:"renamed_"+n.Host,Edge=n.Edge is null?null:n.Edge with {Host="renamed_"+n.Edge.Host}}).ToArray()};var c=SemanticConstructionCompiler.Compile(s.Request,d);Program.Check(c.Evidence.EditTargets.All(t=>t.Owner.StartsWith("renamed_",StringComparison.Ordinal)),"Hardcoded edit target");}});
        Test("different_radial_segment_count",()=>{
            var q=radial.Request with {Input=radial.Request.Input with {Requirements=radial.Request.Input.Requirements.Concat(new[]{Cases.Given("extra_station",70),Cases.Given("extra_diameter",17)}).ToArray()},Rules=radial.Request.Rules.Append(new("extra_radius","half_dimension","extra_diameter")).ToArray()};
            var outer=section.Outer.Take(4).Concat(new[]{new SemanticRadialRange("middle_end","extra_station","tip_radius"),new("extra_station","overall","extra_radius")}).ToArray();
            var c=SemanticConstructionCompiler.Compile(q,radial.Draft with {Nodes=new[]{node with {Profile=node.Profile with {Radial=section with {Outer=outer}}}}});
            var volume=ConstructionPreflight.EvaluateGeometry(c.Response.Program!).VolumeMm3;Program.Check(Math.Abs(volume-Math.PI*(11704+(8.5*8.5-9*9)*10))<.0001,"Four-driver layout uses fixture vertex assumptions");
        });
        Test("canonical_xz_capsule_transform",()=>{
            var s=scenes[3];var d=s.Draft with {Nodes=s.Draft.Nodes.Select(n=>n with {Placement=n.Placement! with {Plane=SemanticPlane.Xz,OriginY=n.Kind==ConstructionKind.CreateExtrude?"zero":"thickness",OriginZ=n.Placement.OriginY}}).ToArray()};
            var q=s.Request with {Input=s.Request.Input with {Intent="Principal xz plate/capsule development variant; all dimensions unchanged."}};var c=SemanticConstructionCompiler.Compile(q,d);var shape=ConstructionPreflight.EvaluateGeometry(c.Response.Program!);
            Program.Check(Math.Abs(shape.VolumeMm3-(47244-81*Math.PI))<.0001 && c.Response.Program!.Operations.Where(o=>o.Placement is not null).All(o=>o.Placement!.Frame.ZAxis.Y==1),"Wrong world/local principal-plane transform");
        });
        Test("unused_given_is_not_silently_consumed",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request with {Input=radial.Request.Input with {Requirements=radial.Request.Input.Requirements.Append(Cases.Given("unused_interface",99)).ToArray()}},radial.Draft)));
        Test("wrong_source_unit",()=>Bad(()=>SemanticConstructionCompiler.Compile(radial.Request with {Input=radial.Request.Input with {Requirements=radial.Request.Input.Requirements.Select(r=>r.SemanticKey=="head_length"?r with {Unit=ScalarUnit.Degree}:r).ToArray()}},radial.Draft)));
        Test("world_origin_cannot_be_used_as_local_center",()=>{var s=scenes[3];Bad(()=>SemanticConstructionCompiler.Compile(s.Request,s.Draft with {Nodes=s.Draft.Nodes.Select(n=>n.Profile?.Capsule is { } p && n.Placement!.OriginX=="slot0_x"?n with {Profile=n.Profile with {Capsule=p with {CenterX="slot0_x"}}}:n).ToArray()}));});
        Test("unsupported_material_intersection",()=>{var s=scenes[2];Bad(()=>SemanticConstructionCompiler.Compile(s.Request,s.Draft with {Nodes=s.Draft.Nodes.Select(n=>n.Kind==ConstructionKind.CreateAdditiveBoss?n with {Placement=n.Placement! with {OriginX="base_max_x"}}:n).ToArray()}));});
        Test("recorded_semantic_structural_pass_is_not_task_pass",()=>{
            var rows=new List<object>();
            foreach(var s in scenes){var path=Path.Combine(root,"artifacts/m20-preparation/T4B/live_v2",s.Id,"call-01-response.json");if(!File.Exists(path))continue;
                using var raw=JsonDocument.Parse(File.ReadAllText(path));var d=ContractJson.Read<SemanticConstructionDraft>(raw.RootElement.GetProperty("response").GetProperty("Json").GetString()!,SemanticConstructionCompiler.ValidateDraft);var c=SemanticConstructionCompiler.Compile(s.Request,d);
                var passed=true;string? failure=null;try{RequireTaskGeometry(root,s,c.Response.Program!);}catch(Exception e){passed=false;failure=e.Message;}
                Program.Check(passed==(s.Id!="shaft"),"Unexpected task recheck; preserve and inspect evidence");rows.Add(new{s.Id,passed,failure,path,sha256=ManagedRevisionStore.Hash(path),actual=ConstructionPreflight.EvaluateGeometry(c.Response.Program!)});
            }
            Program.Write(Path.Combine(directory,"live_v2-task-recheck.json"),new{rows,newProviderCalls=0,nativeMutations=0,originalResultsUnchanged=true});
        });
        Test("missing_critical_and_false_clarification",()=>{
            var draft=new SemanticConstructionDraft(1,PlanOutcome.NeedsClarification,Array.Empty<SemanticConstructionNode>(),new[]{"Supply the critical mounting dimension."},null);
            ConstructionPlanningResult Plan(ConstructionPlanRequest q)=>new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),new RecordingSource(ContractJson.Write(draft,SemanticConstructionCompiler.ValidateDraft))).PlanAsync(q).GetAwaiter().GetResult();
            Program.Check(Plan(radial.Request).Preview is null && Plan(radial.Request).Status==ConstructionPlanningStatus.Rejected,"False clarification accepted");
            var q=radial.Request with {Input=radial.Request.Input with {Requirements=radial.Request.Input.Requirements.Append(Cases.Given("unknown_critical",null)).ToArray()}};
            Program.Check(Plan(q).Status==ConstructionPlanningStatus.NeedsClarification,"True missing critical input not disclosed");
        });
        Test("all_eight_legacy_failures_remain_refused",()=>{
            foreach(var runId in new[]{"live_v1","live_v2"})foreach(var s in scenes)
            {
                var path=Path.Combine(root,"artifacts/m20-preparation/T4",runId,s.Id,"call-01-response.json");using var raw=JsonDocument.Parse(File.ReadAllText(path));var response=raw.RootElement.GetProperty("response").GetProperty("Json").GetString()!;
                var planned=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),new RecordingSource(response)).PlanAsync(s.Expected.Request).GetAwaiter().GetResult();Program.Check(planned.Preview is null,"Legacy failure silently repaired");
                Program.Write(Path.Combine(directory,runId+"-"+s.Id+"-refused.json"),new{path,sha256=ManagedRevisionStore.Hash(path),planned.FailureCode,planned.Message,nativeMutation=false});
            }
        });
        Program.Write(Path.Combine(directory,"result.json"),new{passed=failed==0,total=rows.Count,failed,rows,nativeOpens=0,providerCalls=0});return failed==0?0:1;
    }
    internal static int Live(string root,string run,string? only=null)
    {
        var directory=Path.Combine(root,"artifacts/m20-preparation/T4B",run);Program.Check(!Directory.Exists(directory),"Never overwrite live schedule");var scenes=T4BCases.All(root).Where(s=>only is null || s.Id==only).ToArray();Program.Check(scenes.Length>0,"Unknown targeted case");
        Program.Write(Path.Combine(directory,"freeze.json"),new{baseline="fcddf2d",stage="T4B development, NOT M20",maximumCalls=scenes.Length,noRetries=true,temperature=0,timeoutSeconds=180,maxOutputTokens=16384,compiler=SemanticConstructionCompiler.Version,
            tasks=scenes.Select(s=>new{s.Id,s.Request,schema=SemanticConstructionCompiler.ResponseSchema(s.Request,SolidWorksConstructionPlanningRuntime.ForNewModel())}),source=Program.SourceHashes(root),alias="deepseek-chat",endpoint=DeepSeekPlanSource.Endpoint});
        using var client=OpenAiPlanSource.CreateClient();var key=System.Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY")??throw new Exception("Missing configured credential");var rows=new List<object>();var failed=0;
        foreach(var s in scenes)
        {
            var clock=System.Diagnostics.Stopwatch.StartNew();var evidence=new EvidenceSource(new DeepSeekPlanSource(client,"deepseek-chat",key,16384,180),Path.Combine(directory,s.Id));
            var result=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),evidence).PlanAsync(s.Request).GetAwaiter().GetResult();var taskPassed=false;string? taskFailure=null;
            if(result.Preview is { } preview){try{RequireTaskGeometry(root,s,preview.Read().Program!);taskPassed=true;}catch(Exception e){taskFailure=e.Message;}}
            if(!taskPassed)failed++;
            Program.Write(Path.Combine(directory,s.Id,"result.json"),new{s.Id,passed=taskPassed,plannerAccepted=result.Preview is not null,taskFailure,result,elapsedMilliseconds=clock.Elapsed.TotalMilliseconds});
            if(result.Preview is { } p)Program.Write(Path.Combine(directory,s.Id,"accepted.json"),new{p.RequestJson,p.ResponseJson,p.CompilationJson,p.Sha256,p.CapabilityJson,p.ContextIdentity,p.Revision});
            rows.Add(new{s.Id,passed=taskPassed,plannerAccepted=result.Preview is not null,taskFailure,result.ModelCalls,result.FailureCode,result.Message});Console.WriteLine(s.Id+": "+result.Status+" task="+taskPassed+" "+(taskFailure??result.Message));
        }
        Program.Write(Path.Combine(directory,"result.json"),new{passed=failed==0,total=scenes.Length,failed,rows,formalSuccessRateClaimed=false});return failed==0?0:1;
    }
    internal static void RequireTaskGeometry(string root,T4BCase scene,ConstructionProgram program)
    {
        var actual=ConstructionPreflight.EvaluateGeometry(program);var expected=ConstructionPreflight.EvaluateGeometry(scene.Expected.Response.Program!);
        using var frozen=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json")));
        var index=Array.FindIndex(T4BCases.All(root),s=>s.Id==scene.Id);var oracle=frozen.RootElement.GetProperty("development")[index].GetProperty("oracle");
        var volume=oracle.GetProperty("volumeMm3").GetDouble()+Math.PI*oracle.GetProperty("volumePiCoefficientMm3").GetDouble();
        Program.Check(Math.Abs(actual.VolumeMm3-volume)<.0001 && actual.MinimumLocalMm==expected.MinimumLocalMm && actual.MaximumLocalMm==expected.MaximumLocalMm && actual.Frame==expected.Frame,
            "Task oracle differs: actual volume="+actual.VolumeMm3+", expected="+volume+"; bounds/frame must also match. Structural preflight is not task success.");
    }
}
