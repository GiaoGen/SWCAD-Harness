using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

internal static class T4CTests
{
    private static SemanticRadialDefinitionV2 R(T4CCase s)=>s.Draft.Nodes[0].Profile!.Radial!;
    private static T4CCase Radial(T4CCase s,SemanticRadialDefinitionV2 r)=>s with {Draft=s.Draft with {Nodes=new[]{s.Draft.Nodes[0] with {Profile=s.Draft.Nodes[0].Profile! with {Radial=r}}}}};
    private static T4CCase Given(T4CCase s,string key,double value)=>s with {Request=s.Request with {Input=s.Request.Input with {Requirements=s.Request.Input.Requirements.Select(r=>r.SemanticKey==key?r with {Value=value}:r).ToArray()}}};
    private static ConstructionPlanRequest Task(T4CCase s,SemanticRadialDefinitionV2 r)=>s.Request with {GeometryRequirements=new(1,new[]{s.Request.GeometryRequirements!.Radial[0] with {BaseOuter=r.BaseOuter,BaseInner=r.BaseInner,ExternalRecesses=r.ExternalRecesses}})};
    private static SemanticCompilationResult Compile(T4CCase s)=>SemanticConstructionV2.Compile(s.Request,s.Draft);
    private static GenericConstructionState Empty(ConstructionProgram p)=>new(new(Guid.NewGuid(),Guid.NewGuid(),"Default","D:\\owned.SLDPRT"),0,p with {Operations=Array.Empty<ConstructionOperation>()},Array.Empty<GenericNativeFeature>(),Array.Empty<GenericNativeSketch>(),Array.Empty<GenericNativeParameter>(),Array.Empty<DependencyEdge>(),null);
    private static ConstructionPlanningResult Plan(T4CCase s)=>new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),new RecordingSource(ContractJson.Write(s.Draft,SemanticConstructionV2.ValidateDraft))).PlanAsync(s.Request).GetAwaiter().GetResult();
    internal static int Pure(string root,string run)
    {
        var directory=Path.Combine(root,"artifacts/m20-preparation/T4C",run);var rows=new List<object>();var failed=0;
        void Test(string name,Action a){try{a();rows.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception e){failed++;rows.Add(new{name,passed=false,error=e.ToString()});Console.WriteLine("FAIL "+name+": "+e.Message);}}
        void Bad(Action a,string? prefix=null){try{a();}catch(ContractException e){Program.Check(prefix is null || e.Message.StartsWith(prefix,StringComparison.Ordinal),"Wrong rejection boundary: "+e.Message);return;}throw new Exception("Unsafe v2 plan accepted");}
        void Volume(T4CCase s,double coefficient){var c=Compile(s);var actual=ConstructionPreflight.EvaluateGeometry(c.Response.Program!).VolumeMm3;Program.Check(Math.Abs(actual-Math.PI*coefficient)<.0001,"Independent analytical volume differs");}
        var scenes=T4CCases.All(root);var shaft=scenes[0];var radial=R(shaft);var recess=radial.ExternalRecesses[0];
        foreach(var s in scenes)Test("complete_"+s.Id,()=>
        {
            var c=Compile(s);RequireTaskGeometry(root,s,c.Response.Program!);ProviderSchemaCompatibility.RequireCompatible(SemanticConstructionV2.ResponseSchema(s.Request,SolidWorksConstructionPlanningRuntime.ForNewModel()));
            var result=Plan(s);Program.Check(result.Preview is not null,result.Message);var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();
            result=new ConstructionPlanner(runtime,new RecordingSource(ContractJson.Write(s.Draft,SemanticConstructionV2.ValidateDraft))).PlanAsync(s.Request).GetAwaiter().GetResult();
            var accepted=result.Preview!.Approve(result.Preview.Sha256).ConsumeAccepted(runtime);var empty=Empty(c.Response.Program!);var history=GenericPlanningProvenance.AppendPlan(null,empty,accepted);
            var state=empty with {Program=c.Response.Program!,Revision=1};GenericPlanningProvenance.Verify(history,state);var role=c.Evidence.EditTargets.Single();
            var edit=new GenericScalarCommand(role.Owner,role.Parameter,role.Before,role.After,1,role.ParameterSemanticId);history=GenericPlanningProvenance.AppendEdit(history,state,edit)!;
            Program.Write(Path.Combine(directory,s.Id+"-plan.json"),new{s.Request,s.Draft,c,result,provenanceCharacters=history.Payload.Length,history});
            var v1=SemanticConstructionCompiler.Compile(s.Previous.Request,s.Previous.Draft);Program.Check(ContractJson.Write(v1.Response,ConstructionRequirements.ValidateResponse)==ContractJson.Write(c.Response,ConstructionRequirements.ValidateResponse),"New lowering changed equivalent IR/bindings");
        });
        Test("exact_split_and_source_map",()=>{var c=Compile(shaft);var rows=c.Evidence.SourceMap!.Profiles.Single().Outer;
            Program.Check(rows.Select(x=>(x.Start,x.End,x.Radius,x.RecessId)).SequenceEqual(new (string,string,string?,string?)[]{("zero","head_length","head_radius",null),("head_length","groove_start","middle_radius",null),("groove_start","groove_end","groove_radius",recess.Id),("groove_end","middle_end","middle_radius",null),("middle_end","overall","tip_radius",null)}),"Independent expected split differs");Volume(shaft,11704);
        });
        Test("full_revolve_section_plane_is_not_task_geometry",()=>{
            var s=shaft with {Draft=shaft.Draft with {Nodes=shaft.Draft.Nodes.Select(n=>n with {Placement=n.Placement! with {Plane=SemanticPlane.Xz}}).ToArray()}};
            RequireTaskGeometry(root,s,Compile(s).Response.Program!);
        });
        Test("schema_projects_requested_axes_without_new_qualification",()=>{
            using var schema=JsonDocument.Parse(SemanticConstructionV2.ResponseSchema(shaft.Request,SolidWorksConstructionPlanningRuntime.ForNewModel()));
            var variants=schema.RootElement.GetProperty("properties").GetProperty("nodes").GetProperty("items").GetProperty("anyOf");
            Program.Check(variants.GetArrayLength()==1,"Radial-only material admitted extra operations");var placements=variants[0].GetProperty("properties").GetProperty("placement").GetProperty("anyOf");
            Program.Check(placements.EnumerateArray().All(p=>p.GetProperty("properties").GetProperty("plane").GetProperty("enum")[0].GetString() is "xy" or "xz"),"World-X task advertised world-Y axis");
        });
        Test("required_height_world_direction_is_checked",()=>{
            var s=scenes[2];s=s with {Draft=s.Draft with {Nodes=s.Draft.Nodes.Select(n=>n.Kind==ConstructionKind.CreateAdditiveBoss?n with {Direction=Direction.Reverse}:n).ToArray()}};Bad(()=>Compile(s));
        });
        Test("different_dimensions_and_recess_location",()=>{
            var s=shaft;foreach(var row in new[]{("head_diameter",34d),("middle_diameter",26d),("tip_diameter",20d),("head_length",18d),("middle_length",44d),("tip_length",16d),("groove_start",30d),("groove_width",6d),("groove_depth",1.5d)})s=Given(s,row.Item1,row.Item2);
            s=s with {Request=s.Request with {EditabilityRequirements=s.Request.EditabilityRequirements!.Select(e=>e with {TestValue=14}).ToArray()}};Volume(s,14017.5);
        });
        Test("multiple_recesses_order_independent",()=>{
            var second=new ExternalRadialRecess("another_recess","middle_radius","second_start","second_end","second_floor","second_depth");
            var s=shaft with {Request=shaft.Request with {Input=shaft.Request.Input with {Requirements=shaft.Request.Input.Requirements.Concat(new[]{Cases.Given("second_start",45),Cases.Given("second_end",48),Cases.Given("second_depth",1)}).ToArray()},
                Rules=shaft.Request.Rules.Append(new RequirementRule("second_floor","difference_dimensions",null){Inputs=new[]{"middle_radius","second_depth"}}).ToArray(),DesignDependencies=shaft.Request.DesignDependencies!.Append(new("middle_radius","second_floor","second_depth")).ToArray()}};
            var r=radial with {ExternalRecesses=new[]{recess,second},Dependencies=s.Request.DesignDependencies!};s=Radial(s,r) with {Request=Task(s,r)};Volume(s,11635);
            var reversed=Radial(s,r with {ExternalRecesses=new[]{second,recess}});var a=Compile(s);var b=Compile(reversed);Program.Check(ContractJson.Write(a.Response,ConstructionRequirements.ValidateResponse)==ContractJson.Write(b.Response,ConstructionRequirements.ValidateResponse) && JsonSerializer.Serialize(a.Evidence.SourceMap)==JsonSerializer.Serialize(b.Evidence.SourceMap),"Modifier order changed shape/source map");
        });
        Test("recess_on_other_base_driver",()=>{
            var s=Given(shaft,"groove_start",5);s=s with {Request=s.Request with {Rules=s.Request.Rules.Select(r=>r.SemanticKey=="groove_radius"?r with {Inputs=new[]{"head_radius","groove_depth"}}:r).ToArray(),DesignDependencies=new[]{new DeclaredRadialDependency("head_radius","groove_radius","groove_depth")}}};
            var r=radial with {ExternalRecesses=new[]{recess with {HostRadius="head_radius"}},Dependencies=s.Request.DesignDependencies!};s=Radial(s,r) with {Request=Task(s,r)};Volume(s,11656);
        });
        Test("no_recess_and_positive_bore_wall",()=>{Volume(Simple(false,false),13500);Volume(Simple(true,true),11776);});
        Test("symbols_and_sources_are_not_fixture_names",()=>{
            string K(string x)=>"other_"+x;SemanticRadialRange Range(SemanticRadialRange r)=>new(K(r.Start),K(r.End),r.Radius is null?null:K(r.Radius));
            DeclaredRadialDependency Dep(DeclaredRadialDependency d)=>new(K(d.SourceKey),K(d.TargetKey),K(d.OffsetKey));ExternalRadialRecess Recess(ExternalRadialRecess r)=>new(K(r.Id),K(r.HostRadius),K(r.Start),K(r.End),K(r.FloorRadius),K(r.Depth));
            var r=new SemanticRadialDefinitionV2(radial.BaseOuter.Select(Range).ToArray(),radial.BaseInner.Select(Range).ToArray(),radial.ExternalRecesses.Select(Recess).ToArray(),radial.Dependencies.Select(Dep).ToArray());
            var role=shaft.Request.GeometryRequirements!.Radial.Single();var q=shaft.Request with {Input=shaft.Request.Input with {Requirements=shaft.Request.Input.Requirements.Select(v=>v with {SemanticKey=K(v.SemanticKey)}).ToArray()},
                Rules=shaft.Request.Rules.Select(v=>v with {SemanticKey=K(v.SemanticKey),InputKey=v.InputKey is null?null:K(v.InputKey),Inputs=v.Inputs?.Select(K).ToArray()}).ToArray(),
                DesignDependencies=shaft.Request.DesignDependencies!.Select(Dep).ToArray(),EditabilityRequirements=shaft.Request.EditabilityRequirements!.Select(e=>e with {SourceKey=K(e.SourceKey),PreservedSources=e.PreservedSources.Select(K).ToArray()}).ToArray(),
                GeometryRequirements=new(1,new[]{role with {BaseOuter=r.BaseOuter,BaseInner=r.BaseInner,ExternalRecesses=r.ExternalRecesses,Layout=role.Layout with {OriginX=K("zero"),OriginY=K("zero"),OriginZ=K("zero")}}})};
            var s=Radial(shaft,r) with {Request=q};s=s with {Draft=s.Draft with {Nodes=s.Draft.Nodes.Select(n=>n with {Id="different_rotor",Placement=n.Placement! with {OriginX=K("zero"),OriginY=K("zero"),OriginZ=K("zero"),Angle=K("zero_angle")},Angle=K("turn")}).ToArray()}};Volume(s,11704);
        });
        Test("missing_external_recess_before_preview",()=>Bad(()=>Compile(Radial(shaft,radial with {ExternalRecesses=Array.Empty<ExternalRadialRecess>()})),"STRUCTURE_EXTERNAL_RECESS"));
        Test("inner_cavity_not_external_recess",()=>Bad(()=>Compile(Radial(shaft,radial with {BaseInner=new[]{new SemanticRadialRange("zero","groove_start",null),new("groove_start","groove_end","groove_radius"),new("groove_end","overall",null)},ExternalRecesses=Array.Empty<ExternalRadialRecess>()})),"STRUCTURE_INNER"));
        foreach(var bad in new[]{recess with {HostRadius="head_radius"},recess with {FloorRadius="tip_radius"},recess with {Depth="groove_width"},recess with {Start="head_length"}})
            Test("wrong_obligation_"+bad.HostRadius+"_"+bad.FloorRadius+"_"+bad.Depth+"_"+bad.Start,()=>Bad(()=>Compile(Radial(shaft,radial with {ExternalRecesses=new[]{bad}})),"STRUCTURE_EXTERNAL_RECESS"));
        Test("wrong_world_axis_before_preview",()=>Bad(()=>Compile(shaft with {Draft=shaft.Draft with {Nodes=shaft.Draft.Nodes.Select(n=>n with {Placement=n.Placement! with {Plane=SemanticPlane.Yz}}).ToArray()}}),"STRUCTURE_LAYOUT"));
        Test("program_admission_does_not_trust_its_map",()=>{var c=Compile(shaft);var p=c.Response.Program!;var o=p.Operations[0];var changed=o.Sketch! with {Points=o.Sketch!.Points.Select(v=>v.Y.Millimeters==10?v with {Y=new(9)}:v).ToArray()};Bad(()=>RadialTaskAdmission.VerifyProgram(shaft.Request,p with {Operations=p.Operations.Select(x=>x==o?x with {Sketch=changed}:x).ToArray()}),"STRUCTURE_BOUNDARY");});
        var facts=ConstructionRequirements.Normalize(shaft.Request);
        Test("host_not_found",()=>Bad(()=>SemanticConstructionV2.Expand(radial with {ExternalRecesses=new[]{recess with {HostRadius="tip_radius"}}},facts,shaft.Request.DesignDependencies!)));
        Test("cross_step_recess",()=>Bad(()=>SemanticConstructionV2.Expand(radial with {ExternalRecesses=new[]{recess with {Start="head_length",End="groove_end"}}},facts,shaft.Request.DesignDependencies!)));
        Test("overlap_modifiers",()=>Bad(()=>SemanticConstructionV2.Expand(radial with {ExternalRecesses=new[]{recess,recess with {Id="duplicate_span"}}},facts,shaft.Request.DesignDependencies!)));
        Test("duplicate_modifier_identity",()=>Bad(()=>SemanticConstructionV2.Expand(radial with {ExternalRecesses=new[]{recess,recess}},facts,shaft.Request.DesignDependencies!)));
        Test("undeclared_floor_relation",()=>Bad(()=>SemanticConstructionV2.Expand(radial with {Dependencies=Array.Empty<DeclaredRadialDependency>()},facts,shaft.Request.DesignDependencies!)));
        Test("base_gap_overlap_not_repaired",()=>Bad(()=>SemanticConstructionV2.Expand(radial with {BaseOuter=radial.BaseOuter.Append(new SemanticRadialRange("groove_start","groove_end","groove_radius")).ToArray()},facts,shaft.Request.DesignDependencies!)));
        Test("unknown_source",()=>Bad(()=>SemanticConstructionV2.Expand(radial with {ExternalRecesses=new[]{recess with {End="missing"}}},facts,shaft.Request.DesignDependencies!)));
        Test("wrong_unit",()=>Bad(()=>Compile(shaft with {Request=shaft.Request with {Input=shaft.Request.Input with {Requirements=shaft.Request.Input.Requirements.Select(r=>r.SemanticKey=="groove_start"?r with {Unit=ScalarUnit.Degree}:r).ToArray()}}})));
        Test("derived_primary_stays_readonly",()=>Bad(()=>Compile(shaft with {Request=shaft.Request with {EditabilityRequirements=shaft.Request.EditabilityRequirements!.Select(r=>r with {SourceKey="groove_radius"}).ToArray()}})));
        Test("required_preserved_source_cannot_move",()=>Bad(()=>Compile(shaft with {Request=shaft.Request with {EditabilityRequirements=shaft.Request.EditabilityRequirements!.Select(r=>r with {PreservedSources=r.PreservedSources.Append("groove_radius").ToArray()}).ToArray()}})));
        Test("solid_vs_bore_program_requirement",()=>{var s=Simple(false,false);var c=Compile(s);var sketch=c.Response.Program!.Operations[0];var changed=sketch.Sketch! with {Points=sketch.Sketch!.Points.Select(v=>v.Y.Millimeters==0?v with {Y=new(2)}:v).ToArray()};Bad(()=>RadialTaskAdmission.VerifyProgram(s.Request,c.Response.Program with {Operations=c.Response.Program.Operations.Select(o=>o==sketch?o with {Sketch=changed}:o).ToArray()}),"STRUCTURE_BOUNDARY");});
        Test("zero_and_negative_wall",()=>{foreach(var depth in new[]{10d,15d,20d}){var s=Simple(true,true);s=Given(s,"recess_depth",depth);Bad(()=>Compile(s));}});
        Test("same_numeric_value_is_not_source_identity",()=>{var s=shaft with {Request=shaft.Request with {Input=shaft.Request.Input with {Requirements=shaft.Request.Input.Requirements.Append(Cases.Given("equal_but_independent",35)).ToArray()}}};Bad(()=>Compile(Radial(s,radial with {ExternalRecesses=new[]{recess with {Start="equal_but_independent"}}})),"STRUCTURE_EXTERNAL_RECESS");});
        Test("version_dispatch",()=>{Bad(()=>SemanticConstructionV2.Compile(shaft.Request with {SemanticDraftVersion=1},shaft.Draft));Bad(()=>Compile(shaft with {Draft=shaft.Draft with {Version=1}}));Bad(()=>SemanticConstructionCompiler.Compile(shaft.Request,shaft.Previous.Draft));});
        var good=Compile(shaft);
        Test("compiler_map_modifier_ir_drift",()=>{
            Bad(()=>SemanticConstructionCompiler.VerifyCompilation(shaft.Request,good.Response,good.Evidence with {Version="1"}));
            var map=good.Evidence.SourceMap!;Bad(()=>SemanticConstructionCompiler.VerifyCompilation(shaft.Request,good.Response,good.Evidence with {SourceMap=map with {RuleVersion="unknown"}}));
            var profile=map.Profiles.Single();Bad(()=>SemanticConstructionCompiler.VerifyCompilation(shaft.Request,good.Response,good.Evidence with {SourceMap=map with {Profiles=new[]{profile with {Outer=profile.Outer.Select((r,i)=>i==2?r with {RecessId=null,BaseIndex=0}:r).ToArray()}}}}));
            Bad(()=>SemanticConstructionCompiler.VerifyCompilation(shaft.Request,good.Response,good.Evidence with {DraftV2=Radial(shaft,radial with {ExternalRecesses=new[]{recess with {FloorRadius="head_radius"}}}).Draft}));
            Bad(()=>SemanticConstructionCompiler.VerifyCompilation(shaft.Request,good.Response with {Bindings=good.Response.Bindings.Skip(1).ToArray()},good.Evidence));
        });
        Test("modifier_bound_and_defaults_not_laundered",()=>{
            Bad(()=>SemanticConstructionV2.Expand(radial with {ExternalRecesses=Enumerable.Range(0,13).Select(i=>recess with {Id="item_"+i}).ToArray()},facts,shaft.Request.DesignDependencies!));
            var s=Simple(false,true);var rules=s.Request.Rules.Select(r=>r.SemanticKey=="floor"?new RequirementRule("floor","cosmetic_convex_radius",null):r).ToArray();
            s=Given(s,"recess_depth",14) with {Request=Given(s,"recess_depth",14).Request with {Rules=rules}};Bad(()=>Compile(s));
        });
        Test("approval_capability_source_and_layout_drift",()=>{
            var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();var preview=new ConstructionPlanner(runtime,new RecordingSource(ContractJson.Write(shaft.Draft,SemanticConstructionV2.ValidateDraft))).PlanAsync(shaft.Request).GetAwaiter().GetResult().Preview!;
            Bad(()=>preview.Approve(new string('0',64)));var approval=preview.Approve(preview.Sha256);_=approval.ConsumeAccepted(runtime);Bad(()=>approval.ConsumeAccepted(runtime));
            var changed=shaft.Request with {GeometryRequirements=new(1,new[]{shaft.Request.GeometryRequirements!.Radial.Single() with {Layout=shaft.Request.GeometryRequirements.Radial.Single().Layout with {Axis=PrincipalAxis.Y,Source=RequirementSource.Given,RuleId=null,RuleVersion=null}}})};
            Bad(()=>SemanticConstructionV2.VerifyCompilation(changed,good.Response,good.Evidence));
        });
        Test("v1_real_provenance_replay_remains_valid",()=>{
            foreach(var s in scenes){var c=SemanticConstructionCompiler.Compile(s.Previous.Request,s.Previous.Draft);SemanticConstructionCompiler.VerifyCompilation(s.Previous.Request,c.Response,c.Evidence);Program.Check(c.Evidence.DraftV2 is null && c.Evidence.SourceMap is null,"v1 silently upgraded");}
        });
        Test("old_three_c1_responses_keep_original_meaning",()=>{
            var rows=new List<object>();foreach(var version in new[]{"live_v1","live_v2","live_v3"}){
                var path=Path.Combine(root,"artifacts/m20-preparation/T4B",version,"shaft/call-01-response.json");using var doc=JsonDocument.Parse(File.ReadAllText(path));var raw=doc.RootElement.GetProperty("response").GetProperty("Json").GetString()!;
                var result=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),new RecordingSource(raw)).PlanAsync(shaft.Previous.Request).GetAwaiter().GetResult();var taskPassed=false;
                if(result.Preview is not null){try{T4BTests.RequireTaskGeometry(root,shaft.Previous,result.Preview.Read().Program!);taskPassed=true;}catch{}}
                Program.Check(!taskPassed,"Old C1 failure was promoted");rows.Add(new{version,path,sha256=ManagedRevisionStore.Hash(path),result.Status,result.FailureCode,result.Message,taskPassed});
            }Program.Write(Path.Combine(directory,"old-c1-unchanged.json"),rows);
        });
        Test("actual_v1_wrong_cavity_rejected_by_v2_structure",()=>{
            using var raw=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"artifacts/m20-preparation/T4B/live_v2/shaft/call-01-response.json")));var d=ContractJson.Read<SemanticConstructionDraft>(raw.RootElement.GetProperty("response").GetProperty("Json").GetString()!,SemanticConstructionCompiler.ValidateDraft);var wrong=d.Nodes.Single().Profile!.Radial!;
            var s=Radial(shaft,new(wrong.Outer,wrong.Inner,Array.Empty<ExternalRadialRecess>(),wrong.Dependencies));var result=Plan(s);Program.Check(result.Preview is null && result.Message.StartsWith("STRUCTURE_INNER",StringComparison.Ordinal),"Only version check refused, not cavity semantics");Program.Write(Path.Combine(directory,"wrong-cavity-production-refusal.json"),new{result,setters=0,checkpoints=0,nativeOpens=0});
        });
        Test("v2_payload_bound_not_increased",()=>{
            var s=scenes[2] with {Request=scenes[2].Request with {Input=scenes[2].Request.Input with {Intent=new string('"',4096)}}};var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();var p=new ConstructionPlanner(runtime,new RecordingSource(ContractJson.Write(s.Draft,SemanticConstructionV2.ValidateDraft))).PlanAsync(s.Request).GetAwaiter().GetResult().Preview!;
            try{GenericPlanningProvenance.AppendPlan(null,Empty(p.Read().Program!),p.Approve(p.Sha256).ConsumeAccepted(runtime));throw new Exception("Oversized v2 history accepted");}catch(ContractException e){Program.Check(e.Message=="Planning history exceeds the existing provenance bound.","Wrong payload boundary: "+e.Message);}
        });
        Program.Write(Path.Combine(directory,"result.json"),new{passed=failed==0,total=rows.Count,failed,rows,providerCalls=0,nativeOpens=0});return failed==0?0:1;
    }
    private static T4CCase Simple(bool bore,bool recess)
    {
        var input=new RequirementRecord("0.3","generic_radial",0,"Explicit finite radial stock/bore/recess development input.",new[]{Cases.Given("diameter",30),Cases.Given("span",60)}.Concat(bore?new[]{Cases.Given("bore_diameter",10)}:Array.Empty<Requirement>()).Concat(recess?new[]{Cases.Given("start",20),Cases.Given("end",24),Cases.Given("recess_depth",2)}:Array.Empty<Requirement>()).ToArray());
        var rules=new List<RequirementRule>{new("zero","layout_zero",null),new("zero_angle","layout_zero_angle",null),new("turn","full_revolution",null),new("radius","half_dimension","diameter")};if(bore)rules.Add(new("bore_radius","half_dimension","bore_diameter"));if(recess)rules.Add(new("floor","difference_dimensions",null){Inputs=new[]{"radius","recess_depth"}});
        var dependencies=recess?new[]{new DeclaredRadialDependency("radius","floor","recess_depth")}:Array.Empty<DeclaredRadialDependency>();var modifiers=recess?new[]{new ExternalRadialRecess("finite_recess","radius","start","end","floor","recess_depth")}:Array.Empty<ExternalRadialRecess>();
        var r=new SemanticRadialDefinitionV2(new[]{new SemanticRadialRange("zero","span","radius")},new[]{new SemanticRadialRange("zero","span",bore?"bore_radius":null)},modifiers,dependencies);
        var q=new ConstructionPlanRequest(input,rules){SemanticDraftVersion=2,DesignDependencies=dependencies,GeometryRequirements=new(1,new[]{new RadialGeometryObligation("radial_body",r.BaseOuter,r.BaseInner,r.ExternalRecesses,new(PrincipalAxis.X,RequirementSource.Given,null,null,"zero","zero","zero"))})};
        var d=new SemanticConstructionDraftV2(2,PlanOutcome.Planned,new[]{new SemanticConstructionNodeV2("arbitrary_rotor",ConstructionKind.CreateRevolvedBoss,new(SemanticPlane.Xy,"zero","zero","zero","zero_angle"),new(SemanticProfileKind.RadialSection,null,null,null,r),null,EndCondition.FullRevolution,Direction.Forward,null,"turn",null)},Array.Empty<string>(),null);
        return new("generic",q,d,null!);
    }
    internal static int Live(string root,string run)
    {
        var directory=Path.Combine(root,"artifacts/m20-preparation/T4C",run);Program.Check(!Directory.Exists(directory),"Never overwrite live schedule");var scenes=T4CCases.All(root);
        Program.Write(Path.Combine(directory,"freeze.json"),new{baseline="fcddf2d",stage="T4C development, NOT M20",maximumCalls=4,noRetries=true,temperature=0,timeoutSeconds=180,maxOutputTokens=16384,compiler=SemanticConstructionV2.CompilerVersion,expansion=SemanticConstructionV2.ExpansionRule,
            tasks=scenes.Select(s=>new{s.Id,s.Request,schema=SemanticConstructionV2.ResponseSchema(s.Request,SolidWorksConstructionPlanningRuntime.ForNewModel())}),source=Program.SourceHashes(root),alias="deepseek-chat",endpoint=DeepSeekPlanSource.Endpoint});
        using var client=OpenAiPlanSource.CreateClient();var key=Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY")??throw new Exception("Missing configured credential");var rows=new List<object>();var failed=0;
        foreach(var s in scenes)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();var evidence=new EvidenceSource(new DeepSeekPlanSource(client,"deepseek-chat",key,16384,180),Path.Combine(directory,s.Id));
            var result=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),evidence).PlanAsync(s.Request).GetAwaiter().GetResult();var taskGeometryPassed=false;var editabilityPassed=false;string? taskFailure=null;
            if(result.Preview is { } preview){try{RequireTaskGeometry(root,s,preview.Read().Program!);taskGeometryPassed=true;var c=ContractJson.Read<ConstructionCompilation>(preview.CompilationJson!,_=>{});SemanticConstructionCompiler.VerifyCompilation(s.Request,preview.Read(),c);editabilityPassed=c.EditTargets.Count==s.Request.EditabilityRequirements!.Count;}catch(Exception e){taskFailure=e.Message;}}
            var passed=result.Preview is not null && taskGeometryPassed && editabilityPassed;if(!passed)failed++;
            Program.Write(Path.Combine(directory,s.Id,"result.json"),new{s.Id,passed,plannerAccepted=result.Preview is not null,taskGeometryPassed,editabilityPassed,taskFailure,result,elapsedMilliseconds=timer.Elapsed.TotalMilliseconds});
            if(result.Preview is { } p)Program.Write(Path.Combine(directory,s.Id,"accepted.json"),new{p.RequestJson,p.ResponseJson,p.CompilationJson,p.Sha256,p.CapabilityJson,p.ContextIdentity,p.Revision});
            rows.Add(new{s.Id,passed,plannerAccepted=result.Preview is not null,taskGeometryPassed,editabilityPassed,taskFailure,result.ModelCalls,result.FailureCode,result.Message});Console.WriteLine(s.Id+": "+result.Status+" task="+passed+" "+(taskFailure??result.Message));
        }
        Program.Write(Path.Combine(directory,"result.json"),new{passed=failed==0,total=4,failed,rows,formalSuccessRateClaimed=false});return failed==0?0:1;
    }
    internal static void RequireTaskGeometry(string root,T4CCase scene,ConstructionProgram program)
    {
        if(scene.Request.GeometryRequirements?.Radial.Count is not >0){T4BTests.RequireTaskGeometry(root,scene.Previous,program);return;}
        var shape=ConstructionPreflight.EvaluateGeometry(program);var expected=ConstructionPreflight.EvaluateGeometry(scene.Previous.Expected.Response.Program!);
        using var frozen=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json")));
        var index=Array.FindIndex(T4CCases.All(root),s=>s.Id==scene.Id);var oracle=frozen.RootElement.GetProperty("development")[index].GetProperty("oracle");var volume=oracle.GetProperty("volumeMm3").GetDouble()+Math.PI*oracle.GetProperty("volumePiCoefficientMm3").GetDouble();
        Program.Check(Math.Abs(shape.VolumeMm3-volume)<.0001 && shape.Frame.XAxis==expected.Frame.XAxis && shape.Frame.OriginMm==expected.Frame.OriginMm && shape.MinimumLocalMm==expected.MinimumLocalMm && shape.MaximumLocalMm==expected.MaximumLocalMm,"Independent radial task volume/world axis/envelope differs");
        RadialTaskAdmission.VerifyProgram(scene.Request,program);
    }
    internal static int Mapping(string root,string run,string live)
    {
        var directory=Path.Combine(root,"artifacts/m20-preparation/T4C",run);var rows=new List<object>();
        foreach(var s in T4CCases.All(root))
        {
            var path=Path.Combine(root,"artifacts/m20-preparation/T4C",live,s.Id,"call-01-response.json");using var raw=JsonDocument.Parse(File.ReadAllText(path));
            var draft=ContractJson.Read<SemanticConstructionDraftV2>(raw.RootElement.GetProperty("response").GetProperty("Json").GetString()!,SemanticConstructionV2.ValidateDraft);var result=SemanticConstructionV2.Compile(s.Request,draft);RequireTaskGeometry(root,s,result.Response.Program!);
            var nodes=draft.Nodes.Select(n=>new SemanticConstructionNode(n.Id,n.Kind,n.Placement,n.Profile is null?null:new SemanticProfile(n.Profile.Kind,n.Profile.Rectangle,n.Profile.Circle,n.Profile.Capsule,n.Profile.Radial is null?null:new(
                result.Evidence.SourceMap!.Profiles.Single(p=>p.Owner==n.Id).Outer.Select(r=>new SemanticRadialRange(r.Start,r.End,r.Radius)).ToArray(),n.Profile.Radial.BaseInner,n.Profile.Radial.Dependencies)),n.Host,n.EndCondition,n.Direction,n.Depth,n.Angle,n.Edge)).ToArray();
            var previous=s.Request with {SemanticDraftVersion=1,GeometryRequirements=null};
            var legacy=SemanticConstructionCompiler.Compile(previous,new(1,draft.Outcome,nodes,draft.Questions,draft.Reason));
            Program.Check(ContractJson.Write(legacy.Response,ConstructionRequirements.ValidateResponse)==ContractJson.Write(result.Response,ConstructionRequirements.ValidateResponse),"Actual v2 IR/bindings differ from existing strict lowering: "+s.Id);
            var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();var planned=new ConstructionPlanner(runtime,new RecordingSource(ContractJson.Write(draft,SemanticConstructionV2.ValidateDraft))).PlanAsync(s.Request).GetAwaiter().GetResult();var accepted=planned.Preview!.Approve(planned.Preview.Sha256).ConsumeAccepted(runtime);
            var empty=Empty(result.Response.Program!);var history=GenericPlanningProvenance.AppendPlan(null,empty,accepted);var before=empty with {Program=result.Response.Program!,Revision=1};GenericPlanningProvenance.Verify(history,before);
            var target=result.Evidence.EditTargets.Single();var edit=new GenericScalarCommand(target.Owner,target.Parameter,target.Before,target.After,1,target.ParameterSemanticId);history=GenericPlanningProvenance.AppendEdit(history,before,edit)!;
            var features=result.Response.Program!.Operations.Where(o=>o.Profile is not null || o.EdgeTreatment is not null).Select(o=>new{o.SemanticId,o.Kind,o.EndCondition,o.Direction,qualifiedOperation=GenericFeatureCapabilities.IsOperationQualified(o.Kind)}).ToArray();
            Program.Check(features.All(f=>f.qualifiedOperation) && history.Payload.Length<=65536,"Actual plan needs an unqualified native operation or oversized provenance");
            rows.Add(new{s.Id,path,rawSha256=ManagedRevisionStore.Hash(path),sameExistingStrictIrAndBindings=true,actualLiveResponseNotCandidate=true,features,target,provenanceCharacters=history.Payload.Length,noNativeSubtypeOrAccessorAdded=true});
            Program.Write(Path.Combine(directory,s.Id+"-actual-plan-mapping.json"),new{s.Request,result,history,edit,features});
        }
        Program.Write(Path.Combine(directory,"result.json"),new{passed=true,rows,providerCalls=0,nativeOpens=0,nativeExecutionClaimed=false,source=Program.SourceHashes(root)});return 0;
    }
}
