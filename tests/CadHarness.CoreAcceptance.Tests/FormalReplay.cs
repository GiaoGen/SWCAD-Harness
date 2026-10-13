using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;

internal sealed record ReplayBinding(int Version,string Slot,FrozenFile ProviderRequest,FrozenFile ProviderResponse,FrozenFile Plan,
    string RequestSha,string PromptSha,string RawSemanticSha,string ExpandedSha,string CompilationSha);
internal static partial class Program
{
    private static bool SamePath(string a,string b)=>string.Equals(Path.GetFullPath(a),Path.GetFullPath(b),StringComparison.OrdinalIgnoreCase);
    private static string RawSemantic(JsonElement envelope)
    {
        Check(envelope.TryGetProperty("response",out var response),"Original provider envelope missing");
        var raw=response.Deserialize<StructuredPlanResponse>(Json)??throw new InvalidDataException("Missing structured response");
        Check(!string.IsNullOrWhiteSpace(raw.Json)&&!string.IsNullOrWhiteSpace(raw.ProviderJson),"Missing original semantic JSON/provider envelope");
        using var provider=JsonDocument.Parse(raw.ProviderJson!);
        Check(provider.RootElement.TryGetProperty("id",out _)&&provider.RootElement.TryGetProperty("model",out _),"Not a provider response");
        var texts=provider.RootElement.GetProperty("output").EnumerateArray().Where(o=>o.TryGetProperty("content",out _)).SelectMany(o=>o.GetProperty("content").EnumerateArray())
            .Where(c=>c.TryGetProperty("type",out var t)&&t.GetString()=="output_text").Select(c=>c.GetProperty("text").GetString()).ToArray();
        Check(texts.Length==1&&texts[0]==raw.Json,"Raw semantic bytes do not match actual provider output");
        return raw.Json;
    }
    private static void BindReplay(FreezeRecord freeze,Slot slot,ConstructionPlanningResult result,PlannerPrompt prompt)
    {
        if(result.Preview is null)return;
        var dir=Path.Combine(Output,"slots",slot.Id);
        var raw=RawSemantic(Read<JsonElement>(Path.Combine(dir,"provider-response.json")));
        var request=freeze.Tasks.Single(t=>t.Id==slot.Task).Request;
        Write(Path.Combine(dir,"replay-binding.json"),new ReplayBinding(1,slot.Id,Identity(Path.Combine(dir,"provider-request.json")),
            Identity(Path.Combine(dir,"provider-response.json")),Identity(Path.Combine(dir,"plan.json")),HashText(Canonical(request)),HashText(Canonical(prompt)),
            HashText(raw),HashText(result.Preview.ResponseJson),HashText(result.Preview.CompilationJson??"")));
    }
    // This exact helper is used before native creation and again in the new session.
    private static ConstructionPlanningResult ReplayAccepted(ConstructionPlanRequest request,string raw,ReplayBinding binding,JsonElement offlinePlan,
        IConstructionPlanningRuntime runtime)
    {
        Check(binding.Version==1&&HashText(Canonical(request))==binding.RequestSha&&HashText(raw)==binding.RawSemanticSha,"Replay request/raw/version identity mismatch");
        Check(request.SemanticDraftVersion==2,"Formal real replay requires original versioned semantic v2");
        _=ContractJson.Read<SemanticConstructionDraftV2>(raw,SemanticConstructionV2.ValidateDraft);
        var plan=new ConstructionPlanner(runtime,new ReplaySource(raw)).PlanAsync(request).GetAwaiter().GetResult();
        Check(plan.ModelCalls==0&&plan.Preview is not null,"Native replay admission failed: "+plan.Message);
        var expected=offlinePlan.GetProperty("preview");
        Check(expected.GetProperty("requestJson").GetString()==plan.Preview!.RequestJson&&
            HashText(plan.Preview.ResponseJson)==binding.ExpandedSha&&expected.GetProperty("responseJson").GetString()==plan.Preview.ResponseJson&&
            HashText(plan.Preview.CompilationJson??"")==binding.CompilationSha&&expected.GetProperty("compilationJson").GetString()==plan.Preview.CompilationJson,
            "Replay accepted IR/source-map/requirements drift");
        return plan;
    }
    private static (string Raw,ReplayBinding Binding,JsonElement Plan) VerifyReplay(FreezeRecord freeze,Slot slot)
    {
        var dir=Path.Combine(Output,"slots",slot.Id);VerifyPlanningSeal(dir);var b=StrictRead<ReplayBinding>(Path.Combine(dir,"replay-binding.json"));
        Check(b.Slot==slot.Id&&SamePath(b.ProviderRequest.Path,Path.Combine(dir,"provider-request.json"))&&SamePath(b.ProviderResponse.Path,Path.Combine(dir,"provider-response.json"))&&
            SamePath(b.Plan.Path,Path.Combine(dir,"plan.json")),"Replay evidence belongs to different slot");
        foreach(var f in new[]{b.ProviderRequest,b.ProviderResponse,b.Plan})VerifyIdentity(f);
        var prompt=Read<PlannerPrompt>(Path.Combine(Output,"prompts",slot.Id+".json"));
        Check(HashText(Canonical(prompt))==b.PromptSha,"Replay frozen prompt mismatch");
        var sent=Read<JsonElement>(b.ProviderRequest.Path);var settings=freeze.Formal?.Manifest.Provider??FormalProvider;
        Check(sent.GetProperty("prompt").Deserialize<PlannerPrompt>(Json)==prompt&&sent.GetProperty("requestedModel").GetString()==settings.Alias&&
            sent.GetProperty("provider").GetString()==settings.Endpoint&&sent.GetProperty("store").GetBoolean()==false&&sent.GetProperty("noRetry").GetBoolean(),"Live request provenance mismatch");
        if(freeze.Formal is not null)Check(sent.GetProperty("settings").Deserialize<ProviderSettings>(Json)==settings,"Live provider settings mismatch");
        var plan=Read<JsonElement>(b.Plan.Path);
        Check(plan.GetProperty("status").GetString()=="planned"&&plan.GetProperty("modelCalls").GetInt32()==1&&plan.GetProperty("provenance").GetString()=="live_provider",
            "Cannot score fixture/rejected plan as real call");
        var raw=RawSemantic(Read<JsonElement>(b.ProviderResponse.Path));
        VerifyFreshCall(freeze,Read<JsonElement>(b.ProviderRequest.Path),Read<JsonElement>(b.ProviderResponse.Path));
        _=ReplayAccepted(freeze.Tasks.Single(t=>t.Id==slot.Task).Request,raw,b,plan,SolidWorksConstructionPlanningRuntime.ForNewModel());
        return(raw,b,plan);
    }
    private static bool VerifyUnplannedCall(FreezeRecord freeze,Slot slot)
    {
        var dir=Path.Combine(Output,"slots",slot.Id);VerifyPlanningSeal(dir);var plan=Read<JsonElement>(Path.Combine(dir,"plan.json"));
        if(plan.GetProperty("modelCalls").GetInt32()!=1||plan.GetProperty("provenance").GetString()!="live_provider")return false;
        var requestPath=Path.Combine(dir,"provider-request.json");if(!File.Exists(requestPath))return false;
        var sent=Read<JsonElement>(requestPath);var prompt=Read<PlannerPrompt>(Path.Combine(Output,"prompts",slot.Id+".json"));
        Check(sent.GetProperty("prompt").Deserialize<PlannerPrompt>(Json)==prompt&&sent.GetProperty("settings").Deserialize<ProviderSettings>(Json)==freeze.Formal!.Manifest.Provider,
            "Unplanned live request provenance drift");
        var responsePath=Path.Combine(dir,"provider-response.json");
        if(!File.Exists(responsePath))return false; // Provider outage remains UNVERIFIED, not a sampled model decision.
        var envelope=Read<JsonElement>(responsePath);_=RawSemantic(envelope);VerifyFreshCall(freeze,sent,envelope);return true;
    }
    private static void VerifyPlanningSeal(string dir)
    {
        var files=StrictRead<FrozenFile[]>(Path.Combine(dir,"planning-seal.json"));
        Check(files.Length>=4&&new[]{"plan.json","planning-result.json","provider-request.json"}.All(name=>files.Any(f=>SamePath(f.Path,Path.Combine(dir,name)))),"Missing complete planning provenance seal");
        foreach(var f in files){Check(SamePath(Path.GetDirectoryName(f.Path)!,dir),"Planning seal outside slot");VerifyIdentity(f);}
    }
    private static void VerifyFreshCall(FreezeRecord freeze,JsonElement sent,JsonElement envelope)
    {
        var requestUtc=sent.GetProperty("utc").GetDateTime();var responseUtc=envelope.GetProperty("utc").GetDateTime();
        var structured=envelope.GetProperty("response").Deserialize<StructuredPlanResponse>(Json)!;
        using var provider=JsonDocument.Parse(structured.ProviderJson!);
        Check(requestUtc>=freeze.Utc&&responseUtc>=requestUtc&&provider.RootElement.GetProperty("created_at").GetInt64()>=new DateTimeOffset(freeze.Utc).ToUnixTimeSeconds(),
            "Historical response cannot count as a fresh formal provider attempt");
    }
}
