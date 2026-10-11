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
internal sealed record Slot(string Id,string Mode,string Task,string Source,int MaximumParts,int MaximumOpens);
internal sealed record FreezeRecord(string Baseline,string Provider,string RequestedModel,int MaximumOutputTokens,int TimeoutSeconds,
    string Capability,string ResponseSchema,TaskDefinition[] Tasks,Slot[] Slots,FrozenFile[] Files,OracleInput[] External,
    string[] Deferred,string Authorization,DateTime Utc);

internal static partial class Program
{
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,
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
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Root=Path.GetFullPath(args[0]);
            if(args[1]=="--pure")return Pure();
            Check(ContractValidation.Id(args[2]),"Invalid run ID");Output=Path.Combine(Root,"artifacts/milestone20",args[2]);
            if(args[1]=="--ownership-audit")return OwnershipAudit(args.Length>3?args[3]:"inspect");
            if(args[1]=="--freeze")return Freeze();
            var freeze=Read<FreezeRecord>(Path.Combine(Output,"freeze.json"));VerifyFreeze(freeze);
            var slot=freeze.Slots.Single(s=>s.Id==args[3]);
            if(args[1]=="--cold")Environment.SetEnvironmentVariable("CAD_HARNESS_M20_PHASE","cold");
            var marker=Path.Combine(Output,"slots",slot.Id,args[1].TrimStart('-')+"-started.json");
            Write(marker,new{utc=DateTime.UtcNow,controller=Environment.ProcessId,slot});
            if(args[1]=="--plan")return Plan(freeze,slot);
            return Native(freeze,slot);
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static TaskDefinition[] Definitions()
    {
        using var raw=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json")));
        return Tasks.Load(raw.RootElement);
    }
    private static int Pure()
    {
        var tests=new List<object>();var definitions=Definitions();
        foreach(var task in definitions)
        {
            _=ConstructionRequirements.Normalize(task.Request);
            if(task.Program is not null)
            {
                var expected=ConstructionPreflight.EvaluateGeometry(task.Program);
                Check(Math.Abs(expected.VolumeMm3-task.BeforeVolume)<1e-6,"Frozen volume mismatch: "+task.Id);
                if(task.Family=="C4")
                {
                    var after=task.Program with{Operations=task.Program.Operations.Select(o=>o.SemanticId==task.EditTarget?o with{Depth=new(task.NewValue)}:o).ToArray()};
                    Check(Math.Abs(ConstructionPreflight.EvaluateGeometry(after).VolumeMm3-task.AfterVolume)<1e-6,"Frozen edited volume mismatch");
                }
            }
            tests.Add(new{task.Id,passed=true,task.CapabilityGap});
        }
        Check(!GenericFeatureCapabilities.IsQualified(ConstructionKind.CreateRevolvedBoss,ParameterKey.RevolveAxialSpan),"Unexpected broad native edit qualification");
        Check(ProviderSchemaCompatibility.Audit(ConstructionPlanner.ResponseSchema(SolidWorksConstructionPlanningRuntime.ForNewModel())).Count==0,"Provider schema invalid");
        var path=Path.Combine(Root,"artifacts/milestone20/pure",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"),"result.json");
        Write(path,new{passed=true,tests,providerSchemaCompatible=true,nativeCalls=0});Console.WriteLine(path);return 0;
    }
    private static int Freeze()
    {
        Check(!Directory.Exists(Output),"Never overwrite a frozen run");Directory.CreateDirectory(Output);
        var tasks=Definitions();var slots=new List<Slot>();
        foreach(var t in tasks.Where(t=>!t.HeldOut))slots.Add(new(t.Id+"-deterministic","deterministic",t.Id,"typed_fixture",t.Program is null?0:1,t.Program is null?0:1));
        foreach(var t in tasks.Where(t=>!t.HeldOut))for(var i=1;i<=3;i++)slots.Add(new(t.Id+"-real-"+i,"real",t.Id,"live_provider",1,1));
        slots.Add(new("held-out-composition","deterministic","C4-held-out","typed_fixture",1,1));
        foreach(var id in new[]{"missing-shaft","missing-fit"})slots.Add(new(id,"negative-planning",id,"live_provider",0,0));
        foreach(var id in new[]{"core","manual"}){slots.Add(new(id+"-external","external",id,"external_public",0,12));slots.Add(new(id+"-external-cold","external-cold",id,"external_public",0,1));}
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
                Path.Combine(Root,"tests/CadHarness.ExternalEditing.Tests/NativeEditOracle.cs"),Path.Combine(Root,"scripts/test-milestone20.ps1"),
                Path.Combine(Root,"tests/CadHarness.SolidWorks.Tests/NativeTestBudget.cs"),Path.Combine(Root,"tests/CadHarness.SolidWorks.Tests/NativeResourceGuard.cs"),Path.Combine(Root,"tests/CadHarness.SolidWorks.Tests/TestPartScope.cs"),
                Path.Combine(Root,"tests/CadHarness.Relations.Tests/Fixtures/rect2x2.json")}).Concat(prompts)
            .Concat(inputs.Select(i=>i.Source.Path)).Concat(ProtectedRegistryFiles()).Distinct().Select(Identity).ToArray();
        var runtime=SolidWorksConstructionPlanningRuntime.ForNewModel();
        Write(Path.Combine(Output,"freeze.json"),new FreezeRecord("b25467d","https://api.deepseek.com/responses","deepseek-chat",8192,120,
            runtime.CapabilityJson,ConstructionPlanner.ResponseSchema(runtime),tasks,slots.ToArray(),allFiles,inputs,
            new[]{"M18","M19","C5","C6"},"2026-10-11 direct user authorization: unlimited M20 native budget; lifecycle and attempt accounting remain mandatory.",DateTime.UtcNow));
        Write(Path.Combine(Output,"freeze-seal.json"),Identity(Path.Combine(Output,"freeze.json")));
        Console.WriteLine($"Frozen {slots.Count} slots; maximum {slots.Sum(s=>s.MaximumParts)} Parts and {slots.Sum(s=>s.MaximumOpens)} opens, including explicit cold workers. No measured attempt yet.");return 0;
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
        Check(freeze.RequestedModel=="deepseek-chat"&&freeze.MaximumOutputTokens==8192&&freeze.TimeoutSeconds==120,"Provider settings drift");
    }
    private static int Plan(FreezeRecord freeze,Slot slot)
    {
        var output=Path.Combine(Output,"slots",slot.Id);var time=Stopwatch.StartNew();
        var request=PlanningRequest(freeze.Tasks,slot);
        using var client=OpenAiPlanSource.CreateClient();
        var key=Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY")??throw new InvalidOperationException("Configured provider credential is missing");
        var provider=new DeepSeekPlanSource(client,freeze.RequestedModel,key,freeze.MaximumOutputTokens,freeze.TimeoutSeconds);
        var source=new EvidenceSource(provider,output,Read<PlannerPrompt>(Path.Combine(Output,"prompts",slot.Id+".json")));
        var result=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),source).PlanAsync(request).GetAwaiter().GetResult();
        Write(Path.Combine(output,"plan.json"),result);
        Write(Path.Combine(output,"planning-result.json"),new{slot,result.Status,result.FailureCode,result.Message,result.ModelCalls,result.InputTokens,result.OutputTokens,
            source.ReturnedModel,planningMilliseconds=time.Elapsed.TotalMilliseconds,planningPassed=result.Status==ConstructionPlanningStatus.Planned,
            negativePassed=slot.Mode=="negative-planning"&&result.Status==ConstructionPlanningStatus.NeedsClarification,
            nativePassed=false,editablePassed=false,followUpPassed=false,reopenPassed=false,completeTask=false,nativeMutationStarted=false});
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
    private sealed class EvidenceSource(IStructuredPlanSource source,string path,PlannerPrompt frozen):IStructuredPlanSource
    {
        public bool IsModelBacked=>true;
        internal string? ReturnedModel;
        public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt,CancellationToken token)
        {
            Check(JsonSerializer.Serialize(prompt,Json)==JsonSerializer.Serialize(frozen,Json),"Formal provider prompt differs from premeasurement freeze");
            Write(Path.Combine(path,"provider-request.json"),new{utc=DateTime.UtcNow,prompt,requestedModel="deepseek-chat",provider="https://api.deepseek.com/responses",store=false,noRetry=true});
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
