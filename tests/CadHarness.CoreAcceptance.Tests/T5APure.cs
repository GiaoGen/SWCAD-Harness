using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;

internal static partial class Program
{
    private static int T5APure(string run)
    {
        Check(ContractValidation.Id(run),"Invalid offline run ID");
        var dir=Path.Combine(Root,"artifacts/m20-preparation/T5A",run);var resultPath=Path.Combine(dir,"result.json");
        Check(!File.Exists(resultPath)&&!File.Exists(Path.Combine(dir,"candidate-lock.json")),"Never overwrite T5A evidence");
        var tests=new List<object>();
        void T(string name,bool passed){Check(passed,name);tests.Add(new{name,passed});}
        void Reject(string name,Action action){var refused=false;try{action();}catch(Exception){refused=true;}T(name,refused);}
        var history=Directory.GetFiles(Path.Combine(Root,"artifacts/milestone20"),"*",SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root,"artifacts/m20-preparation/T5"),"*",SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(Root,"artifacts/m20-preparation/T4C"),"*",SearchOption.AllDirectories)).Select(Identity).ToArray();
        Write(Path.Combine(dir,"historical-before.json"),history);
        var candidatePath=Path.Combine(dir,"candidate-lock.json");
        Write(candidatePath,new CandidateLock(1,GitHead(),DateTime.UtcNow,WorkingTree(),CandidateFiles().Select(Identity).OrderBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray()));
        var candidate=Identity(candidatePath);_=VerifyCandidate(candidate);
        var development=DevelopmentCapsuleTask();ValidateCapsuleTask(development);
        T("Development new dimensions/layout/angle/independent depths admitted",development.Expected![0].Slots.Select(s=>s.Depth).Distinct().Count()==3);
        for(var i=0;i<3;i++)
        {
            var spec=development.Expected![i];var measured=SyntheticCapsules(spec);T("Descriptor shape phase "+i,MatchCapsules(spec,measured));
            var moved=spec with{Slots=spec.Slots.Select((s,n)=>n==0?s with{X=s.X+1}:s).ToArray()};
            T("Same volume wrong layout rejected phase "+i,!MatchCapsules(spec,SyntheticCapsules(moved)));
        }
        var initial=development.Expected![0];var good=SyntheticCapsules(initial);
        var lines=good.Lines.Select(l=>l.Select(p=>p.ToArray()).ToArray()).ToArray();lines[0][0][0]+=.2;
        T("Wrong rotated tangent/endpoint rejected",!MatchCapsules(initial,good with{Lines=lines}));
        var cylinders=good.Cylinders.ToArray();var ring=cylinders[0].Rings.ToArray();ring[0]=ring[0] with{Start=new[]{100d,100d,100d}};cylinders[0]=cylinders[0] with{Rings=ring};
        T("Wrong semicircle connection rejected",!MatchCapsules(initial,good with{Cylinders=cylinders}));
        T("Wrong floor/shape rejected",!MatchCapsules(initial,good with{Planes=good.Planes[..^1]}));
        Reject("Untouched slot changed in cold expected",()=>ValidateCapsuleTask(development with{Expected=new[]{initial,development.Expected[1],development.Expected[2] with{Slots=initial.Slots}}}));
        using var a=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"artifacts/milestone14/acceptance-v13/schedule.json")));
        using var b=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"artifacts/milestone14/acceptance-v32/schedule.json")));
        var originals=new[]{a.RootElement.GetProperty("inputs")[0].Deserialize<OracleInput>(Json)! with{Id="core"},b.RootElement.GetProperty("inputs")[0].Deserialize<OracleInput>(Json)! with{Id="manual"}};
        var ext=DevelopmentExternal(originals[1]);ValidateExternalProtocol(ext);T("External visible scalar/batch three phases admitted",ext.Expected.Length==3);
        var bad=ext.Expected.ToArray();bad[2]=bad[2] with{Holes=bad[2].Holes.Select(h=>h.Label=="seed"?h with{X=h.X+1}:h).ToArray()};
        Reject("External untouched position change refused",()=>ValidateExternalProtocol(ext with{Expected=bad}));
        Reject("External action old value mismatch refused",()=>ValidateExternalProtocol(ext with{Actions=new[]{ext.Actions[0] with{Edits=new[]{ext.Actions[0].Edits[0] with{OldValue=999}}},ext.Actions[1]}}));
        var negatives=new[]{new NegativeTask("missing_shaft",Tasks.Request("Make a transmission shaft",new[]{Tasks.Given("axial_lengths",null)}),ConstructionPlanningStatus.NeedsClarification,new[]{"axial_lengths"},null),
            new NegativeTask("missing_fit",Tasks.Request("Make a flanged sleeve guaranteed to fit a 20 mm shaft",new[]{Tasks.Given("fit_tolerance",null)}),ConstructionPlanningStatus.NeedsClarification,new[]{"fit_tolerance"},null)};
        foreach(var n in negatives)
        {
            var response=new ConstructionPlanResponse("0.3",PlanOutcome.NeedsClarification,ConstructionRequirements.Normalize(n.Request),null,n.RequiredQuestionFragments,null,Array.Empty<GeometryRequirementBinding>());
            var r=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),new ReplaySource(ContractJson.Write(response,ConstructionRequirements.ValidateResponse))).PlanAsync(n.Request).GetAwaiter().GetResult();
            T(n.Id+" exact critical clarification offline zero inference",NegativePassed(n,r)&&r.ModelCalls==0);
            T(n.Id+" arbitrary refusal not accepted",!NegativePassed(n,r with{Status=ConstructionPlanningStatus.Unsupported}));
            T(n.Id+" wrong missing fact not accepted",!NegativePassed(n,r with{Questions=new[]{"unrelated"}}));
        }
        var pkgDir=Path.Combine(dir,"development-package");var p0=Path.Combine(pkgDir,"p0.json");var external=Path.Combine(pkgDir,"external.json");
        Write(p0,development);Write(external,ext);
        var packagePath=Path.Combine(pkgDir,"package.json");
        Write(packagePath,new MaintainerPackage(1,"development_example",true,"adapter developer; NOT independent held-out",DateTime.UtcNow,true,
            "Development-visible explicit arithmetic/geometry; NOT formal held-out",candidate,Identity(p0),Identity(external)));
        var sealPath=Path.Combine(pkgDir,"seal.json");Write(sealPath,Identity(packagePath));
        var reuse=new[]{"defaults-two-native","native-fault-recovery","external-history-robustness"}.Select(key=>new RegressionEvidence(key,
            "DEVELOPMENT syntax exercise only; actual formal reuse requires audited requirement-specific mapping",new[]{Identity(Path.Combine(Root,"artifacts/m20-preparation/T5/development_v5/summary-20261012T170205091.json"))},
            VerifyCandidate(candidate).Files.Where(f=>f.Path.StartsWith(Path.Combine(Root,"src"),StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(f.Path)==".cs").ToArray())
            {Assertions=new[]{new EvidenceAssertion(Identity(Path.Combine(Root,"artifacts/m20-preparation/T5/development_v5/summary-20261012T170205091.json")),"/status","\"DEVELOPMENT_PASS\"")},
                CompatibilityFreeze=Identity(Path.Combine(Root,"artifacts/m20-preparation/T5/development_v4/freeze.json"))}).ToArray();
        Write(Path.Combine(dir,"reuse-map-draft.json"),ActualReuseMap(candidate));
        var slots=FormalSchedule(development.Id,ext.Initial.Id,negatives);
        T("Four new deterministic tasks",slots.Count(s=>s.Gate=="deterministic")==4&&slots.Where(s=>s.Gate=="deterministic").All(s=>s.MaximumParts==1));
        T("Exactly three real per family fixed denominator twelve",slots.Count(s=>s.Gate=="core-real")==12&&new[]{"C1","C2","C3","C4"}.All(f=>slots.Count(s=>s.Gate=="core-real"&&s.Task==f)==3));
        T("Fresh separate P0/external paired plus negatives/regressions",slots.Count(s=>s.Gate=="held-p0")==1&&slots.Count(s=>s.Gate=="held-external")==1&&slots.Count(s=>s.Gate=="negative")==2&&slots.Count(s=>s.Gate=="external-regression")==2);
        var manifest=new FormalManifest(1,run,true,candidate,Identity(packagePath),Identity(sealPath),FormalProvider,originals,negatives,reuse,slots.Sum(s=>s.MaximumParts),slots.Sum(s=>s.MaximumOpens));
        var manifestPath=Path.Combine(pkgDir,"manifest.json");Write(manifestPath,manifest);_=LoadFormal(manifestPath,true);
        T("Development manifest fully loads offline",true);
        Reject("Development example forbidden as formal",()=>LoadFormal(manifestPath));
        void Invalid(string name,FormalManifest value)
        {
            var path=Path.Combine(pkgDir,name+".json");Write(path,value);Reject(name,()=>LoadFormal(path,true));
        }
        Invalid("missing_held_out",manifest with{Package=manifest.Package with{Path=Path.Combine(pkgDir,"absent.json")}});
        var falseSeal=Path.Combine(pkgDir,"bad-seal.json");Write(falseSeal,manifest.Package with{Sha256=new string('0',64)});
        Invalid("bad_seal",manifest with{PackageSeal=Identity(falseSeal)});
        Invalid("wrong_version",manifest with{Version=2});Invalid("provider_drift",manifest with{Provider=FormalProvider with{MaximumOutputTokens=8192}});
        Invalid("candidate_source_fingerprint_drift",manifest with{Candidate=candidate with{Sha256=new string('0',64)}});
        Invalid("reuse_missing_result_assertion",manifest with{Regression=reuse.Select((r,i)=>i==0?r with{Assertions=Array.Empty<EvidenceAssertion>()}:r).ToArray()});
        Invalid("reuse_wrong_result_assertion",manifest with{Regression=reuse.Select((r,i)=>i==0?r with{Assertions=new[]{r.Assertions[0] with{ExpectedJson="\"PASS\""}}}:r).ToArray()});
        var unknown=JsonSerializer.SerializeToNode(manifest,Json)!.AsObject();unknown["executionAuthorized"]=true;
        var unknownPath=Path.Combine(pkgDir,"unknown-field.json");Write(unknownPath,unknown);Reject("Unknown manifest fields cannot grant authority",()=>LoadFormal(unknownPath,true));
        var tuned=Path.Combine(pkgDir,"tuned-package.json");Write(tuned,Read<MaintainerPackage>(packagePath) with{Untuned=false});var tunedSeal=Path.Combine(pkgDir,"tuned-seal.json");Write(tunedSeal,Identity(tuned));
        Invalid("tuned_package",manifest with{Package=Identity(tuned),PackageSeal=Identity(tunedSeal)});
        var beforeLock=Path.Combine(pkgDir,"before-lock-package.json");Write(beforeLock,Read<MaintainerPackage>(packagePath) with{SealedUtc=DateTime.MinValue});var earlySeal=Path.Combine(pkgDir,"before-lock-seal.json");Write(earlySeal,Identity(beforeLock));
        Invalid("seal_before_code_lock",manifest with{Package=Identity(beforeLock),PackageSeal=Identity(earlySeal)});
        Output=Path.Combine(dir,run);Development=false;
        var previous=Environment.GetEnvironmentVariable("CAD_HARNESS_T5_REUSE_RUN");Environment.SetEnvironmentVariable("CAD_HARNESS_T5_REUSE_RUN","development_v5");
        try{Reject("Development environment reuse cannot leak",()=>FreezeFormal(manifestPath,true));}finally{Environment.SetEnvironmentVariable("CAD_HARNESS_T5_REUSE_RUN",previous);}
        _=FreezeFormal(manifestPath,true);var frozen=Read<FreezeRecord>(Path.Combine(Output,"freeze.json"));VerifyFreeze(frozen);
        var p1=Read<PlannerPrompt>(Path.Combine(Output,"prompts/C1-real-1.json"));var p2=Read<PlannerPrompt>(Path.Combine(Output,"prompts/C1-real-2.json"));
        T("Same family uses same frozen semantic prompt/schema",p1==p2&&p1.ResponseSchemaJson.Contains("externalRecesses",StringComparison.Ordinal));
        T("Formal adapter freeze zero provider/native",!File.Exists(Ledger)&&!Directory.Exists(Path.Combine(Output,"slots")));
        Reject("Sample cannot authorize Execute",()=>RequireAuthorization(frozen,"absent.json"));
        PureReplay(tests,dir,T,Reject);PureFullReplay(frozen,T,Reject);
        PureFormalScore(T);
        PureFormalBudget(dir,T,Reject);
        foreach(var f in history)VerifyIdentity(f);
        Write(Path.Combine(dir,"historical-after.json"),new{unchanged=true,files=history.Length,sha256=HashText(Canonical(history)),originalM20Ledger=Identity(Path.Combine(Root,"artifacts/milestone20/native-budget.json"))});
        Write(resultPath,new{status="ADAPTER_COMPLETE",passed=true,count=tests.Count,tests,developmentOnly=true,providerCalls=0,nativeCalls=0,
            candidate,manifest=Identity(manifestPath),phaseB="PENDING_INDEPENDENT_MAINTAINER",technicalReadiness="NOT_READY",executionAuthorized=false,budgetApproval="PENDING"});
        Console.WriteLine(resultPath);return 0;
    }
    private static TaskDefinition DevelopmentCapsuleTask()
    {
        var old=Definitions().Single(t=>t.Id=="C4");var values=new Dictionary<string,double>{{"width",112},{"height",72},{"thickness",10},{"slot_width",8},{"slot_length",24},{"slot_depth",4},
            {"slot0_x",-35},{"slot0_y",-4},{"slot1_x",0},{"slot1_y",3},{"slot2_x",34},{"slot2_y",-2},{"slot_angle",113}};
        var input=old.Request.Input with{Intent="Mounting plate 112 by 72 by 10 mm; three independent 8 by 24 mm blind capsule cuts, world long-axis 113 degrees from +X, centered (-35,-4), (0,3), (34,-2) mm with depths 4,5,3.5 mm. Independently edit the first cut depth to 4.5 mm.",
            Requirements=old.Request.Input.Requirements.Select(r=>values.TryGetValue(r.SemanticKey,out var v)?r with{Value=v}:r).Append(Tasks.Given("second_depth",5)).Append(Tasks.Given("third_depth",3.5)).ToArray()};
        var request=old.Request with{Input=input,EditabilityRequirements=old.Request.EditabilityRequirements!.Select(e=>e with{TestValue=4.5}).ToArray()};
        var g=new CapsuleGeometry(112,72,10,new[]{new CapsuleSlot("first",-35,-4,8,24,23,4),new("second",0,3,8,24,23,5),new("third",34,-2,8,24,23,3.5)});
        return new("development_slots","C4",input.Intent,null!,request,4,4.5,4.25,""){Expected=new[]{g,g with{Slots=g.Slots.Select((s,i)=>i==0?s with{Depth=4.5}:s).ToArray()},g with{Slots=g.Slots.Select((s,i)=>i==0?s with{Depth=4.25}:s).ToArray()}}};
    }
    private static ExternalProtocol DevelopmentExternal(OracleInput original)
    {
        var s=original with{Id="development_external"};var scalar=new ExternalAction("scalar",new[]{new ExternalValue("host",ParameterKey.ExtrusionDepth,s.Depth,s.Depth+2)});
        var edited=Changed(s,"host",ParameterKey.ExtrusionDepth,s.Depth+2);
        var batch=new ExternalAction("batch",new[]{new ExternalValue("hole_b",ParameterKey.HoleDiameter,s.Holes.Single(h=>h.Label=="hole_b").Diameter,s.Holes.Single(h=>h.Label=="hole_b").Diameter+1),
            new("hole_c",ParameterKey.HoleDiameter,s.Holes.Single(h=>h.Label=="hole_c").Diameter,s.Holes.Single(h=>h.Label=="hole_c").Diameter+1.5)});
        var final=edited;foreach(var e in batch.Edits)final=Changed(final,e.Label,e.Parameter,e.NewValue);
        return new(s,new[]{scalar,batch},new[]{s,edited,final},false);
    }
    private static CapsuleMeasurement SyntheticCapsules(CapsuleGeometry g)
    {
        var cylinders=new List<MeasuredCylinder>();var lines=new List<double[][]>();var planes=new List<MeasuredPlane>();
        for(var i=0;i<12;i++)planes.Add(new(new[]{1d,0,0},1,new double[6]));
        foreach(var s in g.Slots)
        {
            var a=s.Angle*Math.PI/180;var u=new[]{Math.Cos(a),Math.Sin(a)};var v=new[]{-Math.Sin(a),Math.Cos(a)};var h=(s.Length-s.Width)/2;var r=s.Width/2;var bottom=g.Thickness-s.Depth;
            foreach(var end in new[]{-1,1})
            {
                var x=s.X+end*h*v[0];var y=s.Y+end*h*v[1];var rings=new[]{bottom,g.Thickness}.Select(z=>new MeasuredRing(new[]{x/1000,y/1000,z/1000,0,0,1,r/1000},
                    new[]{x-r*u[0],y-r*u[1],z},new[]{x+r*u[0],y+r*u[1],z})).ToArray();
                cylinders.Add(new(new[]{x/1000,y/1000,0,0,0,1,r/1000},Math.PI,rings));
            }
            foreach(var z in new[]{bottom,g.Thickness})foreach(var sign in new[]{-1,1})lines.Add(new[]{new[]{s.X+sign*r*u[0]-h*v[0],s.Y+sign*r*u[1]-h*v[1],z},new[]{s.X+sign*r*u[0]+h*v[0],s.Y+sign*r*u[1]+h*v[1],z}});
            planes.Add(new(new[]{0d,0,1},s.Width*(s.Length-s.Width)+Math.PI*r*r,new[]{s.X-10,s.Y-10,bottom,s.X+10,s.Y+10,bottom}));
        }
        return new(g.Width*g.Height*g.Thickness-g.Slots.Sum(s=>s.Depth*(s.Width*(s.Length-s.Width)+Math.PI*s.Width*s.Width/4)),
            new[]{-g.Width/2,-g.Height/2,0,g.Width/2,g.Height/2,g.Thickness},cylinders.ToArray(),lines.ToArray(),planes.ToArray(),true);
    }
    private static void PureReplay(List<object> tests,string dir,Action<string,bool> t,Action<string,Action> reject)
    {
        foreach(var (task,name) in Definitions().Zip(new[]{"shaft","sleeve","bracket","slots"}))
        {
            var file=Path.Combine(Root,"artifacts/m20-preparation/T4C/live_v2",name,"call-01-response.json");var original=Identity(file);
            var envelope=Read<JsonElement>(file);var raw=RawSemantic(envelope);var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();
            var accepted=new ConstructionPlanner(runtime,new ReplaySource(raw)).PlanAsync(task.Request).GetAwaiter().GetResult();
            Check(accepted.Preview is not null,"Historical raw response no longer accepted: "+name);
            var plan=JsonSerializer.SerializeToElement(accepted,Json);
            var binding=new ReplayBinding(1,name,original,original,original,HashText(Canonical(task.Request)),"offline-unit-prompt",HashText(raw),HashText(accepted.Preview!.ResponseJson),HashText(accepted.Preview.CompilationJson!));
            var replay=ReplayAccepted(task.Request,raw,binding,plan,runtime);
            t(name+" original T4C raw bytes formal helper accepted",replay.Preview is not null&&replay.ModelCalls==0);
            reject(name+" expanded wrong DTO refused",()=>ReplayAccepted(task.Request,accepted.Preview.ResponseJson,binding with{RawSemanticSha=HashText(accepted.Preview.ResponseJson)},plan,runtime));
            reject(name+" missing raw refused",()=>ReplayAccepted(task.Request,"",binding,plan,runtime));
            reject(name+" tampered raw refused",()=>ReplayAccepted(task.Request,raw+" ",binding,plan,runtime));
            reject(name+" wrong request refused",()=>ReplayAccepted(task.Request with{Input=task.Request.Input with{Intent="changed"}},raw,binding,plan,runtime));
            reject(name+" wrong version refused",()=>ReplayAccepted(task.Request,raw,binding with{Version=2},plan,runtime));
            reject(name+" source-map drift refused",()=>ReplayAccepted(task.Request,raw,binding with{CompilationSha=new string('0',64)},plan,runtime));
            reject(name+" IR drift refused",()=>ReplayAccepted(task.Request,raw,binding with{ExpandedSha=new string('0',64)},plan,runtime));
            VerifyIdentity(original);tests.Add(new{evidence=original,requestSha=binding.RequestSha,rawSemanticSha=binding.RawSemanticSha,expandedSha=binding.ExpandedSha,compilationSha=binding.CompilationSha});
        }
        reject("Missing original provider envelope",()=>RawSemantic(JsonSerializer.SerializeToElement(new{response=new{Json="{}"}})));
        var rawOriginal=Read<JsonElement>(Path.Combine(Root,"artifacts/m20-preparation/T4C/live_v2/shaft/call-01-response.json"));
        var structured=rawOriginal.GetProperty("response").Deserialize<StructuredPlanResponse>(Json)!;
        reject("Raw semantic does not match actual provider body",()=>RawSemantic(JsonSerializer.SerializeToElement(new{response=structured with{Json=structured.Json+" "}},Json)));
    }
    private static void PureFormalScore(Action<string,bool> t)
    {
        var negatives=new[]{new NegativeTask("missing_shaft",null!,0,Array.Empty<string>(),null),new NegativeTask("missing_fit",null!,0,Array.Empty<string>(),null)};
        var rows=FormalSchedule("sample_p0","sample_external",negatives).Where(s=>s.Gate!="pair").Select(s=>new FormalRow(s.Id,s.Gate,s.Task,
            s.Gate=="core-real"&&s.Id.EndsWith("-3",StringComparison.Ordinal)?"FAIL":"PASS",true,true,s.Mode=="real")).ToArray();
        var score=ScoreFormal(rows,true);t("Four contained FAIL rows retained with 8/12 release PASS",score.Status=="PASS"&&score.RealTotal==8&&rows.Count(r=>r.Status=="FAIL")==4);
        var one=rows.Select(r=>r.Id=="C1-real-2"?r with{Status="FAIL"}:r.Id=="C2-real-3"?r with{Status="PASS"}:r).ToArray();
        t("8/12 with 1/3 family fails",ScoreFormal(one,true).Status=="FAIL");
        t("7/12 fails",ScoreFormal(rows.Select(r=>r.Id=="C1-real-2"?r with{Status="FAIL"}:r).ToArray(),true).Status=="FAIL");
        t("Interrupted prevents PASS",ScoreFormal(rows.Select(r=>r.Id=="C1-real-3"?r with{Status="STOPPED"}:r).ToArray(),true).Status=="STOPPED");
        t("Missing held-out prevents PASS",ScoreFormal(rows.Select(r=>r.Gate=="held-p0"?r with{Status="NOT_RUN"}:r).ToArray(),true).Status=="PARTIAL");
        t("Resource/cleanup failure blocks",ScoreFormal(rows.Select(r=>r.Id=="C1-real-3"?r with{Contained=false}:r).ToArray(),true).Status=="BLOCKED");
        t("Identity/seal drift blocks",ScoreFormal(rows.Select(r=>r.Id=="C1-real-3"?r with{Integrity=false}:r).ToArray(),true).Status=="BLOCKED");
        t("Invalid reuse blocks release",ScoreFormal(rows,false).Status=="FAIL");
        t("Fixture cannot masquerade as live PASS",ScoreFormal(rows.Select(r=>r.Gate=="core-real"?r with{Live=false}:r).ToArray(),true).Status!="PASS");
    }
    private static void PureFullReplay(FreezeRecord freeze,Action<string,bool> t,Action<string,Action> reject)
    {
        var slot=freeze.Slots.Single(s=>s.Id=="C1-real-1");var dir=Path.Combine(Output,"slots",slot.Id);
        var original=Path.Combine(Root,"artifacts/m20-preparation/T4C/live_v2/shaft/call-01-response.json");var envelope=Read<JsonElement>(original);
        var raw=RawSemantic(envelope);var request=freeze.Tasks.Single(task=>task.Id==slot.Task).Request;
        var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();var plan=new ConstructionPlanner(runtime,new ReplaySource(raw)).PlanAsync(request).GetAwaiter().GetResult();
        var prompt=Read<PlannerPrompt>(Path.Combine(Output,"prompts",slot.Id+".json"));var utc=envelope.GetProperty("utc").GetDateTime();
        Write(Path.Combine(dir,"provider-request.json"),new{utc=utc.AddSeconds(-20),prompt,requestedModel=FormalProvider.Alias,provider=FormalProvider.Endpoint,settings=FormalProvider,store=false,noRetry=true});
        Directory.CreateDirectory(dir);File.Copy(original,Path.Combine(dir,"provider-response.json"),false);
        var offline=plan with{ModelCalls=1,Provenance="live_provider"};Write(Path.Combine(dir,"plan.json"),offline);Write(Path.Combine(dir,"planning-result.json"),new{planningPassed=true,contained=true});
        BindReplay(freeze,slot,offline,prompt);
        Write(Path.Combine(dir,"planning-seal.json"),Directory.GetFiles(dir,"*.json").Select(Identity).ToArray());
        // Unit-only freeze time allows exercising the complete path using archived real bytes.
        // The actual new freeze MUST refuse this historical response as a new sample.
        var unitFreeze=freeze with{Utc=utc.AddMinutes(-2)};
        _=VerifyReplay(unitFreeze,slot);t("Full frozen real loader accepts archived bytes only in offline historical context",true);
        reject("Actual new freeze cannot count historical call",()=>VerifyReplay(freeze,slot));
        var b=Read<ReplayBinding>(Path.Combine(dir,"replay-binding.json"));
        reject("Wrong frozen prompt binding refused",()=>Check(b.PromptSha==HashText(Canonical(prompt with{Intent="tampered"})),"Prompt drift"));
        Write(Path.Combine(dir,"offline-test-only.json"),new{developmentVisible=true,newProviderCalls=0,source=Identity(original),unitMetadataSynthesized=true,notFormalAttempt=true});
    }
    private static void PureFormalBudget(string dir,Action<string,bool> t,Action<string,Action> reject)
    {
        var path=Path.Combine(dir,"pure-reservations");var approval=new ExecutionAuthorization(1,"pure_run",null!,"OFFLINE UNIT ONLY",DateTime.UtcNow,true,1,3,
            new(){{"sample",new(1,3)}});
        new FormalReservations(path,approval,"sample","native").Reserve("create");
        new FormalReservations(path,approval,"sample","native").Reserve("open");
        new FormalReservations(path,approval,"sample","rollback").Reserve("open");
        new FormalReservations(path,approval,"sample","cold").Reserve("open");
        var count=Directory.GetFiles(path,"*-reservation.json").Length;
        reject("Cold/read cross-controller global open limit",()=>new FormalReservations(path,approval,"sample","read").Reserve("open"));
        reject("Creation limit across workers",()=>new FormalReservations(path,approval,"sample","native").Reserve("create"));
        t("Rejected reservations zero new event/native call",Directory.GetFiles(path,"*-reservation.json").Length==count);
        var slot=new ExecutionAuthorization(1,"pure_slot",null!,"UNIT",DateTime.UtcNow,true,3,5,new(){{"one",new(0,1)},{"two",new(1,1)}});
        var p=Path.Combine(dir,"pure-slot-limit");new FormalReservations(p,slot,"one","native").Reserve("open");
        reject("Per-slot limit enforced before aggregate exhaustion",()=>new FormalReservations(p,slot,"one","cold").Reserve("open"));
        reject("Zero-create slot cannot create",()=>new FormalReservations(p,slot,"one","native").Reserve("create"));
        new FormalReservations(p,slot,"two","native").Reserve("open");
        var immutable=Path.Combine(dir,"create-new.json");Write(immutable,new{first=true});reject("Stage/log evidence cannot be overwritten",()=>Write(immutable,new{first=false}));
        t("CreateNew original evidence retained",Read<JsonElement>(immutable).GetProperty("first").GetBoolean());
        var actualPath=Path.Combine(dir,"pure-lifecycle/native-budget.json");var reservationsPath=Path.Combine(dir,"pure-lifecycle/reservations");var calls=0;
        using(var budget=new CadHarness.SolidWorks.Tests.NativeTestBudget(actualPath,"PURE NO NATIVE API",1))
        {
            var lifecycle=new Lifecycle(budget,(_,_)=>{},new FormalReservations(reservationsPath,approval,"sample","native"),3);
            lifecycle.BeforeCreate();calls++;
            lifecycle.BeforeOpen("fake-no-native");calls++;
        }
        using(var budget=new CadHarness.SolidWorks.Tests.NativeTestBudget(actualPath,"PURE NO NATIVE API",1))
        {
            var lifecycle=new Lifecycle(budget,(_,_)=>{},new FormalReservations(reservationsPath,approval,"sample","rollback-cold"),3);
            lifecycle.BeforeOpen("fake-no-native");calls++;lifecycle.BeforeOpen("fake-no-native");calls++;
            reject("Actual lifecycle refuses before API callback",()=>{lifecycle.BeforeOpen("fake-no-native");calls++;});
            reject("Actual lifecycle refuses create before API callback",()=>{lifecycle.BeforeCreate();calls++;});
            t("Actual budget and reservations persist across worker instances",budget.Snapshot.CreationAttempts==1&&budget.Snapshot.OpenAttempts==3&&calls==4);
        }
    }
    private static RegressionEvidence[] ActualReuseMap(FrozenFile candidate)
    {
        var source=VerifyCandidate(candidate).Files.Where(f=>f.Path.StartsWith(Path.Combine(Root,"src"),StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(f.Path)==".cs").ToArray();
        var baseline=Identity(Path.Combine(Root,"artifacts/m20-preparation/T5/development_v4/freeze.json"));
        RegressionEvidence Evidence(string requirement,string basis,params (string Path,string Pointer,string Value)[] proofs)
        {
            var assertions=proofs.Select(p=>new EvidenceAssertion(Identity(Path.Combine(Root,p.Path)),p.Pointer,p.Value)).ToArray();
            return new(requirement,basis,assertions.Select(a=>a.File).Distinct().ToArray(),source){Assertions=assertions,CompatibilityFreeze=baseline};
        }
        var defaults=new[]{"C3","C4"}.SelectMany(f=>new[]{"native","cold","read"}.Select(phase=>("artifacts/m20-preparation/T5/development_v4/slots/"+f+"-deterministic/"+phase+"-result.json","/status","\"PASS\""))).ToArray();
        var faults=new[]{"recover_save","recover_before","recover_after"}.Select(s=>("artifacts/m20-preparation/T1/durable_v2/"+s+"/011-result.json","/data/passed","true"))
            .Append(("artifacts/m20-preparation/T3/public_v1/full/recover/012-result.json","/data/passed","true")).ToArray();
        var history=new[]{"equivalent-direct","equivalent-equation","manual-final-cold"}.Select(s=>("artifacts/milestone14/acceptance-v33/results/"+s+"/result.json","/passed","true")).ToArray();
        history=history.Append(("artifacts/milestone14/acceptance-v12/results/boundaries/result.json","/passed","true")).ToArray();
        return new[]{Evidence("defaults-two-native","C3 cosmetic corner and C4 disclosed layout/rules; full create/edit/cold-edit/read native chains. Accepted preview and original reports must accompany final maintainer review.",defaults),
            Evidence("native-fault-recovery","T1 real save/before-pointer/after-pointer independent recovery and T3 public fault recovery; source compatibility locked. Original fault steps/reports retain commit semantics, not only terminal passed flag.",faults),
            Evidence("external-history-robustness","M14 final equivalent direct/equation and manual cold plus M14 final report mapping driven-hole/geometry-pattern/reference/config/identity negatives. Internal candidate v12 remains explicitly internal.",history)};
    }
}
