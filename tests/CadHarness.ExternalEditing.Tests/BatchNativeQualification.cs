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

// M14B scenarios only. Mutations, snapshots, publication and recovery use production code.
internal static class BatchNativeQualification
{
    internal static void Run(AcceptanceScenario slot,OracleInput initial,ExternalPartSession session,ISldWorks app,StepJournal journal,string output,
        Action<ExternalEditFault> onFault,Action<Action<ExternalEditFault>?> setFault,Action<DurableFaultPoint?> setPublish)
    {
        var input=initial;bool nativeFault=false;bool? nativeRebuild=null;
        IModelDoc2 Doc()=>(IModelDoc2)app.GetOpenDocumentByName(session.Store.WorkingPath);
        session.EvidenceSink=e=>
        {
            journal.Add(new{step="batch-native-event",evidence=e});
            if(nativeFault&&e.Phase=="rebuild-return")
            {
                nativeRebuild=e.Rebuilt;
                journal.Add(new{step="actual-native-rebuild-diagnostics",returned=e.Rebuilt,features=input.References.Select(p=>
                {var f=NativeEditOracle.Resolve(Doc(),p.Value);var error=f.GetErrorCode2(out var warning);return new{p.Key,error,warning};}).ToArray()});
            }
        };
        object Oracle()=>NativeEditOracle.Read(Doc(),input,m=>journal.Add(new{step="batch-oracle-measurement",measurement=m}));
        object Snapshot()=>new{revision=session.Store.ReadCurrent().State.Revision,native=AcceptanceFiles.Identity(session.Store.WorkingPath),pointer=AcceptanceFiles.Identity(session.Store.PointerPath),
            manifest=session.Store.ReadCurrent().ManifestArtifact,recovery=File.Exists(session.Store.RecoveryPath)};
        EditSetRequest Request(params (string Label,ParameterKey Key,double Value)[] changes)
        {
            var state=session.CurrentExternal;
            return new("0.3",RequestMode.EditSet,ModelOrigin.External,state.Observation.Selection,changes.Select(e=>
            {
                var f=state.Observation.Features.Single(f=>f.NativeReference?.Base64==input.References[e.Label]);var p=f.Parameters.Single(p=>p.Key==e.Key);
                return new ScalarEdit(f.SemanticId,e.Key,ContractValidation.Unit(e.Key),p.Value,e.Value);
            }).ToArray());
        }
        ExternalEditResult Execute(EditSetRequest request)
        {
            journal.Add(new{step="batch-request",request,preparation=ExternalEditPlanning.Prepare(session.CurrentExternal,request),before=Snapshot()});
            if(slot.PublicEntry)return session.Edit(request);
            var backend=new ExternalEditTransactionBackend(session,onFault);
            var result=new RequestMutationTransaction<ExternalEditCommand,ExternalEditPreparation,ExternalEditRollback>(session.Store,backend).Execute(new(request,null));
            if(File.Exists(session.Store.RecoveryPath)||result.RollbackAttempted&&!result.RollbackSucceeded)session.Invalidate();
            return new(result,backend.Checkpoints,backend.PartialNativeSteps,session.Status,true);
        }
        void Durable()
        {
            var revision=session.Store.ReadCurrent();var external=revision.External!;
            var adapter=ExternalEditPlanning.Adapter(external);
            Program.Check(revision.Manifest.Revision==revision.State.Revision&&adapter.Revision==revision.State.Revision&&session.CurrentExternal.Observation.Selection.ExpectedRevision==adapter.Revision&&
                revision.Manifest.Program is null&&ManagedRevisionStore.Hash(session.Store.WorkingPath)==revision.Manifest.NativePart.Sha256&&
                ManagedRevisionStore.Hash(revision.Manifest.State.Path)==revision.Manifest.State.Sha256,"Native/State/Companion/Manifest disagree.");
            Program.Check(adapter.Parameters.OrderBy(p=>p.SemanticId).SequenceEqual(revision.State.Parameters.OrderBy(p=>p.SemanticId)),"Adapter CADState parameters differ from companion.");
            journal.Add(new{step="batch-durable-consistency",revision=revision.State.Revision,revision.Manifest,revision.State.Parameters,external.Observation.Selection,snapshot=Snapshot(),oracle=Oracle()});
        }
        void Apply(string name,params (string Label,ParameterKey Key,double Value)[] changes)
        {
            var before=session.Store.Load().Revision;var request=Request(changes);var result=Execute(request);
            journal.Add(new{step=name,result});
            Program.Check(result.Transaction.Succeeded&&result.Transaction.StateCommitted&&result.Transaction.Revision==before+1&&result.Checkpoints==1&&result.PartialNativeSteps.Count==changes.Length,"Batch did not commit once.");
            input=changes.Aggregate(input,(spec,e)=>AcceptanceFiles.Change(spec,e.Label,e.Key,e.Value));Durable();
        }
        void First(ExternalEditFault point)
        {
            if(point!=ExternalEditFault.FirstEdit)return;
            var b=NativeEditOracle.DrivingDiameter(Doc(),input,"hole_b");var c=NativeEditOracle.DrivingDiameter(Doc(),input,"hole_c");
            journal.Add(new{step="first-setter-before-second",b,c,expectedB=14,expectedC=input.Holes.Single(h=>h.Label=="hole_c").Diameter});
            Program.Check(Math.Abs(b-14)<1e-6&&Math.Abs(c-input.Holes.Single(h=>h.Label=="hole_c").Diameter)<1e-6,"Injection was not between the two actual setters.");
            throw new StateException("INJECTED_FIRST_EDIT_FAILURE","First actual setter changed B; C setter not executed.");
        }
        void Rollback(string name,ExternalEditResult result,long revision,string native,string pointer)
        {
            journal.Add(new{step=name,result,after=Snapshot()});
            Program.Check(!result.Transaction.Succeeded&&result.Transaction.RollbackSucceeded&&result.Checkpoints==1&&!result.Transaction.StateCommitted&&
                session.Store.Load().Revision==revision&&ManagedRevisionStore.Hash(session.Store.WorkingPath)==native&&ManagedRevisionStore.Hash(session.Store.PointerPath)==pointer&&!File.Exists(session.Store.RecoveryPath),"Atomic rollback/authority differs.");
            Durable();
        }
        void Failure(string name,Action<ExternalEditFault>? inject=null,DurableFaultPoint? publication=null)
        {
            var revision=session.Store.Load().Revision;var native=ManagedRevisionStore.Hash(session.Store.WorkingPath);var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);
            setFault(inject);setPublish(publication);
            var result=Execute(Request(("hole_b",ParameterKey.HoleDiameter,14),("hole_c",ParameterKey.HoleDiameter,10)));
            setFault(null);setPublish(null);
            Rollback(name,result,revision,native,pointer);
            if(name=="first-edit-failure")Program.Check(result.PartialNativeSteps.Count==1,"Second setter executed before first-edit failure.");
        }
        void PreserveMarker(string name)
        {
            Program.Check(File.Exists(session.Store.RecoveryPath),"Recovery evidence missing.");
            var preserved=Path.Combine(output,name+"-recovery-marker.json");File.Copy(session.Store.RecoveryPath,preserved,false);
            journal.Add(new{step=name+"-interruption-scene",marker=AcceptanceFiles.Identity(preserved),snapshot=Snapshot(),status=session.Status});
            try{session.Store.Load();throw new InvalidOperationException("Unresolved recovery remained dispatchable.");}catch(StateException e){Program.Check(e.Code==V03FailureCodes.IncompleteDurablePublish,"Unexpected quarantine refusal.");}
        }
        journal.Add(new{step="batch-baseline",scope=slot.PublicEntry?"public":"candidate",snapshot=Snapshot(),oracle=Oracle()});
        if(slot.Kind=="batch-cold")
        {
            var expected=input.Id=="batch-final-r2"?2:input.Id=="batch-published-r1"?1:0;
            Program.Check(session.Store.Load().Revision==expected&&!File.Exists(session.Store.RecoveryPath),"Independent cold/recovery authority differs.");Durable();return;
        }
        Program.Check(session.Store.Load().Revision==0,"M14B fresh package is not revision 0.");
        if(slot.Kind=="batch-sequence")
        {
            Negatives(session,input,journal,slot.PublicEntry);
            Apply("independent-two-hole-editset",("hole_b",ParameterKey.HoleDiameter,12),("hole_c",ParameterKey.HoleDiameter,8));
            Apply("cross-type-editset",("hole_b",ParameterKey.HoleDiameter,11),("pattern",ParameterKey.PatternSpacing,24));
            if(slot.PublicEntry)Failure("first-edit-failure",First);
        }
        else if(slot.Kind=="batch-publication-faults")
        {
            Failure("first-edit-failure",First);
            foreach(var point in Enum.GetValues<DurableFaultPoint>().Where(p=>p!=DurableFaultPoint.AfterPointerPublish))Failure("publish-"+point,publication:point);
            var previous=session.Store.Load().Revision;setPublish(DurableFaultPoint.AfterPointerPublish);
            var result=Execute(Request(("hole_b",ParameterKey.HoleDiameter,14),("hole_c",ParameterKey.HoleDiameter,10)));setPublish(null);
            journal.Add(new{step="after-pointer-publish-committed",result});
            Program.Check(result.Transaction.Succeeded&&result.Transaction.StateCommitted&&!result.Transaction.RollbackAttempted&&result.Transaction.Revision==previous+1&&result.Checkpoints==1,"Post-commit maintenance was misreported/rolled back.");
            input=AcceptanceFiles.Change(AcceptanceFiles.Change(input,"hole_b",ParameterKey.HoleDiameter,14),"hole_c",ParameterKey.HoleDiameter,10);Durable();PreserveMarker("postpointer");
        }
        else if(slot.Kind=="batch-native-rebuild")
        {
            nativeFault=true;
            Failure("actual-native-rebuild-failure",point=>
            {
                if(point!=ExternalEditFault.FirstEdit)return;
                var f=NativeEditOracle.Resolve(Doc(),input.References["hole_b"]);
                var sketch=NativeEditOracle.Items<IFeature>(f.GetParents()).Single(p=>p.GetTypeName2()=="ProfileFeature");
                var display=(IDisplayDimension)sketch.GetFirstDisplayDimension();var dimension=(IDimension)display.GetDimension2(0);
                Program.Check(display.Type2==(int)swDimensionType_e.swDiameterDimension,"Fault preparation is not a native diameter.");
                var code=dimension.SetSystemValue3(1,(int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration,null);
                journal.Add(new{step="native-engine-fault-preparation",code,readbackMeters=dimension.SystemValue,requestedMeters=1,reason="Unbounded cut removes the entire host; unsaved owned copy only"});
                Program.Check(code==(int)swSetValueReturnStatus_e.swSetValue_Successful,"Native fault dimension refused; cannot claim rebuild failure.");
            });
            Program.Check(nativeRebuild==false,"UNVERIFIED: actual native ForceRebuild3 did not report false; no mock is native proof.");
        }
        else if(slot.Kind=="batch-interrupted")
        {
            setFault(p=>{if(p==ExternalEditFault.Reopen)throw new StateException("INJECTED_CONTROLLER_REOPEN_INTERRUPTION","Stopped before actual OpenDoc6 in commit/rollback; native application preserved.");});
            var result=Execute(Request(("hole_b",ParameterKey.HoleDiameter,14),("hole_c",ParameterKey.HoleDiameter,10)));setFault(null);
            journal.Add(new{step="interrupted-reopen",result});
            Program.Check(!result.Transaction.Succeeded&&!result.Transaction.RollbackSucceeded&&result.Transaction.RollbackAttempted&&result.Checkpoints==1&&session.Store.ReadCurrent().State.Revision==0&&
                session.Status==ReopenStatus.Quarantined,"Interrupted controller was wrongly committed or not isolated.");PreserveMarker("interrupted");
        }
        else if(slot.Kind=="batch-boundary-faults")
        {
            foreach(var point in new[]{ExternalEditFault.Preparation,ExternalEditFault.LastEdit,ExternalEditFault.Postcondition})
            {
                var revision=session.Store.Load().Revision;var native=ManagedRevisionStore.Hash(session.Store.WorkingPath);var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);
                setFault(p=>{if(p==point)throw new StateException("INJECTED_BATCH_BOUNDARY_FAILURE",point.ToString());});
                var result=Execute(Request(("hole_b",ParameterKey.HoleDiameter,14),("hole_c",ParameterKey.HoleDiameter,10)));setFault(null);
                if(point==ExternalEditFault.Preparation)
                {
                    journal.Add(new{step="preparation-failure",result,after=Snapshot()});
                    Program.Check(!result.Transaction.Succeeded&&!result.Transaction.MutationStarted&&result.Checkpoints==0&&result.PartialNativeSteps.Count==0&&
                        session.Store.Load().Revision==revision&&ManagedRevisionStore.Hash(session.Store.WorkingPath)==native&&ManagedRevisionStore.Hash(session.Store.PointerPath)==pointer&&!File.Exists(session.Store.RecoveryPath),"Preparation failure mutated/checkpointed.");
                }
                else
                {
                    Rollback("boundary-"+point,result,revision,native,pointer);
                    Program.Check(result.PartialNativeSteps.Count==2,"Boundary was not reached after both native setters.");
                }
            }
            setFault(p=>{if(p is ExternalEditFault.FirstEdit or ExternalEditFault.Rollback)throw new StateException("INJECTED_ROLLBACK_FAILURE",p.ToString());});
            var isolated=Execute(Request(("hole_b",ParameterKey.HoleDiameter,14),("hole_c",ParameterKey.HoleDiameter,10)));setFault(null);
            journal.Add(new{step="rollback-failure-isolation",result=isolated});
            Program.Check(!isolated.Transaction.Succeeded&&isolated.Transaction.RollbackAttempted&&!isolated.Transaction.RollbackSucceeded&&isolated.Checkpoints==1&&
                isolated.PartialNativeSteps.Count==1&&session.Store.ReadCurrent().State.Revision==0&&session.Status==ReopenStatus.Quarantined,"Failed rollback was not isolated.");
            PreserveMarker("rollback");
            try{session.Edit(Request(("hole_b",ParameterKey.HoleDiameter,14),("hole_c",ParameterKey.HoleDiameter,10)));throw new InvalidOperationException("Quarantined public session accepted another request.");}
            catch(StateException e){Program.Check(e.Code==V03FailureCodes.IncompleteDurablePublish,"Unexpected quarantine refusal.");journal.Add(new{step="quarantined-public-entry-refused",e.Code});}
        }
        else throw new InvalidOperationException("Unknown M14B scenario.");
    }
    private static void Negatives(ExternalPartSession session,OracleInput input,StepJournal journal,bool publicEntry)
    {
        var state=session.CurrentExternal;string Target(string label)=>state.Observation.Features.Single(f=>f.NativeReference?.Base64==input.References[label]).SemanticId;
        ScalarEdit E(string label,ParameterKey key,double value)=>new(Target(label),key,ContractValidation.Unit(key),state.Observation.Features.Single(f=>f.SemanticId==Target(label)).Parameters.Single(p=>p.Key==key).Value,value);
        var valid=new EditSetRequest("0.3",RequestMode.EditSet,ModelOrigin.External,state.Observation.Selection,new[]{E("hole_b",ParameterKey.HoleDiameter,12),E("hole_c",ParameterKey.HoleDiameter,8)});
        var revision=session.Store.Load().Revision;var native=ManagedRevisionStore.Hash(session.Store.WorkingPath);var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);
        foreach(var kind in new[]{"duplicate-pair","missing-second-target","invalid-final-geometry","no-safe-dependency-order","unqualified-parameter","old-value","configuration","revision","source-fingerprint"})
        {
            var request=kind switch{
                "duplicate-pair"=>valid with{Edits=new[]{valid.Edits[0],valid.Edits[0]}},"missing-second-target"=>valid with{Edits=new[]{valid.Edits[0],valid.Edits[1] with{Target="missing"}}},
                "invalid-final-geometry"=>valid with{Edits=new[]{valid.Edits[0] with{Value=300},valid.Edits[1]}},
                "no-safe-dependency-order"=>valid with{Edits=new[]{E("seed",ParameterKey.HoleDiameter,30),E("pattern",ParameterKey.PatternSpacing,40)}},
                "unqualified-parameter"=>valid with{Edits=new[]{valid.Edits[0],valid.Edits[1] with{Parameter=ParameterKey.CutDepth}}},"old-value"=>valid with{Edits=new[]{valid.Edits[0],valid.Edits[1] with{ExpectedOldValue=7}}},
                "configuration"=>valid with{Selection=valid.Selection with{ConfigurationName="Changed"}},"revision"=>valid with{Selection=valid.Selection with{ExpectedRevision=1}},
                _=>valid with{Selection=valid.Selection with{Source=valid.Selection.Source with{Sha256=new string('a',64)}}}};
            if(kind=="no-safe-dependency-order")ExternalEditPlanning.ValidateGeometry(state.Geometry with{Holes=state.Geometry.Holes.Select(h=>h.Target==Target("seed")?h with{DiameterMm=30}:h).ToArray(),Patterns=state.Geometry.Patterns.Select(p=>p with{SpacingMm=40}).ToArray()});
            ExternalEditResult? result=null;string? code=null;
            try
            {
                if(publicEntry)result=session.Edit(request);
                else {var backend=new ExternalEditTransactionBackend(session);var r=new RequestMutationTransaction<ExternalEditCommand,ExternalEditPreparation,ExternalEditRollback>(session.Store,backend).Execute(new(request,null));result=new(r,backend.Checkpoints,backend.PartialNativeSteps,session.Status,true);}
                code=result.Transaction.FailureCode;
            }
            catch(ContractException e){code=e.Code;}
            journal.Add(new{step="batch-preflight-refusal",kind,code,result});
            Program.Check(code is not null&&(result is null||!result.Transaction.Succeeded&&!result.Transaction.MutationStarted&&result.Checkpoints==0&&result.PartialNativeSteps.Count==0),"Invalid EditSet reached checkpoint/setter.");
            if(kind=="no-safe-dependency-order")Program.Check(code=="NO_SAFE_EDIT_ORDER","Not the actual final-legal/intermediate-unsafe boundary.");
        }
        Program.Check(session.Store.Load().Revision==revision&&ManagedRevisionStore.Hash(session.Store.WorkingPath)==native&&ManagedRevisionStore.Hash(session.Store.PointerPath)==pointer&&
            !File.Exists(session.Store.RecoveryPath),"Preflight negatives changed authority.");
    }
}
