using System;
using System.IO;
using System.Linq;
using System.Text.Json;

internal sealed record Reservation(int Number,string Run,string Slot,string Phase,string Kind,int Controller,DateTime Utc,int TotalParts,int TotalOpens,int SlotParts,int SlotOpens);
internal sealed class FormalReservations(string path,ExecutionAuthorization approval,string slot,string phase)
{
    internal void Reserve(string kind)
    {
        Program.Check(kind is "create" or "open","Unknown reservation");
        Directory.CreateDirectory(path);
        using var mutex=new FileStream(Path.Combine(path,"reservation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var files=Directory.GetFiles(path,"*-reservation.json").OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        var prior=files.Select(Program.Read<Reservation>).ToArray();
        Program.Check(prior.Select((p,i)=>p.Number==i+1&&p.Run==approval.RunId&&p.Kind is "create" or "open").All(v=>v),"Corrupt formal reservation history");
        var parts=prior.Count(p=>p.Kind=="create");var opens=prior.Count(p=>p.Kind=="open");
        var sp=prior.Count(p=>p.Slot==slot&&p.Kind=="create");var so=prior.Count(p=>p.Slot==slot&&p.Kind=="open");
        var runningParts=0;var runningOpens=0;
        foreach(var row in prior)
        {
            if(row.Kind=="create")runningParts++;else runningOpens++;
            Program.Check(row.TotalParts==runningParts&&row.TotalOpens==runningOpens&&approval.Slots.ContainsKey(row.Slot)&&
                row.SlotParts==prior.Take(row.Number).Count(p=>p.Slot==row.Slot&&p.Kind=="create")&&row.SlotOpens==prior.Take(row.Number).Count(p=>p.Slot==row.Slot&&p.Kind=="open"),"Corrupt cumulative reservation counters");
            var l=approval.Slots[row.Slot];Program.Check(row.SlotParts<=l.Parts&&row.SlotOpens<=l.Opens,"Historical slot reservation exceeds approval");
        }
        Program.Check(parts<=approval.ApprovedParts&&opens<=approval.ApprovedOpens,"Historical total reservations exceed approval");
        Program.Check(approval.Slots.TryGetValue(slot,out var limit),"Slot not approved");
        Program.Check(kind=="create"?parts<approval.ApprovedParts&&sp<limit!.Parts:opens<approval.ApprovedOpens&&so<limit!.Opens,"FORMAL_BUDGET_EXHAUSTED: no native call made");
        if(kind=="create"){parts++;sp++;}else{opens++;so++;}
        Program.Write(Path.Combine(path,(prior.Length+1).ToString("D6")+"-reservation.json"),new Reservation(prior.Length+1,approval.RunId,slot,phase,kind,Environment.ProcessId,DateTime.UtcNow,parts,opens,sp,so));
    }
}
internal static partial class Program
{
    private static ExecutionAuthorization? ExecutionApproval;
    private static int BeginExecution(FreezeRecord freeze,string authorization)
    {
        VerifyFreeze(freeze);var approval=RequireAuthorization(freeze,authorization);
        Write(Path.Combine(Output,"execution-authorized.json"),new{utc=DateTime.UtcNow,authorization=Identity(authorization),approval,noRetry=true,noRepair=true});
        return 0;
    }
    private static void CheckExecution(FreezeRecord freeze)
    {
        var marker=Read<System.Text.Json.JsonElement>(Path.Combine(Output,"execution-authorized.json"));
        var file=marker.GetProperty("authorization").Deserialize<FrozenFile>(Json)!;VerifyIdentity(file);
        ExecutionApproval=RequireAuthorization(freeze,file.Path);
        Check(Canonical(ExecutionApproval)==Canonical(marker.GetProperty("approval").Deserialize<ExecutionAuthorization>(Json)),"Execution authority drift");
    }
}
