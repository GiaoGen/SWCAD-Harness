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
        Program.Check(grant.MaximumCumulativeOpens==budget.MaximumOpenCycles||grant.PreviousMaximumOpens==budget.MaximumOpenCycles,"Schedule grant must continue the current audited ceiling.");
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
        DurableFaultPoint? publish=null;bool interrupt=false;string? failure=null;Action<ExternalEditFault>? scenarioFault=null;
        void OnPublish(DurableFaultPoint point)
        {
            reports.Add(new{step="durable-publish-boundary",point});
            if(publish==point)throw new StateException("INJECTED_PUBLISH_FAILURE",point.ToString());
        }
        void OnSessionFault(ExternalEditFault point)
        {
            reports.Add(new{step="session-boundary",point});
            scenarioFault?.Invoke(point);
            if(interrupt&&point==ExternalEditFault.Reopen)throw new StateException("INJECTED_REOPEN_INTERRUPTION","Interrupted before OpenDoc6; recovery marker retained.");
        }
        try
        {
            if(slot.Kind=="origin-driver-probe")
            {
                var working=Path.Combine(slot.Package,"working","CADHarnessManagedPart.SLDPRT");
                Program.Check(connection.Application.GetOpenDocumentByName(input.Source.Path) is null,"Never inspect an engineer's open source.");
                Directory.CreateDirectory(Path.GetDirectoryName(working)!);File.Copy(input.Source.Path,working,false);
                var originalActive=(connection.Application.IActiveDoc2 as IModelDoc2)?.GetTitle();
                ledger.BeforeOpen(working);var errors=0;var warnings=0;
                var probe=connection.Application.OpenDoc6(working,(int)swDocumentTypes_e.swDocPART,(int)(swOpenDocOptions_e.swOpenDocOptions_Silent|swOpenDocOptions_e.swOpenDocOptions_ReadOnly),input.Configuration,ref errors,ref warnings) as IModelDoc2;
                Program.Check(probe is not null,"Probe open failed.");ledger.Opened(working,probe!.GetTitle());
                try
                {
                    Program.Check(errors==0&&warnings==0&&probe.GetPathName()==working&&!probe.GetSaveFlag(),"Probe document identity/open failed.");
                    var visited=new HashSet<string>();
                    void Walk(IFeature? f,bool sub)
                    {
                        while(f is not null)
                        {
                            Program.Check(visited.Count<512,"Probe traversal bound exceeded.");
                            var id=f.Name+"/"+f.GetTypeName2();
                            if(visited.Add(id))
                            {
                                if(f.GetSpecificFeature2() is ISketch sketch)
                                {
                                    reports.Add(new{step="origin-sketch-points",feature=f.Name,nativeType=f.GetTypeName2(),points=NativeEditOracle.Items<ISketchPoint>(sketch.GetSketchPoints2()).Take(32).Select(p=>new{type=p.Type,id=p.GetID(),p.X,p.Y,p.Z,reference=probe.Extension.GetPersistReference3(p) is byte[] b?Convert.ToBase64String(b):null}).ToArray()});
                                    foreach(var filter in new[]{swSketchRelationFilterType_e.swExternal,swSketchRelationFilterType_e.swDefinedInContext})
                                    {
                                        var relations=NativeEditOracle.Items<ISketchRelation>(sketch.RelationManager.GetRelations((int)filter)).ToArray();
                                        object Describe(object e)
                                        {
                                            var bytes=probe.Extension.GetPersistReference3(e) as byte[];var status=-1;var resolved=bytes is null?null:probe.Extension.GetObjectByPersistReference3(bytes,out status);
                                            return new{point=e is ISketchPoint p?new{type=p.Type,id=p.GetID(),x=p.X,y=p.Y,z=p.Z,ownerIsInspectedSketch=ReferenceEquals(p.GetSketch(),sketch)}:null,
                                                reference=bytes is null?null:Convert.ToBase64String(bytes),status,resolved=resolved is not null};
                                        }
                                        reports.Add(new{step="origin-driver-probe",feature=f.Name,nativeType=f.GetTypeName2(),filter=filter.ToString(),count=sketch.RelationManager.GetRelationsCount((int)filter),relations=relations.Select(r=>new{type=r.GetRelationType(),entityTypes=r.GetEntitiesType(),entities=NativeEditOracle.Items<object>(r.GetEntities()).Select(Describe).ToArray(),definition=NativeEditOracle.Items<object>(r.GetDefinitionEntities()).Select(Describe).ToArray(),definition2=NativeEditOracle.Items<object>(r.GetDefinitionEntities2()).Select(Describe).ToArray()}).ToArray()});
                                    }
                                }
                                Walk(f.GetFirstSubFeature() as IFeature,true);
                            }
                            f=(sub?f.GetNextSubFeature():f.GetNextFeature()) as IFeature;
                        }
                    }
                    Walk(probe.FirstFeature() as IFeature,false);
                }
                finally{var title=probe.GetTitle();connection.Application.CloseDoc(title);Program.Check(connection.Application.GetOpenDocumentByName(working) is null,"Probe remained open.");ledger.Closed(working,title);if(originalActive is not null){var e=0;connection.Application.ActivateDoc3(originalActive,false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref e);Program.Check(e==0,"Probe active document restore failed.");}}
            }
            else
            {
            var opened=slot.PreviousPackage is not null||slot.Kind is "candidate-continuation" or "scalar-candidate-v4" or "scalar-public"?ExternalPartSession.Open(connection,slot.Package,Path.Combine(slot.Package,"working","CADHarnessManagedPart.SLDPRT"),ledger,OnPublish,OnSessionFault):
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
                if(slot.Kind.StartsWith("external-",StringComparison.Ordinal))
                    ExternalModelQualification.Run(slot,input,session,connection.Application,reports);
                else if(slot.Kind.StartsWith("batch-",StringComparison.Ordinal))
                    BatchNativeQualification.Run(slot,input,session,connection.Application,reports,output,OnSessionFault,f=>scenarioFault=f,p=>publish=p);
                else if(slot.Kind.StartsWith("scalar-",StringComparison.Ordinal))
                {
                    reports.Add(new{step="scalar-baseline",revision=session.Store.Load().Revision,oracle=ReadOracle(doc!,input),controller=System.Environment.ProcessId});
                    if(slot.Kind=="scalar-candidate-v4")
                    {
                        Program.Check(session.Store.Load().Revision==4&&NativeQualificationCandidates.Rows.All(r=>!r.Qualified),"Candidate must start at revision 4 with public gate closed.");
                        ScalarNegatives(session,input,reports,doc!);
                        Apply("hole-diameter",new[]{("seed",ParameterKey.HoleDiameter,10d)});
                        Apply("pattern-count",new[]{("pattern",ParameterKey.PatternCount,3d)});
                        Apply("pattern-spacing",new[]{("pattern",ParameterKey.PatternSpacing,24d)});
                        var revision=session.Store.Load().Revision;var nativeHash=ManagedRevisionStore.Hash(session.Store.WorkingPath);var pointerHash=ManagedRevisionStore.Hash(session.Store.PointerPath);
                        var command=Command(session,input,new[]{("hole_b",ParameterKey.HoleDiameter,13d)});
                        var injection=ExternalEditPlanning.Prepare(session.CurrentExternal,command.Scalar!).Ordered.Single();
                        var backend=new ExternalEditTransactionBackend(session,f=>{if(f==ExternalEditFault.FirstEdit){Program.Check(session.RebuildNative(),"Pre-injection circle rebuild failed.");session.Apply(injection with{Edit=injection.Edit with{ExpectedOldValue=13,Value=14}});}});
                        reports.Add(new{step="scalar-postcondition-mismatch-start",revision,nativeHash,pointerHash});
                        var result=Execute(session,backend,command);reports.Add(new{step="scalar-postcondition-mismatch",result,backend.Checkpoints,backend.PartialNativeSteps});
                        Program.Check(!result.Succeeded&&result.FailureCode=="STATE_DRIFT_DETECTED"&&result.RollbackSucceeded&&backend.Checkpoints==1&&session.Store.Load().Revision==revision&&
                            ManagedRevisionStore.Hash(session.Store.WorkingPath)==nativeHash&&ManagedRevisionStore.Hash(session.Store.PointerPath)==pointerHash,"Scalar mismatch did not restore exact authoritative bytes.");
                        doc=(IModelDoc2)connection.Application.GetOpenDocumentByName(session.Store.WorkingPath);
                        reports.Add(new{step="scalar-mismatch-restored-oracle",oracle=ReadOracle(doc,input)});
                    }
                    else if(slot.Kind=="scalar-native-boundaries")
                    {
                        var revision=session.Store.Load().Revision;var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);var native=ManagedRevisionStore.Hash(session.Store.WorkingPath);
                        foreach(var kind in new[]{"driven-hole-dimension","geometry-pattern"})
                        {
                            var nativeDoc=(IModelDoc2)connection.Application.GetOpenDocumentByName(session.Store.WorkingPath);
                            var label=kind=="driven-hole-dimension"?"seed":"pattern";
                            var feature=nativeDoc.Extension.GetObjectByPersistReference3(Convert.FromBase64String(input.References[label]),out var error) as IFeature;
                            Program.Check(feature is not null&&error==0,"Negative preparation reference failed.");
                            if(kind=="driven-hole-dimension")
                            {
                                var sketch=NativeEditOracle.Items<IFeature>(feature!.GetParents()).Single(f=>ExternalProfileOwnership.IsConsumingProfile(f.GetTypeName2()));
                                var display=(IDisplayDimension)sketch.GetFirstDisplayDimension();var dimension=(IDimension)display.GetDimension2(0);
                                Program.Check(display.Type2==(int)swDimensionType_e.swDiameterDimension&&dimension.DrivenState==(int)swDimensionDrivenState_e.swDimensionDriving,"Expected unique driving diameter.");
                                dimension.DrivenState=(int)swDimensionDrivenState_e.swDimensionDriven;
                                reports.Add(new{step="native-negative-preparation",kind,dimension.DrivenState,dimension.SystemValue});
                                Program.Check(dimension.DrivenState==(int)swDimensionDrivenState_e.swDimensionDriven,"Native dimension did not become driven.");
                            }
                            else
                            {
                                var data=(ILinearPatternFeatureData)feature!.GetDefinition();Program.Check(data.AccessSelections(nativeDoc,null),"Negative pattern selection failed.");
                                var modified=false;
                                try{data.GeometryPattern=true;modified=feature.ModifyDefinition(data,nativeDoc,null);}
                                finally{if(!modified)data.ReleaseSelectionAccess();nativeDoc.ClearSelection2(true);}
                                reports.Add(new{step="native-negative-preparation",kind,modified,geometryPattern=((ILinearPatternFeatureData)feature.GetDefinition()).GeometryPattern});
                                Program.Check(modified&&((ILinearPatternFeatureData)feature.GetDefinition()).GeometryPattern,"Native geometry pattern preparation failed.");
                            }
                            Program.Check(nativeDoc.EditRebuild3(),"Negative preparation rebuild failed.");
                            foreach(var key in kind=="driven-hole-dimension"?new[]{ParameterKey.HoleDiameter}:new[]{ParameterKey.PatternCount,ParameterKey.PatternSpacing})
                            {
                                var result=session.Edit(Command(session,input,new[]{(label,key,key==ParameterKey.HoleDiameter?9d:key==ParameterKey.PatternCount?4d:24d)}).Scalar!);
                                reports.Add(new{step="native-scalar-boundary-refusal",kind,key,result});
                                Program.Check(!result.Transaction.Succeeded&&!result.Transaction.MutationStarted&&result.Checkpoints==0&&result.PartialNativeSteps.Count==0&&
                                    result.Transaction.FailureCode==(kind=="driven-hole-dimension"?"UNSUPPORTED_PARAMETER_DRIVER":V03FailureCodes.UnsupportedNativeSubtype),"Native scalar boundary did not refuse before setter.");
                            }
                            // Preparation is unsaved. Journal and restore the existing authoritative bytes.
                            session.Store.PrepareCheckpoint();session.Restore(session.Store.Inspect(session.Store.WorkingPath));
                            nativeDoc=(IModelDoc2)connection.Application.GetOpenDocumentByName(session.Store.WorkingPath);
                            reports.Add(new{step="native-negative-restored",kind,oracle=ReadOracle(nativeDoc,input),revision=session.Store.Load().Revision});
                            Program.Check(session.Store.Load().Revision==revision&&ManagedRevisionStore.Hash(session.Store.PointerPath)==pointer&&ManagedRevisionStore.Hash(session.Store.WorkingPath)==native&&
                                !nativeDoc.GetSaveFlag()&&!File.Exists(session.Store.RecoveryPath),"Negative preparation did not restore exact authority.");
                        }
                    }
                    else if(slot.Kind=="scalar-origin")Apply("origin-circle-diameter",new[]{("hole_b",ParameterKey.HoleDiameter,11d)});
                    else if(slot.Kind=="scalar-public")
                    {
                        Program.Check(session.Store.Load().Revision==7&&NativeQualificationCandidates.Rows.All(r=>r.Qualified),"Public scalar run requires completed native qualification and revision 7.");
                        PublicScalarNegatives(session,input,reports,doc!);
                        Apply("public-hole-diameter",new[]{("seed",ParameterKey.HoleDiameter,9d)});
                        Apply("public-pattern-count",new[]{("pattern",ParameterKey.PatternCount,4d)});
                        Apply("public-pattern-spacing",new[]{("pattern",ParameterKey.PatternSpacing,20d)});
                        Apply("public-extrusion-depth",new[]{("host",ParameterKey.ExtrusionDepth,11d)});
                    }
                    else Program.Check(slot.Kind=="scalar-cold"&&session.Store.Load().Revision==(input.Id=="origin-r1"?1:input.Id=="core-public-r11"?11:7)&&!File.Exists(session.Store.RecoveryPath),"Independent scalar cold authority differs.");
                }
                else if(slot.PreviousPackage is not null)
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
    internal static OracleInput ScalarFinal(OracleInput input)=>input with{Depth=12,Holes=input.Holes.Select(h=>h.Label=="seed"?h with{Diameter=10}:h).ToArray(),Pattern=input.Pattern with{Count=3,Spacing=24}};
    internal static OracleInput PublicFinal(OracleInput input)=>input with{Depth=11,Holes=input.Holes.Select(h=>h.Label=="seed"?h with{Diameter=9}:h).ToArray(),Pattern=input.Pattern with{Count=4,Spacing=20}};
    private static void ScalarNegatives(ExternalPartSession session,OracleInput input,StepJournal reports,IModelDoc2 doc)
    {
        var valid=Command(session,input,new[]{("seed",ParameterKey.HoleDiameter,10d)}).Scalar!;
        foreach(var kind in new[]{"invalid-value","invalid-unit","old-value-mismatch","selection-source-fingerprint-mismatch"})
        {
            var request=kind switch{"invalid-value"=>valid with{Edit=valid.Edit with{Value=-1}},"invalid-unit"=>valid with{Edit=valid.Edit with{Unit=ScalarUnit.Degree}},
                "old-value-mismatch"=>valid with{Edit=valid.Edit with{ExpectedOldValue=11}},_=>valid with{Selection=valid.Selection with{Source=valid.Selection.Source with{Sha256=new string('a',64)}}}};
            var backend=new ExternalEditTransactionBackend(session);var result=Execute(session,backend,new(null,request));reports.Add(new{step=kind,result,backend.Checkpoints});
            Program.Check(!result.Succeeded&&!result.MutationStarted&&backend.Checkpoints==0,"Scalar negative reached mutation.");
        }
        var prepared=ExternalEditPlanning.Prepare(session.CurrentExternal,valid).Ordered.Single();var reference=Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("not-a-native-feature"));
        var resolved=doc.Extension.GetObjectByPersistReference3(Convert.FromBase64String(reference),out var error);string? refusal=null;
        try{session.Apply(prepared with{Feature=prepared.Feature with{NativeReference=new(reference)}});}catch(StateException failed){refusal=failed.Code;}
        reports.Add(new{step="unresolvable-native-reference",reference,nativeError=error,resolved=resolved is not null,refusal});
        Program.Check(resolved is null&&error!=0&&refusal=="STALE_REFERENCE"&&!doc.GetSaveFlag(),"Native unresolved reference was not refused before setter.");
    }
    private static ExternalEditCommand Command(ExternalPartSession session,OracleInput input,(string Label,ParameterKey Key,double Value)[] edits)
    {
        var state=session.CurrentExternal;var values=edits.Select(e=>{var f=state.Observation.Features.Single(f=>f.NativeReference?.Base64==input.References[e.Label]);var p=f.Parameters.Single(p=>p.Key==e.Key);return new ScalarEdit(f.SemanticId,e.Key,ContractValidation.Unit(e.Key),p.Value,e.Value);}).ToArray();
        return values.Length==1?new(null,new("0.3",RequestMode.ExternalScalarEdit,ModelOrigin.External,state.Observation.Selection,values[0])):new(new("0.3",RequestMode.EditSet,ModelOrigin.External,state.Observation.Selection,values),null);
    }
    private static void PublicScalarNegatives(ExternalPartSession session,OracleInput input,StepJournal reports,IModelDoc2 doc)
    {
        var revision=session.Store.Load().Revision;var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);var native=ManagedRevisionStore.Hash(session.Store.WorkingPath);
        foreach(var (label,key) in new[]{("host",ParameterKey.ExtrusionDepth),("seed",ParameterKey.HoleDiameter),("pattern",ParameterKey.PatternCount),("pattern",ParameterKey.PatternSpacing)})
        {
            var valid=Command(session,input,new[]{(label,key,key==ParameterKey.PatternCount?4d:10d)}).Scalar!;
            foreach(var kind in new[]{"invalid-value","invalid-unit","unsupported-key","source-fingerprint-mismatch"})
            {
                var request=kind switch{"invalid-value"=>valid with{Edit=valid.Edit with{Value=-1}},"invalid-unit"=>valid with{Edit=valid.Edit with{Unit=ScalarUnit.Degree}},"unsupported-key"=>valid with{Edit=valid.Edit with{Parameter=ParameterKey.CutDepth}},_=>valid with{Selection=valid.Selection with{Source=valid.Selection.Source with{Sha256=new string('a',64)}}}};
                string? refusal=null;ExternalEditResult? result=null;
                try{result=session.Edit(request);refusal=result.Transaction.FailureCode;}catch(ContractException e){refusal=e.Code;}
                reports.Add(new{step="public-scalar-refusal",key,kind,refusal,result});
                Program.Check(refusal is not null&&(result is null||!result.Transaction.MutationStarted&&result.Checkpoints==0),"Public scalar negative mutated native state.");
            }
        }
        string? batchRefusal=null;
        try{session.Edit(Command(session,input,new[]{("hole_b",ParameterKey.HoleDiameter,11d),("hole_c",ParameterKey.HoleDiameter,10d)}).Batch!);}
        catch(ContractException e){batchRefusal=e.Code;}
        reports.Add(new{step="public-editset-gate-closed",batchRefusal});
        Program.Check(batchRefusal==V03FailureCodes.CapabilityUnavailable&&!doc.GetSaveFlag()&&session.Store.Load().Revision==revision&&
            ManagedRevisionStore.Hash(session.Store.PointerPath)==pointer&&ManagedRevisionStore.Hash(session.Store.WorkingPath)==native&&!File.Exists(session.Store.RecoveryPath),"Scalar promotion widened EditSet or negative test changed authority.");
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
