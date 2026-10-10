using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Bounded M14C trials; edits always use the public production session.
internal static class ExternalModelQualification
{
    internal static int Prepare(string root,string run,string phase="models")
    {
        var output=Path.Combine(root,"artifacts","milestone14",run);
        var old=AcceptanceFiles.Read<AcceptanceSchedule>(Path.Combine(root,"artifacts","milestone14","acceptance-v5","schedule.json"));
        var catalog=AcceptanceFiles.Read<ExternalCatalog.Freeze>(Path.Combine(root,"artifacts","milestone14","acceptance-v17","catalog.json"));
        foreach(var i in catalog.Inputs)AcceptanceFiles.Verify(i.Source);
        var inputs=new List<OracleInput>();var slots=new List<AcceptanceScenario>();
        string Package(string id)=>Path.Combine(output,"packages",id);
        foreach(var id in new[]{"engineer_current","engineer_old"})
        {
            var input=old.Inputs.Single(i=>i.Id==id);inputs.Add(input);
            var scalar=AcceptanceFiles.Change(input,"hole_b",ParameterKey.HoleDiameter,14) with{Id=id+"-r1"};inputs.Add(scalar);
            var batch=AcceptanceFiles.Change(AcceptanceFiles.Change(scalar,"hole_b",ParameterKey.HoleDiameter,13),"hole_c",ParameterKey.HoleDiameter,6) with{Id=id+"-r2"};inputs.Add(batch);
            slots.Add(new(id,"external-scalar",id,Package(id),null,2,true));
            slots.Add(new(id+"-scalar-cold","external-cold",scalar.Id,Package(id),id,1,true));
            slots.Add(new(id+"-batch","external-batch",scalar.Id,Package(id),id+"-scalar-cold",2,true));
            slots.Add(new(id+"-batch-cold","external-cold",batch.Id,Package(id),id+"-batch",1,true));
        }
        var current=inputs.Single(i=>i.Id=="engineer_current");
        slots.Add(new("identity","external-identity",current.Id,Package("identity"),null,3,true));
        var identity=AcceptanceFiles.Change(AcceptanceFiles.Change(current,"hole_b",ParameterKey.HoleDiameter,8),"hole_c",ParameterKey.HoleDiameter,8) with{Id="identity-r2"};inputs.Add(identity);
        slots.Add(new("identity-cold","external-cold",identity.Id,Package("identity"),"identity",1,true));
        slots.Add(new("deleted","external-deleted",current.Id,Package("deleted"),null,2,true));
        slots.Add(new("suppressed","external-suppressed",current.Id,Package("suppressed"),null,2,true));
        if(phase=="probe")slots=new(){new("probe","origin-driver-probe",current.Id,Package("probe"),null,1,false)};
        if(phase is "boundaries" or "negative-targets")
        {
            var core=old.Inputs.Single(i=>i.Id=="dev_core");inputs=new(){core};slots=new();
            foreach(var (id,kind,opens) in new[]{("identity","external-identity",3),("deleted","external-deleted",2),("suppressed","external-suppressed",2),("source-drift","external-source-drift",1),("configuration","external-configuration",2)})
            {
                var source=Path.Combine(output,"sources",id+".SLDPRT");Directory.CreateDirectory(Path.GetDirectoryName(source)!);File.Copy(core.Source.Path,source,false);
                var spec=core with{Id=id,Source=AcceptanceFiles.Identity(source),Provenance="Disposable development source for M14C native boundary only; never independent/held-out"};inputs.Add(spec);
                slots.Add(new(id,kind,id,Package(id),null,opens,true));
                if(id=="identity")
                {
                    var final=AcceptanceFiles.Change(AcceptanceFiles.Change(spec,"hole_b",ParameterKey.HoleDiameter,8),"hole_c",ParameterKey.HoleDiameter,8) with{Id="identity-r2"};inputs.Add(final);
                    slots.Add(new("identity-cold","external-cold",final.Id,Package(id),id,1,true));
                }
            }
            if(phase=="negative-targets")slots=slots.Where(s=>s.Id is "deleted" or "suppressed" or "configuration").ToList();
        }
        if(phase=="configuration")
        {
            var core=old.Inputs.Single(i=>i.Id=="dev_core");inputs=new(){core};
            slots=new(){new("configuration","external-configuration",core.Id,Package("configuration"),null,2,true)};
        }
        var grant=Path.Combine(output,"authorization.json");
        AcceptancePreparation.Write(grant,new M14AdditionalBudget("0.3",true,14,27,int.MaxValue-27,int.MaxValue,0,
            "No user limit. M14C explicit human unlimited-open authorization; bounded scenarios, no original edits, no new Parts; preserve prior failures and stop on repeated stagnation."));
        var schedule=new AcceptanceSchedule("0.3",run,grant,inputs,slots);AcceptanceFiles.Validate(schedule);
        var path=Path.Combine(output,"schedule.json");AcceptancePreparation.Write(path,schedule);
        var budget=Path.Combine(root,"artifacts","milestone14","native-budget.json");File.Copy(budget,Path.Combine(output,"initial-budget.json"),false);
        var files=Directory.EnumerateFiles(Path.Combine(output,"source"),"*",SearchOption.AllDirectories).Concat(Directory.EnumerateFiles(Path.Combine(output,"bin")))
            .Concat(new[]{grant,Path.Combine(output,"initial-budget.json"),Path.Combine(root,"artifacts","milestone14","acceptance-v17","catalog.json")}).Select(AcceptanceFiles.Identity)
            .Concat(inputs.Select(i=>i.Source)).DistinctBy(f=>f.Path).ToArray();
        AcceptancePreparation.Write(Path.Combine(output,"freeze.json"),new AcceptanceFreeze("0.3",run,DateTime.UtcNow,AcceptanceFiles.Identity(path),files,AcceptanceFiles.Identity(budget)));
        Console.WriteLine($"M14C {phase} schedule: {slots.Count} slots, {slots.Sum(s=>s.MaximumOpens)} bounded opens; prior authority and attempts retained.");return 0;
    }
    internal static void Run(AcceptanceScenario slot,OracleInput initial,ExternalPartSession session,ISldWorks app,StepJournal journal)
    {
        var input=initial;
        IModelDoc2 Doc()=>(IModelDoc2)app.GetOpenDocumentByName(session.Store.WorkingPath);
        object Oracle()=>NativeEditOracle.Read(Doc(),input,m=>journal.Add(new{step="external-oracle-measurement",measurement=m}));
        ScalarEdit Edit(string label,ParameterKey key,double value)
        {
            var f=session.CurrentExternal.Observation.Features.Single(f=>f.NativeReference?.Base64==input.References[label]);
            return new(f.SemanticId,key,ContractValidation.Unit(key),f.Parameters.Single(p=>p.Key==key).Value,value);
        }
        ScalarEditRequest Scalar(string label,double value)=>new("0.3",RequestMode.ExternalScalarEdit,ModelOrigin.External,session.CurrentExternal.Observation.Selection,Edit(label,ParameterKey.HoleDiameter,value));
        void Durable()
        {
            var r=session.Store.ReadCurrent();var adapter=ExternalEditPlanning.Adapter(r.External!);
            Program.Check(r.Manifest.Revision==r.State.Revision&&adapter.Revision==r.State.Revision&&
                adapter.Parameters.OrderBy(p=>p.SemanticId).SequenceEqual(r.State.Parameters.OrderBy(p=>p.SemanticId))&&
                ManagedRevisionStore.Hash(session.Store.WorkingPath)==r.Manifest.NativePart.Sha256&&
                ManagedRevisionStore.Hash(r.Manifest.State.Path)==r.Manifest.State.Sha256&&!File.Exists(session.Store.RecoveryPath),"External durable package inconsistent.");
            journal.Add(new{step="external-durable",r.Manifest,r.State.Parameters,revision=r.State.Revision,oracle=Oracle()});
        }
        void Apply(string name,params (string Label,double Value)[] changes)
        {
            var before=session.Store.Load().Revision;var selection=session.CurrentExternal.Observation.Selection;
            var edits=changes.Select(e=>Edit(e.Label,ParameterKey.HoleDiameter,e.Value)).ToArray();
            journal.Add(new{step=name+"-request",selection,edits,before});
            var result=edits.Length==1?session.Edit(new ScalarEditRequest("0.3",RequestMode.ExternalScalarEdit,ModelOrigin.External,selection,edits[0])):
                session.Edit(new EditSetRequest("0.3",RequestMode.EditSet,ModelOrigin.External,selection,edits));
            journal.Add(new{step=name,result});
            Program.Check(result.Transaction.Succeeded&&result.Transaction.StateCommitted&&result.Transaction.Revision==before+1&&result.Checkpoints==1&&result.PartialNativeSteps.Count==edits.Length,"External public edit did not commit one revision: "+result.Transaction.Message);
            input=changes.Aggregate(input,(s,c)=>AcceptanceFiles.Change(s,c.Label,ParameterKey.HoleDiameter,c.Value));Durable();
        }
        journal.Add(new{step="external-baseline",revision=session.Store.Load().Revision,oracle=Oracle()});
        if(slot.Kind=="external-cold") {Program.Check(session.Store.Load().Revision==(input.Id.EndsWith("-r1")?1:2),"External independent cold revision differs.");Durable();return;}
        if(slot.Kind=="external-scalar") {Apply("engineer-public-scalar",("hole_b",14));return;}
        if(slot.Kind=="external-batch") {Apply("engineer-public-editset",("hole_b",13),("hole_c",6));return;}
        if(slot.Kind=="external-source-drift")
        {
            var bytes=File.ReadAllBytes(input.Source.Path);var driftRequest=Scalar("hole_c",8);var driftRevision=session.Store.Load().Revision;
            var events=0;session.EvidenceSink=e=>{events++;journal.Add(new{step="source-drift-native-event",evidence=e});};
            try
            {
                using(var file=new FileStream(input.Source.Path,FileMode.Append,FileAccess.Write,FileShare.None)){file.WriteByte(0x4d);file.Flush(true);}
                journal.Add(new{step="actual-source-drift",before=input.Source,after=AcceptanceFiles.Identity(input.Source.Path)});
                string? code=null;ExternalEditResult? result=null;
                try{result=session.Edit(driftRequest);}catch(StateException e){code=e.Code;}
                journal.Add(new{step="source-drift-public-refusal",code,result,events});
                Program.Check(code==V03FailureCodes.SourceFileDrift&&events==0&&session.Store.Load().Revision==driftRevision&&!File.Exists(session.Store.RecoveryPath),"Source drift reached native mutation or failed to reject.");
            }
            finally{File.WriteAllBytes(input.Source.Path,bytes);}
            AcceptanceFiles.Verify(input.Source);session.VerifyLive(session.CurrentExternal);Durable();return;
        }
        if(slot.Kind=="external-identity")
        {
            Apply("same-size-distinct-targets",("hole_b",7),("hole_c",7));
            var b=NativeEditOracle.Resolve(Doc(),input.References["hole_b"]);var c=NativeEditOracle.Resolve(Doc(),input.References["hole_c"]);
            Program.Check(input.References["hole_b"]!=input.References["hole_c"],"Same-size targets must have distinct identities.");
            b.Name="M14C_Renamed_B";c.Name="M14C_Renamed_C";
            var moved=Doc().Extension.ReorderFeature(c.Name,b.Name,(int)swMoveLocation_e.swMoveBefore);
            journal.Add(new{step="actual-rename-reorder",moved,b=b.Name,c=c.Name,oracle=Oracle()});
            Program.Check(moved,"Native legal independent reorder failed.");
            Apply("same-size-exact-reference-after-reorder",("hole_b",8),("hole_c",8));return;
        }
        var revision=session.Store.Load().Revision;var native=ManagedRevisionStore.Hash(session.Store.WorkingPath);var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);
        var request=Scalar("hole_c",6);var target=NativeEditOracle.Resolve(Doc(),input.References["hole_c"]);
        try
        {
            if(slot.Kind=="external-deleted")
            {
                Doc().ClearSelection2(true);Program.Check(target.Select2(false,0)&&Doc().Extension.DeleteSelection2(0),"Actual native deletion failed.");
                var resolved=Doc().Extension.GetObjectByPersistReference3(Convert.FromBase64String(input.References["hole_c"]),out var status);
                journal.Add(new{step="actual-deleted-reference",status,resolved=resolved is not null});Program.Check(resolved is null||status!=0,"Deleted native reference still resolves.");
            }
            else if(slot.Kind=="external-configuration")
            {
                var before=Doc().ConfigurationManager.ActiveConfiguration.Name;
                var added=Doc().AddConfiguration3("M14C_Changed","M14C disposable configuration refusal","",0);
                var changed=Doc().ShowConfiguration2("M14C_Changed");
                journal.Add(new{step="actual-native-configuration-change",api="IModelDoc2.AddConfiguration3",created=added is not null,changed,before,after=Doc().ConfigurationManager.ActiveConfiguration.Name,count=Doc().GetConfigurationCount(),names=Doc().GetConfigurationNames()});
                Program.Check(added is not null&&Doc().ConfigurationManager.ActiveConfiguration.Name=="M14C_Changed","Actual native configuration creation/switch failed.");
                Program.Check(Doc().ConfigurationManager.ActiveConfiguration.Name!=before,"Native configuration did not change.");
            }
            else
            {
                Program.Check(target.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,(int)swInConfigurationOpts_e.swThisConfiguration,null)&&target.IsSuppressed(),"Actual native suppress failed.");
                journal.Add(new{step="actual-suppressed-target",reference=input.References["hole_c"],suppressed=target.IsSuppressed()});
            }
            ExternalEditResult? result=null;string? thrown=null;
            try{result=session.Edit(request);}catch(Exception e){thrown=e.ToString();}
            journal.Add(new{step="public-native-state-refusal",result,thrown});
            Program.Check(result is not null&&!result.Transaction.Succeeded&&!result.Transaction.MutationStarted&&result.Checkpoints==0&&result.PartialNativeSteps.Count==0&&
                result.Transaction.Stage=="preflight"&&result.Transaction.FailureCode is "STATE_DRIFT_DETECTED" or "FEATURE_REBUILD_FAILED" or "STALE_REFERENCE", "Native refusal was masked or changed target reached Setter/checkpoint.");
        }
        finally{session.Store.PrepareCheckpoint();session.Restore(session.Store.Inspect(session.Store.WorkingPath));}
        Program.Check(session.Store.Load().Revision==revision&&ManagedRevisionStore.Hash(session.Store.WorkingPath)==native&&ManagedRevisionStore.Hash(session.Store.PointerPath)==pointer,"Native negative did not restore original authority bytes.");Durable();
    }
}
