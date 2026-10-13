using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using Environment=System.Environment;

internal static partial class Program
{
    private static string Ledger=>Development?Path.Combine(Root,"artifacts/m20-preparation/T5/native-budget.json"):Path.Combine(Output,"native-budget.json");
    private static int Native(FreezeRecord freeze,Slot slot)
    {
        var mode=Environment.GetEnvironmentVariable("CAD_HARNESS_M20_PHASE")??"native";
        Check(slot.MaximumOpens>0||slot.MaximumParts>0,"This slot reuses immutable evidence; never execute it again");
        var path=Path.Combine(Output,"slots",slot.Id);var events=0;var time=Stopwatch.StartNew();
        using var telemetry=ExecutionTelemetry.Start();
        void Journal(string phase,object data)=>Write(Path.Combine(path,"steps",mode,(++events).ToString("D4")+"-"+phase+".json"),new{utc=DateTime.UtcNow,controller=Environment.ProcessId,elapsedMilliseconds=time.Elapsed.TotalMilliseconds,phase,data,telemetry=telemetry.Snapshot()});
        if(slot.Mode=="real"&&Read<JsonElement>(Path.Combine(path,"plan.json")).GetProperty("status").GetString()!="planned")
        {Write(Path.Combine(path,"native-result.json"),new{status="REFUSED",attempted=false,reason="No executable live plan",completeTask=false,cleanup=true,contained=true});return 0;}
        var replay=slot.Mode=="real"?VerifyReplay(freeze,slot):((string Raw,ReplayBinding Binding,JsonElement Plan)?)null;
        using var budget=new NativeTestBudget(Ledger,Development?"T5":"M20",Development?int.MaxValue:ExecutionApproval!.ApprovedParts);
        using var connection=SolidWorksConnection.Connect(false);var app=connection.Application;
        var before=Documents(app);var resource=NativeResourceGuard.Inspect(app,budget);
        Check(resource.Responding&&resource.GdiCount<7000&&budget.Snapshot.OpenTestOwnedTitles.Count==0,"Native resource/ownership refusal");
        var initial=JsonSerializer.Serialize(budget.Snapshot,Json);Journal("before",new{resource,budget=budget.Snapshot,documents=before});
        var reservations=Development?null:new FormalReservations(Path.Combine(Output,"reservations"),ExecutionApproval!,slot.Id,mode);
        var lifecycle=new Lifecycle(budget,Journal,reservations,Development?int.MaxValue:ExecutionApproval!.ApprovedOpens);GenericPartSession? session=null;
        bool native=false,editable=false,edited=false,reopened=false,saved=false,cleanup=false;
        string? failure=null,package=null,nativeSha=null,manifestSha=null;long? revision=null;double? value=null;var integrity=true;
        var runtimeSpan=ExecutionTelemetry.Measure(ExecutionPhase.Runtime);
        try
        {
            if((freeze.Formal is not null||IncrementalRun)&&slot.Mode=="g2-cold")
            {
                var prior=Stage(Path.Combine(Output,"slots",slot.Pair!,"native-result.json"));
                var root=Path.Combine(Output,"packages/G2");
                Check(prior is {Status:"PASS",Cleanup:true}&&prior.Controller!=Environment.ProcessId&&prior.Package==root&&
                    ManagedRevisionStore.Hash(Path.Combine(root,"current.json"))==prior.ManifestSha&&ManagedRevisionStore.Hash(Path.Combine(root,"working/CADHarnessManagedPart.SLDPRT"))==prior.NativeSha,
                    "G2 independent cold/controller authority association failed");
            }
            if(slot.Mode.StartsWith("external",StringComparison.Ordinal)||slot.Mode=="readonly")
            {
                External(freeze,slot,connection,lifecycle,Journal);
                native=true;editable=slot.Mode!="readonly";edited=slot.Mode=="external";reopened=slot.Mode=="external-cold";saved=slot.Mode!="readonly";
                if(slot.Mode!="readonly")package=freeze.CompletedExternal?.Task==slot.Task?freeze.CompletedExternal.Package:Path.Combine(Output,"packages",slot.Task);
            }
            else if(slot.Mode.StartsWith("g2",StringComparison.Ordinal))
            {G2(slot,connection,lifecycle,budget,Journal);native=editable=saved=true;edited=slot.Mode=="g2";reopened=slot.Mode=="g2-cold";package=Path.Combine(Output,"packages/G2");}
            else
            {
                var task=freeze.Tasks.Single(t=>t.Id==slot.Task);var continuation=freeze.Continuation?.Task==task.Id?freeze.Continuation:null;
                package=continuation?.Package??Path.Combine(path,"package");
                if(mode=="native"&&continuation is not null)
                {
                    Check(Identity(continuation.InitialNative.Path)==continuation.InitialNative&&Identity(continuation.InitialPointer.Path)==continuation.InitialPointer&&Identity(continuation.PriorCreationEvidence.Path)==continuation.PriorCreationEvidence,"Continuation initial authority/evidence drift");
                    var opened=GenericPartSession.Open(connection,package,continuation.InitialNative.Path,lifecycle:lifecycle);Journal("continuation-admission",new{continuation,opened.Status,opened.FailureCode,opened.Message,priorSlotRemainsFailed=true});
                    Check(opened.Session is not null,"Cannot restore preserved R1");session=opened.Session!;
                }
                else if(mode=="native")session=GenericPartSession.Create(connection,package,Environment.GetEnvironmentVariable("CAD_HARNESS_PART_TEMPLATE")!,lifecycle:lifecycle);
                else
                {
                    var prior=Read<JsonElement>(Path.Combine(path,mode=="cold"?"native-result.json":"cold-result.json"));
                    Check(prior.GetProperty("status").GetString()=="PASS"&&prior.GetProperty("controller").GetInt32()!=Environment.ProcessId,"No independent successful prior controller");
                    Check(ManagedRevisionStore.Hash(Path.Combine(package,"working/CADHarnessManagedPart.SLDPRT"))==prior.GetProperty("nativeSha").GetString(),"Saved native drift before reopen");
                    var opened=GenericPartSession.Open(connection,package,Path.Combine(package,"working/CADHarnessManagedPart.SLDPRT"),lifecycle:lifecycle);
                    Journal("public-restore",new{opened.Status,opened.FailureCode,opened.Message});Check(opened.Session is not null,"Public restoration failed");session=opened.Session!;reopened=true;
                }
                session.Context.SketchEvidenceSink=t=>Journal("sketch-api",t);session.Context.FeatureEvidenceSink=t=>Journal("feature-api",t);session.SaveEvidenceSink=t=>Journal("save-api",t);
                if(mode=="native"&&continuation is null)
                {
                    var runtime=SolidWorksConstructionPlanningRuntime.ForManagedSession(session.Context);
                    var plan=replay is { } live?ReplayAccepted(task.Request,live.Raw,live.Binding,live.Plan,runtime):
                        new ConstructionPlanner(runtime,new ReplaySource(task.DraftJson)).PlanAsync(task.Request).GetAwaiter().GetResult();
                    Journal("public-preview",plan);Check(plan.Preview is not null,"Native-context task admission failed: "+plan.Message);
                    var built=session.ExecuteConstructionPlan(plan.Preview!.Approve(plan.Preview.Sha256));Journal("public-create",built);
                    Check(built.Succeeded&&built.StateCommitted&&built.Revision==1,"Creation/publish failed: "+built.Message);
                    ValidatePhase(session,task,0,Journal);native=true;
                }
                else {ValidatePhase(session,task,mode=="native"?0:mode=="cold"?1:2,Journal);native=true;}
                var parameter=ResolveEditTarget(session.CurrentGeneric);
                editable=parameter.Qualified;Check(editable,"Accepted edit role not qualified");
                if(mode!="read")
                {
                    var beforeState=session.CurrentGeneric.State;var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);var bytes=ManagedRevisionStore.Hash(session.Store.WorkingPath);
                    var setters=0;var checkpoints=new System.Collections.Generic.HashSet<string>();
                    session.Context.FeatureEvidenceSink=t=>{Journal("feature-api",t);if(t.Stage is "radial-driver-applied" or "native-setter-return")
                    {setters++;Check(File.Exists(session.Store.RecoveryPath),"No durable checkpoint before Setter");checkpoints.Add(ManagedRevisionStore.Hash(session.Store.RecoveryPath));Journal("checkpoint-observation",Read<JsonElement>(session.Store.RecoveryPath));}};
                    var invalid=new GenericScalarCommand(parameter.Owner,parameter.Key,parameter.Value+100,parameter.Value+.5,beforeState.Revision,parameter.SemanticId);
                    var refused=session.Edit(invalid);Journal("preflight-negative",new{invalid,refused,setters,checkpoints});
                    Check(!refused.Succeeded&&!refused.MutationStarted&&setters==0&&checkpoints.Count==0&&!File.Exists(session.Store.RecoveryPath)&&pointer==ManagedRevisionStore.Hash(session.Store.PointerPath)&&bytes==ManagedRevisionStore.Hash(session.Store.WorkingPath),"Invalid old value reached native mutation");
                    var command=new GenericScalarCommand(parameter.Owner,parameter.Key,parameter.Value,mode=="native"?task.NewValue:task.ColdValue,beforeState.Revision,parameter.SemanticId);
                    Journal("edit-request",command);var result=session.Edit(command);Journal("public-edit",new{result,setters,checkpoints});
                    Check(result.Succeeded&&result.StateCommitted&&result.Revision==beforeState.Revision+1&&checkpoints.Count==1&&setters>0,"Follow-up publish failed: "+result.Message);
                    ValidatePhase(session,task,mode=="native"?1:2,Journal);edited=true;
                }
                var current=session.CurrentGeneric;revision=current.State.Revision;value=ResolveEditTarget(current).Value;
                Check(session.Store.ReadCurrent().Manifest.Revision==revision&&!File.Exists(session.Store.RecoveryPath),"Authority or recovery marker differs");saved=true;
                Journal("durable-package",new{current,manifest=session.Store.ReadCurrent().Manifest});
            }
            if(package is not null)
            {
                nativeSha=ManagedRevisionStore.Hash(Path.Combine(package,"working/CADHarnessManagedPart.SLDPRT"));
                manifestSha=ManagedRevisionStore.Hash(Path.Combine(package,"current.json"));
                if(revision is null)using(var store=new ManagedRevisionStore(package,slot.Mode.StartsWith("external",StringComparison.Ordinal)?new ExternalAuditOnly():new AuditOnly()))revision=store.ReadCurrent().Manifest.Revision;
            }
        }
        catch(Exception e){failure=e.ToString();Journal("failure",new{failure});}
        finally
        {
            session?.Dispose();var after=Documents(app);var resources=NativeResourceGuard.Inspect(app,budget);
            cleanup=lifecycle.Owned==0&&budget.Snapshot.OpenTestOwnedTitles.Count==0&&JsonSerializer.Serialize(before,Json)==JsonSerializer.Serialize(after,Json)&&resources.Responding&&resources.GdiCount<7000&&(session is null||session.OriginalActiveRestored);
            try{VerifyFreeze(freeze);}catch(Exception e){integrity=false;failure=(failure??"")+e;}
            if(package is not null&&File.Exists(Path.Combine(package,"recovery.json"))){integrity=false;failure=(failure??"")+" Unresolved durable recovery marker";}
            if(failure?.Contains("FORMAL_BUDGET_EXHAUSTED",StringComparison.Ordinal)==true)integrity=false;
            if(!cleanup)failure=(failure??"")+" Cleanup/resource failed";
            runtimeSpan.Dispose();
            Write(Path.Combine(path,mode+"-result.json"),new{slot,phase=mode,status=failure is null?"PASS":"FAIL",controller=Environment.ProcessId,package,revision,value,nativeSha,manifestSha,
                nativePassed=native,editablePassed=editable,followUpPassed=edited,reopenPassed=reopened,saved,cleanup,failure,integrity,contained=cleanup&&integrity,
                initialBudget=JsonSerializer.Deserialize<JsonElement>(initial),budget=budget.Snapshot,elapsedMilliseconds=time.Elapsed.TotalMilliseconds,resources,before,after,telemetry=telemetry.Snapshot()});
        }
        Console.WriteLine(slot.Id+" "+mode+": "+(failure is null?"PASS":"FAIL "+failure));
        // A contained failure is still FAIL in evidence; only unsafe cleanup stops the schedule.
        return cleanup&&integrity?0:1;
    }
    private static object[] Documents(ISldWorks app)=>NativeEditOracle.Items<IModelDoc2>(app.GetDocuments()).Select(d=>(object)new{title=d.GetTitle(),path=d.GetPathName(),dirty=d.GetSaveFlag()}).OrderBy(d=>JsonSerializer.Serialize(d)).ToArray();
    private static GenericNativeParameter ResolveEditTarget(GenericRevisionPackage package)
    {
        var history=ContractJson.Read<PlanningHistory>(package.Provenance!.Payload,_=>{});
        var accepted=history.Steps.First(s=>s.Plan is not null).Plan!;
        var target=accepted.Compilation!.EditTargets.Single();
        var parameter=package.State.Parameters.Single(p=>p.SemanticId==target.ParameterSemanticId);
        Check(parameter.Owner==target.Owner&&parameter.Key==target.Parameter,"Accepted role/owner/accessor mismatch");return parameter;
    }
    private static void ValidatePhase(GenericPartSession session,TaskDefinition task,int phase,Action<string,object> log)
    {
        var expected=Tasks.Geometry(task,phase);var current=session.CurrentGeneric;
        var parameter=ResolveEditTarget(current);
        Check(current.State.Revision==expected.Revision&&Math.Abs(parameter.Value-expected.Value)<1e-8,"Phase-specific revision/value differs");
        foreach(var feature in current.State.Features)
        {
            var resolved=session.Context.Document.Extension.GetObjectByPersistReference3(Convert.FromBase64String(feature.Reference.Base64),out var error) as IFeature;
            Check(error==0&&resolved is not null&&resolved.GetTypeName2()==feature.NativeType,"Oracle persistent feature unhealthy");
        }
        Oracle(session.Context.Document,task,expected,log);
        log("phase-state",new{expected,current.State.Revision,parameter,features=current.State.Features.Count,dependencies=current.State.Dependencies});
    }
    private sealed class Lifecycle(NativeTestBudget budget,Action<string,object> journal,FormalReservations? reservations=null,int maximumOpens=int.MaxValue):IGenericDocumentLifecycle
    {
        internal int Owned;
        internal void ReserveLegacyCreation()=>reservations?.Reserve("create");
        public void BeforeCreate(){reservations?.Reserve("create");budget.ReserveCreation();journal("reserve-create",budget.Snapshot);}
        public void Created(string title){budget.RegisterCreated(title);Owned++;journal("created",new{title});}
        public void Renamed(string a,string b){budget.RenameOwned(a,b);journal("renamed",new{a,b});}
        public void BeforeOpen(string path){reservations?.Reserve("open");budget.ReserveOpen(maximumOpens);journal("reserve-open",new{path,budget=budget.Snapshot});}
        public void Opened(string path,string title){budget.RegisterOpened(title);Owned++;journal("opened",new{path,title});}
        public void Closed(string path,string title){budget.RegisterClosed(title);Owned--;journal("closed",new{path,title});}
    }
}
