using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;

internal static partial class Program
{
    private static bool IncrementalRun;
    private static string PriorRun=>Path.Combine(Root,"artifacts/milestone20/acceptance_t5b_v2");
    private static string Words(string text)=>Regex.Replace(text.ToLowerInvariant(),"[^a-z0-9]+"," ").Trim();

    // Version 2 is supplementary adjudication only. The frozen v1 scorer is unchanged.
    private static bool NegativeV2(NegativeTask task,ConstructionPlanningResult result,bool fitCapabilityAbsent)
    {
        if(result.Preview is not null||result.Questions.Count>8||result.Questions.Any(q=>q.Length>10924))return false;
        var requirements=ConstructionRequirements.Normalize(task.Request).Requirements;
        if(task.RequiredQuestionFragments.Length==0||!task.RequiredQuestionFragments.All(key=>
            requirements.Any(r=>r.SemanticKey==key&&r.Critical&&(r.Value is null||r.Ambiguous))))return false;
        if(result.Status==ConstructionPlanningStatus.NeedsClarification)
            return result.FailureCode=="NEEDS_CLARIFICATION"&&task.RequiredQuestionFragments.All(key=>
                result.Questions.Any(q=>(" "+Words(q)+" ").Contains(" "+Words(key)+" ",StringComparison.Ordinal)));
        // Closed capability proof is mandatory; arbitrary Unsupported is not sufficient.
        var message=Words(result.Message);
        return fitCapabilityAbsent&&task.RequiredQuestionFragments.SequenceEqual(new[]{"fit_tolerance"})&&
            result.Status==ConstructionPlanningStatus.Unsupported&&result.FailureCode=="CAPABILITY_UNAVAILABLE"&&
            Words(task.Request.Input.Intent).Contains("guaranteed to fit",StringComparison.Ordinal)&&
            message.Contains("fit",StringComparison.Ordinal)&&message.Contains("tolerance",StringComparison.Ordinal)&&
            message.Contains("no fit clearance or tolerance modeling or verification",StringComparison.Ordinal);
    }

    private static int Incremental(string id,string phase,string? slotId)
    {
        Check(ContractValidation.Id(id),"Invalid incremental namespace");
        Output=Path.Combine(Root,"artifacts/milestone20",id);IncrementalRun=true;Development=false;
        if(phase=="prepare")return PrepareIncremental();
        var freeze=Read<FreezeRecord>(Path.Combine(Output,"freeze.json"));VerifyFreeze(freeze);
        ExecutionApproval=Read<ExecutionAuthorization>(Path.Combine(Output,"execution-approval.json"));
        Check(ExecutionApproval.Freeze==Identity(Path.Combine(Output,"freeze.json"))&&ExecutionApproval.ExecutionAuthorized&&
            ExecutionApproval.RunId==id&&ExecutionApproval.ApprovedParts==1&&ExecutionApproval.ApprovedOpens==10,"Incremental authority mismatch");
        if(phase=="audit")return IncrementalAudit();
        if(phase=="finish")return FinishIncremental(freeze);
        Check(phase is "native" or "cold" or "read","Unknown incremental phase");
        var slot=freeze.Slots.Single(s=>s.Id==slotId);
        Check(phase=="native"||slot.Mode=="deterministic","Only C3 has continued cold/read stages");
        if(slot.Mode=="g2-cold")Check(Stage(Path.Combine(Output,"slots/G2-regression/native-result.json")) is {Status:"PASS",Cleanup:true},"G2 prior controller did not pass");
        Write(Path.Combine(Output,"slots",slot.Id,phase+"-started.json"),new{utc=DateTime.UtcNow,controller=Environment.ProcessId,slot});
        Environment.SetEnvironmentVariable("CAD_HARNESS_M20_PHASE",phase);
        var code=Native(freeze,slot);
        Check(Read<JsonElement>(Path.Combine(Output,"slots",slot.Id,phase+"-result.json")).GetProperty("status").GetString()=="PASS","Incremental failure retained; stop, no automatic retry");
        return code;
    }

    private static int PrepareIncremental()
    {
        Check(!Directory.Exists(Output),"Never overwrite an incremental run");
        var old=Read<FreezeRecord>(Path.Combine(PriorRun,"freeze.json"));
        var fixes=new Dictionary<string,(string Before,string After)>
        {
            ["GenericFeatureConstruction.cs"]=("c.Document.ForceRebuild3(false)","ExecutionTelemetry.Rebuild(() => c.Document.ForceRebuild3(false))"),
            ["PlanarSketchBackend.cs"]=("!Document.ForceRebuild3(false)","!ExecutionTelemetry.Rebuild(() => Document.ForceRebuild3(false))")
        };
        var compatibility=new List<object>();
        foreach(var prior in old.Files.Where(f=>f.Path.StartsWith(Path.Combine(Root,"src")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&f.Path.EndsWith(".cs",StringComparison.Ordinal)))
        {
            var current=Identity(prior.Path);
            if(current==prior){compatibility.Add(new{prior,current,classification="BYTE_IDENTICAL"});continue;}
            Check(fixes.TryGetValue(Path.GetFileName(prior.Path),out var patch),"Unapproved production drift: "+prior.Path);
            var reconstructed=File.ReadAllText(prior.Path).Replace(patch.After,patch.Before,StringComparison.Ordinal);
            Check(ManagedRevisionStore.Hash(prior.Path)!=prior.Sha256&&new[]{reconstructed,reconstructed.Replace("\r\n","\n"),reconstructed.Replace("\r\n","\n").Replace("\n","\r\n")}.Any(s=>HashText(s)==prior.Sha256),"Telemetry patch changed behavior beyond approved wrapper: "+prior.Path);
            compatibility.Add(new{prior,current,classification="OBSERVATIONAL_WRAPPER_ONLY",patch.Before,patch.After});
        }
        var history=Directory.GetFiles(PriorRun,"*",SearchOption.AllDirectories).Select(Identity)
            .Concat(Read<FrozenFile[]>(Path.Combine(PriorRun,"execution-history-before.json")))
            .Concat(old.Files.Where(f=>!f.Path.StartsWith(Path.Combine(Root,"src")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&
                !f.Path.StartsWith(Path.Combine(Root,"tests")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))
            .Distinct().ToArray();
        foreach(var f in history)VerifyIdentity(f);
        Write(Path.Combine(Output,"preserved-history.json"),history);
        Write(Path.Combine(Output,"source-compatibility.json"),new{production=compatibility,behaviorChanges=0,capabilityChanges=0,promptChanges=0,
            reuse="Original successful tasks retain their frozen binary identity; new binaries qualify observational instrumentation only."});
        Write(Path.Combine(Output,"human-authorization.json"),new{utc=DateTime.UtcNow,
            authorization="批准上述有限的判分纠正与遥测证据分配，然后只做一次增量收尾。全部增量条件通过后再标 M20 COMPLETE；不新增 CAD 能力、不调 Prompt、不重跑整个 M20。开始执行",
            negativePolicyVersion=2,originalFrozenVerdict="FAIL_UNCHANGED",providerCalls=0,maxParts=1,maxOpens=10,
            allocation="Reuse original 23 successes and 12/12 live tasks; re-adjudicate original two negative responses. Historical incomplete telemetry remains UNMEASURED/null. Fresh C3/G2 measures telemetry, not retrospective counts or efficiency."});
        IncrementalPure(old);
        var slots=new[]{new Slot("C3-telemetry","deterministic","C3","targeted-public-regression",1,6),
            new Slot("G2-regression","g2","G2","immutable-original-migrate-copy",0,3){Pair="G2-cold"},
            new Slot("G2-cold","g2-cold","G2","independent-controller",0,1){Pair="G2-regression"}};
        var files=CandidateFiles().Select(Identity).Concat(history).Concat(Directory.GetFiles(Output,"*.json").Select(Identity)).Distinct().ToArray();
        // Only current source identities are frozen here; historical old source identities are audited above.
        var freeze=new FreezeRecord(GitHead(),"none","none",0,0,old.Capability,"no-provider",new[]{old.Tasks.Single(t=>t.Id=="C3")},slots,files,
            Array.Empty<OracleInput>(),new[]{"M18","M19","C5","C6"},"Human-approved bounded incremental evidence allocation",DateTime.UtcNow);
        Write(Path.Combine(Output,"freeze.json"),freeze);Write(Path.Combine(Output,"freeze-seal.json"),Identity(Path.Combine(Output,"freeze.json")));
        var approval=new ExecutionAuthorization(1,Path.GetFileName(Output),Identity(Path.Combine(Output,"freeze.json")),"Explicit latest user approval",DateTime.UtcNow,true,1,10,
            slots.ToDictionary(s=>s.Id,s=>new SlotLimit(s.MaximumParts,s.MaximumOpens)));
        Write(Path.Combine(Output,"execution-approval.json"),approval);
        Console.WriteLine("Incremental freeze ready: 0 provider, at most 1 Part / 10 opens; original evidence unchanged.");return 0;
    }

    private static void IncrementalPure(FreezeRecord old)
    {
        var tests=new List<string>();
        void Test(string name,bool pass){Check(pass,name);tests.Add(name);}
        var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();
        Test("capability-identical",runtime.CapabilityJson==old.Capability);
        using var cap=JsonDocument.Parse(runtime.CapabilityJson);
        var operations=cap.RootElement.GetProperty("operations").EnumerateArray().Select(v=>v.GetString()).ToArray();
        var bounded=new[]{"create_sketch","create_datum_plane","create_extrude","create_revolved_boss","create_additive_boss","create_extruded_cut","apply_fillet"};
        Test("closed-capability-no-fit-tolerance",operations.Length>0&&operations.All(o=>bounded.Contains(o)));
        var adjudications=new List<object>();
        foreach(var task in old.Formal!.Manifest.Negatives)
        {
            var dir=Path.Combine(PriorRun,"slots",task.Id);
            foreach(var f in Read<FrozenFile[]>(Path.Combine(dir,"planning-seal.json")))VerifyIdentity(f);
            var actual=Read<ConstructionPlanningResult>(Path.Combine(dir,"plan.json"));
            var replay=new ConstructionPlanner(runtime,new ReplaySource(RawSemantic(Read<JsonElement>(Path.Combine(dir,"provider-response.json"))))).PlanAsync(task.Request).GetAwaiter().GetResult();
            Test(task.Id+"-strict-original-replay",replay.ModelCalls==0&&replay.Status==actual.Status&&replay.FailureCode==actual.FailureCode&&replay.Message==actual.Message&&replay.Questions.SequenceEqual(actual.Questions)&&replay.Preview is null);
            Test(task.Id+"-v1-fail-preserved",!NegativePassed(task,actual));
            Test(task.Id+"-v2-safe-pass",NegativeV2(task,actual,true));
            Test(task.Id+"-wrong-code-refused",!NegativeV2(task,actual with{FailureCode="OTHER"},true));
            Test(task.Id+"-generic-refusal-refused",!NegativeV2(task,actual with{Status=ConstructionPlanningStatus.Unsupported,Message="Unsupported"},true));
            Test(task.Id+"-wrong-question-refused",!NegativeV2(task,actual with{Status=ConstructionPlanningStatus.NeedsClarification,Questions=new[]{"What color?"},FailureCode="NEEDS_CLARIFICATION"},true));
            if(task.Id=="missing_fit")Test("unsupported-without-capability-proof-refused",!NegativeV2(task,actual,false));
            if(task.Id=="missing_shaft")
            {
                Test("underscore-question-compatible",NegativeV2(task,actual with{Questions=new[]{"What are the axial_lengths?"}},false));
                Test("partial-token-refused",!NegativeV2(task,actual with{Questions=new[]{"What are nonaxial lengths?"}},false));
            }
            var invocation=Read<JsonElement>(Path.Combine(dir,"planning-result.json"));
            Test(task.Id+"-no-mutation",!invocation.GetProperty("nativeMutationStarted").GetBoolean()&&invocation.GetProperty("cleanup").GetBoolean());
            adjudications.Add(new{task.Id,originalVerdict="FAIL",supplementVerdict="PASS",policyVersion=2,actual.Status,actual.FailureCode,actual.Message,actual.Questions,
                capabilityProof=new{operations,capabilitySha=HashText(runtime.CapabilityJson),absence="No fit/tolerance guarantee operation or verifier in closed executable vocabulary"},
                files=Directory.GetFiles(dir,"*.json").Select(Identity).ToArray(),providerCalls=0,nativeCalls=0});
        }
        using(var telemetry=ExecutionTelemetry.Start())
        {
            using(ExecutionTelemetry.Measure(ExecutionPhase.Runtime))
            {
                using(ExecutionTelemetry.Measure(ExecutionPhase.Validation))Test("false-rebuild-return-preserved",!ExecutionTelemetry.Rebuild(()=>false));
                Test("recovery-return-preserved",ExecutionTelemetry.Recover(()=>true));ExecutionTelemetry.Rollback(()=>{});
                try{ExecutionTelemetry.Rebuild(()=>throw new InvalidOperationException("expected"));Test("exception-must-propagate",false);}
                catch(InvalidOperationException e){Test("exception-preserved",e.Message=="expected");}
            }
            var timing=telemetry.Snapshot();Test("physical-attempt-count-includes-throw",timing.Rebuilds==2&&timing.Recoveries==1&&timing.Rollbacks==1);
            Test("exclusive-nonnegative-timing",timing.RuntimeWallMs>=0&&timing.RebuildWallMs>=0&&timing.ValidationWallMs>=0);
        }
        Test("inactive-telemetry-preserves-return",ExecutionTelemetry.Rebuild(()=>true));
        Write(Path.Combine(Output,"negative-adjudication-v2.json"),new{passed=true,policyVersion=2,adjudications,oldVerdictUnchanged=true});
        Write(Path.Combine(Output,"pure-result.json"),new{passed=true,tests,assertions=tests.Count,providerCalls=0,nativeCalls=0});
    }

    private static int FinishIncremental(FreezeRecord freeze)
    {
        foreach(var f in Read<FrozenFile[]>(Path.Combine(Output,"preserved-history.json")))VerifyIdentity(f);
        var stages=new[]{("C3-telemetry","native"),("C3-telemetry","cold"),("C3-telemetry","read"),("G2-regression","native"),("G2-cold","native")}
            .Select(p=>Read<JsonElement>(Path.Combine(Output,"slots",p.Item1,p.Item2+"-result.json"))).ToArray();
        Check(stages.All(s=>s.GetProperty("status").GetString()=="PASS"&&s.GetProperty("cleanup").GetBoolean()&&s.GetProperty("integrity").GetBoolean()),"Native increment incomplete");
        Check(stages.Take(3).Select(s=>s.GetProperty("controller").GetInt32()).Distinct().Count()==3&&stages[3].GetProperty("controller").GetInt32()!=stages[4].GetProperty("controller").GetInt32(),"Independent controllers required");
        var timings=stages.Select(s=>s.GetProperty("telemetry").Deserialize<ExecutionTiming>(Json)!).ToArray();
        Check(timings[0].Rebuilds>0&&timings[1].Rebuilds>0&&timings[3].Rebuilds>0&&timings[0].ValidationWallMs>0&&timings[3].ValidationWallMs>0,"Missing physical instrumentation");
        foreach(var marker in Directory.GetFiles(Output,"recovery.json",SearchOption.AllDirectories))throw new InvalidOperationException("Unresolved recovery: "+marker);
        var ledger=Read<JsonElement>(Ledger);Check(ledger.GetProperty("OpenTestOwnedTitles").GetArrayLength()==0,"Unresolved ownership");
        var old=Read<JsonElement>(Path.Combine(PriorRun,"final-verification.json"));
        Check(old.GetProperty("aggregatePass").GetInt32()==23&&old.GetProperty("aggregateFail").GetInt32()==2&&old.GetProperty("score").GetProperty("realTotal").GetInt32()==12,"Original result changed");
        Check(Read<JsonElement>(Path.Combine(Output,"negative-adjudication-v2.json")).GetProperty("passed").GetBoolean(),"Negative correction failed");
        var snapshot=Read<JsonElement>(Path.Combine(Output,"ownership-inspect-result.json"));
        Check(snapshot.GetProperty("passed").GetBoolean(),"Final audit failed");
        Write(Path.Combine(Output,"completion.json"),new{utc=DateTime.UtcNow,milestone20="COMPLETE",basis="Explicit approved incremental adjudication and telemetry allocation; not a rerun",
            originalFrozenVerdict="FAIL_UNCHANGED",originalSuccessfulTasks=23,supplementaryNegativePasses=2,deterministic=4,realTotal=12,realByFamily=new{C1=3,C2=3,C3=3,C4=3},
            telemetryScope="New C3/G2 controllers only; historical unmeasured fields remain null",historicalPhysicalRebuilds=(int?)null,
            measuredPhysicalRebuilds=timings.Sum(t=>t.Rebuilds),measured=timings,providerCalls=0,ledger,
            cumulative=new{creations=23+ledger.GetProperty("CreationAttempts").GetInt32(),opens=142+ledger.GetProperty("OpenAttempts").GetInt32(),closes=165+ledger.GetProperty("PartsClosed").GetInt32()},
            safetyAudit=Identity(Path.Combine(Output,"ownership-inspect-result.json")),freeze=Identity(Path.Combine(Output,"freeze.json")),deferred=freeze.Deferred});
        Console.WriteLine("M20 COMPLETE under approved incremental evidence allocation; original formal FAIL preserved.");return 0;
    }

    private static int IncrementalAudit()
    {
        using var connection=SolidWorksConnection.Connect(false);
        using var budget=new NativeTestBudget(Ledger,"M20 incremental",1);
        var resource=NativeResourceGuard.Inspect(connection.Application,budget);var documents=Documents(connection.Application);
        var originals=Read<FreezeRecord>(Path.Combine(Output,"freeze.json")).Files.Where(f=>f.Path.EndsWith(".SLDPRT",StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach(var f in originals)VerifyIdentity(f);
        var recovery=Directory.Exists(Path.Combine(Output,"packages"))?Directory.GetFiles(Path.Combine(Output,"packages"),"recovery.json",SearchOption.AllDirectories):Array.Empty<string>();
        var passed=documents.Length==0&&resource.Responding&&resource.GdiCount<7000&&budget.Snapshot.OpenTestOwnedTitles.Count==0&&recovery.Length==0;
        var name=File.Exists(Path.Combine(Output,"preflight-audit.json"))?"ownership-inspect-result.json":"preflight-audit.json";
        Write(Path.Combine(Output,name),new{utc=DateTime.UtcNow,passed,resource,documents,budget=budget.Snapshot,originals,recovery,actions="Read-only inventory; no document closed or opened"});
        Check(passed,"Unsafe preflight/final resources or ownership; no native mutation permitted");return 0;
    }
}
