using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;

[assembly: SupportedOSPlatform("windows")]
internal sealed record FrozenFile(string Path,string Sha256,long Bytes);
internal sealed record OracleHole(string Label,double X,double Y,double Diameter);
internal sealed record OraclePattern(string Label,string Seed,int Count,double Spacing);
internal sealed record OracleInput(string Id,string Provenance,FrozenFile Source,string Configuration,double Width,double Height,double Depth,
    IReadOnlyList<OracleHole> Holes,OraclePattern Pattern,IReadOnlyDictionary<string,string> References,string? FactoryReady,double BlindDepth,double BlindX,double BlindY,double BlindDiameter,int EquationCount);
internal sealed record Slot(string Id,string Mode,string Task,string Source,int MaximumParts,int MaximumOpens)
{
    public string Gate {get;init;}="development";
    public string? Pair {get;init;}
    public string[] Phases=>Mode switch
    {
        "real"=>new[]{"plan","native","cold","read"},"deterministic"=>new[]{"native","cold","read"},"negative-planning"=>new[]{"plan"},_=>new[]{"native"}
    };
}
internal sealed record FreezeRecord(string Baseline,string Provider,string RequestedModel,int MaximumOutputTokens,int TimeoutSeconds,
    string Capability,string ResponseSchema,TaskDefinition[] Tasks,Slot[] Slots,FrozenFile[] Files,OracleInput[] External,
    string[] Deferred,string Authorization,DateTime Utc)
{
    public FormalContext? Formal {get;init;}
    public ExternalProtocol[] FormalExternal {get;init;}=Array.Empty<ExternalProtocol>();
    public Continuation? Continuation {get;init;}
    public CoreEvidence[] ReusedCore {get;init;}=Array.Empty<CoreEvidence>();
    public CompletedExternal? CompletedExternal {get;init;}
}
internal sealed record Continuation(string Task,string Package,FrozenFile InitialNative,FrozenFile InitialPointer,FrozenFile PriorCreationEvidence);
internal sealed record CoreEvidence(string Slot,FrozenFile Native,FrozenFile Cold,FrozenFile Read);
internal sealed record CompletedExternal(string Task,string Package,FrozenFile PriorResult,FrozenFile Expected,FrozenFile Pointer,FrozenFile Native);

internal static partial class Program
{
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,WriteIndented=true,
        Converters={new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)}};
    internal static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    internal static void Write(string path,object data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
        JsonSerializer.Serialize(file,data,Json);file.Flush(true);
    }
    internal static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json)??throw new InvalidDataException(path);
    internal static FrozenFile Identity(string path)=>new(Path.GetFullPath(path),ManagedRevisionStore.Hash(path),new FileInfo(path).Length);
    private static string Root="",Output="";
    private static bool Development;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Root=Path.GetFullPath(args[0]);
            if(args[1]=="--incremental")return Incremental(args[2],args[3],args.Length>4?args[4]:null);
            if(args[1]=="--pure")return Pure();
            if(args[1]=="--t5a-pure")return T5APure(args[2]);
            Development=args.Contains("--development");
            Check(ContractValidation.Id(args[2]),"Invalid run ID");Output=Path.Combine(Root,Development?"artifacts/m20-preparation/T5":"artifacts/milestone20",args[2]);
            if(args[1]=="--audit")return PreparationAudit();
            if(args[1]=="--verify-package")return VerifyExistingPackage(args[3],args[4]);
            if(args[1]=="--ownership-audit")return OwnershipAudit(args.Length>3?args[3]:"inspect");
            if(args[1]=="--freeze")return Development?Freeze():FreezeFormal(args[3]);
            var freeze=Read<FreezeRecord>(Path.Combine(Output,"freeze.json"));
            if(args[1]=="--summarize")return Summarize(freeze);
            VerifyFreeze(freeze);
            if(args[1]=="--begin-execution")return BeginExecution(freeze,args[3]);
            if(!Development)CheckExecution(freeze);
            var slot=freeze.Slots.Single(s=>s.Id==args[3]);
            Check(args[1] is "--plan" or "--native" or "--cold" or "--read","Unknown execution phase");
            Check(args[1]!="--plan"||slot.Mode is "real" or "negative-planning","Planning not scheduled for this slot");
            Check(args[1] is not "--cold" and not "--read"||slot.Mode is "real" or "deterministic","Continuation not scheduled for this slot");
            Environment.SetEnvironmentVariable("CAD_HARNESS_M20_PHASE",args[1]=="--cold"?"cold":args[1]=="--read"?"read":"native");
            var marker=Path.Combine(Output,"slots",slot.Id,args[1].TrimStart('-')+"-started.json");
            Write(marker,new{utc=DateTime.UtcNow,controller=Environment.ProcessId,slot});
            if(args[1]=="--plan")return Plan(freeze,slot);
            return Native(freeze,slot);
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static TaskDefinition[] Definitions()=>Tasks.Load(Root);
    private static int Pure()
    {
        var tests=new List<object>();var definitions=Definitions();
        foreach(var task in definitions)
        {
            _=ConstructionRequirements.Normalize(task.Request);
            var expected=ConstructionPreflight.EvaluateGeometry(task.Program);
            Check(Math.Abs(expected.VolumeMm3-Tasks.Geometry(task,0).Volume)<1e-6,"Frozen volume mismatch: "+task.Id);
            TestTask(task,tests);
        }
        Check(!GenericFeatureCapabilities.IsQualified(ConstructionKind.CreateRevolvedBoss,ParameterKey.RevolveAxialSpan),"Unexpected broad native edit qualification");
        Check(ProviderSchemaCompatibility.Audit(ConstructionPlanner.ResponseSchema(SolidWorksConstructionPlanningRuntime.ForNewModel())).Count==0,"Provider schema invalid");
        PureAggregation(tests);
        var path=Path.Combine(Root,"artifacts/m20-preparation/T5/pure",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"),"result.json");
        Write(path,new{passed=true,tests,providerSchemaCompatible=true,nativeCalls=0});Console.WriteLine(path);return 0;
    }
    private static int Freeze()
    {
        Check(Development,"Formal freeze is deferred until an independent maintainer freezes fresh held-out inputs. T5 does not authorize formal M20.");
        Check(!Directory.Exists(Output),"Never overwrite a frozen run");Directory.CreateDirectory(Output);
        var tasks=Definitions();var slots=new List<Slot>();
        Continuation? continuation=null;
        var continuationPath=Environment.GetEnvironmentVariable("CAD_HARNESS_T5_CONTINUATION");
        if(!string.IsNullOrWhiteSpace(continuationPath))
        {
            var package=Path.GetFullPath(continuationPath);Check(package.StartsWith(Path.GetFullPath(Path.Combine(Root,"artifacts/m20-preparation/T5"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Continuation outside T5 owned packages");
            using var store=new ManagedRevisionStore(package,new GenericAuditOnly());var current=store.ReadCurrent();
            Check(current.Manifest.Revision==1&&!File.Exists(store.RecoveryPath),"Continuation requires a proven R1 without pending recovery");
            var evidence=Directory.GetFiles(Path.Combine(Path.GetDirectoryName(package)!,"steps/native"),"*-public-create.json").Single();
            var previous=Read<JsonElement>(evidence).GetProperty("data");Check(previous.GetProperty("succeeded").GetBoolean()&&previous.GetProperty("stateCommitted").GetBoolean()&&previous.GetProperty("revision").GetInt64()==1,"Cannot continue failed creation");
            continuation=new("C1",package,Identity(store.WorkingPath),Identity(store.PointerPath),Identity(evidence));
        }
        var reused=LoadCoreReuse();var completed=LoadCompletedExternal();
        foreach(var t in tasks){var reuse=reused.Any(r=>r.Slot==t.Id+"-deterministic");slots.Add(new(t.Id+"-deterministic","deterministic",t.Id,reuse?"reused_valid_development_chain":t.Id==continuation?.Task?"owned_R1_continuation":"typed_fixture",reuse||t.Id==continuation?.Task?0:1,reuse?0:6));}
        foreach(var id in new[]{"core","manual"}){slots.Add(new(id+"-external","external",id,id==completed?.Task?"preserved_successful_public_steps":"external_public",0,id==completed?.Task?0:12));slots.Add(new(id+"-external-cold","external-cold",id,"external_public",0,1));}
        slots.Add(new("equation-readonly","readonly","equation","external_public",0,1));
        slots.Add(new("unknown-readonly","readonly","unknown","external_public",0,1));
        slots.Add(new("G2-regression","g2","G2","production_v02",1,3));slots.Add(new("G2-cold","g2-cold","G2","production_v02",0,1));
        using var dev=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"artifacts/milestone14/acceptance-v13/schedule.json")));
        using var manual=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"artifacts/milestone14/acceptance-v32/schedule.json")));
        var inputs=new[]{("core",dev.RootElement.GetProperty("inputs")[0]),("manual",manual.RootElement.GetProperty("inputs")[0])}
            .Select(p=>JsonSerializer.Deserialize<OracleInput>(p.Item2.GetRawText(),Json)! with{Id=p.Item1}).ToArray();
        var prompts=new List<string>();
        foreach(var slot in slots.Where(s=>s.Mode is "real" or "negative-planning"))
        {
            var capture=new PromptCapture();
            _=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),capture).PlanAsync(PlanningRequest(tasks,slot)).GetAwaiter().GetResult();
            Check(capture.Prompt is not null,"Frozen task did not reach provider boundary");
            var promptPath=Path.Combine(Output,"prompts",slot.Id+".json");Write(promptPath,capture.Prompt!);prompts.Add(promptPath);
        }
        var allFiles=Directory.GetFiles(Path.Combine(Root,"src"),"*.cs",SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root,"tests/CadHarness.CoreAcceptance.Tests"),"*.cs",SearchOption.AllDirectories))
            .Where(p=>!p.Contains(Path.DirectorySeparatorChar+"obj"+Path.DirectorySeparatorChar)&&!p.Contains(Path.DirectorySeparatorChar+"bin"+Path.DirectorySeparatorChar))
            .Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(Program).Assembly.Location)!,"*"))
            .Concat(Directory.GetFiles(Path.Combine(Root,"schemas"),"*.json"))
            .Concat(new[]{Path.Combine(Root,"SWCAD_Harness_v0.3_PRD.md"),Path.Combine(Root,"tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json"),
                Path.Combine(Root,"tests/CadHarness.ExternalEditing.Tests/NativeEditOracle.cs"),Path.Combine(Root,"scripts/test-milestone20.ps1"),Path.Combine(Root,"scripts/test-m20-preparation-T5.ps1"),
                Path.Combine(Root,"tests/CadHarness.SolidWorks.Tests/NativeTestBudget.cs"),Path.Combine(Root,"tests/CadHarness.SolidWorks.Tests/NativeResourceGuard.cs"),Path.Combine(Root,"tests/CadHarness.SolidWorks.Tests/TestPartScope.cs"),
                Path.Combine(Root,"tests/CadHarness.Relations.Tests/Fixtures/rect2x2.json")}).Concat(prompts)
            .Concat(inputs.Select(i=>i.Source.Path)).Concat(ProtectedRegistryFiles())
            .Concat(new[]{"T4Cases.cs","T4BCases.cs","T4CCases.cs"}.Select(p=>Path.Combine(Root,"tests/CadHarness.ConstructionPlanning.Tests",p)))
            .Concat(reused.SelectMany(r=>new[]{r.Native.Path,r.Cold.Path,r.Read.Path}))
            .Concat(completed is null?Array.Empty<string>():Directory.GetFiles(Path.Combine(Path.GetDirectoryName(completed.PriorResult.Path)!,"steps/native"),"*.json").Concat(new[]{completed.PriorResult.Path,completed.Expected.Path,completed.Pointer.Path,completed.Native.Path}))
            .Distinct().Select(Identity).ToArray();
        var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();
        Write(Path.Combine(Output,"freeze.json"),new FreezeRecord(GitHead(),"https://api.deepseek.com/responses","deepseek-chat",8192,120,
            runtime.CapabilityJson,ConstructionPlanner.ResponseSchema(runtime),tasks,slots.ToArray(),allFiles,inputs,
            new[]{"M18","M19","C5","C6","fresh-held-out","formal-M20"},"T5 preparation only; user authorized unlimited preparation attempts with finite evidence-driven execution. Never resume stopped M20.",DateTime.UtcNow){Continuation=continuation,ReusedCore=reused,CompletedExternal=completed});
        Write(Path.Combine(Output,"freeze-seal.json"),Identity(Path.Combine(Output,"freeze.json")));
        if(completed is not null)ReconcileCompletedExternal(completed);
        Console.WriteLine($"Frozen {slots.Count} slots; maximum {slots.Sum(s=>s.MaximumParts)} Parts and {slots.Sum(s=>s.MaximumOpens)} opens, including explicit cold workers. No measured attempt yet.");return 0;
    }
    private static string GitHead()
    {
        var info=new ProcessStartInfo("git"){WorkingDirectory=Root,RedirectStandardOutput=true,UseShellExecute=false};
        foreach(var s in new[]{"-c","safe.directory="+Root.Replace('\\','/'),"rev-parse","HEAD"})info.ArgumentList.Add(s);
        using var p=Process.Start(info)!;var head=p.StandardOutput.ReadToEnd().Trim();p.WaitForExit();Check(p.ExitCode==0,"Cannot record actual HEAD");return head;
    }
    private static IEnumerable<string> ProtectedRegistryFiles()
    {
        var path=Path.Combine(Root,"artifacts/fixture-factory/audits/final-v1/fixture-registry.json");
        yield return path;
        using var raw=JsonDocument.Parse(File.ReadAllText(path));
        foreach(var row in raw.RootElement.GetProperty("fixtures").EnumerateArray())
            foreach(var key in new[]{"nativePart","manifest","proof","ready"})yield return row.GetProperty(key).GetProperty("path").GetString()!;
    }
    private static void VerifyFreeze(FreezeRecord freeze)
    {
        var seal=Read<FrozenFile>(Path.Combine(Output,"freeze-seal.json"));Check(Identity(seal.Path)==seal,"Freeze metadata changed");
        foreach(var f in freeze.Files)Check(Identity(f.Path)==f,"Frozen file changed: "+f.Path);
        if(freeze.Formal is { } formal)
        {
            Check(formal.ManifestIdentity==Identity(formal.ManifestIdentity.Path),"Manifest drift");
            var current=LoadFormal(formal.ManifestIdentity.Path,formal.Manifest.Development);
            Check(Canonical(current)==Canonical(formal)&&freeze.RequestedModel==FormalProvider.Alias&&freeze.Provider==FormalProvider.Endpoint&&
                freeze.MaximumOutputTokens==FormalProvider.MaximumOutputTokens&&freeze.TimeoutSeconds==FormalProvider.TimeoutSeconds,"Formal source/schedule/provider drift");
            Check(freeze.Continuation is null&&freeze.ReusedCore.Length==0&&freeze.CompletedExternal is null,"Development reuse leaked into formal freeze");
        }
        else if(IncrementalRun)Check(freeze.Provider=="none"&&freeze.RequestedModel=="none"&&freeze.Slots.Length==3,"Incremental schedule drift");
        else Check(Development&&freeze.RequestedModel=="deepseek-chat"&&freeze.MaximumOutputTokens==8192&&freeze.TimeoutSeconds==120,"Provider settings drift or legacy formal freeze rejected");
    }
    private static int Plan(FreezeRecord freeze,Slot slot)
    {
        var output=Path.Combine(Output,"slots",slot.Id);var time=Stopwatch.StartNew();
        var request=freeze.Formal is not null&&slot.Mode=="negative-planning"?freeze.Formal.Manifest.Negatives.Single(n=>n.Id==slot.Task).Request:PlanningRequest(freeze.Tasks,slot);
        using var client=OpenAiPlanSource.CreateClient();
        var key=Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY")??throw new InvalidOperationException("Configured provider credential is missing");
        var provider=new DeepSeekPlanSource(client,freeze.RequestedModel,key,freeze.MaximumOutputTokens,freeze.TimeoutSeconds);
        var promptPath=Path.Combine(Output,"prompts",slot.Id+".json");
        var source=new EvidenceSource(provider,output,File.Exists(promptPath)?Read<PlannerPrompt>(promptPath):null,freeze.Formal?.Manifest.Provider);
        var result=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),source).PlanAsync(request).GetAwaiter().GetResult();
        Write(Path.Combine(output,"plan.json"),result);
        if(slot.Mode=="real"&&result.Preview is not null)BindReplay(freeze,slot,result,source.Frozen!);
        Write(Path.Combine(output,"planning-result.json"),new{slot,result.Status,result.FailureCode,result.Message,result.ModelCalls,result.InputTokens,result.OutputTokens,
            source.ReturnedModel,planningMilliseconds=time.Elapsed.TotalMilliseconds,planningPassed=result.Status==ConstructionPlanningStatus.Planned,
            negativePassed=slot.Mode=="negative-planning"&&(freeze.Formal is null?result.Status==ConstructionPlanningStatus.NeedsClarification:NegativePassed(freeze.Formal.Manifest.Negatives.Single(n=>n.Id==slot.Task),result)),
            contained=true,cleanup=true,
            nativePassed=false,editablePassed=false,followUpPassed=false,reopenPassed=false,completeTask=false,nativeMutationStarted=false});
        Write(Path.Combine(output,"planning-seal.json"),Directory.GetFiles(output,"*.json").Where(p=>Path.GetFileName(p)!="plan-started.json").Select(Identity).ToArray());
        Console.WriteLine(slot.Id+": "+result.Status+" "+result.Message);return 0;
    }
    private static ConstructionPlanRequest PlanningRequest(TaskDefinition[] tasks,Slot slot)=>slot.Mode=="negative-planning"?
        Tasks.Request(slot.Task=="missing-shaft"?"Make a transmission shaft":"Make a flanged sleeve guaranteed to fit a 20 mm shaft",new[]{Tasks.Given(slot.Task=="missing-shaft"?"axial_lengths":"fit_tolerance",null)}):tasks.Single(t=>t.Id==slot.Task).Request;
    private sealed class PromptCapture:IStructuredPlanSource
    {
        public bool IsModelBacked=>false;
        internal PlannerPrompt? Prompt;
        public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt,CancellationToken token){Prompt=prompt;return Task.FromResult(new StructuredPlanResponse("{}"));}
    }
    private sealed class EvidenceSource(IStructuredPlanSource source,string path,PlannerPrompt? frozen,ProviderSettings? settings=null):IStructuredPlanSource
    {
        public bool IsModelBacked=>true;
        internal string? ReturnedModel;
        internal PlannerPrompt? Frozen=>frozen;
        public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt,CancellationToken token)
        {
            Check(frozen is not null&&JsonSerializer.Serialize(prompt,Json)==JsonSerializer.Serialize(frozen,Json),"Formal provider prompt differs from premeasurement freeze");
            Write(Path.Combine(path,"provider-request.json"),new{utc=DateTime.UtcNow,prompt,requestedModel="deepseek-chat",provider="https://api.deepseek.com/responses",settings,store=false,noRetry=true});
            try
            {
                var response=await source.GenerateAsync(prompt,token).ConfigureAwait(false);
                if(response.ProviderJson is not null){using var json=JsonDocument.Parse(response.ProviderJson);if(json.RootElement.TryGetProperty("model",out var m))ReturnedModel=m.GetString();}
                Write(Path.Combine(path,"provider-response.json"),new{utc=DateTime.UtcNow,returnedModel=ReturnedModel,response});return response;
            }
            catch(PlannerException e){Write(Path.Combine(path,"provider-failure.json"),new{e.Code,e.Message,e.InputTokens,e.OutputTokens,e.ProviderJson});throw;}
        }
    }
    private sealed class ReplaySource(string response):IStructuredPlanSource
    {
        public bool IsModelBacked=>false;
        public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt,CancellationToken token)=>Task.FromResult(new StructuredPlanResponse(response));
    }
}
