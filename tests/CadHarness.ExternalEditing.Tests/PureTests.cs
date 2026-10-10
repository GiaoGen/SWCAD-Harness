using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;

internal static class PureTests
{
    private static readonly List<object> results = new();
    private static string output = "";
    internal static int Run(string root)
    {
        output = Path.Combine(root, "artifacts", "milestone14", "pure", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")); Directory.CreateDirectory(output);
        Test("completed native step survives missing final report",()=>
        {
            var directory=Path.Combine(output,"journal");var journal=new StepJournal(directory,"test/step",new{source="pure"},()=>new{opens=14,closed=14});
            journal.Add(new{phase="setter-return",reference="cHVyZQ==",before=10,readback=12,setter=new ObservedSetterResult("mock enum",0,true)});
            using var record=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"00001.json")));
            Program.Check(record.RootElement.GetProperty("budget").GetProperty("opens").GetInt32()==14&&
                record.RootElement.GetProperty("evidence").GetProperty("setter").GetProperty("ReturnCode").GetInt32()==0&&
                !File.Exists(Path.Combine(directory,"result.json")),"Step depends on controller finalization.");
        });
        Test("step journal never overwrites an interrupted run",()=>
        {
            var directory=Path.Combine(output,"journal_collision");var first=new StepJournal(directory,"test",new{},()=>new{});first.Add(new{value=1});
            var path=Path.Combine(directory,"00001.json");var original=ManagedRevisionStore.Hash(path);
            try {new StepJournal(directory,"test",new{},()=>new{}).Add(new{value=2});}
            catch(IOException){Program.Check(ManagedRevisionStore.Hash(path)==original,"Evidence overwritten.");return;}
            throw new InvalidOperationException("Duplicate evidence write accepted.");
        });
        Test("step evidence sink failure does not mark step complete",()=>
        {
            var path=Path.Combine(output,"journal_not_directory");File.WriteAllText(path,"pure mock");var journal=new StepJournal(path,"test",new{},()=>new{});
            try{journal.Add(new{});}catch(IOException){Program.Check(journal.Count==0,"Undurable step marked complete.");return;}
            throw new InvalidOperationException("Sink failure ignored.");
        });
        Test("remaining schedule uses latest 14 of 27 not historic 2 of 12",()=>
        {
            var plan=new AcceptanceSchedule("0.3","test","unused",Array.Empty<OracleInput>(),new[]{new AcceptanceScenario("core","candidate","core","unused",null,13,false)});
            var budget=new NativeTests.Budget{OpenAttempts=14,MaximumOpenCycles=27};AcceptanceFiles.RequireBudget(plan,budget);
            try{AcceptanceFiles.RequireBudget(plan with{Scenarios=new[]{plan.Scenarios[0] with{MaximumOpens=14}}},budget);}catch(InvalidOperationException){return;}
            throw new InvalidOperationException("Schedule exceeds remaining budget.");
        });
        Test("completed slots are not charged twice but remaining slots must fit",()=>
        {
            var plan=new AcceptanceSchedule("0.3","test","unused",Array.Empty<OracleInput>(),new[]{new AcceptanceScenario("done","candidate","core","unused",null,9,false),new AcceptanceScenario("next","candidate","core","unused",null,4,false)});
            var budget=new NativeTests.Budget{OpenAttempts=23,MaximumOpenCycles=27,AttemptedSteps=new(){"test/done"}};AcceptanceFiles.RequireBudget(plan,budget);
            budget.OpenAttempts=24;try{AcceptanceFiles.RequireBudget(plan,budget);}catch(InvalidOperationException){return;}throw new InvalidOperationException("Unattempted schedule overbooked.");
        });
        Test("controller registration does not erase pending slot from budget check",()=>
        {
            var plan=new AcceptanceSchedule("0.3","test","unused",Array.Empty<OracleInput>(),new[]{new AcceptanceScenario("pending","candidate","core","unused",null,9,false),new AcceptanceScenario("next","candidate","core","unused",null,5,false)});
            var budget=new NativeTests.Budget{OpenAttempts=14,MaximumOpenCycles=27,AttemptedSteps=new(){"test/pending"}};
            try{AcceptanceFiles.RequireBudget(plan,budget,"test","pending");}catch(InvalidOperationException){return;}throw new InvalidOperationException("Pending registered slot escaped full-plan budget.");
        });
        Test("unresolved ownership refuses schedule before COM",()=>
        {
            var plan=new AcceptanceSchedule("0.3","test","unused",Array.Empty<OracleInput>(),Array.Empty<AcceptanceScenario>());
            try{AcceptanceFiles.RequireBudget(plan,new NativeTests.Budget{OwnedTitles=new(){"owned"}});}catch(InvalidOperationException){return;}throw new InvalidOperationException("Owned document ignored.");
        });
        Test("rejected controller releases its ledger lease without native calls",()=>
        {
            var directory=Path.Combine(output,"rejected_lease");Directory.CreateDirectory(directory);
            Program.WriteNew(Path.Combine(directory,"native-budget.json"),new NativeTests.Budget{OwnedTitles=new(){"owned"}});
            Refuse(()=>new NativeTests.Ledger(directory,null!,"never-open").Dispose());
            using var lease=new FileStream(Path.Combine(directory,"native-budget.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var budget=System.Text.Json.JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(Path.Combine(directory,"native-budget.json")))!;
            Program.Check(budget.OpenAttempts==0&&budget.OwnedTitles.Count==1&&budget.AttemptedSteps.Count==0,"Rejected controller changed history.");
        });
        Test("origin sketch is not a consumed circular profile", () => Program.Check(new[] { "OriginProfileFeature", "ProfileFeature" }.Count(ExternalProfileOwnership.IsConsumingProfile) == 1, "Origin incorrectly counted as second profile."));
        Test("verified same-sketch origin coincidence is not an external driver",()=>Program.Check(ExternalProfileOwnership.IsLocalOriginRelation(true,true,true,true,true,true,false),"Local origin rejected."));
        foreach(var rejected in Enumerable.Range(0,7))Test("unknown external origin fact refuses "+rejected,()=>
        {
            var flags=new[]{true,true,true,true,true,true,false};flags[rejected]=!flags[rejected];
            Program.Check(!ExternalProfileOwnership.IsLocalOriginRelation(flags[0],flags[1],flags[2],flags[3],flags[4],flags[5],flags[6]),"Unknown/foreign/context relation permitted.");
        });
        Test("qualified circle contract describes verified driver setter",()=>
        {
            var row=NativeQualificationCandidates.Rows.Single(r=>r.Parameter==ParameterKey.HoleDiameter);
            Program.Check(row.Qualified&&row.WriteContract.Contains("SetSystemValue3",StringComparison.Ordinal)&&!row.WriteContract.Contains("SetRadius",StringComparison.Ordinal),"Scalar descriptor diverges from verified driver handler.");
        });
        Test("real duplicate profiles remain ambiguous", () => Program.Check(new[] { "OriginProfileFeature", "ProfileFeature", "ProfileFeature" }.Count(ExternalProfileOwnership.IsConsumingProfile) == 2, "Duplicate profiles silently accepted."));
        Test("unknown and 3D profile types remain unsupported", () => Program.Check(!ExternalProfileOwnership.IsConsumingProfile("3DProfileFeature") && !ExternalProfileOwnership.IsConsumingProfile("unrecognized"), "Subtype safety relaxed."));
        Test("qualified scalar descriptor permits exact accessor without mutation", () => { using var p = Package("production_gate"); NativeQualificationCandidates.RequireExecutable(p.CurrentExternal.Observation,"hole_a",ParameterKey.HoleDiameter); Program.Check(p.Mutations==0,"Gate mutated native state."); });
        Test("contract-only batch cannot grant native session qualification",()=>{using var p=Package("batch_gate");Refuse(()=>CadHarness.Planning.V03ContractCapabilities.RequireExecutable(Batch(p.CurrentExternal),RequestMode.EditSet));Program.Check(p.Mutations==0,"Batch gate mutated.");});
        Test("qualified atomic entry checks every target before mutation",()=>
        {
            using var p=Package("atomic_gate");var model=p.CurrentExternal.Observation;var request=Batch(p.CurrentExternal);
            Program.Check(NativeQualificationCandidates.AtomicEditSetQualified,"M14B atomic capability is unavailable.");
            NativeQualificationCandidates.RequireExecutable(model,request);
            foreach(var variant in new[]{"missing","unsupported","duplicate","readonly","wrong-accessor"})
            {
                var rejected=variant switch{
                    "missing"=>request with{Edits=new[]{request.Edits[0],request.Edits[1] with{Target="missing"}}},
                    "unsupported"=>request with{Edits=new[]{request.Edits[0],request.Edits[1] with{Parameter=ParameterKey.CutDepth}}},
                    "duplicate"=>request with{Edits=new[]{request.Edits[0],request.Edits[0]}},_=>request};
                var observed=variant is "readonly" or "wrong-accessor"?model with{Features=model.Features.Select(f=>f.SemanticId==request.Edits[1].Target?
                    variant=="readonly"?f with{EditSupport=EditSupport.ReadOnly}:f with{Parameters=f.Parameters.Select(v=>v with{Accessor=NativeAccessor.ExtrudeDepthDirection1}).ToArray()}:f).ToArray()}:model;
                Refuse(()=>NativeQualificationCandidates.RequireExecutable(observed,rejected));
            }
            Program.Check(p.Mutations==0&&!File.Exists(p.Store.RecoveryPath),"Capability gate mutated/checkpointed.");
        });
        foreach(var row in NativeQualificationCandidates.Rows)Test("exact scalar capability pair "+row.Parameter,()=>
        {
            using var p=Package("scalar_gate_"+row.Parameter);var model=p.CurrentExternal.Observation;var target=model.Features.Single(f=>f.SemanticId=="hole_a");
            var parameter=target.Parameters.Single() with{Key=row.Parameter,Accessor=row.Accessor,Unit=ContractValidation.Unit(row.Parameter),Value=row.Parameter==ParameterKey.PatternCount?3:10};
            target=target with{Subtype=row.Subtype,Parameters=new[]{parameter}};model=model with{Features=model.Features.Select(f=>f.SemanticId==target.SemanticId?target:f).ToArray()};
            NativeQualificationCandidates.RequireExecutable(model,target.SemanticId,row.Parameter);
            Refuse(()=>NativeQualificationCandidates.RequireExecutable(model,target.SemanticId,ParameterKey.CutDepth));
            foreach(var variant in new[]{"readonly","suppressed","missing-ref","unknown-dependency","wrong-accessor"})
            {
                var rejected=variant switch{"readonly"=>target with{EditSupport=EditSupport.ReadOnly},"suppressed"=>target with{Health=ObservationHealth.Suppressed},"missing-ref"=>target with{NativeReference=null},"unknown-dependency"=>target with{DependencyCompleteness=EvidenceCompleteness.Unknown},_=>target with{Parameters=new[]{parameter with{Accessor=(NativeAccessor)int.MaxValue}}}};
                Refuse(()=>NativeQualificationCandidates.RequireExecutable(model with{Features=model.Features.Select(f=>f.SemanticId==target.SemanticId?rejected:f).ToArray()},target.SemanticId,row.Parameter));
            }
        });
        Test("native inventory rename and order do not change identity",()=>{using var p=Package("identity_order");var m=p.CurrentExternal.Observation;Program.Check(ExternalInventoryIdentity.Matches(m with{Features=m.Features.Reverse().Select(f=>f with{DisplayName="renamed"}).ToArray()},m),"Tree order/name treated as identity.");});
        Test("native inventory persistent reference drift still refuses",()=>{using var p=Package("identity_drift");var m=p.CurrentExternal.Observation;Program.Check(!ExternalInventoryIdentity.Matches(m with{Features=m.Features.Select((f,i)=>i==0?f with{NativeReference=new("Y2hhbmdlZA==")}:f).ToArray()},m),"Reference drift accepted.");});
        Test("native inventory missing dependency still refuses",()=>{using var p=Package("identity_dependency");var m=p.CurrentExternal.Observation;Program.Check(!ExternalInventoryIdentity.Matches(m with{Dependencies=m.Dependencies.Skip(1).ToArray()},m),"Dependency drift accepted.");});
        Test("budget extension never permits new Parts",()=>{new M14AdditionalBudget("0.3",true,14,12,8,20,0,"Human +8 authorization").Validate();try{new M14AdditionalBudget("0.3",true,14,12,8,20,1,"invalid").Validate();}catch(InvalidOperationException){return;}throw new InvalidOperationException("New Part budget widened.");});
        Test("saved native float noise retains exact intent and raw evidence",()=>{using var p=Package("saved_float");var expected=p.CurrentExternal.Observation;var measured=expected with{Features=expected.Features.Reverse().Select(f=>f with{Parameters=f.Parameters.Select(v=>v.Key==ParameterKey.HoleDiameter?v with{Value=v.Value+1e-12}:v).ToArray()}).ToArray()};var result=ExternalEditPlanning.ReconcileSavedObservation(expected,measured);Program.Check(result.Features.SelectMany(f=>f.Parameters).All(v=>v.Value==expected.Features.SelectMany(f=>f.Parameters).Single(e=>e.SemanticId==v.SemanticId).Value),"Intent float changed.");});
        Test("saved dimension drift outside tolerance still refuses",()=>{using var p=Package("saved_drift");var expected=p.CurrentExternal.Observation;Refuse(()=>ExternalEditPlanning.ReconcileSavedObservation(expected,expected with{Features=expected.Features.Select(f=>f with{Parameters=f.Parameters.Select(v=>v.Key==ParameterKey.HoleDiameter?v with{Value=v.Value+0.01}:v).ToArray()}).ToArray()}));});
        Test("saved accessor drift still refuses",()=>{using var p=Package("saved_accessor");var expected=p.CurrentExternal.Observation;Refuse(()=>ExternalEditPlanning.ReconcileSavedObservation(expected,expected with{Features=expected.Features.Select(f=>f with{Parameters=f.Parameters.Select(v=>v.Key==ParameterKey.HoleDiameter?v with{Accessor=NativeAccessor.ExtrudeDepthDirection1}:v).ToArray()}).ToArray()}));});
        Test("publication preserves identity when native inventory order changes",()=>{using var p=Package("saved_order");p.ReorderOnSave=true;Program.Check(Execute(p).Result.Succeeded,"Ordered association refused stable identities.");});
        Test("strict batch roundtrip", () => { using var p = Package("roundtrip"); var r = Batch(p.CurrentExternal); Program.Check(ContractJson.Read<EditSetRequest>(ContractJson.Write(r, ContractValidation.Edits), ContractValidation.Edits).Edits.Count == 2, "roundtrip"); });
        foreach (var variant in new[] { "empty", "single", "too_many", "duplicate", "unit", "count", "unknown_field", "missing_field", "duplicate_field", "managed_origin", "identity", "revision", "source_hash", "copy_hash", "old_value", "unknown_target", "unsupported_key" })
            Test("reject " + variant, () => { using var p = Package(variant); var r = Batch(p.CurrentExternal); var e = r.Edits[0];
                r = variant switch {
                    "empty" => r with { Edits = Array.Empty<ScalarEdit>() }, "single" => r with { Edits = new[] { e } },
                    "too_many" => r with { Edits = Enumerable.Repeat(e, 17).ToArray() }, "duplicate" => r with { Edits = new[] { e, e } },
                    "unit" => r with { Edits = new[] { e with { Unit = ScalarUnit.Degree }, r.Edits[1] } },
                    "count" => r with { Edits = new[] { new ScalarEdit("pattern", ParameterKey.PatternCount, ScalarUnit.Count, 3, 2.5), r.Edits[1] } },
                    "managed_origin" => r with { Origin = ModelOrigin.Harness },
                    "identity" => r with { Selection = r.Selection with { DocumentId = Guid.NewGuid() } },
                    "revision" => r with { Selection = r.Selection with { ExpectedRevision = 7 } },
                    "source_hash" => r with { Selection = r.Selection with { Source = r.Selection.Source with { Sha256 = new string('a',64) } } },
                    "copy_hash" => r with { Selection = r.Selection with { WorkingCopy = r.Selection.WorkingCopy with { Sha256 = new string('a',64) } } },
                    "old_value" => r with { Edits = new[] { e with { ExpectedOldValue = 11 }, r.Edits[1] } },
                    "unknown_target" => r with { Edits = new[] { e with { Target = "missing" }, r.Edits[1] } },
                    "unsupported_key" => r with { Edits = new[] { e with { Parameter = ParameterKey.CutDepth }, r.Edits[1] } }, _ => r };
                var json = ContractJson.Write(Batch(p.CurrentExternal), ContractValidation.Edits);
                if (variant == "unknown_field") Refuse(() => ContractJson.Read<EditSetRequest>(json.Replace("\"edits\":", "\"nativeApi\":\"SetRadius\",\"edits\":"), ContractValidation.Edits));
                else if (variant == "missing_field") Refuse(() => ContractJson.Read<EditSetRequest>(json.Replace("\"schemaVersion\": \"0.3\",", ""), ContractValidation.Edits));
                else if (variant == "duplicate_field") Refuse(() => ContractJson.Read<EditSetRequest>(json.Replace("\"schemaVersion\":", "\"schemaVersion\":\"0.3\",\"schemaVersion\":"), ContractValidation.Edits));
                else Refuse(() => ExternalEditPlanning.Prepare(p.CurrentExternal, r));
                Program.Check(p.Mutations == 0 && !File.Exists(p.Store.RecoveryPath), "Refusal mutated or checkpointed."); });
        foreach (var v in new[] { "suppressed", "stale", "readonly", "unknown_dependency", "unknown_descendant", "rectangular_pattern", "partial" })
            Test("qualification rejects " + v, () => { using var p = Package(v); var s = p.CurrentExternal; var f = s.Observation.Features.First(x => x.SemanticId == "hole_a");
                f = v switch { "suppressed" => f with { Health = ObservationHealth.Suppressed, EditSupport = EditSupport.ReadOnly },
                    "stale" => f with { Health = ObservationHealth.Stale, EditSupport = EditSupport.ReadOnly }, "readonly" => f with { EditSupport = EditSupport.ReadOnly },
                    "unknown_dependency" => f with { DependencyCompleteness = EvidenceCompleteness.Unknown, EditSupport = EditSupport.ReadOnly }, _ => f };
                s = s with { Observation = s.Observation with { Features = s.Observation.Features.Select(x => x.SemanticId == f.SemanticId ? f : x).ToArray() } };
                if (v == "partial") s = s with { Observation = s.Observation with { InventoryComplete = false, LimitOutcome = V03FailureCodes.ObservationLimitExceeded } };
                if (v == "rectangular_pattern") s = s with { Observation = s.Observation with { Features = s.Observation.Features.Select(x => x.SemanticId == "pattern" ? x with { Subtype = NativeSubtype.TwoDirectionRectangularPattern, EditSupport = EditSupport.ReadOnly } : x).ToArray() } };
                if (v == "unknown_descendant") s = s with { Observation = s.Observation with { Dependencies = s.Observation.Dependencies.Append(new("hole_a", "unknown", NativeDependencyKind.ParentChild, Evidence())).ToArray() } };
                Refuse(() => ExternalEditPlanning.Prepare(s, Batch(s))); });
        Test("prepare all aggregates independent targets once", () => { using var p = Package("aggregate"); var prepared = ExternalEditPlanning.Prepare(p.CurrentExternal, Batch(p.CurrentExternal));
            Program.Check(prepared.Ordered.Count == 2 && prepared.Changes.ChangedFeatures.Count == 2 && prepared.Proposed.Observation.Selection.ExpectedRevision == 1 && p.Mutations == 0, "aggregate"); });
        Test("final boundary rejects before first setter", () => { using var p = Package("boundary"); var r = Batch(p.CurrentExternal); Refuse(() => ExternalEditPlanning.Prepare(p.CurrentExternal, r with { Edits = new[] { r.Edits[0] with { Value = 300 }, r.Edits[1] } })); });
        Test("final overlap rejects before first setter", () => { using var p = Package("overlap"); var r = Batch(p.CurrentExternal); Refuse(() => ExternalEditPlanning.Prepare(p.CurrentExternal, r with { Edits = new[] { r.Edits[0] with { Value = 30 }, r.Edits[1] } })); });
        Test("safe intermediate order uses spacing before count", () => { using var p = Package("order"); var s = p.CurrentExternal;
            var g = s.Geometry with { Holes = new[] { new ExternalHoleContract("hole_a",40,-20,10), new ExternalHoleContract("hole_b",-40,30,10) }, Patterns = new[] { new ExternalPatternContract("pattern","hole_a",2,25,1,0) } };
            s = s with { Geometry = g, Observation = s.Observation with { Features = s.Observation.Features.Select(f => f with { Parameters = f.Parameters.Select(q => q.Key == ParameterKey.PatternCount ? q with { Value = 2 } : q).ToArray() }).ToArray() } };
            var r = new EditSetRequest("0.3",RequestMode.EditSet,ModelOrigin.External,s.Observation.Selection,new[] { new ScalarEdit("pattern",ParameterKey.PatternCount,ScalarUnit.Count,2,4), new ScalarEdit("pattern",ParameterKey.PatternSpacing,ScalarUnit.Millimeter,25,12) });
            Program.Check(ExternalEditPlanning.Prepare(s,r).Ordered[0].Edit.Parameter == ParameterKey.PatternSpacing,"unsafe intermediate order"); });
        Test("final legal batch rejects when native seed dependency forbids safe intermediate order",()=>
        {
            using var p=Package("no_safe_order");var s=p.CurrentExternal;
            ExternalEditPlanning.ValidateGeometry(s.Geometry with{Holes=s.Geometry.Holes.Select(h=>h.Target=="hole_a"?h with{DiameterMm=30}:h).ToArray(),Patterns=s.Geometry.Patterns.Select(pattern=>pattern with{SpacingMm=40}).ToArray()});
            var request=new EditSetRequest("0.3",RequestMode.EditSet,ModelOrigin.External,s.Observation.Selection,new[]{new ScalarEdit("hole_a",ParameterKey.HoleDiameter,ScalarUnit.Millimeter,10,30),new ScalarEdit("pattern",ParameterKey.PatternSpacing,ScalarUnit.Millimeter,25,40)});
            var backend=new ExternalEditTransactionBackend(p);var result=new RequestMutationTransaction<ExternalEditCommand,ExternalEditPreparation,ExternalEditRollback>(p.Store,backend).Execute(new(request,null));
            Program.Check(!result.Succeeded&&result.FailureCode=="NO_SAFE_EDIT_ORDER"&&!result.MutationStarted&&backend.Checkpoints==0&&p.Mutations==0&&!File.Exists(p.Store.RecoveryPath),"Intermediate native order bypassed dependency/clearance.");
        });
        Test("cyclic declared dependencies reject before checkpoint",()=>
        {
            using var p=Package("dependency_cycle");var s=p.CurrentExternal;s=s with{Observation=s.Observation with{Dependencies=s.Observation.Dependencies.Append(new("pattern","hole_a",NativeDependencyKind.ParentChild,Evidence())).ToArray()}};
            Refuse(()=>ExternalEditPlanning.Prepare(s,Batch(s)));Program.Check(p.Mutations==0&&!File.Exists(p.Store.RecoveryPath),"Dependency conflict mutated state.");
        });
        Test("rename not retargeting", () => { using var p = Package("rename"); var s=p.CurrentExternal; s=s with { Observation=s.Observation with { Features=s.Observation.Features.Select(f=>f with { DisplayName="renamed" }).ToArray() } }; Program.Check(ExternalEditPlanning.Prepare(s,Batch(s)).Ordered.Count==2,"name binding"); });
        Test("batch one checkpoint one revision no program", () => { using var p = Package("success"); var result = Execute(p); Program.Check(result.Result.Succeeded && result.Backend.Checkpoints == 1 && result.Backend.PartialNativeSteps.Count == 2 && p.Store.Load().Revision == 1, "single aggregate commit");
            Program.Check(p.Store.ReadCurrent().Manifest.Program is null && p.Store.ReadCurrent().External is not null, "fabricated program"); Refuse(() => _ = p.Store.ReadCurrent().Program); });
        Test("single uses same coordinator", () => { using var p=Package("single_success"); var e=Batch(p.CurrentExternal).Edits[0]; var b=new ExternalEditTransactionBackend(p); var r=new RequestMutationTransaction<ExternalEditCommand,ExternalEditPreparation,ExternalEditRollback>(p.Store,b).Execute(new(null,new("0.3",RequestMode.ExternalScalarEdit,ModelOrigin.External,p.CurrentExternal.Observation.Selection,e))); Program.Check(r.Succeeded&&r.Revision==1&&b.Checkpoints==1,"single"); });
        foreach (var point in Enum.GetValues<ExternalEditFault>().Where(p=>p is not ExternalEditFault.Rollback and not ExternalEditFault.Reopen))
            Test("coordinator fault " + point, () => { using var p=Package("fault_"+point); var original=ManagedRevisionStore.Hash(p.Store.WorkingPath); var pointer=ManagedRevisionStore.Hash(p.Store.PointerPath);
                var r=Execute(p, f=> { if(f==point) throw new StateException("INJECTED_FAILURE",point.ToString()); });
                Program.Check(!r.Result.Succeeded && p.Store.Load().Revision==0 && ManagedRevisionStore.Hash(p.Store.WorkingPath)==original && ManagedRevisionStore.Hash(p.Store.PointerPath)==pointer,"batch-start bytes/state not restored");
                Program.Check(point==ExternalEditFault.Preparation ? !r.Result.MutationStarted&&r.Backend.Checkpoints==0 : r.Result.RollbackSucceeded&&r.Backend.Checkpoints==1,"rollback semantics"); });
        foreach (var point in Enum.GetValues<DurableFaultPoint>().Where(p=>p!=DurableFaultPoint.AfterPointerPublish))
            Test("publication fault " + point, () => { using var p=Package("publish_"+point); var bytes=ManagedRevisionStore.Hash(p.Store.WorkingPath); p.PublishFault=point;
                var r=Execute(p); Program.Check(!r.Result.Succeeded&&r.Result.RollbackSucceeded&&p.Store.Load().Revision==0&&ManagedRevisionStore.Hash(p.Store.WorkingPath)==bytes,"durable rollback"); });
        Test("post-pointer interruption new revision authoritative", () => { using var p=Package("post_pointer"); p.PublishFault=DurableFaultPoint.AfterPointerPublish; Program.Check(Execute(p).Result.Succeeded&&File.Exists(p.Store.RecoveryPath),"commit point");
            var inspection=p.Store.Inspect(p.Store.WorkingPath); Program.Check(inspection.Revision!.State.Revision==1,"wrong authority"); p.Restore(inspection); Program.Check(p.Store.Load().Revision==1,"new revision lost"); });
        Test("rollback failure quarantines no next dispatch", () => { using var p=Package("quarantine"); var r=Execute(p, f=> { if(f is ExternalEditFault.FirstEdit or ExternalEditFault.Rollback) throw new StateException("INJECTED_FAILURE","failure"); });
            Program.Check(!r.Result.RollbackSucceeded&&p.Invalidated&&File.Exists(p.Store.RecoveryPath),"quarantine"); Refuse(()=>p.Store.Load());
            var inspection=p.Store.Inspect(p.Store.WorkingPath); p.Restore(inspection); Program.Check(p.Store.Load().Revision==0,"recovery"); });
        Test("journal authority rejects forged revision", () => { using var p=Package("forged"); p.Store.PrepareCheckpoint(); var i=p.Store.Inspect(p.Store.WorkingPath); Refuse(()=>p.Store.RestoreWorkingCopy(i with { Revision=i.Revision! with { ManifestArtifact=i.Revision!.ManifestArtifact with { Sha256=new string('a',64) } } })); });
        Test("source drift blocks before checkpoint", () => { using var p=Package("drift"); File.AppendAllText(p.CurrentExternal.Observation.Selection.Source.Path,"drift"); var r=Execute(p); Program.Check(!r.Result.Succeeded&&!r.Result.MutationStarted&&r.Backend.Checkpoints==0,"source drift"); });
        Test("package drift blocks before checkpoint", () => { using var p=Package("copy_drift"); File.AppendAllText(p.Store.WorkingPath,"drift"); var r=Execute(p); Program.Check(!r.Result.Succeeded&&!r.Result.MutationStarted,"copy drift"); });
        Test("state hash mismatch refuses cold recovery", () => { using var p=Package("state_drift"); File.AppendAllText(p.Store.ReadCurrent().Manifest.State.Path," "); Program.Check(p.Store.Inspect(p.Store.WorkingPath).FailureCode is not null,"state hash ignored"); });
        Test("exclusive controller lease", () => { using var p=Package("lease"); Refuse(()=>new ManagedRevisionStore(p.Store.Root,p).Dispose()); });
        Program.WriteNew(Path.Combine(output,"result.json"),new { milestone=14,kind="pure mocks; no native CAD proof",passed=results.Count,results });
        Console.WriteLine($"M14 pure: {results.Count}/{results.Count}; {output}"); return 0;
    }
    private static void Test(string name,Action test)
    {
        try { test(); results.Add(new { name,passed=true }); }
        catch(Exception error) { results.Add(new { name,passed=false,error=error.ToString() }); Program.WriteNew(Path.Combine(output,"failed-result.json"),new { milestone=14,results }); throw; }
    }
    private static void Refuse(Action action) { try { action(); } catch(Exception e) when(e is StateException or ContractException) { return; } throw new InvalidOperationException("Expected typed refusal."); }
    private static IReadOnlyList<ObservationEvidence> Evidence()=>new[] { new ObservationEvidence(EvidenceSource.NativeDefinition,"Pure mock evidence; not native qualification.") };
    private static FakeSession Package(string id)=>new(Path.Combine(output,id));
    internal static EditSetRequest Batch(ExternalEditState s)=>new("0.3",RequestMode.EditSet,ModelOrigin.External,s.Observation.Selection,new[] {
        new ScalarEdit("hole_a",ParameterKey.HoleDiameter,ScalarUnit.Millimeter,10,12),new ScalarEdit("hole_b",ParameterKey.HoleDiameter,ScalarUnit.Millimeter,10,8) });
    private static (MutationResult Result,ExternalEditTransactionBackend Backend) Execute(FakeSession p,Action<ExternalEditFault>? fault=null)
    { var b=new ExternalEditTransactionBackend(p,fault); return(new RequestMutationTransaction<ExternalEditCommand,ExternalEditPreparation,ExternalEditRollback>(p.Store,b).Execute(new(Batch(p.CurrentExternal),null)),b); }
    private sealed class FakeSession : IExternalEditSession,IDisposable
    {
        public ManagedRevisionStore Store { get; }
        public ExternalEditState CurrentExternal { get; private set; }
        public int Mutations { get; private set; }
        public bool Invalidated { get; private set; }
        public DurableFaultPoint? PublishFault { get; set; }
        public bool ReorderOnSave {get;set;}
        private readonly Dictionary<(string,ParameterKey),double> live=new();
        internal FakeSession(string root)
        {
            Directory.CreateDirectory(root); var source=Path.Combine(root,"original.SLDPRT"); File.WriteAllText(source,"PURE MOCK BYTES; not a native Part");
            Store=new(Path.Combine(root,"package"),this,f=> { if(PublishFault==f) throw new StateException("INJECTED_PUBLISH_FAILURE",f.ToString()); });
            Store.CopyInitialNative(source); var selection=ExternalObservation.Selection(ManagedRevisionStore.Fingerprint(source),ManagedRevisionStore.Fingerprint(Store.WorkingPath),"Default");
            ObservedFeature Feature(string id,NativeSubtype type,params (ParameterKey Key,double Value,NativeAccessor Accessor)[] ps)=>new(id,id,live.Count,ModelOrigin.External,type.ToString(),type,ObservationHealth.Healthy,new(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(id))),EvidenceCompleteness.Known,EditSupport.Editable,"pure",Evidence(),
                ps.Select(p=>new ObservedParameter(id+"."+p.Key.ToString().ToLowerInvariant(),p.Key,ContractValidation.Unit(p.Key),p.Value,p.Accessor,Evidence())).ToArray(),Array.Empty<GeometryObservation>());
            var features=new[] { Feature("host",NativeSubtype.StraightBlindBossExtrude,(ParameterKey.ExtrusionDepth,10,NativeAccessor.ExtrudeDepthDirection1)),
                Feature("hole_a",NativeSubtype.SingleCircleThroughAllCut,(ParameterKey.HoleDiameter,10,NativeAccessor.SingleCircleRadius)),Feature("hole_b",NativeSubtype.SingleCircleThroughAllCut,(ParameterKey.HoleDiameter,10,NativeAccessor.SingleCircleRadius)),
                Feature("pattern",NativeSubtype.SingleDirectionLinearPattern,(ParameterKey.PatternCount,3,NativeAccessor.LinearPatternDirection1Count),(ParameterKey.PatternSpacing,25,NativeAccessor.LinearPatternDirection1Spacing)),
                new ObservedFeature("unknown","unknown",8,ModelOrigin.External,"nonphysical_diagnostic",NativeSubtype.Unrecognized,ObservationHealth.Unknown,null,EvidenceCompleteness.Unknown,EditSupport.ReadOnly,"Unknown remains visible",Evidence(),Array.Empty<ObservedParameter>(),Array.Empty<GeometryObservation>()) };
            var edges=new[] { new ObservedDependency("host","hole_a",NativeDependencyKind.ParentChild,Evidence()),new("host","hole_b",NativeDependencyKind.ParentChild,Evidence()),new("hole_a","pattern",NativeDependencyKind.ParentChild,Evidence()) };
            CurrentExternal=new("0.3",new("0.3",ModelOrigin.External,selection,true,null,features,edges,Array.Empty<IdentityRemapping>()),new("host",-100,-100,100,100,0,10,new[] { new ExternalHoleContract("hole_a",-40,-20,10),new ExternalHoleContract("hole_b",40,30,10) },new[] { new ExternalPatternContract("pattern","hole_a",3,25,1,0) }));
            ResetLive(); Store.Initialize(ExternalEditPlanning.Adapter(CurrentExternal));
        }
        private void ResetLive() { live.Clear(); foreach(var f in CurrentExternal.Observation.Features) foreach(var p in f.Parameters) live[(f.SemanticId,p.Key)]=p.Value; }
        public void VerifyLive(ExternalEditState expected)
        {
            if(ManagedRevisionStore.Hash(expected.Observation.Selection.Source.Path)!=expected.Observation.Selection.Source.Sha256) throw new StateException(V03FailureCodes.SourceFileDrift,"source");
            foreach(var f in expected.Observation.Features) foreach(var p in f.Parameters) ExternalEditPlanning.Near(live[(f.SemanticId,p.Key)],p.Value,p.Key);
        }
        public void Apply(ExternalPreparedEdit edit) { Mutations++; live[(edit.Edit.Target,edit.Edit.Parameter)]=edit.Edit.Value; }
        public bool RebuildNative()=>true;
        public void Stage(ExternalEditState state)=>CurrentExternal=state;
        public void SaveNative()
        {
            if(Mutations>0) File.WriteAllText(Store.WorkingPath,"PURE MOCK SAVED REVISION "+CurrentExternal.Observation.Selection.ExpectedRevision);
            CurrentExternal=CurrentExternal with { Observation=CurrentExternal.Observation with { Selection=CurrentExternal.Observation.Selection with { WorkingCopy=ManagedRevisionStore.Fingerprint(Store.WorkingPath) } } };
            if(ReorderOnSave)CurrentExternal=CurrentExternal with{Observation=CurrentExternal.Observation with{Features=CurrentExternal.Observation.Features.Reverse().ToArray(),Dependencies=CurrentExternal.Observation.Dependencies.Reverse().ToArray()}};
        }
        public void VerifySavedExternal(ExternalEditState state)=>VerifyLive(state);
        public void Restore(ManagedRecoveryInspection checkpoint)
        { Store.RestoreWorkingCopy(checkpoint); CurrentExternal=checkpoint.Revision!.External!; ResetLive(); Store.CompleteRecovery(checkpoint); }
        public void Invalidate()=>Invalidated=true;
        public void Dispose()=>Store.Dispose();
    }
}
