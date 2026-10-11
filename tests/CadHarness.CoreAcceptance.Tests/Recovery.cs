using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using SolidWorks.Interop.sldworks;

internal static partial class Program
{
    private static int OwnershipAudit(string mode)
    {
        var failed=Path.Combine(Output,"slots/C1-deterministic");
        var expected=Path.GetFullPath(Path.Combine(failed,"working.SLDPRT"));
        Check(File.Exists(Path.Combine(failed,"native-result.json")),"No preserved interrupted slot");
        using var connection=SolidWorksConnection.Connect(false);
        var docs=NativeEditOracle.Items<IModelDoc2>(connection.Application.GetDocuments()).Select(d=>new{title=d.GetTitle(),path=d.GetPathName(),dirty=d.GetSaveFlag()}).ToArray();
        var ownedPresent=docs.Any(d=>string.Equals(d.path,expected,StringComparison.OrdinalIgnoreCase));
        var audit=Path.Combine(Output,"recovery",DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".json");
        using var budget=new NativeTestBudget(Path.Combine(Root,"artifacts/milestone20/native-budget.json"),"M20",int.MaxValue);
        Write(audit,new{utc=DateTime.UtcNow,mode,expected,ownedPresent,documents=docs,budget=budget.Snapshot,actions="Full path inventory; never close or mutate another document"});
        Console.WriteLine(JsonSerializer.Serialize(new{expected,ownedPresent,documents=docs},Json));
        if(mode=="inspect")return 0;
        Check(budget.Snapshot.OpenTestOwnedTitles.Count==1&&budget.Snapshot.OpenTestOwnedTitles[0]=="working.SLDPRT","Ownership record does not exactly match failed scope");
        var state=Read<JsonElement>(Path.Combine(failed,"steps/0061-state-before-edit.json"));
        Check(string.Equals(state.GetProperty("data").GetProperty("document").GetProperty("savedPath").GetString(),expected,StringComparison.OrdinalIgnoreCase),"Owned path not proven by native state");
        if(mode=="close-owned")
        {
            Check(ownedPresent&&docs.Count(d=>string.Equals(d.path,expected,StringComparison.OrdinalIgnoreCase))==1,"Exact owned document absent or nonunique");
            connection.Application.CloseDoc(expected);
            var remaining=NativeEditOracle.Items<IModelDoc2>(connection.Application.GetDocuments()).Select(d=>d.GetPathName()).ToArray();
            Check(!remaining.Any(p=>string.Equals(p,expected,StringComparison.OrdinalIgnoreCase)),"Exact-path close failed; retain ownership");
            Check(docs.Where(d=>!string.Equals(d.path,expected,StringComparison.OrdinalIgnoreCase)).All(d=>remaining.Contains(d.path)),"Unrelated document inventory changed");
        }
        else Check(!ownedPresent,"Test copy remains open; no ambiguous title-based close is permitted");
        budget.RegisterClosed("working.SLDPRT");
        Write(audit+".reconciled.json",new{utc=DateTime.UtcNow,basis=audit,action="Append exact-path closure reconciliation; preserve original failure and unresolved pre-run original-document restoration",budget=budget.Snapshot,noNativeOpen=true,noDocumentClosedByAudit=mode!="close-owned",originalActiveRestorationProven=false});
        return 0;
    }
}
