using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static class NativeAcceptance
{
    internal static int Run(string root,string evidence,string schedulePath)
    {
        var parts=evidence.Split('/');Program.Check(parts.Length==2,"Expected frozen-run/slot evidence identity.");
        var run=Path.Combine(root,"artifacts","milestone14",parts[0]);var output=Path.Combine(run,"results",parts[1]);
        Program.Check(!Directory.Exists(output),"Never rerun or overwrite an attempted native slot.");
        AcceptanceFiles.Freeze(Path.Combine(run,"freeze.json"),typeof(Program).Assembly.Location,schedulePath);
        var schedule=AcceptanceFiles.Read<AcceptanceSchedule>(schedulePath);AcceptanceFiles.Validate(schedule);
        var slot=schedule.Scenarios.Single(s=>s.Id==parts[1]);var input=schedule.Inputs.Single(i=>i.Id==slot.Input);
        var grant=AcceptanceFiles.Read<M14AdditionalBudget>(schedule.Authorization);grant.Validate();
        var budgetPath=Path.Combine(root,"artifacts","milestone14","native-budget.json");
        var budget=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(budgetPath))!;
        Program.Check(grant.MaximumCumulativeOpens==budget.MaximumOpenCycles,"Schedule grant must match current audited ceiling; no implicit reset/extension.");
        AcceptanceFiles.RequireBudget(schedule,budget,parts[0]);
        Directory.CreateDirectory(output);
        using var connection=SolidWorksConnection.Connect(false);
        using var ledger=new NativeTests.Ledger(Path.Combine(root,"artifacts","milestone14"),connection.Application,evidence,schedule.Authorization,slot.MaximumOpens);
        AcceptanceFiles.RequireBudget(schedule,ledger.Data,parts[0],parts[1]);
        var before=ledger.Titles();var initialOpens=ledger.Data.OpenAttempts;
        var reports=new StepJournal(Path.Combine(output,"steps"),evidence,
            new{binary=AcceptanceFiles.Identity(typeof(Program).Assembly.Location),freeze=AcceptanceFiles.Identity(Path.Combine(run,"freeze.json")),schedule=AcceptanceFiles.Identity(schedulePath)},
            ()=>new{ledger.Data.OpenAttempts,ledger.Data.DocumentsClosed,ledger.Data.MaximumOpenCycles,owned=ledger.Data.OwnedTitles.ToArray()});
        ExternalPartSession? session=null;
        DurableFaultPoint? publish=null;bool interrupt=false;string? failure=null;
        void OnPublish(DurableFaultPoint point)
        {
            reports.Add(new{step="durable-publish-boundary",point});
            if(publish==point)throw new StateException("INJECTED_PUBLISH_FAILURE",point.ToString());
        }
        void OnSessionFault(ExternalEditFault point)
        {
            reports.Add(new{step="session-boundary",point});
            if(interrupt&&point==ExternalEditFault.Reopen)throw new StateException("INJECTED_REOPEN_INTERRUPTION","Interrupted before OpenDoc6; recovery marker retained.");
        }
        try
        {
            var opened=slot.PreviousPackage is not null||slot.Kind=="candidate-continuation"?ExternalPartSession.Open(connection,slot.Package,Path.Combine(slot.Package,"working","CADHarnessManagedPart.SLDPRT"),ledger,OnPublish,OnSessionFault):
                ExternalPartSession.CreateCopy(connection,slot.Package,input.Source.Path,input.Configuration,ledger,OnPublish,OnSessionFault);
            session=opened.Session;
            if(session is not null)session.EvidenceSink=e=>reports.Add(new{step="native-event",evidence=e});
            reports.Add(new{step="intake",opened.Status,opened.FailureCode,opened.Message,opened.Observation});
            var doc=connection.Application.GetOpenDocumentByName(Path.Combine(slot.Package,"working","CADHarnessManagedPart.SLDPRT")) as IModelDoc2;
            if(slot.Kind=="mandatory-refusal")
            {
                Program.Check(session is null&&opened.Status==ReopenStatus.Inspectable&&opened.FailureCode==(input.EquationCount>0?"UNSUPPORTED_PARAMETER_DRIVER":V03FailureCodes.UnsupportedNativeSubtype),"Negative history was not explicitly withheld before mutation.");
                Program.Check(!File.Exists(Path.Combine(slot.Package,"current.json"))&&!File.Exists(Path.Combine(slot.Package,"recovery.json")),"Refusal published/checkpointed state.");
            }
            else if(session is null)
            {
                Program.Check(slot.Kind=="engineer-history-comparison"&&opened.Status==ReopenStatus.Inspectable,"Required native intake failed: "+opened.Message);
                reports.Add(new{step="old-history",result="Inspectable/read-only; original failure evidence preserved; no claim of edit equivalence"});
            }
            else
            {
                Program.Check(doc is not null,"Owned document missing.");
                if(slot.PreviousPackage is not null)
                {
                    input=CoreFinal(input);Program.Check(session.Store.Load().Revision==4,"Fresh controller recovered wrong authoritative revision.");
                    reports.Add(new{step="fresh-controller",controller=System.Environment.ProcessId,oracle=ReadOracle(doc!,input),recoveryCleared=!File.Exists(session.Store.RecoveryPath)});
                }
                else
                {
                    if(slot.Kind=="candidate-continuation"){input=input with{Depth=12};Program.Check(session.Store.Load().Revision==1,"Continuation must preserve accepted depth revision 1, never replay it.");}
                    reports.Add(new{step="independent-baseline",oracle=ReadOracle(doc!,input)});
                    if(slot.Kind is "candidate-sequence" or "candidate-continuation")
                    {
                        // Runtime entry remains closed while candidates use the production transaction backend.
                        foreach(var row in NativeQualificationCandidates.Rows)Program.Check(!row.Qualified,"Candidate sequence must precede gate promotion.");
                        NegativeContracts(session,input,reports);
                        if(slot.Kind=="candidate-sequence")Apply("depth",new[]{("host",ParameterKey.ExtrusionDepth,12d)});
                        Apply("diameter",new[]{("seed",ParameterKey.HoleDiameter,9d)});
                        Apply("pattern-count-spacing",new[]{("pattern",ParameterKey.PatternCount,4d),("pattern",ParameterKey.PatternSpacing,20d)});
                        Apply("independent-two-target",new[]{("hole_b",ParameterKey.HoleDiameter,12d),("hole_c",ParameterKey.HoleDiameter,12d)});
                        foreach(var fault in new[]{"first","file","state","rebuild","interrupted"})
                        {
                            var nativeHash=ManagedRevisionStore.Hash(session.Store.WorkingPath);var pointerHash=ManagedRevisionStore.Hash(session.Store.PointerPath);var revision=session.Store.Load().Revision;
                            publish=fault=="file"?DurableFaultPoint.AfterNativeSave:fault=="state"?DurableFaultPoint.AfterStateFlush:null;interrupt=fault=="interrupted";
                            object? partial=null;
                            var adapter=fault=="rebuild"?new RebuildFailure(session):null;
                            reports.Add(new{step="fault-start-"+fault,revision,nativeHash,pointerHash});
                            var backend=new ExternalEditTransactionBackend(adapter??(IExternalEditSession)session,f=>{if(fault=="first"&&f==ExternalEditFault.FirstEdit){partial=new{b=NativeEditOracle.DrivingDiameter(doc!,input,"hole_b"),c=NativeEditOracle.DrivingDiameter(doc!,input,"hole_c")};reports.Add(new{step="first-setter-partial",partial});Program.Check(Math.Abs(NativeEditOracle.DrivingDiameter(doc!,input,"hole_b")-14)<1e-6&&Math.Abs(NativeEditOracle.DrivingDiameter(doc!,input,"hole_c")-12)<1e-6,"First-edit injection not between the two setters.");throw new StateException("INJECTED_FIRST_EDIT_FAILURE","First setter changed; second untouched.");}});
                            var result=Execute(session,backend,Command(session,input,new[]{("hole_b",ParameterKey.HoleDiameter,14d),("hole_c",ParameterKey.HoleDiameter,10d)}));
                            reports.Add(new{step="fault-"+fault,result,backend.Checkpoints,backend.PartialNativeSteps,partial,nativeHash,pointerHash,rebuildAdapterInjection=adapter?.Injected??false});
                            Program.Check(!result.Succeeded&&backend.Checkpoints==1&&result.RollbackAttempted,"Fault did not exercise one aggregate checkpoint.");
                            if(interrupt)Program.Check(!result.RollbackSucceeded&&session.Status==ReopenStatus.Quarantined&&File.Exists(session.Store.RecoveryPath),"Interrupted reopen did not invalidate safely.");
                            else
                            {
                                Program.Check(result.RollbackSucceeded&&ManagedRevisionStore.Hash(session.Store.WorkingPath)==nativeHash&&ManagedRevisionStore.Hash(session.Store.PointerPath)==pointerHash&&session.Store.Load().Revision==revision,"Fault did not restore exact batch-start native/state bytes.");
                                doc=(IModelDoc2)connection.Application.GetOpenDocumentByName(session.Store.WorkingPath);
                                reports.Add(new{step="rollback-oracle-"+fault,oracle=ReadOracle(doc,input)});
                            }
                            publish=null;
                        }
                    }
                    else if(slot.PublicEntry)
                    {
                        Program.Check(NativeQualificationCandidates.Rows.All(r=>r.Qualified),"Public edit requires evidence-based promotion first.");
                        if(input.Id=="dev_origin")
                        {
                            var b=NativeEditOracle.Resolve(doc!,input.References["hole_b"]);var c=NativeEditOracle.Resolve(doc!,input.References["hole_c"]);
                            b.Name="NativeIdentityB";c.Name="NativeIdentityC";
                            var moved=doc!.Extension.ReorderFeature(c.Name,b.Name,(int)swMoveLocation_e.swMoveBefore);
                            Program.Check(moved&&doc.ForceRebuild3(false),"Native identity-preserving reorder failed.");
                            reports.Add(new{step="renamed-reordered-native",oracle=ReadOracle(doc,input)});
                            Apply("origin-independent-two-target",new[]{("hole_b",ParameterKey.HoleDiameter,12d),("hole_c",ParameterKey.HoleDiameter,8d)});
                        }
                        else Apply("engineer-four-parameter-batch",new[]{("host",ParameterKey.ExtrusionDepth,12d),("seed",ParameterKey.HoleDiameter,8d),("pattern",ParameterKey.PatternCount,3d),("pattern",ParameterKey.PatternSpacing,20d)});
                    }
                }
            }
            void Apply(string name,(string Label,ParameterKey Key,double Value)[] changes)
            {
                var command=Command(session!,input,changes);var beforeRevision=session!.Store.Load().Revision;
                var expected=changes.Aggregate(input,(s,c)=>AcceptanceFiles.Change(s,c.Label,c.Key,c.Value));
                reports.Add(new{step=name+"-start",command,beforeRevision,nativeFile=AcceptanceFiles.Identity(session.Store.WorkingPath),pointer=AcceptanceFiles.Identity(session.Store.PointerPath)});
                MutationResult result;int checkpoints;IReadOnlyList<string> steps;
                if(slot.PublicEntry){var r=command.Batch is null?session.Edit(command.Scalar!):session.Edit(command.Batch);result=r.Transaction;checkpoints=r.Checkpoints;steps=r.PartialNativeSteps;}
                else {var backend=new ExternalEditTransactionBackend(session);result=Execute(session,backend,command);checkpoints=backend.Checkpoints;steps=backend.PartialNativeSteps;}
                reports.Add(new{step=name,command,result,checkpoints,steps});
                Program.Check(result.Succeeded&&result.StateCommitted&&result.Revision==beforeRevision+1&&checkpoints==1&&steps.Count==changes.Length,"Native mutation not one saved aggregate revision: "+result.Message);
                doc=(IModelDoc2)connection.Application.GetOpenDocumentByName(session.Store.WorkingPath);
                reports.Add(new{step=name+"-saved-cold-readback",oracle=ReadOracle(doc,expected),revision=session.Store.Load().Revision,nativeFile=AcceptanceFiles.Identity(session.Store.WorkingPath),pointer=AcceptanceFiles.Identity(session.Store.PointerPath)});input=expected;
            }
            object ReadOracle(IModelDoc2 document,OracleInput spec)=>NativeEditOracle.Read(document,spec,m=>reports.Add(new{step="oracle-measurement",measurement=m}));
        }
        catch(Exception error){failure=error.ToString();}
        finally
        {
            try{session?.Dispose();}catch(Exception cleanup){failure=(failure??"")+"\nCLEANUP: "+cleanup;}
            foreach(var i in schedule.Inputs)try{AcceptanceFiles.Verify(i.Source);}catch(Exception drift){failure=(failure??"")+"\nSOURCE: "+drift;}
            var after=ledger.Titles();if(!before.OrderBy(x=>x).SequenceEqual(after.OrderBy(x=>x))||ledger.Data.OwnedTitles.Count!=0)failure=(failure??"")+"\nNative document ownership/initial set changed.";
            Program.WriteNew(Path.Combine(output,"result.json"),new{milestone=14,slot,passed=failure is null,failure,controller=System.Environment.ProcessId,utc=DateTime.UtcNow,initialOpens,finalOpens=ledger.Data.OpenAttempts,opensUsed=ledger.Data.OpenAttempts-initialOpens,before,after,activeRestored=session?.OriginalActiveRestored,reports,sourceHashes=schedule.Inputs.Select(i=>new{i.Id,i.Source})});
        }
        Console.WriteLine($"M14 {parts[1]}: {(failure is null?"PASS":"FAIL")}; cumulative opens={ledger.Data.OpenAttempts}/{ledger.Data.MaximumOpenCycles}");if(failure is not null)Console.Error.WriteLine(failure);return failure is null?0:2;
    }
    internal static OracleInput CoreFinal(OracleInput input)=>input with{Depth=12,Holes=input.Holes.Select(h=>h with{Diameter=h.Label=="seed"?9:12}).ToArray(),Pattern=input.Pattern with{Count=4,Spacing=20}};
    private static ExternalEditCommand Command(ExternalPartSession session,OracleInput input,(string Label,ParameterKey Key,double Value)[] edits)
    {
        var state=session.CurrentExternal;var values=edits.Select(e=>{var f=state.Observation.Features.Single(f=>f.NativeReference?.Base64==input.References[e.Label]);var p=f.Parameters.Single(p=>p.Key==e.Key);return new ScalarEdit(f.SemanticId,e.Key,ContractValidation.Unit(e.Key),p.Value,e.Value);}).ToArray();
        return values.Length==1?new(null,new("0.3",RequestMode.ExternalScalarEdit,ModelOrigin.External,state.Observation.Selection,values[0])):new(new("0.3",RequestMode.EditSet,ModelOrigin.External,state.Observation.Selection,values),null);
    }
    private static MutationResult Execute(ExternalPartSession session,ExternalEditTransactionBackend backend,ExternalEditCommand command)=>new RequestMutationTransaction<ExternalEditCommand,ExternalEditPreparation,ExternalEditRollback>(session.Store,backend).Execute(command);
    private static void NegativeContracts(ExternalPartSession session,OracleInput input,StepJournal reports)
    {
        var valid=Command(session,input,new[]{("hole_b",ParameterKey.HoleDiameter,12d),("hole_c",ParameterKey.HoleDiameter,8d)}).Batch!;
        foreach(var kind in new[]{"invalid-value","request-configuration-mismatch","invalid-target-format","duplicate-edit-target"})
        {
            var request=kind switch{
                "invalid-value"=>valid with{Edits=new[]{valid.Edits[0] with{Value=-1},valid.Edits[1]}},
                "request-configuration-mismatch"=>valid with{Selection=valid.Selection with{ConfigurationName="ChangedConfiguration"}},
                "invalid-target-format"=>valid with{Edits=new[]{valid.Edits[0] with{Target="deleted-reference"},valid.Edits[1]}},
                _=>valid with{Edits=new[]{valid.Edits[0],valid.Edits[0]}}};
            var backend=new ExternalEditTransactionBackend(session);var result=Execute(session,backend,new(request,null));
            reports.Add(new{step=kind,result,backend.Checkpoints});Program.Check(!result.Succeeded&&!result.MutationStarted&&backend.Checkpoints==0&&!File.Exists(session.Store.RecoveryPath),"Negative preflight reached native mutation.");
        }
    }
    private sealed class RebuildFailure : IExternalEditSession
    {
        private readonly ExternalPartSession inner;internal bool Injected {get;private set;}
        internal RebuildFailure(ExternalPartSession inner)=>this.inner=inner;
        public ManagedRevisionStore Store=>inner.Store;public ExternalEditState CurrentExternal=>inner.CurrentExternal;
        public void VerifyLive(ExternalEditState s)=>inner.VerifyLive(s);public void Apply(ExternalPreparedEdit e)=>inner.Apply(e);
        public bool RebuildNative(){var actual=inner.RebuildNative();if(!Injected){Injected=true;return false;}return actual;}
        public void Stage(ExternalEditState s)=>inner.Stage(s);public void Restore(ManagedRecoveryInspection c)=>inner.Restore(c);
        public void Invalidate()=>inner.Invalidate();public void SaveNative()=>inner.SaveNative();public void VerifySavedExternal(ExternalEditState s)=>inner.VerifySavedExternal(s);
    }
}
