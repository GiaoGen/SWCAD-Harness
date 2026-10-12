extern alias FeatureOracle;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;

internal static partial class T4Tests
{
    internal static int Native(string root,string run,string model,string phase,string packageRun,string liveRun,bool compiled=false,int semanticVersion=1,string? evidenceStage=null,string? packageStage=null)
    {
        var stage=evidenceStage??(semanticVersion==2?"T4C":compiled?"T4B":"T4");
        if(phase=="audit")return Audit(root,run,stage);
        Program.Check(ContractValidation.Id(run) && ContractValidation.Id(packageRun) && model is "shaft" or "bracket" && phase is "create" or "edit" or "read","Invalid T4 integration slot");
        var output=Path.Combine(root,"artifacts/m20-preparation",stage,run,model,phase);var step=0;var timer=System.Diagnostics.Stopwatch.StartNew();
        void Log(string name,object data)=>Program.Write(Path.Combine(output,(++step).ToString("D3")+"-"+name+".json"),new{utc=DateTime.UtcNow,controller=System.Environment.ProcessId,elapsedMilliseconds=timer.Elapsed.TotalMilliseconds,data});
        using var budget=new NativeTestBudget(Path.Combine(root,"artifacts/m20-preparation/T4/native-budget.json"),"T4",int.MaxValue);
        using var connection=SolidWorksConnection.Connect(false);var before=NativeResourceGuard.DocumentTitles(connection.Application);var resource=NativeResourceGuard.Inspect(connection.Application,budget);
        Program.Check(resource.Responding && resource.GdiCount<7000 && budget.Snapshot.OpenTestOwnedTitles.Count==0,"Ownership/resource refusal");Log("before",new{resource,budget=budget.Snapshot,before});Log("source",new{source=Program.SourceHashes(root),binaries=Binaries()});
        var package=Path.Combine(root,"artifacts/m20-preparation",packageStage??stage,packageRun,model,"package");GenericPartSession? session=null;
        try
        {
            var lifecycle=new Lifecycle(budget,Log);
            if(phase=="create")session=GenericPartSession.Create(connection,package,@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot",lifecycle:lifecycle);
            else {var opened=GenericPartSession.Open(connection,package,Path.Combine(package,"working/CADHarnessManagedPart.SLDPRT"),lifecycle:lifecycle);Log("open",new{opened.Status,opened.FailureCode,opened.Message});Program.Check(opened.Session is not null,"Restore failed: "+opened.Message);session=opened.Session!;}
            session.Context.FeatureEvidenceSink=t=>Log("feature-api",t);session.Context.SketchEvidenceSink=t=>Log("sketch-api",t);session.SaveEvidenceSink=t=>Log("save-api",t);
            if(phase=="create")
            {
                var scene=T4Cases.All(root).Single(c=>c.Id==model);var semantic=compiled?T4BCases.All(root).Single(c=>c.Id==model):null;string response;var provenance="fixture";
                var v2=semanticVersion==2?T4CCases.All(root).Single(c=>c.Id==model):null;
                if(liveRun=="fixture")response=v2 is not null?ContractJson.Write(v2.Draft,SemanticConstructionV2.ValidateDraft):semantic is null?Cases.Wire(scene.Response):ContractJson.Write(semantic.Draft,SemanticConstructionCompiler.ValidateDraft);
                else
                {
                    var path=Path.Combine(root,"artifacts/m20-preparation",stage,liveRun,model,"call-01-response.json");using var doc=JsonDocument.Parse(File.ReadAllText(path));response=doc.RootElement.GetProperty("response").GetProperty("Json").GetString()!;
                    Log("preserved-live-replay",new{path,sha256=ManagedRevisionStore.Hash(path),newModelCalls=0,unmodified=true});provenance="recorded_live_response_revalidated_in_fresh_native_context";
                }
                var plan=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForManagedSession(session.Context),new RecordingSource(response)).PlanAsync(v2?.Request??semantic?.Request??scene.Request).GetAwaiter().GetResult();Log("plan",new{plan,provenance});Program.Check(plan.Preview is not null,"Native fresh preflight: "+plan.Message);
                if(compiled){if(v2 is not null)T4CTests.RequireTaskGeometry(root,v2,plan.Preview!.Read().Program!);else T4BTests.RequireTaskGeometry(root,semantic!,plan.Preview!.Read().Program!);Log("task-oracle-precheck",new{passed=true,structuralPassIsNotTaskPass=true});}
                var approval=plan.Preview!.Approve(plan.Preview.Sha256);var result=session.ExecuteConstructionPlan(approval);Log("publish",result);Program.Check(result.Succeeded && result.StateCommitted && result.Revision==1,"Approved publication: "+result.Message+" / "+result.RollbackFailureMessage);
                try{session.ExecuteConstructionPlan(approval);throw new Exception("Reused approval");}catch(ContractException e){Log("reuse-refused",new{e.Code});Program.Check(e.Code==V03FailureCodes.PreviewExpired,"Wrong approval refusal");}
            }
            else if(phase=="edit")
            {
                var state=session.CurrentGeneric.State;var history=ContractJson.Read<PlanningHistory>(session.CurrentGeneric.Provenance!.Payload,_=>{});var accepted=history.Steps.First(s=>s.Plan is not null).Plan!.ReadResponse();
                var b=accepted.Bindings.Single(b=>model=="shaft"?b.SemanticKey=="middle_radius" && b.Metric==RequirementMetric.RadialDriverRadius:b.SemanticKey=="leg_height" && b.Metric==RequirementMetric.Depth);
                var owner=model=="shaft"?state.Program.Operations.Single(o=>o.Profile?.SemanticId==b.Target+".profile").SemanticId:b.Target;
                var parameter=state.Parameters.Single(p=>p.SemanticId==owner+(model=="shaft"?".radius."+b.Element:".depth"));
                var required=history.Steps.First(s=>s.Plan is not null).Plan!.Compilation?.EditTargets.Single();
                if(semanticVersion==2)Program.Check(required is not null && required.ParameterSemanticId==parameter.SemanticId && required.Before==parameter.Value,"Actual edit target differs from accepted role");
                var command=new GenericScalarCommand(owner,parameter.Key,parameter.Value,required?.After??(model=="shaft"?13:44),state.Revision,parameter.SemanticId);Log("request",command);var result=session.Edit(command);Log("edit",result);Program.Check(result.Succeeded && result.StateCommitted && result.Revision==2,"Provenance-preserving edit: "+result.Message+" / "+result.RollbackFailureMessage);
            }
            var current=session.CurrentGeneric;Log("package",current);
            var saved=ContractJson.Read<PlanningHistory>(current.Provenance!.Payload,_=>{}).Steps.First(s=>s.Plan is not null).Plan!.ReadResponse();
            var binding=saved.Bindings.Single(b=>model=="shaft"?b.SemanticKey=="middle_radius" && b.Metric==RequirementMetric.RadialDriverRadius:b.SemanticKey=="leg_height" && b.Metric==RequirementMetric.Depth);
            var feature=model=="shaft"?current.State.Program.Operations.Single(o=>o.Profile?.SemanticId==binding.Target+".profile").SemanticId:binding.Target;
            var driver=current.State.Parameters.Single(p=>p.SemanticId==feature+(model=="shaft"?".radius."+binding.Element:".depth"));
            if(model=="shaft")FeatureOracle::T2Tests.Oracle(session.Context.Document,false,driver.Value,Log);else FeatureOracle::T3Tests.Oracle(session.Context.Document,driver.Value,true,Log);
            var record=ContractJson.Read<PlanningHistory>(current.Provenance!.Payload,_=>{});Program.Check(record.Version==1 && record.Steps.Count==current.State.Revision && current.Provenance.Kind=="construction_planning" && !File.Exists(session.Store.RecoveryPath),"Provenance/authority/marker association differs");
            if(compiled)Program.Check(record.Steps[0].Plan!.Compilation is not null && record.Steps[0].Plan!.Request.SemanticDraftVersion==semanticVersion,"Compiled provenance lost");
            Log("result",new{passed=true,current.State.Revision,driver.Value,current.FormatVersion,historyEvents=record.Steps.Count,native=ManagedRevisionStore.Hash(session.Store.WorkingPath),source=current.Provenance.Kind});
        }
        catch(Exception e){Log("failure",new{error=e.ToString()});throw;}
        finally
        {
            session?.Dispose();var after=NativeResourceGuard.Inspect(connection.Application,budget);Log("cleanup",new{session?.OriginalActiveRestored,budget=budget.Snapshot,resource=after});Program.Check(budget.Snapshot.OpenTestOwnedTitles.Count==0 && after.Responding && after.GdiCount<7000 && before.OrderBy(x=>x).SequenceEqual(NativeResourceGuard.DocumentTitles(connection.Application).OrderBy(x=>x)),"Cleanup failed");
        }
        return 0;
    }
    private static int Audit(string root,string run,string stage="T4")
    {
        using var connection=SolidWorksConnection.Connect(false);
        using var budget=new NativeTestBudget(Path.Combine(root,"artifacts/m20-preparation/T4/native-budget.json"),"T4",int.MaxValue);
        var resource=NativeResourceGuard.Inspect(connection.Application,budget);
        Program.Check(resource.Responding && resource.GdiCount<7000 && budget.Snapshot.OpenTestOwnedTitles.Count==0,"Unresolved ownership/resources");
        var originals=new System.Collections.Generic.List<object>();
        using(var registry=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"artifacts/fixture-factory/audits/m14-preflight/fixture-registry.json"))))
            foreach(var f in registry.RootElement.GetProperty("fixtures").EnumerateArray())
            {
                var part=f.GetProperty("nativePart");var path=part.GetProperty("path").GetString()!;var expected=part.GetProperty("sha256").GetString()!;var actual=ManagedRevisionStore.Hash(path);
                Program.Check(actual==expected,"Frozen original drift: "+path);originals.Add(new{path,expected,actual});
                Program.Check(connection.Application.GetOpenDocumentByName(path) is null,"Original is open: "+path);
            }
        var manual=Path.Combine(root,"M14测试用夹具.SLDPRT");var sha=ManagedRevisionStore.Hash(manual);
        Program.Check(sha=="60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b" && connection.Application.GetOpenDocumentByName(manual) is null,"Manual original protection differs");originals.Add(new{path=manual,actual=sha});
        var historical=Path.Combine(root,"artifacts/milestone20/acceptance_v3/stop-audit-20261011T145238147.json");using var stop=JsonDocument.Parse(File.ReadAllText(historical));var count=0;
        foreach(var item in stop.RootElement.GetProperty("preservedEvidence").EnumerateArray()){var path=item.GetProperty("path").GetString()!;Program.Check(ManagedRevisionStore.Hash(path)==item.GetProperty("sha256").GetString(),"Stopped evidence changed: "+path);count++;}
        var paths=new[]{"T4","T4B",stage}.Distinct().Select(s=>Path.Combine(root,"artifacts/m20-preparation",s)).Where(Directory.Exists);
        var markers=paths.SelectMany(p=>Directory.EnumerateFiles(p,"recovery*.json",SearchOption.AllDirectories)).ToArray();
        var isolated=new System.Collections.Generic.List<object>();
        foreach(var path in markers)
        {
            using var store=new ManagedRevisionStore(Path.GetDirectoryName(path)!,new InspectionOnly());var inspection=store.Inspect(store.WorkingPath);
            var marker=ContractJson.Read<ManagedRecoveryMarker>(File.ReadAllText(path),_=>{});
            Program.Check(inspection.Status==CadHarness.State.V03.ReopenStatus.Quarantined && inspection.Revision is null && marker.PreviousManifest is null && !File.Exists(store.PointerPath),"Unresolved authoritative recovery requires stopping: "+path);
            var disposition=new{path,sha256=ManagedRevisionStore.Hash(path),inspection.Status,inspection.FailureCode,inspection.Message,neverReuse=true,authorityExists=false,controllerLeaseAcquired=true,nativeOpen=false,budget.Snapshot.OpenTestOwnedTitles};
            if(!File.Exists(Path.Combine(store.Root,"quarantine-disposition.json")))Program.Write(Path.Combine(store.Root,"quarantine-disposition.json"),disposition);
            isolated.Add(disposition);
        }
        Program.Write(Path.Combine(root,"artifacts/m20-preparation",stage,run,"native-audit.json"),new{utc=DateTime.UtcNow,resource,documents=NativeResourceGuard.DocumentTitles(connection.Application),budget=budget.Snapshot,originals,preservedHistoricalFiles=count,isolatedInitialFailures=isolated,unresolvedAuthoritativeMarkers=0,source=Program.SourceHashes(root),binaries=Binaries(),
            plan=new{parts=stage=="T4C"?1:2,expectedOpens=stage=="T4C"?7:10,slots=stage=="T4C"?new[]{"shaft.real.create:2","shaft.real.edit:2","shaft.real.read:1","legacy_v1.shaft.read:1","legacy_v1.bracket.read:1"}:new[]{"shaft.create:2","bracket.create:2","shaft.cold_edit:2","bracket.cold_edit:2","shaft.final_cold_read:1","bracket.final_cold_read:1"},oneOwnedPartAtATime=true,noModelCalls=true,formalM20=false}});
        return 0;
    }
    private static object[] Binaries()=>Directory.EnumerateFiles(AppContext.BaseDirectory,"CadHarness*.dll").OrderBy(p=>p).Select(p=>(object)new{path=p,sha256=ManagedRevisionStore.Hash(p)}).ToArray();
    private sealed class InspectionOnly:IManagedRevisionNative
    {
        public CadHarness.Ir.CadProgram CurrentProgram=>throw new NotSupportedException();
        public void SaveNative()=>throw new NotSupportedException();
        public void VerifySavedRevision(CadState s,CadHarness.Ir.CadProgram p)=>throw new NotSupportedException();
    }
    private sealed class Lifecycle(NativeTestBudget budget,Action<string,object> log):IGenericDocumentLifecycle
    {
        public void BeforeCreate(){budget.ReserveCreation();log("reserve-create",budget.Snapshot);}public void Created(string title){budget.RegisterCreated(title);log("created",new{title});}public void Renamed(string a,string b){budget.RenameOwned(a,b);log("rename",new{a,b});}
        public void BeforeOpen(string path){budget.ReserveOpen(int.MaxValue);log("reserve-open",new{path});}public void Opened(string path,string title){budget.RegisterOpened(title);log("opened",new{path,title});}public void Closed(string path,string title){budget.RegisterClosed(title);log("closed",new{path,title});}
    }
}
