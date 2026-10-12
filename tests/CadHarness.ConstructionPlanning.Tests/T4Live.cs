using System;
using System.IO;
using System.Linq;
using CadHarness.Planning;
using CadHarness.SolidWorks;

internal static partial class T4Tests
{
    internal static int Live(string root,string run,string? only=null)
    {
        var output=Path.Combine(root,"artifacts/m20-preparation/T4",run);Program.Check(!Directory.Exists(output),"Never replace a live run");Directory.CreateDirectory(output);
        var cases=T4Cases.All(root).Where(c=>only is null || c.Id==only).ToArray();Program.Check(cases.Length>0,"No scheduled task");
        Program.Write(Path.Combine(output,"freeze.json"),new{baseline="571851e",stage="T4 development smoke, NOT formal M20",model="deepseek-chat",endpoint=DeepSeekPlanSource.Endpoint,maxOutputTokens=16384,timeoutSeconds=180,temperature=0,noRetries=true,
            maximumCalls=cases.Length,tasks=cases.Select(c=>new{c.Id,c.Request}).ToArray(),source=Program.SourceHashes(root),schema=ConstructionPlanner.ResponseSchema(SolidWorksConstructionPlanningRuntime.ForNewModel()),
            outbound="Only synthetic task inputs, normalized requirements, generic schema and capability. No native Part, external history, Persistent Reference, source path or credentials in payload."});
        using var client=OpenAiPlanSource.CreateClient();var key=System.Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY")??throw new Exception("Missing configured credential");
        var rows=new System.Collections.Generic.List<object>();var passed=true;
        foreach(var scene in cases)
        {
            var directory=Path.Combine(output,scene.Id);Directory.CreateDirectory(directory);var source=new EvidenceSource(new DeepSeekPlanSource(client,"deepseek-chat",key,16384,180),directory);var clock=System.Diagnostics.Stopwatch.StartNew();
            var result=new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(),source).PlanAsync(scene.Request).GetAwaiter().GetResult();
            Program.Write(Path.Combine(directory,"result.json"),new{scene.Id,passed=result.Preview is not null,result,elapsedMilliseconds=clock.Elapsed.TotalMilliseconds,nativeMutation=false});
            if(result.Preview is { } p){Program.Write(Path.Combine(directory,"accepted.json"),new{p.Sha256,p.ContextIdentity,p.Revision,p.RequestJson,p.ResponseJson,p.CapabilityJson});}
            Console.WriteLine(scene.Id+": "+result.Status+" "+result.Message);rows.Add(new{scene.Id,status=result.Status.ToString(),result.ModelCalls,result.InputTokens,result.OutputTokens,passed=result.Preview is not null});passed &=result.Preview is not null;
        }
        Program.Write(Path.Combine(output,"result.json"),new{passed,total=rows.Count,rows,formalSuccessRateClaimed=false});return passed?0:1;
    }
}
