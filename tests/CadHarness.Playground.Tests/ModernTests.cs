using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.Playground;
using CadHarness.State.V03;
using Microsoft.Extensions.Hosting;

namespace CadHarness.Playground.Tests;

internal static class ModernTests
{
    internal static async Task Serve()
    {
        await using var app = PlaygroundHost.Build([], new ModernFake(), new ModernProvider(), 5188);
        Console.WriteLine("M19 MOCK ONLY http://127.0.0.1:5188; no COM or provider."); await app.RunAsync();
    }
    internal static async Task Run()
    {
        var records = new List<object>();
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        var output = Path.Combine(root,"artifacts/milestone19/qualification",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));
        Directory.CreateDirectory(output);
        void Evidence(string name, object value)
        {
            using var f = new FileStream(Path.Combine(output,name+".json"),FileMode.CreateNew); JsonSerializer.Serialize(f,value,Wire.Options); f.Flush(true);
        }
        void Check(bool condition,string name)
        {
            records.Add(new{name,passed=condition}); Evidence("step_"+records.Count,new{name,passed=condition});
            if(!condition)throw new Exception(name); Console.WriteLine("PASS M19 "+name);
        }
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var native=new ModernFake();var provider=new ModernProvider();
        await using var app=PlaygroundHost.Build([],native,provider,port);await app.StartAsync();
        using var client=new HttpClient{BaseAddress=new("http://127.0.0.1:"+port)};
        var secret=Guid.NewGuid().ToString("N");
        async Task<JsonElement> Post(string path,object body)
        {
            var r=await client.PostAsync(path,new StringContent(JsonSerializer.Serialize(body,Wire.Options),Encoding.UTF8,"application/json"));
            var raw=await r.Content.ReadAsStringAsync();if(raw.Contains(secret,StringComparison.Ordinal))throw new Exception("Secret leak");return Wire.Json(raw);
        }
        async Task<JsonElement> Get(string path)=>Wire.Json(await client.GetStringAsync(path));
        bool Pass(JsonElement r)=>r.GetProperty("success").GetBoolean();
        string? Code(JsonElement r)=>r.GetProperty("failureCode").GetString();
        var intake=new ModelOpenRequest("inspect","D:\\mock\\source.SLDPRT",null,null,true);
        var scalar=new ScalarPreviewRequest(4,"hole_b",ParameterKey.HoleDiameter,ScalarUnit.Millimeter,10,12);
        ExternalTargetQuery Q(QueryCardinality count=QueryCardinality.IntentionalBatch,double[]? values=null)=>new(NativeSubtype.SingleCircleThroughAllCut,null,
            ParameterKey.HoleDiameter,ScalarUnit.Millimeter,values is null?null:new(null,null,values),null,null,null,count);
        BatchPreviewRequest Batch()=>new(4,new[]{new QueryParameterChange(Q(values:new[]{6d,10d}),8)});
        ModernConfirmation Confirmation(JsonElement view,string[] targets)=>new(view.GetProperty("id").GetString()!,view.GetProperty("sha256").GetString()!,
            view.GetProperty("revision").GetInt64(),targets,true);
        string[] Targets(JsonElement view)=>view.GetProperty("data").GetProperty("orderedEdits").EnumerateArray().Select(e=>e.GetProperty("target").GetString()!).ToArray();
        try
        {
            await Get("/api/status");await Get("/api/model");await Get("/api/state");
            Check(native.StaCalls==0&&provider.Calls==0,"startup/status polling never activates native or provider");
            Check(Code(await Get("/api/model/relations?target=base"))=="NO_MODEL","no model local query refuses");
            Check(Code(await Post("/api/model/open",intake with{Confirmed=false}))=="REQUEST_INVALID","intake requires explicit confirmation");
            Check(Code(await Post("/api/model/open",intake))=="SOLIDWORKS_NOT_CONNECTED","intake requires explicit connection");
            await Post("/api/solidworks/connect",new{});
            var inspect=await Post("/api/model/open",intake);Evidence("read_only_inspection",inspect);
            Check(Pass(inspect)&&!native.Status.PartOpen&&native.Model!.Features.All(f=>f.Support=="read_only"&&f.Parameters.All(p=>!p.Editable)),"read-only inspection cannot grant edit rights");
            var relations=await Get("/api/model/relations?target=pattern");Evidence("local_relations",relations);
            Check(Pass(relations)&&relations.GetProperty("details").GetProperty("parents").GetArrayLength()==2,"immediate host and seed relationships projected");
            Check(native.Model!.Edges.All(e=>e.Provenance!="declared_design")&&!native.Model.Edges.Any(e=>e.From=="hole_b"&&e.To=="hole_c"),"no alignment/design inference from observed edges");
            Check(Code(await Get("/api/model/relations?target=missing"))=="RELATION_TARGET_NOT_FOUND","local relation exact missing ID refuses");
            Check(Code(await Post("/api/scalar/preview",scalar))=="NO_LIVE_EDIT_STATE","inspection cannot scalar edit");
            Check(Code(await Post("/api/batch/preview",Batch()))=="NO_LIVE_EDIT_STATE","inspection cannot batch edit");
            await Post("/api/model/open",intake with{Mode="external_copy"});
            Check(native.Model is {Owned:true,Editable:true,Revision:4,Origin:"external"},"external source and working copy are distinct at actual revision");
            Check(Code(await Post("/api/model/open",intake))=="TEST_RESOURCE_LIMIT","cannot replace owned session without close");
            var before=native.Dispatches;
            Check(!Pass(await Post("/api/scalar/preview",scalar with{ExpectedOldValue=9})),"expected old mismatch rejected before mutation");
            Check(!Pass(await Post("/api/scalar/preview",scalar with{Unit=ScalarUnit.Degree})),"invalid unit rejected before mutation");
            Check(!Pass(await Post("/api/scalar/preview",scalar with{Value=100})),"invalid final geometry uses production preflight");
            Check(native.Dispatches==before&&native.Checkpoints==0,"preflight refusals have no Setter/checkpoint");
            var ambiguous=await Post("/api/batch/preview",new BatchPreviewRequest(4,new[]{new QueryParameterChange(Q(QueryCardinality.ExactlyOne),8)}));Evidence("ambiguous",ambiguous);
            Check(!Pass(ambiguous)&&Code(ambiguous)==V03FailureCodes.AmbiguousTargetSet&&ambiguous.GetProperty("details").GetProperty("matches").GetArrayLength()==3,"genuine underspecified query shows ambiguous exact references");
            Check((await Get("/api/status")).GetProperty("details").GetProperty("modernPreview").ValueKind==JsonValueKind.Null,"ambiguous result has no executable preview");
            Check(!Pass(await Post("/api/batch/preview",new BatchPreviewRequest(4,new[]{new QueryParameterChange(Q(values:new[]{99d,100d}),8)}))),"empty query has no fallback");
            var p=(await Post("/api/batch/preview",Batch())).GetProperty("details");Evidence("batch_preview",p);
            Check(Targets(p).SequenceEqual(new[]{"hole_b","hole_c"})&&native.Dispatches==before,"batch uses real M18 ordered exact-set preview");
            var confirm=Confirmation(p,Targets(p));
            Check(Code(await Post("/api/modern/execute",confirm with{Targets=confirm.Targets.Reverse().ToArray()}))=="QUERY_CONFIRMATION_MISMATCH","ordered exact targets required");
            Check(Code(await Post("/api/modern/execute",confirm with{Sha256="changed"}))=="PREFLIGHT_REQUIRED","hash substitution rejected");
            Check(Code(await Post("/api/modern/execute",confirm with{Confirmed=false}))=="PREFLIGHT_REQUIRED","second explicit confirmation required");
            Check(Code(await Post("/api/modern/execute",new{id=confirm.Id,sha256=confirm.Sha256,revision=4,targets=confirm.Targets,confirmed=true,program=new{operations=new object[0]}}))=="REQUEST_INVALID","browser execution IR substitution rejected");
            native.Block=new();var running=Post("/api/modern/execute",confirm);await Task.Delay(80);
            Check(Code(await Post("/api/part/close",new{}))=="SESSION_BUSY","STA execution admission rejects concurrent close");
            native.Block.Set();var committed=await running;native.Block=null;Evidence("batch_mock_commit",committed);
            Check(Pass(committed)&&native.SetterCount==2&&native.Checkpoints==1&&native.Model!.Revision==5,"mock dispatch performs one aggregate transaction / revision");
            Check(Code(await Post("/api/modern/execute",confirm))=="STALE_PLAN","successful approval cannot replay");
            await Post("/api/part/close",new{});await Post("/api/model/open",intake with{Mode="external_copy"});
            p=(await Post("/api/scalar/preview",scalar)).GetProperty("details");var old=p;
            await Post("/api/batch/preview",Batch());
            Check(Code(await Post("/api/modern/execute",Confirmation(old,new[]{"hole_b"})))=="STALE_PLAN","replacement invalidates scalar preview");
            p=(await Post("/api/scalar/preview",scalar)).GetProperty("details");native.Fail=true;
            var fail=await Post("/api/modern/execute",Confirmation(p,new[]{"hole_b"}));Evidence("mock_rollback",fail);
            Check(Code(fail)=="MOCK_ROLLBACK"&&native.Model!.Revision==4,"mock rollback retains committed revision / no false success");
            Check(Code(await Post("/api/modern/execute",Confirmation(p,new[]{"hole_b"})))=="STALE_PLAN","failure consumes approval without native retry");native.Fail=false;
            p=(await Post("/api/batch/preview",Batch())).GetProperty("details");native.External=native.External with{Observation=native.External.Observation with{Selection=native.External.Observation.Selection with{ExpectedRevision=5}}};
            Check(!Pass(await Post("/api/modern/execute",Confirmation(p,Targets(p)))),"dispatch revalidation refuses live revision drift");
            await Post("/api/part/close",new{});native.Unsupported=true;await Post("/api/model/open",intake with{Mode="external_copy"});
            Check(!Pass(await Post("/api/batch/preview",Batch()))&&native.Model!.Features.Single(f=>f.Id=="hole_c").Support=="read_only","unsupported match is shown / never silently skipped");native.Unsupported=false;
            await Post("/api/part/close",new{});
            await Post("/api/model/open",intake with{Mode="external_copy"});
            p=(await Post("/api/scalar/preview",scalar)).GetProperty("details");
            Check(Pass(await Post("/api/modern/execute",Confirmation(p,new[]{"hole_b"})))&&native.Model!.Revision==5,"scalar preview public dispatch succeeds on exact feature");
            await Post("/api/part/close",new{});
            await Post("/api/llm/configure",new LlmSettings("deepseek",secret,"https://api.deepseek.com/responses","mock-model"));
            p=(await Post("/api/construction/plan",Cases.Plate())).GetProperty("details");Evidence("construction_plan",p);
            Check(p.GetProperty("stage").GetString()=="plan"&&!p.GetProperty("executable").GetBoolean()&&provider.Calls==1,"qualified construction planner / single mock provider call");
            Check(Code(await Post("/api/modern/execute",Confirmation(p,Array.Empty<string>())))=="PREFLIGHT_REQUIRED","construction plan cannot bypass explicit preflight");
            p=(await Post("/api/construction/preflight",new PlanRequest(p.GetProperty("id").GetString()!,0))).GetProperty("details");
            Check(Pass(await Post("/api/modern/execute",Confirmation(p,Array.Empty<string>())))&&native.Model!.Origin=="managed","exact original response rebound and dispatched as managed mock");
            Check(native.Model!.Edges.All(e=>e.Provenance=="tracked_native"),"managed tracked dependencies not inferred external design relations");
            await Post("/api/part/close",new{});
            provider.Response="{}";Check(!Pass(await Post("/api/construction/plan",Cases.Plate())),"invalid provider response cannot publish preview");provider.Response=null;
            foreach(var version in new[]{1,2})
            {
                var request=Cases.Plate() with{Rules=new[]{new RequirementRule("zero","layout_zero",null),new RequirementRule("frame_angle","layout_zero_angle",null)},SemanticDraftVersion=version};
                p=(await Post("/api/construction/plan",request)).GetProperty("details");Evidence("semantic_v"+version,p);
                Check(p.GetProperty("data").GetProperty("compilation").GetProperty("version").GetString()==version.ToString(),"original semantic v"+version+" response retained / strict compilation visible");
                p=(await Post("/api/construction/preflight",new PlanRequest(p.GetProperty("id").GetString()!,0))).GetProperty("details");
                Check(Pass(await Post("/api/modern/execute",Confirmation(p,Array.Empty<string>()))),"semantic v"+version+" deterministic rebind does not replay expanded IR");
                await Post("/api/part/close",new{});
            }
            var secretRequest=Cases.Plate() with{Input=Cases.Plate().Input with{Intent="Request contains "+secret}};
            Check(Code(await Post("/api/construction/plan",secretRequest))=="SECRET_IN_PLAN_REJECTED","credential-bearing plan rejected before retention / execution");
            p=(await Post("/api/construction/plan",Cases.Plate())).GetProperty("details");await Post("/api/llm/clear",new{});
            Check(Code(await Post("/api/construction/preflight",new PlanRequest(p.GetProperty("id").GetString()!,0)))=="STALE_PLAN","credential changes invalidate modern plans");
            await Post("/api/llm/configure",new LlmSettings("deepseek",secret,"https://api.deepseek.com/responses","mock-model"));
            provider.Block=new();var planning=Post("/api/construction/plan",Cases.Plate());await Task.Delay(80);
            Check(Code(await Post("/api/model/open",intake))=="SESSION_BUSY","modern planning shares admission with native intake");provider.Block.SetResult();await planning;provider.Block=null;
            var export=await Post("/api/session/export",new{});Evidence("export",export);
            Check(!export.GetRawText().Contains(secret,StringComparison.Ordinal),"modern export and retained data are credential redacted");
            var prior=native.StaCalls;await Get("/api/model");await Get("/api/status");
            Check(prior==native.StaCalls,"model polling never performs native read");
            foreach(var asset in new[]{"/","/app.js","/modern.js","/app.css"})Check((await client.GetAsync(asset)).IsSuccessStatusCode,"self-hosted UI asset "+asset);
            using var evil=new HttpRequestMessage(HttpMethod.Post,"/api/modern/execute"){Content=new StringContent("{}",Encoding.UTF8,"application/json")};evil.Headers.Add("Origin","https://unrelated.example");
            Check((await client.SendAsync(evil)).StatusCode==HttpStatusCode.Forbidden,"modern dispatch preserves loopback / origin guard");
            Check(!native.Status.PartOpen&&native.StaCalls>0,"mock ownership released / native methods use STA");
            var realLazy=new CadHarness.Playground.NativeSession(Path.Combine(output,"unused-native-package"),null);
            Check(!realLazy.Status.Connected&&realLazy.ConstructionRuntime().Revision==0,"production adapter offline construction projection requires no COM");realLazy.Dispose();
            Evidence("summary",new{verdict="PASS",checks=records.Count,records,nativeParts=0,nativeOpens=0,realProviderCalls=0,mockDispatches=native.Dispatches});
            Console.WriteLine($"M19 {records.Count} checks PASS. Native: 0 Parts / 0 opens. Evidence: {output}");
        }
        catch(Exception e){Evidence("failure",new{error=e.ToString(),records,nativeParts=0,nativeOpens=0});throw;}
        finally{native.Block?.Set();provider.Block?.TrySetResult();await app.StopAsync();}
    }
}
