using System;
using System.IO;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

internal static partial class T3Tests
{
    internal static int Run(string root,string run,string model,string phase,string? packageRun)
    {
        Program.Check(ContractValidation.Id(run) && (packageRun is null || ContractValidation.Id(packageRun)) && model is "cross" or "full" &&
            phase is "audit" or "candidate_bind_probe" or "candidate_fillet" or "candidate_create" or "candidate_edit" or "candidate_cold_edit" or "candidate_fault" or "create" or "edit" or "cold_edit" or "fault" or "read" or "recover","Unknown T3 slot");
        var directory=Path.Combine(root,"artifacts/m20-preparation/T3",run,model,phase); Directory.CreateDirectory(directory);
        var timer=System.Diagnostics.Stopwatch.StartNew(); var step=0;
        void Log(string name,object data)=>Program.Write(Path.Combine(directory,(++step).ToString("D3")+"-"+name+".json"),new { utc=DateTime.UtcNow,controller=System.Environment.ProcessId,elapsedMilliseconds=timer.Elapsed.TotalMilliseconds,data });
        using var budget=new NativeTestBudget(Path.Combine(root,"artifacts/m20-preparation/T3/native-budget.json"),"T3",int.MaxValue);
        using var connection=SolidWorksConnection.Connect(false);
        var titles=NativeResourceGuard.DocumentTitles(connection.Application); var resource=NativeResourceGuard.Inspect(connection.Application,budget);
        Log("before",new { resource,budget=budget.Snapshot,documents=titles });
        Program.Check(resource.Responding && resource.GdiCount<7000 && budget.Snapshot.OpenTestOwnedTitles.Count==0,"Safety/ownership refusal");
        Log("code-version",new { sources=Directory.GetFiles(Path.Combine(root,"src"),"*.cs",SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root,"tests/CadHarness.FeatureConstruction.Tests"),"*.cs",SearchOption.AllDirectories))
            .Where(p=>!p.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar)).Select(p=>new { path=p,sha256=ManagedRevisionStore.Hash(p) }).ToArray(),
            binaries=Directory.GetFiles(AppContext.BaseDirectory,"*.dll").Select(p=>new { path=p,sha256=ManagedRevisionStore.Hash(p) }).ToArray() });
        if(phase=="audit")
        {
            using var registry=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"artifacts/fixture-factory/audits/m14-preflight/fixture-registry.json")));
            var originals=registry.RootElement.GetProperty("fixtures").EnumerateArray().Select(f=> {var n=f.GetProperty("nativePart");var p=n.GetProperty("path").GetString()!;return new { path=p,sha256=ManagedRevisionStore.Hash(p),expected=n.GetProperty("sha256").GetString() };}).ToArray();
            var manual=ManagedRevisionStore.Hash(Path.Combine(root,"M14测试用夹具.SLDPRT"));
            using var stop=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"artifacts/milestone20/acceptance_v3/stop-audit-20261011T145238147.json")));
            var drift=stop.RootElement.GetProperty("preservedEvidence").EnumerateArray().Where(e=>!File.Exists(e.GetProperty("path").GetString()) || ManagedRevisionStore.Hash(e.GetProperty("path").GetString()!)!=e.GetProperty("sha256").GetString()).ToArray();
            Log("preservation",new { originals,manual,historicalRecords=stop.RootElement.GetProperty("preservedEvidence").GetArrayLength(),driftCount=drift.Length });
            Program.Check(originals.All(o=>o.sha256==o.expected) && manual=="60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b" && drift.Length==0,"Protected history/source drift");
            if(packageRun is not null)
            {
                foreach(var item in new[]{("candidate_v1","cross"),(packageRun,"full"),("candidate_v1","full")}.Distinct())
                {
                    var packagePath=Path.Combine(root,"artifacts/m20-preparation/T3",item.Item1,item.Item2,"package");using var store=new ManagedRevisionStore(packagePath,new MetadataReader());
                    var inspection=store.Inspect(store.WorkingPath);Log("authority-"+item.Item1+"-"+item.Item2,new { inspection.Status,inspection.FailureCode,inspection.RequiresNativeRecovery,revision=inspection.Revision?.State.Revision,
                        marker=File.Exists(store.RecoveryPath),native=ManagedRevisionStore.Hash(store.WorkingPath),leaseReleased=true });
                    if(item.Item1=="candidate_v1" && item.Item2=="full") Program.Check(inspection.Revision is null && inspection.Status==CadHarness.State.V03.ReopenStatus.Quarantined && File.Exists(Path.Combine(root,"artifacts/m20-preparation/T3/candidate_v1/full/quarantine-disposition.json")),"Initial failed package must remain isolated");
                    else Program.Check(inspection.FailureCode is null && !inspection.RequiresNativeRecovery && !File.Exists(store.RecoveryPath),"Unresolved active authority/recovery");
                }
            }
            Log("result",new { passed=true,budget=budget.Snapshot,documents=titles }); return 0;
        }
        var package=Path.Combine(root,"artifacts/m20-preparation/T3",packageRun??run,model,"package");
        var candidate=phase.StartsWith("candidate_",StringComparison.Ordinal); var action=candidate?phase[10..]:phase; GenericPartSession? session=null;
        try
        {
            var lifecycle=new Lifecycle(budget,Log);
            if(action=="create") session=GenericPartSession.Create(connection,package,@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot",lifecycle:lifecycle);
            else { var open=GenericPartSession.Open(connection,package,Path.Combine(package,"working/CADHarnessManagedPart.SLDPRT"),lifecycle:lifecycle);Log("open",new { open.Status,open.FailureCode,open.Message });Program.Check(open.Session is not null,"Restore: "+open.Message);session=open.Session!; }
            var setters=0;var checkpoints=0;var saves=0;
            session.SaveEvidenceSink=t=> {Log("save-api",t);if(t.Stage=="before-save3")saves++;};
            session.Context.SketchEvidenceSink=t=>Log("sketch-api",t);
            session.Context.FeatureEvidenceSink=t=> {Log("feature-api",t);if(t.Stage=="native-setter-return") { setters++; if(action=="fault" && setters==1) throw new StateException("T3_HEIGHT_FAULT","Actual native height Setter succeeded; downstream fillet verification interrupted."); }};
            if(action is "bind_probe" or "fillet")
            {
                var state=session.Context.CaptureGenericConstructionState();var operation=T3Cases.Full().Operations.Last();var requested=state.Program with { Operations=state.Program.Operations.Append(operation).ToArray() };Log("request",requested);
                if(action=="bind_probe") {session.Context.ProbeConvexEdge(operation,requested);Log("result",new { passed=true,mutation=false });return 0;}
                var result=session.ConstructCandidate(requested);Log("construction",new { result,session.Status });Program.Check(result.Succeeded,"Fillet continuation failed: "+result.Message+" / "+result.RollbackFailureMessage);
            }
            if(action=="create")
            {
                var request=model=="full"?T3Cases.Full():T3Cases.Cross();Log("request",request);
                var result=candidate?session.ConstructCandidate(request):session.Construct(request);Log("construction",new { result,session.Status });
                Program.Check(result.Succeeded && result.StateCommitted && result.Revision==1,"Creation failed: "+result.Message+" / "+result.RollbackFailureMessage);
            }
            else if(action is "edit" or "cold_edit" or "fault")
            {
                var before=session.Context.CaptureGenericConstructionState(); var parameter=before.Parameters.Single(p=>p.Owner=="upright" && p.Key==ParameterKey.AdditiveBossDepth);
                var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);var native=ManagedRevisionStore.Hash(session.Store.WorkingPath);
                var prepare=session.Context.GenericDurableCheckpoint!; session.Context.GenericDurableCheckpoint=()=>{ checkpoints++;Log("checkpoint",new { checkpoints });prepare();};
                if(action=="edit")
                {
                    var invalid=new GenericScalarCommand(parameter.Owner,parameter.Key,parameter.Value,10,before.Revision,parameter.SemanticId);
                    var refused=candidate?session.EditCandidate(invalid):session.Edit(invalid); Log("preflight-negative",new { invalid,refused,setters,checkpoints,saves });
                    Program.Check(!refused.Succeeded && !refused.MutationStarted && setters==0 && checkpoints==0 && saves==0,"Illegal material reached mutation");
                }
                var request=new GenericScalarCommand(parameter.Owner,parameter.Key,parameter.Value,parameter.Value+(action=="cold_edit"?-2:action=="fault"?1:4),before.Revision,parameter.SemanticId);Log("request",request);
                var result=candidate?session.EditCandidate(request):session.Edit(request);Log("edit",new { result,session.Status,setters,checkpoints,saves,before.Revision,beforePointer=pointer,afterPointer=ManagedRevisionStore.Hash(session.Store.PointerPath) });
                if(action=="fault")
                {
                    Program.Check(!result.StateCommitted && result.RollbackAttempted && result.RollbackSucceeded && setters==2 && checkpoints==1 && saves==0 &&
                        pointer==ManagedRevisionStore.Hash(session.Store.PointerPath) && native==ManagedRevisionStore.Hash(session.Store.WorkingPath),"Height/fillet failure did not rollback atomically");
                    Oracle(session.Context.Document,parameter.Value,before.Program.Operations.Any(o=>o.EdgeTreatment is not null),Log);
                    Log("result",new { passed=true,rollback=true,before.Revision,forwardSetters=1,restoreSetters=1,session.Status,recoveryMarker=File.Exists(session.Store.RecoveryPath) });return 0;
                }
                Program.Check(result.Succeeded && result.StateCommitted && result.Revision==before.Revision+1 && checkpoints==1 && setters==1 && saves==1,"Edit failed: "+result.Message+" / "+result.RollbackFailureMessage);
                Program.Check(result.Changes.ChangedParameters.SequenceEqual(new[]{parameter.SemanticId}) &&
                    (!before.Program.Operations.Any(o=>o.EdgeTreatment is not null) || result.Dirty!.Features.Contains("corner")),"Dependent fillet or exact changed parameter missing from transaction graph");
            }
            var current=session.Context.CaptureGenericConstructionState();var height=current.Parameters.Single(p=>p.Owner=="upright").Value;
            Oracle(session.Context.Document,height,current.Program.Operations.Any(o=>o.EdgeTreatment is not null),Log);
            Program.Check(session.Store.ReadCurrent().State.Revision==current.Revision && !File.Exists(session.Store.RecoveryPath),"Authority/recovery differs");
            Log("result",new { passed=true,current.Revision,height,format=session.CurrentGeneric.FormatVersion,native=ManagedRevisionStore.Hash(session.Store.WorkingPath),candidate,setters,checkpoints,saves });
        }
        catch(Exception e){ Log("failure",new { error=e.ToString() });throw; }
        finally
        {
            session?.Dispose();var after=NativeResourceGuard.Inspect(connection.Application,budget);Log("cleanup",new { session?.OriginalActiveRestored,budget=budget.Snapshot,resource=after });
            Program.Check(budget.Snapshot.OpenTestOwnedTitles.Count==0 && after.Responding && after.GdiCount<7000 && titles.OrderBy(t=>t).SequenceEqual(NativeResourceGuard.DocumentTitles(connection.Application).OrderBy(t=>t)),"Resource cleanup failed");
        }
        return 0;
    }
    private sealed class Lifecycle(NativeTestBudget budget,Action<string,object> log):IGenericDocumentLifecycle
    {
        public void BeforeCreate(){budget.ReserveCreation();log("reserve-create",budget.Snapshot);}
        public void Created(string title){budget.RegisterCreated(title);log("created",new { title });}
        public void Renamed(string before,string after){budget.RenameOwned(before,after);log("renamed",new { before,after });}
        public void BeforeOpen(string path){budget.ReserveOpen(int.MaxValue);log("reserve-open",new { path,budget=budget.Snapshot });}
        public void Opened(string path,string title){budget.RegisterOpened(title);log("opened",new { path,title });}
        public void Closed(string path,string title){budget.RegisterClosed(title);log("closed",new { path,title });}
    }
    private sealed class MetadataReader:IGenericRevisionNative
    {
        public GenericRevisionPackage CurrentGeneric=>throw new NotSupportedException("Audit does not mutate");
        public void SaveNative()=>throw new NotSupportedException("Audit never saves");
        public void VerifySavedGeneric(GenericRevisionPackage package)=>throw new NotSupportedException("Metadata is not native verification");
    }
    private static T[] Items<T>(object? value)=>value is Array a?a.Cast<object>().OfType<T>().ToArray():Array.Empty<T>();
    private static void Oracle(IModelDoc2 document,double height,bool fillet,Action<string,object> log)
    {
        // Frozen test geometry and independent native measurement; no production
        // material proof, selector, binding or expected state is used here.
        var body=Items<IBody2>(((IPartDoc)document).GetBodies2(0,false)).Single();var faces=Items<IFace2>(body.GetFaces());
        var cylinders=faces.Where(f=>((ISurface)f.GetSurface()).IsCylinder()).Select(f=>new { p=(double[])((ISurface)f.GetSurface()).CylinderParams,uv=(double[])f.GetUVBounds(),area=f.GetArea()*1e6,
            rings=Items<IEdge>(f.GetEdges()).Select(e=>(ICurve)e.GetCurve()).Where(c=>c.IsCircle()).Select(c=>(double[])c.CircleParams).ToArray() }).ToArray();
        var bores=new[] { new[]{30d,-10,0,2,0,8},new[]{30d,10,0,2,0,8},new[]{0d,0,30,0,0,8} };
        var holeMatch=bores.All(e=>cylinders.Count(c=>Math.Abs(c.p[6]*1000-3)<1e-6 && Math.Abs(Math.Abs(c.p[3+(int)e[3]])-1)<1e-8 &&
            Enumerable.Range(0,3).Where(i=>i!=(int)e[3]).All(i=>Math.Abs(c.p[i]*1000-e[i])<1e-6) && Math.Abs(c.uv[1]-c.uv[0]-2*Math.PI)<1e-8 && c.rings.Length==2 &&
            Math.Abs(c.rings.Min(r=>r[(int)e[3]])*1000-e[4])<1e-6 && Math.Abs(c.rings.Max(r=>r[(int)e[3]])*1000-e[5])<1e-6)==1);
        var cornerMatch=!fillet || cylinders.Count(c=>Math.Abs(c.p[6]*1000-1)<1e-6 && Math.Abs(c.p[0]*1000-1)<1e-6 && Math.Abs(c.p[2]*1000-(8+height-1))<1e-6 &&
            Math.Abs(Math.Abs(c.p[4])-1)<1e-8 && Math.Abs(c.uv[1]-c.uv[0]-Math.PI/2)<1e-7 && Math.Abs(c.area-20*Math.PI)<1e-4 && c.rings.Length==2 &&
            Math.Abs(c.rings.Min(r=>r[1])*1000+20)<1e-6 && Math.Abs(c.rings.Max(r=>r[1])*1000-20)<1e-6)==1;
        var lines=Items<IEdge>(body.GetEdges()).Where(e=>((ICurve)e.GetCurve()).IsLine()).Select(e=>new[]{(double[])((IVertex)e.GetStartVertex()).GetPoint(),(double[])((IVertex)e.GetEndVertex()).GetPoint()}).ToArray();
        var tangency=!fillet || new[]{new[]{0d,8+height-1},new[]{1d,8+height}}.All(v=>lines.Count(l=>l.All(p=>Math.Abs(p[0]*1000-v[0])<1e-6 && Math.Abs(p[2]*1000-v[1])<1e-6) && Math.Abs(l.Min(p=>p[1])*1000+20)<1e-6 && Math.Abs(l.Max(p=>p[1])*1000-20)<1e-6)==1);
        var normals=faces.Where(f=>((ISurface)f.GetSurface()).IsPlane()).Select(f=>new { normal=(double[])f.Normal,area=f.GetArea()*1e6 }).ToArray();
        var installation=normals.Count(f=>Math.Abs(f.normal[2]+1)<1e-8 && Math.Abs(f.area-(2400-18*Math.PI))<1e-4)==1 &&
            normals.Any(f=>Math.Abs(f.normal[0]+1)<1e-8 && Math.Abs(f.area-(40*(8+height)-(fillet?40:0)-9*Math.PI))<1e-4);
        var bounds=new double[6];for(var i=0;i<6;i++){var axis=i%3;var sign=i<3?-1:1;Program.Check(body.GetExtremePoint(axis==0?sign:0,axis==1?sign:0,axis==2?sign:0,out var x,out var y,out var z),"Oracle supports");bounds[i]=new[]{x,y,z}[axis]*1000;}
        var volume=((double[])body.GetMassProperties(1))[3]*1e9;var expected=60*40*8+8*40*height-216*Math.PI-(fillet?(1-Math.PI/4)*40:0);
        var passed=holeMatch && cornerMatch && tangency && installation && cylinders.Length==(fillet?4:3) && faces.All(f=>((ISurface)f.GetSurface()).IsPlane() || ((ISurface)f.GetSurface()).IsCylinder()) &&
            Math.Abs(volume-expected)<.01 && bounds.Zip(new[]{0d,-20,0,60,20,8+height}).All(p=>Math.Abs(p.First-p.Second)<.01);
        log("independent-oracle",new { passed,height,fillet,volume,expected,difference=volume-expected,bounds,cylinders,lines,normals,holeMatch,cornerMatch,tangency,installation });Program.Check(passed,"Independent cross-axis/fillet Oracle failed");
    }
}
