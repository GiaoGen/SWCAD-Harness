using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;

internal sealed record ProviderSettings(string Endpoint,string Alias,string Format,double Temperature,int MaximumOutputTokens,int TimeoutSeconds,string CredentialEnvironment);
internal sealed record CandidateLock(int Version,string Head,DateTime Utc,string WorkingTree,FrozenFile[] Files);
internal sealed record NegativeTask(string Id,ConstructionPlanRequest Request,ConstructionPlanningStatus ExpectedStatus,string[] RequiredQuestionFragments,string? FailureCode);
internal sealed record EvidenceAssertion(FrozenFile File,string JsonPointer,string ExpectedJson);
internal sealed record RegressionEvidence(string Requirement,string CompatibilityBasis,FrozenFile[] Evidence,FrozenFile[] Production)
{
    public EvidenceAssertion[] Assertions {get;init;}=Array.Empty<EvidenceAssertion>();
    public FrozenFile? CompatibilityFreeze {get;init;}
}
internal sealed record MaintainerPackage(int Version,string Id,bool Development,string Maintainer,DateTime SealedUtc,bool Untuned,string OracleProvenance,
    FrozenFile Candidate,FrozenFile P0,FrozenFile External);
internal sealed record FormalManifest(int Version,string RunId,bool Development,FrozenFile Candidate,FrozenFile Package,FrozenFile PackageSeal,
    ProviderSettings Provider,OracleInput[] ExternalRegression,NegativeTask[] Negatives,RegressionEvidence[] Regression,int ProposedParts,int ProposedOpens);
internal sealed record ExecutionAuthorization(int Version,string RunId,FrozenFile Freeze,string HumanAuthorization,DateTime Utc,
    bool ExecutionAuthorized,int ApprovedParts,int ApprovedOpens,Dictionary<string,SlotLimit> Slots);
internal sealed record SlotLimit(int Parts,int Opens);
internal sealed record FormalContext(FormalManifest Manifest,MaintainerPackage Package,FrozenFile ManifestIdentity,Slot[] Schedule);
internal sealed record ScheduleCost(string Slot,string Gate,int Creations,int InitialOpens,int PublishReadbacks,int ColdControllers,int RecoveryOpens,int MaximumOpens,int MaximumCloses);

internal static partial class Program
{
    internal static readonly ProviderSettings FormalProvider=new("https://api.deepseek.com/responses","deepseek-chat","json_schema",0,16384,180,"CAD_HARNESS_LLM_API_KEY");
    internal static string HashText(string s)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
    internal static string Canonical<T>(T value)=>JsonSerializer.Serialize(value,Json);
    private static void VerifyIdentity(FrozenFile f){Check(File.Exists(f.Path)&&Identity(f.Path)==f,"Identity drift: "+f.Path);}
    private static T StrictRead<T>(string path)
    {
        var options=new JsonSerializerOptions(Json){UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow};
        Check(new FileInfo(path).Length<=8*1024*1024,"Bounded package size exceeded");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path),options)??throw new InvalidDataException(path);
    }
    private static CandidateLock VerifyCandidate(FrozenFile identity)
    {
        VerifyIdentity(identity);var c=StrictRead<CandidateLock>(identity.Path);
        Check(c.Version==1&&c.Head==GitHead()&&c.Files.Length>0,"Candidate version/HEAD mismatch");
        Check(c.Files.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()==c.Files.Length,"Duplicate candidate path");
        foreach(var f in c.Files)VerifyIdentity(f);
        var current=CandidateFiles().Select(Identity).OrderBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray();
        Check(Canonical(current)==Canonical(c.Files.OrderBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).ToArray()),"Candidate source/binary inventory differs");
        return c;
    }
    private static IEnumerable<string> CandidateFiles()=>new[]{"src","tests","scripts","schemas"}.SelectMany(dir=>Directory.GetFiles(Path.Combine(Root,dir),"*",SearchOption.AllDirectories))
        .Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(s=>s is "bin" or "obj")&&new[]{".cs",".csproj",".props",".ps1",".json"}.Contains(Path.GetExtension(p)))
        .Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(Program).Assembly.Location)!,"*"))
        .Concat(new[]{Path.Combine(Root,"SWCAD_Harness_v0.3_PRD.md"),Path.Combine(Root,"global.json")}).Distinct(StringComparer.OrdinalIgnoreCase);
    private static string WorkingTree()
    {
        var p=new ProcessStartInfoForGit(Root,"status","--porcelain");return p.Run();
    }
    private sealed class ProcessStartInfoForGit(string root,params string[] arguments)
    {
        internal string Run()
        {
            var info=new System.Diagnostics.ProcessStartInfo("git"){WorkingDirectory=root,RedirectStandardOutput=true,UseShellExecute=false};
            foreach(var a in new[]{"-c","safe.directory="+root.Replace('\\','/')}.Concat(arguments))info.ArgumentList.Add(a);
            using var process=System.Diagnostics.Process.Start(info)!;var s=process.StandardOutput.ReadToEnd();process.WaitForExit();Check(process.ExitCode==0,"Git audit failed");return s;
        }
    }
    private static Slot[] FormalSchedule(string p0,string external,NegativeTask[] negatives)
    {
        var slots=new List<Slot>();
        foreach(var f in new[]{"C1","C2","C3","C4"})slots.Add(new(f+"-deterministic","deterministic",f,"new_typed_chain",1,6){Gate="deterministic"});
        foreach(var f in new[]{"C1","C2","C3","C4"})for(var i=1;i<=3;i++)slots.Add(new(f+"-real-"+i,"real",f,"fresh_provider_response",1,6){Gate="core-real"});
        slots.Add(new("held-p0","real",p0,"sealed_requirement_task",1,6){Gate="held-p0"});
        foreach(var id in new[]{"core","manual",external})
        {
            slots.Add(new(id+"-external","external",id,"frozen_actions",0,id==external?5:12){Gate=id==external?"held-external":"external-regression",Pair=id+"-external-cold"});
            slots.Add(new(id+"-external-cold","external-cold",id,"independent_controller",0,1){Gate="pair",Pair=id+"-external"});
        }
        slots.Add(new("equation-readonly","readonly","equation","frozen_factory_source",0,1){Gate="readonly"});
        slots.Add(new("unknown-readonly","readonly","unknown","frozen_factory_source",0,1){Gate="readonly"});
        slots.Add(new("G2-regression","g2","G2","new_legacy_chain",1,3){Gate="g2",Pair="G2-cold"});
        slots.Add(new("G2-cold","g2-cold","G2","independent_controller",0,1){Gate="pair",Pair="G2-regression"});
        foreach(var n in negatives)slots.Add(new(n.Id,"negative-planning",n.Id,"frozen_critical_omission",0,0){Gate="negative"});
        return slots.ToArray();
    }
    private static ScheduleCost[] Costs(Slot[] slots)=>slots.Select(s=>s.Mode switch
    {
        "real" or "deterministic"=>new ScheduleCost(s.Id,s.Gate,s.MaximumParts,1,3,2,0,s.MaximumOpens,7),
        "external"=>new(s.Id,s.Gate,0,1,s.Gate=="held-external"?2:5,0,s.Gate=="held-external"?2:6,s.MaximumOpens,s.MaximumOpens),
        "external-cold" or "g2-cold"=>new(s.Id,s.Gate,0,0,0,1,0,1,1),
        "readonly"=>new(s.Id,s.Gate,0,1,0,0,0,1,1),
        "g2"=>new(s.Id,s.Gate,1,1,1,0,1,3,4),
        _=>new(s.Id,s.Gate,0,0,0,0,0,0,0)
    }).ToArray();
    private static FormalContext LoadFormal(string path,bool developmentExample=false)
    {
        var m=StrictRead<FormalManifest>(path);
        Check(m.Version==1&&ContractValidation.Id(m.RunId)&&m.Development==developmentExample,"Formal version/run/development mismatch");
        Check(m.Provider==FormalProvider,"Provider settings differ from qualified semantic configuration");
        var candidate=VerifyCandidate(m.Candidate);
        VerifyIdentity(m.Package);VerifyIdentity(m.PackageSeal);
        Check(StrictRead<FrozenFile>(m.PackageSeal.Path)==m.Package,"Package seal mismatch");
        var p=StrictRead<MaintainerPackage>(m.Package.Path);
        Check(p.Version==1&&ContractValidation.Id(p.Id)&&p.Development==developmentExample&&p.Untuned&&p.Candidate==m.Candidate&&p.SealedUtc>candidate.Utc&&p.SealedUtc<=DateTime.UtcNow,
            "Package not untuned/after candidate lock or version mismatch");
        Check(!string.IsNullOrWhiteSpace(p.Maintainer)&&!string.IsNullOrWhiteSpace(p.OracleProvenance),"Missing maintainer/independent oracle provenance");
        VerifyIdentity(p.P0);VerifyIdentity(p.External);
        var t=StrictRead<TaskDefinition>(p.P0.Path);ValidateCapsuleTask(t);
        var e=StrictRead<ExternalProtocol>(p.External.Path);ValidateExternalProtocol(e);
        Check(e.Actions.Length==2&&e.Actions[0].Kind=="scalar"&&e.Actions[1].Kind=="batch"&&!e.FirstEditFault,"Fresh external slot cost assumes one scalar, one batch, no resampled fault matrix");
        Check(m.ExternalRegression.Length==2&&m.ExternalRegression.Select(e=>e.Id).Order().SequenceEqual(new[]{"core","manual"}),"Missing external regressions");
        foreach(var original in m.ExternalRegression)VerifyIdentity(original.Source);
        Check(m.ExternalRegression.Any(original=>original.Source==e.Initial.Source),"External held-out must use a disclosed approved source in the regression inventory");
        Check(t.Id is not "C1" and not "C2" and not "C3" and not "C4"&&e.Initial.Id is not "core" and not "manual","Held-out identity collides with core");
        Check(m.Negatives.Length==2&&m.Negatives.Select(n=>n.Id).Distinct().Count()==2,"Exactly two frozen negative intents required");
        foreach(var n in m.Negatives)
        {
            Check(n.ExpectedStatus==ConstructionPlanningStatus.NeedsClarification&&n.RequiredQuestionFragments.Length>0,"Negative must declare critical clarification facts");
            var normalized=ConstructionRequirements.Normalize(n.Request);
            Check(normalized.Requirements.Any(r=>r.Critical&&(r.Value is null||r.Ambiguous)),"Negative intent must truly omit a critical fact");
            Check(n.RequiredQuestionFragments.All(q=>normalized.Requirements.Any(r=>r.SemanticKey==q&&r.Critical&&(r.Value is null||r.Ambiguous))),"Expected missing fact is not a critical omission");
        }
        Check(m.Regression.Length==3,"Exact bounded regression mapping required");
        foreach(var required in new[]{"defaults-two-native","native-fault-recovery","external-history-robustness"})
            Check(m.Regression.Count(r=>r.Requirement==required)==1,"Missing regression requirement: "+required);
        foreach(var r in m.Regression)
        {
            Check(r.Evidence.Length>0&&r.Production.Length>0&&!string.IsNullOrWhiteSpace(r.CompatibilityBasis),"Unsubstantiated regression reuse");
            foreach(var f in r.Evidence.Concat(r.Production))VerifyIdentity(f);
            Check(r.Production.All(f=>candidate.Files.Contains(f)),"Reuse production fingerprint not candidate-compatible");
            Check(r.CompatibilityFreeze is not null,"Reuse missing historical production freeze");VerifyIdentity(r.CompatibilityFreeze!);
            var prior=Read<FreezeRecord>(r.CompatibilityFreeze!.Path);
            var production=candidate.Files.Where(f=>f.Path.StartsWith(Path.Combine(Root,"src")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetExtension(f.Path)==".cs").ToArray();
            Check(production.All(f=>r.Production.Contains(f)&&prior.Files.Contains(f)),"Reused evidence production implementation drift");
            Check(r.Assertions.Length>0&&r.Assertions.All(a=>r.Evidence.Contains(a.File)),"Reuse requires explicit expected-result assertions");
            foreach(var assertion in r.Assertions)
            {
                var node=Read<JsonElement>(assertion.File.Path);
                foreach(var key in assertion.JsonPointer.Split('/').Skip(1))node=node.ValueKind==JsonValueKind.Array?node[int.Parse(key)]:node.GetProperty(key.Replace("~1","/").Replace("~0","~"));
                Check(assertion.JsonPointer.StartsWith('/')&&node.GetRawText()==assertion.ExpectedJson,"Regression assertion failed: "+assertion.JsonPointer);
            }
        }
        var schedule=FormalSchedule(t.Id,e.Initial.Id,m.Negatives);
        Check(m.ProposedParts==schedule.Sum(s=>s.MaximumParts)&&m.ProposedOpens==schedule.Sum(s=>s.MaximumOpens),"Proposed lifecycle total mismatch");
        return new(m,p,Identity(path),schedule);
    }
    private static bool NegativePassed(NegativeTask task,ConstructionPlanningResult result)=>result.Status==task.ExpectedStatus&&result.Preview is null&&
        (task.FailureCode is null||task.FailureCode==result.FailureCode)&&task.RequiredQuestionFragments.All(q=>result.Questions.Any(s=>s.Contains(q,StringComparison.OrdinalIgnoreCase)));
    private static int FreezeFormal(string path,bool example=false)
    {
        Check(new[]{"CAD_HARNESS_T5_CONTINUATION","CAD_HARNESS_T5_REUSE_RUN","CAD_HARNESS_T5_EXTERNAL_CONTINUATION"}.All(v=>string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))),"Development reuse environment forbidden in formal Prepare");
        var context=LoadFormal(path,example);Check(context.Manifest.RunId==Path.GetFileName(Output),"Manifest run differs from namespace");
        Check(!Directory.Exists(Output),"Never overwrite formal namespace");
        var tasks=Definitions().Append(StrictRead<TaskDefinition>(context.Package.P0.Path)).ToArray();
        var external=context.Manifest.ExternalRegression.Append(StrictRead<ExternalProtocol>(context.Package.External.Path).Initial).ToArray();
        var files=VerifyCandidate(context.Manifest.Candidate).Files.Concat(new[]{context.ManifestIdentity,context.Manifest.Candidate,context.Manifest.Package,context.Manifest.PackageSeal,context.Package.P0,context.Package.External})
            .Concat(ProtectedRegistryFiles().Select(Identity)).Concat(external.Select(e=>e.Source)).Concat(context.Manifest.Regression.SelectMany(r=>r.Evidence.Append(r.CompatibilityFreeze!))).Distinct().ToList();
        foreach(var s in context.Schedule.Where(s=>s.Mode is "real" or "negative-planning"))
        {
            var request=s.Mode=="negative-planning"?context.Manifest.Negatives.Single(n=>n.Id==s.Task).Request:tasks.Single(t=>t.Id==s.Task).Request;
            var capture=new PromptCapture();_=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),capture).PlanAsync(request).GetAwaiter().GetResult();
            Check(capture.Prompt is not null,"Task never reached planning boundary");
            var prompt=Path.Combine(Output,"prompts",s.Id+".json");Write(prompt,capture.Prompt!);files.Add(Identity(prompt));
        }
        var protocols=context.Manifest.ExternalRegression.Select(RegressionProtocol).Append(StrictRead<ExternalProtocol>(context.Package.External.Path)).ToArray();
        foreach(var protocol in protocols)ValidateExternalProtocol(protocol);
        var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();
        Write(Path.Combine(Output,"schedule-costs.json"),new{costs=Costs(context.Schedule),creations=context.Manifest.ProposedParts,opens=context.Manifest.ProposedOpens,
            normalOpens=127,maximumCloses=Costs(context.Schedule).Sum(c=>c.MaximumCloses),providerCalls=15,budgetApproval="PENDING",executionAuthorized=false,legacyBudget=new{parts=16,opens=32},
            historicalLedger=Identity(Path.Combine(Root,"artifacts/milestone20/native-budget.json"))});
        files.Add(Identity(Path.Combine(Output,"schedule-costs.json")));
        var freeze=new FreezeRecord(GitHead(),FormalProvider.Endpoint,FormalProvider.Alias,FormalProvider.MaximumOutputTokens,FormalProvider.TimeoutSeconds,
            runtime.CapabilityJson,"per-request-frozen-semantic-schema",tasks,context.Schedule,files.ToArray(),external,new[]{"M18","M19","C5","C6"},"Prepare only; explicit separate execution and budget approval required",DateTime.UtcNow){Formal=context,FormalExternal=protocols};
        Write(Path.Combine(Output,"freeze.json"),freeze);Write(Path.Combine(Output,"freeze-seal.json"),Identity(Path.Combine(Output,"freeze.json")));
        Console.WriteLine("Offline formal adapter prepared; no execution authority, no native/provider calls.");return 0;
    }
    private static ExecutionAuthorization RequireAuthorization(FreezeRecord freeze,string path)
    {
        Check(freeze.Formal is not null&&!freeze.Formal.Manifest.Development&&!Development,"Development sample cannot Execute");
        var a=StrictRead<ExecutionAuthorization>(path);VerifyIdentity(a.Freeze);
        Check(a.Version==1&&a.RunId==Path.GetFileName(Output)&&a.Freeze==Identity(Path.Combine(Output,"freeze.json"))&&a.ExecutionAuthorized&&
            !string.IsNullOrWhiteSpace(a.HumanAuthorization)&&a.Utc>=freeze.Utc&&a.Utc<=DateTime.UtcNow,"Missing applicable independent human execution authorization");
        Check(a.ApprovedParts>=freeze.Formal!.Manifest.ProposedParts&&a.ApprovedOpens>=freeze.Formal.Manifest.ProposedOpens&&a.ApprovedParts<1000&&a.ApprovedOpens<10000,"Finite approved complete-run budget required");
        Check(a.Slots.Count==freeze.Slots.Length&&freeze.Slots.All(s=>a.Slots.TryGetValue(s.Id,out var l)&&l.Parts==s.MaximumParts&&l.Opens==s.MaximumOpens),"Approval must bind every frozen slot limit");
        return a;
    }
}
