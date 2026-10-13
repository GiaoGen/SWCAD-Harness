using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal sealed record FormalRow(string Id,string Gate,string Family,string Status,bool Contained,bool Integrity,bool Live);
internal sealed record FormalScore(string Status,int Deterministic,int RealTotal,Dictionary<string,int> RealByFamily,string HeldOutStatus,bool HardGates,bool AllMeasured,bool Protection);
internal static partial class Program
{
    internal static FormalScore ScoreFormal(FormalRow[] rows,bool reuseValid)
    {
        var real=rows.Where(r=>r.Gate=="core-real").ToArray();var families=new[]{"C1","C2","C3","C4"};
        var byFamily=families.ToDictionary(f=>f,f=>real.Count(r=>r.Family==f&&r.Status=="PASS"&&r.Live));
        var protection=rows.All(r=>r.Integrity&&(r.Status is "NOT_RUN" or "PARTIAL" or "STOPPED"||r.Contained));
        var measured=rows.All(r=>r.Status is "PASS" or "FAIL" or "REFUSED"&&(!(r.Gate is "core-real" or "held-p0")||r.Live));
        var deterministic=rows.Count(r=>r.Gate=="deterministic"&&r.Status=="PASS");
        var held=rows.Where(r=>r.Gate is "held-p0" or "held-external").ToArray();
        var heldStatus=held.Length==2&&held.All(r=>r.Status=="PASS")?"PASS":held.Any(r=>r.Status=="STOPPED")?"STOPPED":held.Any(r=>r.Status is "FAIL" or "REFUSED")?"FAIL":held.All(r=>r.Status=="NOT_RUN")?"NOT_RUN":"PARTIAL";
        bool Gate(string name,int count)=>rows.Count(r=>r.Gate==name)==count&&rows.Where(r=>r.Gate==name).All(r=>r.Status=="PASS");
        var hard=Gate("deterministic",4)&&heldStatus=="PASS"&&Gate("external-regression",2)&&Gate("readonly",2)&&Gate("g2",1)&&Gate("negative",2)&&reuseValid;
        var threshold=real.Length==12&&families.All(f=>real.Count(r=>r.Family==f)==3&&byFamily[f]>=2)&&byFamily.Values.Sum()>=8;
        var status=!protection?"BLOCKED":rows.Any(r=>r.Status=="STOPPED")?"STOPPED":rows.All(r=>r.Status=="NOT_RUN")?"NOT_RUN":!measured?"PARTIAL":hard&&threshold?"PASS":"FAIL";
        return new(status,deterministic,byFamily.Values.Sum(),byFamily,heldStatus,hard,measured,protection);
    }
    private static int SummarizeFormal(FreezeRecord freeze)
    {
        var integrity=true;string? error=null;
        try{VerifyFreeze(freeze);}catch(Exception e){integrity=false;error=e.Message;}
        var rows=new List<FormalRow>();var detail=new List<object>();var times=new List<double>();var providerIds=new HashSet<string>();
        foreach(var slot in freeze.Slots.Where(s=>s.Gate!="pair"))
        {
            var dir=Path.Combine(Output,"slots",slot.Id);var n=Stage(Path.Combine(dir,"native-result.json"));StageEvidence? c=null,r=null;
            var providerPath=Path.Combine(dir,"provider-response.json");
            if(File.Exists(providerPath))
            {
                try
                {
                    var response=Read<JsonElement>(providerPath).GetProperty("response").Deserialize<CadHarness.Planning.StructuredPlanResponse>(Json)!;
                    using var provider=JsonDocument.Parse(response.ProviderJson!);Check(providerIds.Add(provider.RootElement.GetProperty("id").GetString()!),"Duplicate provider response identity across independent slots");
                }
                catch(Exception e){integrity=false;error=e.Message;}
            }
            var status="NOT_RUN";var contained=true;var live=false;
            if(slot.Mode=="negative-planning")
            {
                var path=Path.Combine(dir,"plan.json");
                if(File.Exists(path))
                {
                    var result=Read<CadHarness.Planning.ConstructionPlanningResult>(path);
                    try{status=NegativePassed(freeze.Formal!.Manifest.Negatives.Single(t=>t.Id==slot.Task),result)&&VerifyUnplannedCall(freeze,slot)?"PASS":"FAIL";}
                    catch(Exception e){integrity=false;error=e.Message;status="FAIL";}
                }
                else if(File.Exists(Path.Combine(dir,"plan-started.json")))status="STOPPED";
            }
            else if(slot.Mode=="readonly")status=n is null?"NOT_RUN":n.Status=="PASS"&&n.Native&&n.Cleanup?"PASS":n.Status;
            else if(slot.Pair is not null){c=Stage(Path.Combine(Output,"slots",slot.Pair,"native-result.json"));status=CompleteStatus(n,c,null,false);}
            else{c=Stage(Path.Combine(dir,"cold-result.json"));r=Stage(Path.Combine(dir,"read-result.json"));status=CompleteStatus(n,c,r,true);}
            foreach(var stage in new[]{(n,Path.Combine(dir,"native-result.json")),(c,slot.Pair is null?Path.Combine(dir,"cold-result.json"):Path.Combine(Output,"slots",slot.Pair,"native-result.json")),(r,Path.Combine(dir,"read-result.json"))})
                if(stage.Item1 is not null&&File.Exists(stage.Item2))
                {
                    var data=Read<JsonElement>(stage.Item2);
                    contained&=data.TryGetProperty("contained",out var flag)&&flag.GetBoolean();
                }
            if(slot.Mode=="real")
            {
                var pp=Path.Combine(dir,"planning-result.json");
                if(!File.Exists(pp))status=File.Exists(Path.Combine(dir,"plan-started.json"))?"STOPPED":"NOT_RUN";
                else
                {
                    var plan=Read<JsonElement>(pp);
                    contained&=plan.TryGetProperty("contained",out var flag)&&flag.GetBoolean();
                    try
                    {
                        if(plan.GetProperty("planningPassed").GetBoolean()){_=VerifyReplay(freeze,slot);live=true;}
                        else live=VerifyUnplannedCall(freeze,slot);
                    }
                    catch(Exception e){integrity=false;error=e.Message;status="FAIL";}
                }
            }
            rows.Add(new(slot.Id,slot.Gate,slot.Task,status,contained,integrity,live));detail.Add(new{slot,status,contained,live,native=n,cold=c,read=r});
            if(status=="PASS")times.Add((n?.Milliseconds??0)+(c?.Milliseconds??0)+(r?.Milliseconds??0));
        }
        var score=ScoreFormal(rows.ToArray(),integrity);
        var ledger=File.Exists(Ledger)?Read<JsonElement>(Ledger):JsonSerializer.SerializeToElement(new{creationAttempts=0,openAttempts=0,partsClosed=0});
        var output=Path.Combine(Output,"summary-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".json");
        var development=freeze.Formal!.Manifest.Development;
        Write(output,new{development,score,releaseVerdict=development?"NOT_MEASURED":score.Status,rows=detail,integrityError=error,ledger,successfulOnlyMilliseconds=times,
            realDenominator=12,allFailuresRetained=true,executionState=score.Status,technicalReadiness="SEPARATE_FROM_RELEASE",formalGateMeasured=!development&&score.AllMeasured});
        Console.WriteLine(output);return 0;
    }
}
