using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

internal static partial class Program
{
    private static int Native(FreezeRecord freeze,Slot slot)
    {
        var path=Path.Combine(Output,"slots",slot.Id);var elapsed=Stopwatch.StartNew();var events=0;
        void Journal(string phase,object data)=>Write(Path.Combine(path,"steps",(++events).ToString("D4")+"-"+phase+".json"),new{utc=DateTime.UtcNow,controller=Environment.ProcessId,elapsedMilliseconds=elapsed.Elapsed.TotalMilliseconds,phase,data});
        var mode=Environment.GetEnvironmentVariable("CAD_HARNESS_M20_PHASE")??"native";
        if(slot.Mode=="real"&&Read<JsonElement>(Path.Combine(path,"plan.json")).GetProperty("status").GetString()!="planned")
        {Write(Path.Combine(path,"native-result.json"),new{attempted=false,reason="No executable live plan; no Part created",completeTask=false});return 0;}
        if(slot.Task=="C3")
        {Write(Path.Combine(path,"native-result.json"),new{attempted=false,reason=freeze.Tasks.Single(t=>t.Id=="C3").CapabilityGap,completeTask=false});return 0;}
        using var budget=new NativeTestBudget(Path.Combine(Root,"artifacts/milestone20/native-budget.json"),"M20",int.MaxValue);
        using var connection=SolidWorksConnection.Connect(false);var app=connection.Application;
        NativeResourceGuard.TestTitlePrefix="CADHarnessM20Test_";
        var before=NativeResourceGuard.DocumentTitles(app);
        var beforePaths=NativeEditOracle.Items<IModelDoc2>(app.GetDocuments()).Select(d=>new{title=d.GetTitle(),path=d.GetPathName(),dirty=d.GetSaveFlag()}).ToArray();
        var resource=NativeResourceGuard.Inspect(app,budget);
        Check(resource.Responding&&resource.GdiCount<7000&&budget.Snapshot.OpenTestOwnedTitles.Count==0,"Native resources or ownership refused");
        var initial=JsonSerializer.Serialize(budget.Snapshot,Json);Journal("resources-before",new{resource,budget=budget.Snapshot,documents=beforePaths,appVersion=app.RevisionNumber()});
        var lifecycle=new Lifecycle(budget,Journal);bool native=false,editable=false,edited=false,reopened=false,saved=false,cleanup=true;string? failure=null;
        TestPartScope? scope=null;
        try
        {
            if(slot.Mode.StartsWith("external",StringComparison.Ordinal)||slot.Mode=="readonly")
            {External(freeze,slot,connection,lifecycle,Journal);native=true;editable=slot.Mode!="readonly";edited=slot.Mode=="external";reopened=slot.Mode=="external-cold";saved=slot.Mode!="readonly";}
            else if(slot.Mode.StartsWith("g2",StringComparison.Ordinal))
            {G2(slot,connection,lifecycle,budget,Journal);native=editable=saved=true;edited=slot.Mode=="g2";reopened=slot.Mode=="g2-cold";}
            else
            {
                var task=freeze.Tasks.Single(t=>t.Id==slot.Task);scope=new(app,budget);
                if(mode=="cold")
                {
                    var savedRecord=Read<FrozenFile>(Path.Combine(path,"saved-file.json"));var part=savedRecord.Path;
                    Check(Identity(part)==savedRecord,"Saved Part changed before cold controller");
                    var c=scope.OpenOwnedCopy(part,int.MaxValue);
                    var snapshot=Read<GenericConstructionState>(Path.Combine(path,"saved-state.json"));
                    Oracle(c.Document,task,Read<JsonElement>(Path.Combine(path,"native-result.json")).GetProperty("followUpPassed").GetBoolean(),Journal);
                    foreach(var f in snapshot.Features)
                    {
                        var actual=c.Document.Extension.GetObjectByPersistReference3(Convert.FromBase64String(f.Reference.Base64),out var error) as IFeature;
                        Check(error==0&&actual is not null&&actual.GetTypeName2()==f.NativeType,"Cold feature identity unhealthy");
                    }
                    native=reopened=true;
                    try{_ = c.CaptureGenericConstructionState();editable=true;}catch(StateException e){failure=e.Code+": "+e.Message;Journal("generic-cold-edit-refused",new{e.Code,e.Message,nativeGeometryPassed=true,noStateRestorationClaim=true});}
                    Journal("cold-native-readback",new{native,editable,reopened,snapshot.Revision,priorController=Read<JsonElement>(Path.Combine(path,"controller.json")).GetProperty("controller").GetInt32(),controller=Environment.ProcessId});
                }
                else
                {
                    Write(Path.Combine(path,"controller.json"),new{controller=Environment.ProcessId});
                    var c=scope.Create(connection,connection.ResolvePartTemplate(Environment.GetEnvironmentVariable("CAD_HARNESS_PART_TEMPLATE")));
                    var part=Path.Combine(path,"CADHarnessM20_"+slot.Id+".SLDPRT");int errors=0,warnings=0;
                    Check(!beforePaths.Any(d=>Path.GetFileName(d.path).Equals(Path.GetFileName(part),StringComparison.OrdinalIgnoreCase)||d.title.Equals(Path.GetFileName(part),StringComparison.OrdinalIgnoreCase)),"Test filename collides with an already-open document");
                    Check(c.Document.Extension.SaveAs(part,0,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,null!,ref errors,ref warnings)&&errors==0&&warnings==0,"Owned blank save failed");scope.RefreshOwnedTitle();
                    c.FeatureEvidenceSink=t=>Journal("feature-api",t);c.SketchEvidenceSink=t=>Journal("sketch-api",t);
                    MutationResult result;
                    if(slot.Mode=="real")
                    {
                        var live=Read<JsonElement>(Path.Combine(path,"plan.json"));
                        var preview=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForManagedSession(c),new ReplaySource(live.GetProperty("preview").GetProperty("responseJson").GetString()!)).PlanAsync(task.Request).GetAwaiter().GetResult();
                        Journal("live-plan-native-admission",new{source="recorded_response_revalidation_not_new_inference",preview.Status,preview.FailureCode});
                        Check(preview.Preview is not null,"Live plan failed native-context preflight");
                        result=c.ExecuteConstructionPlan(preview.Preview!.Approve(preview.Preview.Sha256));
                    }
                    else result=c.ConstructGeneric(task.Program!);
                    Journal("construction",result);Check(result.Succeeded&&result.StateCommitted&&result.RebuildSucceeded,"Native construction failed: "+result.FailureCode+" "+result.Message);
                    Oracle(c.Document,task,false,Journal);native=true;
                    var state=c.CaptureGenericConstructionState();Journal("state-before-edit",state);
                    editable=state.Parameters.Any(p=>p.Owner==task.EditTarget&&p.Key==task.EditParameter&&p.Qualified);
                    var edit=c.EditGenericScalar(new(task.EditTarget,task.EditParameter,task.OldValue,task.NewValue,state.Revision));Journal("required-follow-up",edit);
                    edited=edit.Succeeded&&edit.StateCommitted&&edit.Revision==state.Revision+1;
                    if(edited)Oracle(c.Document,task,true,Journal);
                    else failure="Required follow-up edit failed: "+edit.FailureCode+" "+edit.Message;
                    var final=c.CaptureGenericConstructionState();
                    Check(edited||GenericConstructionStatePolicy.MatchesNativeSnapshot(state,final),"Rejected edit changed native/state");
                    errors=warnings=0;var saveResult=c.Document.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent,ref errors,ref warnings);
                    Journal("native-save-api",new{saveResult,errors,warnings,dirty=c.Document.GetSaveFlag(),nativePath=c.Document.GetPathName()});
                    Check(saveResult&&errors==0&&warnings==0&&!c.Document.GetSaveFlag(),"Native save failed");saved=true;
                    Write(Path.Combine(path,"saved-state.json"),final);Write(Path.Combine(path,"saved-file.json"),Identity(part));Journal("save",new{saved,final.Revision,path=part,sha256=ManagedRevisionStore.Hash(part)});
                }
            }
        }
        catch(Exception e){failure=e.ToString();Journal("failure",new{failure});}
        finally
        {
            scope?.Cleanup();cleanup=scope is null||scope.CleanupError is null&&scope.Closed&&scope.OriginalActiveRestored;
            var after=NativeResourceGuard.DocumentTitles(app);var resources=NativeResourceGuard.Inspect(app,budget);
            var afterPaths=NativeEditOracle.Items<IModelDoc2>(app.GetDocuments()).Select(d=>new{title=d.GetTitle(),path=d.GetPathName(),dirty=d.GetSaveFlag()}).ToArray();
            cleanup &= lifecycle.Owned==0&&budget.Snapshot.OpenTestOwnedTitles.Count==0&&before.OrderBy(s=>s).SequenceEqual(after.OrderBy(s=>s))&&resources.Responding&&resources.GdiCount<7000;
            cleanup &= JsonSerializer.Serialize(beforePaths.OrderBy(d=>d.path),Json)==JsonSerializer.Serialize(afterPaths.OrderBy(d=>d.path),Json);
            if(!cleanup)failure=(failure??"")+"; native cleanup/resource guard failed";
            VerifyFreeze(freeze);
            var filename=mode=="cold"?"cold-result.json":"native-result.json";
            Write(Path.Combine(path,filename),new{slot,controller=Environment.ProcessId,nativePassed=native,editablePassed=editable,followUpPassed=edited,reopenPassed=reopened,
                durableReopenEditable=mode=="cold"&&editable,saved,completeTask=native&&editable&&edited&&reopened&&cleanup,failure,cleanup,
                initialBudget=JsonSerializer.Deserialize<JsonElement>(initial),budget=budget.Snapshot,elapsedMilliseconds=elapsed.Elapsed.TotalMilliseconds,resources,before=beforePaths,after=afterPaths,cleanupError=scope?.CleanupError,originalHashesPreserved=true});
        }
        Console.WriteLine(slot.Id+" "+mode+": native="+native+" editable="+editable+" edit="+edited+" reopen="+reopened+" cleanup="+cleanup+" "+failure);
        return cleanup?0:1;
    }
    private sealed class Lifecycle(NativeTestBudget budget,Action<string,object> journal):IManagedDocumentLifecycle
    {
        internal int Owned;
        public void BeforeOpen(string path){budget.ReserveOpen(int.MaxValue);journal("reserve-open",new{path,budget=budget.Snapshot});}
        public void Opened(string path,string title){budget.RegisterOpened(title);Owned++;journal("opened",new{path,title});}
        public void Closed(string path,string title){budget.RegisterClosed(title);Owned--;journal("closed",new{path,title});}
    }
    private static void Oracle(IModelDoc2 doc,TaskDefinition task,bool after,Action<string,object> journal)
    {
        var bodies=NativeEditOracle.Items<IBody2>(((IPartDoc)doc).GetBodies2(0,false)).ToArray();Check(bodies.Length==1,"Oracle single body required");
        var body=bodies[0];var volume=((double[])body.GetMassProperties(1))[3]*1e9;var expected=after?task.AfterVolume:task.BeforeVolume;
        var bounds=new double[6];for(var i=0;i<6;i++){var k=i%3;var sign=i<3?-1:1;Check(body.GetExtremePoint(k==0?sign:0,k==1?sign:0,k==2?sign:0,out var x,out var y,out var z),"Oracle extreme point missing");bounds[i]=new[]{x,y,z}[k]*1000;}
        var cylinders=new List<double[]>();bool surfaces=true;
        foreach(var face in NativeEditOracle.Items<IFace2>(body.GetFaces()))
        {
            var surface=(ISurface)face.GetSurface();surfaces &= surface.IsPlane()||surface.IsCylinder();
            if(!surface.IsCylinder())continue;
            var p=(double[])surface.CylinderParams;
            var circular=NativeEditOracle.Items<IEdge>(face.GetEdges()).Select(e=>(ICurve)e.GetCurve()).Where(c=>c.IsCircle()).Select(c=>(double[])c.CircleParams).ToArray();
            if(task.Family=="C4")
            {
                Check(Math.Abs(Math.Abs(p[5])-1)<1e-8&&circular.Length==2,"Oracle slot axis/boundaries differ");
                var levels=circular.Select(c=>c[2]*1000).OrderBy(z=>z).ToArray();cylinders.Add(new[]{p[6]*1000,p[0]*1000,p[1]*1000,levels[0],levels[1]});
            }
            else
            {
                Check(Math.Abs(Math.Abs(p[3])-1)<1e-8&&circular.Length==2,"Oracle coaxial axial boundaries differ");
                Check(Math.Abs(p[1]*1000)<.01&&Math.Abs(p[2]*1000)<.01,"Oracle noncoaxial cylinder");
                var levels=circular.Select(c=>c[0]*1000).OrderBy(z=>z).ToArray();cylinders.Add(new[]{p[6]*1000,levels[0],levels[1]});
            }
        }
        var cylinderExpected=task.Cylinders.Select(c=>c.ToArray()).ToArray();
        if(after&&task.Family=="C4")
        {
            var index=int.Parse(task.EditTarget[^1..]);cylinderExpected[index*2][3]=task.Bounds[5]-task.NewValue;cylinderExpected[index*2+1][3]=task.Bounds[5]-task.NewValue;
        }
        var matches=cylinderExpected.All(e=>cylinders.Count(a=>a.Length==e.Length&&a.Zip(e).All(p=>Math.Abs(p.First-p.Second)<.01))==1);
        var passed=surfaces&&matches&&cylinders.Count==cylinderExpected.Length&&Math.Abs(volume-expected)<=Math.Max(.01,expected*1e-5)&&bounds.Zip(task.Bounds).All(p=>Math.Abs(p.First-p.Second)<.01);
        journal("independent-oracle",new{passed,volume,expected,difference=volume-expected,bounds,expectedBounds=task.Bounds,cylinders,cylinderExpected,surfaces,matches,kind="Independent official API mass/extreme/cylinder/ring read; no production geometric formula"});
        Check(passed,"Frozen independent native geometry failed");
    }
}
