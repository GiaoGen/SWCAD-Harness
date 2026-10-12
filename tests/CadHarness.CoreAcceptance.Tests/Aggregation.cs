using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal sealed record StageEvidence(string Status,int Controller,string? Package,long? Revision,string? NativeSha,string? ManifestSha,bool Native,bool Editable,bool Edited,bool Reopened,bool Saved,bool Cleanup,double Milliseconds,bool Reconciled=false);
internal static partial class Program
{
    private static string CompleteStatus(StageEvidence? native,StageEvidence? cold,StageEvidence? read,bool continuation)
    {
        var stages=continuation?new[]{native,cold,read}:new[]{native,cold};
        if(stages.Any(s=>s?.Status=="FAIL"))return "FAIL";
        if(stages.Any(s=>s?.Status=="REFUSED"))return "REFUSED";
        if(stages.Any(s=>s?.Status=="STOPPED"))return "STOPPED";
        if(stages.All(s=>s is null))return "NOT_RUN";
        if(stages.Any(s=>s is null))return "PARTIAL";
        var n=native!;var c=cold!;
        if(stages.Any(s=>s!.Status!="PASS"||!s.Native||!s.Cleanup)||!n.Editable||!n.Edited||!n.Saved||!c.Editable||!c.Reopened||!c.Saved||n.Controller==c.Controller||string.IsNullOrWhiteSpace(n.Package)||n.Package!=c.Package)return "FAIL";
        if(!continuation)return n.Revision==c.Revision&&n.NativeSha==c.NativeSha&&n.ManifestSha==c.ManifestSha?"PASS":"FAIL";
        var r=read!;
        return c.Edited&&c.Revision==n.Revision+1&&r.Editable&&r.Reopened&&r.Saved&&r.Controller!=c.Controller&&r.Controller!=n.Controller&&r.Package==c.Package&&r.Revision==c.Revision&&r.NativeSha==c.NativeSha&&r.ManifestSha==c.ManifestSha?"PASS":"FAIL";
    }
    private static StageEvidence? Stage(string path)
    {
        if(!File.Exists(path))
        {
            var started=path.EndsWith("-result.json",StringComparison.Ordinal)?path[..^12]+"-started.json":null;
            return started is not null&&File.Exists(started)?new StageEvidence("STOPPED",Read<JsonElement>(started).GetProperty("controller").GetInt32(),null,null,null,null,false,false,false,false,false,false,0):null;
        }
        var j=Read<JsonElement>(path);
        bool B(string key)=>j.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.True;
        string? S(string key)=>j.TryGetProperty(key,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():null;
        return new(S("status")??"FAIL",j.TryGetProperty("controller",out var ctrl)?ctrl.GetInt32():0,S("package"),j.TryGetProperty("revision",out var rev)&&rev.ValueKind==JsonValueKind.Number?rev.GetInt64():null,
            S("nativeSha"),S("manifestSha"),B("nativePassed"),B("editablePassed"),B("followUpPassed"),B("reopenPassed"),B("saved"),B("cleanup"),j.TryGetProperty("elapsedMilliseconds",out var ms)?ms.GetDouble():0,B("reconciledSuccessfulSteps"));
    }
    private static int Summarize(FreezeRecord freeze)
    {
        var rows=new List<object>();var statuses=new List<string>();var successTimes=new List<double>();var realByFamily=new Dictionary<string,int>();var deterministic=0;
        foreach(var slot in freeze.Slots.Where(s=>s.Mode is not "external-cold" and not "g2-cold"))
        {
            var dir=Path.Combine(Output,"slots",slot.Id);var reused=freeze.ReusedCore.SingleOrDefault(r=>r.Slot==slot.Id);
            var n=Stage(reused?.Native.Path??Path.Combine(dir,freeze.CompletedExternal?.Task==slot.Task&&slot.Mode=="external"?"reconciled-successful-steps.json":"native-result.json"));StageEvidence? c=null,r=null;string status;
            if(slot.Mode=="readonly")status=n is null?"NOT_RUN":n.Status=="PASS"&&n.Native&&n.Cleanup?"PASS":n.Status;
            else if(slot.Mode is "external" or "g2")
            {
                var paired=freeze.Slots.Single(s=>s.Task==slot.Task&&s.Mode==(slot.Mode=="g2"?"g2-cold":"external-cold"));
                c=Stage(Path.Combine(Output,"slots",paired.Id,"native-result.json"));status=CompleteStatus(n,c,null,false);
            }
            else
            {c=Stage(reused?.Cold.Path??Path.Combine(dir,"cold-result.json"));r=Stage(reused?.Read.Path??Path.Combine(dir,"read-result.json"));status=CompleteStatus(n,c,r,true);}
            if(status=="NOT_RUN"&&File.Exists(Path.Combine(dir,"native-started.json")))status="STOPPED";
            statuses.Add(status);rows.Add(new{slot,status,native=n,cold=c,read=r,completeTask=status=="PASS"});
            if(status=="PASS")
            {
                if(n?.Reconciled!=true)successTimes.Add((n?.Milliseconds??0)+(c?.Milliseconds??0)+(r?.Milliseconds??0));
                if(slot.Mode=="deterministic"&&slot.Task is "C1" or "C2" or "C3" or "C4")deterministic++;
                if(slot.Mode=="real")realByFamily[slot.Task]=realByFamily.GetValueOrDefault(slot.Task)+1;
            }
        }
        var allPassed=statuses.Count>0&&statuses.All(s=>s=="PASS");var gate=new[]{"C1","C2","C3","C4"}.Select(f=>new{family=f,required=2,scheduled=freeze.Slots.Count(s=>s.Mode=="real"&&s.Task==f),complete=realByFamily.GetValueOrDefault(f)}).ToArray();
        var runStatus=allPassed?(Development?"DEVELOPMENT_PASS":deterministic==4&&gate.All(g=>g.scheduled>=3&&g.complete>=2)&&gate.Sum(g=>g.complete)>=8?"PASS":"PARTIAL"):
            statuses.Contains("FAIL")?"FAIL":statuses.Contains("STOPPED")?"STOPPED":statuses.All(s=>s=="NOT_RUN")?"NOT_RUN":"PARTIAL";
        var path=Path.Combine(Output,"summary-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".json");
        Write(path,new{freeze.Baseline,development=Development,status=runStatus,deterministicComplete=deterministic,deterministicRequired=4,realGate=gate,realCompleteTotal=gate.Sum(g=>g.complete),realRequiredTotal=8,rows,
            ledger=Read<JsonElement>(Ledger),successfulOnlyMilliseconds=successTimes,allAttemptsRetained=true,formalGateMeasured=!Development,heldOutStatus="NOT_RUN",freeze.Deferred});Console.WriteLine(path);return 0;
    }
    private static void PureAggregation(List<object> tests)
    {
        var n=new StageEvidence("PASS",1,"p",2,"a","x",true,true,true,false,true,true,1);
        var c=n with{Controller=2,Reopened=true};var ce=c with{Revision=3,NativeSha="b",ManifestSha="y"};var r=ce with{Controller=3,Edited=false};
        void T(string name,bool b){Check(b,name);tests.Add(new{name,passed=true});}
        T("No stages not run",CompleteStatus(null,null,null,false)=="NOT_RUN");
        T("Missing cold partial",CompleteStatus(n,null,null,false)=="PARTIAL");
        T("External pairs pass",CompleteStatus(n,c,null,false)=="PASS");
        T("Wrong package refused",CompleteStatus(n,c with{Package="other"},null,false)=="FAIL");
        T("Wrong revision refused",CompleteStatus(n,c with{Revision=1},null,false)=="FAIL");
        T("Wrong native refused",CompleteStatus(n,c with{NativeSha="wrong"},null,false)=="FAIL");
        T("Wrong manifest refused",CompleteStatus(n,c with{ManifestSha="wrong"},null,false)=="FAIL");
        T("Same controller refused",CompleteStatus(n,c with{Controller=1},null,false)=="FAIL");
        T("Native failure retained",CompleteStatus(n with{Status="FAIL"},null,null,false)=="FAIL");
        T("Refusal distinct",CompleteStatus(n with{Status="REFUSED"},null,null,false)=="REFUSED");
        T("Interrupted distinct",CompleteStatus(n with{Status="STOPPED"},null,null,false)=="STOPPED");
        T("Three controller full cycle",CompleteStatus(n,ce,r,true)=="PASS");
        T("Read-only cold not continuation",CompleteStatus(n,c,r,true)=="FAIL");
        T("No final read partial",CompleteStatus(n,ce,null,true)=="PARTIAL");
        T("Final read geometry association",CompleteStatus(n,ce,r with{NativeSha="wrong"},true)=="FAIL");
        T("Cleanup false refuses",CompleteStatus(n,c with{Cleanup=false},null,false)=="FAIL");
        T("Missing native flags refuses",CompleteStatus(n with{Native=false},c,null,false)=="FAIL");
        T("Capsule tangent edges",TangentLines(SlotLines(),0,0,5,8));
        var bad=SlotLines();bad[0][0][1]+=.2;T("Wrong capsule end rejected",!TangentLines(bad,0,0,5,8));
        T("Missing capsule edge rejected",!TangentLines(SlotLines()[1..],0,0,5,8));
        var legacy=AuditBudget("{\"OpenAttempts\":145,\"DocumentsClosed\":145,\"OwnedTitles\":[\"owned\"]}");
        T("Historical M14 ledger schema",legacy.OpenAttempts==145&&legacy.PartsClosed==145&&legacy.OpenTestOwnedTitles.SequenceEqual(new[]{"owned"}));
        var modern=AuditBudget("{\"CreationAttempts\":4,\"PartsCreated\":4,\"PartsClosed\":29,\"OpenAttempts\":25,\"OpenTestOwnedTitles\":[]}");
        T("Preparation ledger schema",modern.PartsCreated==4&&modern.PartsClosed==29&&modern.OpenAttempts==25&&modern.OpenTestOwnedTitles.Count==0);
        var probe=Path.Combine(Root,"artifacts/m20-preparation/T5/status-pure",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));
        Write(Path.Combine(probe,"cold-started.json"),new{controller=123});
        T("Interrupted cold has STOPPED evidence",Stage(Path.Combine(probe,"cold-result.json"))?.Status=="STOPPED");
        T("Unstarted read stays NOT_RUN",Stage(Path.Combine(probe,"read-result.json")) is null);
    }
    private static double[][][] SlotLines()=>new[]{5d,8}.SelectMany(z=>new[]{-3d,3}.Select(x=>new[]{new[]{x,-7d,z},new[]{x,7d,z}})).ToArray();
}
