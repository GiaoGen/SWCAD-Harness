using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static class RecoveryAudit
{
    internal static int Assess(string root,string id)
    {
        var milestone=Path.Combine(root,"artifacts","milestone14");var output=Path.Combine(milestone,id);
        Program.Check(!Directory.Exists(output),"Assessment namespace already exists.");Directory.CreateDirectory(output);
        using var lease=new FileStream(Path.Combine(milestone,"native-budget.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var budgetPath=Path.Combine(milestone,"native-budget.json");var budgetIdentity=AcceptanceFiles.Identity(budgetPath);
        var budget=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(budgetPath))!;
        var schedule=AcceptanceFiles.Read<AcceptanceSchedule>(Path.Combine(milestone,"acceptance-v5","schedule.json"));
        var freeze=AcceptanceFiles.Read<AcceptanceFreeze>(Path.Combine(milestone,"acceptance-v5","freeze.json"));
        AcceptanceFiles.Verify(freeze.Schedule);foreach(var file in freeze.Files)AcceptanceFiles.Verify(file);
        Program.Check(budget.OpenAttempts==14&&budget.MaximumOpenCycles==27&&budget.DocumentsClosed==14&&budget.MaximumNewParts==0&&budget.OwnedTitles.Count==0,"Recovery budget changed; reassess rather than reuse this plan.");
        var grantRecords=new[]{("acceptance-v1",12,20),("acceptance-v3",20,23),("acceptance-v5",23,27)}.Select(g=>
        {
            var path=Path.Combine(milestone,g.Item1,"authorization.json");var grant=AcceptanceFiles.Read<M14AdditionalBudget>(path);grant.Validate();
            Program.Check(grant.PreviousMaximumOpens==g.Item2&&grant.MaximumCumulativeOpens==g.Item3,"Broken grant lineage.");
            var identity=AcceptanceFiles.Identity(path);var historical=AcceptanceFiles.Read<AcceptanceFreeze>(Path.Combine(milestone,g.Item1,"freeze.json"));
            Program.Check(historical.Files.Contains(identity),"Historical grant not frozen.");return new{grant,identity};
        }).ToArray();
        var registryPath=Path.Combine(root,"artifacts","fixture-factory","audits","final-v1","fixture-registry.json");
        using var registry=JsonDocument.Parse(File.ReadAllText(registryPath));
        var fixtures=registry.RootElement.GetProperty("fixtures").EnumerateArray().Select(f=>
        {
            Program.Check(f.GetProperty("dataset").GetString()=="development"&&f.GetProperty("status").GetString()=="PREPARATION_SELF_CHECKED_NOT_M14_ACCEPTANCE","Fixture provenance changed.");
            var files=new[]{"manifest","proof","ready","nativePart"}.Select(k=>f.GetProperty(k).Deserialize<FrozenFile>(AcceptanceFiles.Json)!).ToArray();foreach(var file in files)AcceptanceFiles.Verify(file);
            using var ready=JsonDocument.Parse(File.ReadAllText(files[2].Path));foreach(var key in new[]{"manifest","proof","nativePart","readerFreeze"})AcceptanceFiles.Verify(ready.RootElement.GetProperty(key).Deserialize<FrozenFile>(AcceptanceFiles.Json)!);
            using var proof=JsonDocument.Parse(File.ReadAllText(files[1].Path));using var manifest=JsonDocument.Parse(File.ReadAllText(files[0].Path));
            Program.Check(proof.RootElement.GetProperty("passed").GetBoolean()&&manifest.RootElement.GetProperty("nativePart").Deserialize<FrozenFile>(AcceptanceFiles.Json)==files[3]&&proof.RootElement.GetProperty("nativePart").Deserialize<FrozenFile>(AcceptanceFiles.Json)==files[3],"Fixture identity/self-check disagreement.");
            return new{id=f.GetProperty("fixtureId").GetString(),files,history=manifest.RootElement.GetProperty("features").Clone(),nativeEditAcceptance=false};
        }).ToArray();
        foreach(var input in schedule.Inputs)AcceptanceFiles.Verify(input.Source);
        var package=schedule.Scenarios.Single(s=>s.Id=="core").Package;
        using var store=new ManagedRevisionStore(package,new DiskOnlyNative());var authority=store.ReadCurrent();var inspection=store.Inspect(store.WorkingPath);
        Program.Check(inspection.FailureCode is null&&!inspection.RequiresNativeRecovery&&authority.State.Revision==4,"Disk authority changed; stop.");
        AcceptanceFiles.Verify(budgetIdentity);
        Program.WriteNew(Path.Combine(output,"offline-audit.json"),new
        {
            milestone=14,status="PARTIAL",utc=DateTime.UtcNow,nativeCalls=0,newParts=0,newOpens=0,
            budget=budgetIdentity,budget.OpenAttempts,budget.DocumentsClosed,budget.MaximumOpenCycles,remaining=budget.MaximumOpenCycles-budget.OpenAttempts,budget.OwnedTitles,
            grants=grantRecords,registry=AcceptanceFiles.Identity(registryPath),fixtures,sources=schedule.Inputs.Select(i=>new{i.Id,i.Provenance,i.Source}),
            authority=authority.Manifest,inspection.RequiresNativeRecovery,working=AcceptanceFiles.Identity(store.WorkingPath),
            preservedFrozenFiles=freeze.Files.Count,previousScheduleMaximum=schedule.Scenarios.Sum(s=>s.MaximumOpens),
            oldScheduleExecutable=false,reason="Old continuation assumes revision 1; authority is revision 4. Missing final v5 Oracle/transaction reports cannot be recreated as passed evidence.",
            requiredOpenPlan=new[]{new{id="core-revision4-three-row-plus-dual-batch-and-faults",opens=8},new{id="native-engine-rebuild-failure-isolated-copy",opens=2},new{id="equation-and-blind-refusals",opens=2},
                new{id="native-configuration-and-unsupported-pattern-history",opens=1},new{id="actual-design-table-driver",opens=1},new{id="origin-public-rename-reorder-batch",opens=2},
                new{id="current-A1-public-four-row-edit",opens=2},new{id="old-A1-public-edit-if-qualified-else-read-only",opens=2},new{id="independent-held-out-public-edit",opens=2}},
            proposedRemainingCeiling=24,proposedCumulativeCeiling=38,requestedAdditionalCeiling=11,proposalAuthorized=false,
            missingInputs=new[]{"Independent custody held-out engineer Part/spec/hash", "Existing single-configuration unsupported linear-pattern history (or explicit negative-only multi-config source spec)","Existing native actual design-table Part/spec/hash"},
            productionQualified=NativeQualificationCandidates.Rows.Select(r=>new{r.Parameter,r.Qualified}),
            recoveryProof=AcceptanceFiles.Identity(Path.Combine(milestone,"recovery-v1","closure-proof.json"))
        });
        Console.WriteLine("Offline M14 audit: 14/27 opens, 13 remaining; complete planned maximum 22 + 2 contingency; proposal only, no authorization or native calls.");return 0;
    }
    internal static int Run(string root,string id)
    {
        var milestone=Path.Combine(root,"artifacts","milestone14");var output=Path.Combine(milestone,id);
        Program.Check(!Directory.Exists(output),"Recovery audit namespace already exists.");Directory.CreateDirectory(output);
        using var lease=new FileStream(Path.Combine(milestone,"native-budget.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var budgetPath=Path.Combine(milestone,"native-budget.json");var beforeIdentity=AcceptanceFiles.Identity(budgetPath);
        var budget=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(budgetPath))!;
        Program.Check(budget.Milestone==14&&budget.MaximumNewParts==0&&budget.MaximumOpenCycles==27&&budget.OwnedTitles.Count==1,"Unexpected budget/ownership state; manual review required.");
        File.Copy(budgetPath,Path.Combine(output,"budget-before.json"),false);
        var last=budget.Events.Cast<JsonElement>().Last(e=>e.GetProperty("kind").GetString()=="opened-owned");
        var controller=last.GetProperty("controller").GetInt32();
        try {using var process=Process.GetProcessById(controller);Program.Check(process.HasExited,"Interrupted controller is still alive; do not take ownership.");}catch(ArgumentException){}
        var working=Path.GetFullPath(last.GetProperty("value").GetString()!);var package=Path.GetDirectoryName(Path.GetDirectoryName(working)!)!;
        Program.Check(working.StartsWith(Path.Combine(milestone,"acceptance-v3","packages","core")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Ownership is not the documented interrupted package.");
        var schedule=AcceptanceFiles.Read<AcceptanceSchedule>(Path.Combine(milestone,"acceptance-v5","schedule.json"));
        foreach(var i in schedule.Inputs)AcceptanceFiles.Verify(i.Source);
        var grants=new[]{("acceptance-v1",12,20),("acceptance-v3",20,23),("acceptance-v5",23,27)}.Select(g=>{
            var path=Path.Combine(milestone,g.Item1,"authorization.json");var grant=AcceptanceFiles.Read<M14AdditionalBudget>(path);grant.Validate();
            Program.Check(grant.PreviousMaximumOpens==g.Item2&&grant.MaximumCumulativeOpens==g.Item3,"Broken human authorization chain.");
            var identity=AcceptanceFiles.Identity(path);var freeze=AcceptanceFiles.Read<AcceptanceFreeze>(Path.Combine(milestone,g.Item1,"freeze.json"));
            Program.Check(freeze.Files.Contains(identity),"Authorization is not bound to its historical freeze.");return new{grant,identity};}).ToArray();
        using var store=new ManagedRevisionStore(package,new DiskOnlyNative());var revision=store.ReadCurrent();var inspection=store.Inspect(working);
        Program.Check(inspection.FailureCode is null&&revision.External is not null,"Disk authority cannot be verified: "+inspection.Message);
        var fingerprint=AcceptanceFiles.Identity(working);
        if(!inspection.RequiresNativeRecovery)Program.Check(fingerprint.Sha256==revision.Manifest.NativePart.Sha256,"Unjournaled working-copy drift; no cleanup/adoption.");
        using var connection=SolidWorksConnection.Connect(false);var app=connection.Application;
        using var nativeProcess=Process.GetProcessById(app.GetProcessID());nativeProcess.Refresh();var gdi=GetGuiResources(nativeProcess.Handle,0);
        Program.Check(nativeProcess.Responding&&gdi>0&&gdi<7000,"Native resource guard failed.");
        var docs=NativeEditOracle.Items<IModelDoc2>(app.GetDocuments()).ToArray();
        var exact=docs.Where(d=>string.Equals(d.GetPathName(),working,StringComparison.OrdinalIgnoreCase)).ToArray();
        Program.Check(exact.Length<=1&&!docs.Any(d=>budget.OwnedTitles.Contains(d.GetTitle())&&!string.Equals(d.GetPathName(),working,StringComparison.OrdinalIgnoreCase)),"Native title/path ownership is ambiguous.");
        var owned=exact.SingleOrDefault();if(owned is not null)Program.Check(owned.GetTitle()==budget.OwnedTitles[0]&&owned.GetType()==(int)swDocumentTypes_e.swDocPART,"Native owned identity differs.");
        var others=docs.Where(d=>d!=owned).Select(d=>new{path=d.GetPathName(),title=d.GetTitle(),dirty=d.GetSaveFlag()}).ToArray();
        var active=app.IActiveDoc2 as IModelDoc2;var activePath=active?.GetPathName();
        Program.WriteNew(Path.Combine(output,"verified-before.json"),new{utc=DateTime.UtcNow,controller,controllerAbsent=true,appPid=app.GetProcessID(),gdi,budget=beforeIdentity,grants,working=fingerprint,
            authority=revision.Manifest,inspection.Status,inspection.RequiresNativeRecovery,ownedPresent=owned is not null,ownedDirty=owned?.GetSaveFlag(),activePath,engineerDocuments=others,sources=schedule.Inputs.Select(i=>new{i.Id,i.Source}),newOpens=0,newParts=0});
        if(owned is not null)
        {
            app.CloseDoc(owned.GetTitle());
            Program.Check(app.GetOpenDocumentByName(working) is null,"Owned document remains open; retain ownership and stop.");
            if(Marshal.IsComObject(owned))Marshal.ReleaseComObject(owned);
        }
        var after=NativeEditOracle.Items<IModelDoc2>(app.GetDocuments()).Select(d=>new{path=d.GetPathName(),title=d.GetTitle(),dirty=d.GetSaveFlag()}).ToArray();
        Program.Check(others.OrderBy(d=>d.path).SequenceEqual(after.OrderBy(d=>d.path)),"Engineer document set/dirty state changed.");
        if(activePath is not null&&!string.Equals(activePath,working,StringComparison.OrdinalIgnoreCase))
            Program.Check(app.IActiveDoc2 is IModelDoc2 restored&&string.Equals(restored.GetPathName(),activePath,StringComparison.OrdinalIgnoreCase),"Engineer active document changed.");
        foreach(var i in schedule.Inputs)AcceptanceFiles.Verify(i.Source);AcceptanceFiles.Verify(fingerprint);
        var closePath=Path.Combine(output,"closure-proof.json");
        Program.WriteNew(closePath,new{utc=DateTime.UtcNow,exactWorkingPath=working,nativePid=app.GetProcessID(),action=owned is null?"Observed previously owned document already closed":"Closed exact verified owned copy without save/rebuild",
            after,workingPreserved=true,sourcePreserved=true,authoritativeRevision=revision.State.Revision,inspection.RequiresNativeRecovery,newOpens=0,newParts=0});
        // The immutable absence/closure proof precedes the reconciled mutable ledger.
        var proof=AcceptanceFiles.Identity(closePath);var title=budget.OwnedTitles.Single();budget.OwnedTitles.Remove(title);budget.DocumentsClosed++;
        budget.Events.Add(new{kind=owned is null?"ownership-reconciled-already-closed":"recovery-closed-owned",value=working,controller=System.Environment.ProcessId,native=app.GetProcessID(),utc=DateTime.UtcNow,gdi,audit=proof});
        var temp=budgetPath+".reconciliation.tmp";using(var stream=new FileStream(temp,FileMode.CreateNew)){JsonSerializer.Serialize(stream,budget,new JsonSerializerOptions{WriteIndented=true});stream.Flush(true);}File.Replace(temp,budgetPath,null);
        File.Copy(budgetPath,Path.Combine(output,"budget-after.json"),false);
        Program.WriteNew(Path.Combine(output,"result.json"),new{status="OWNERSHIP_RECONCILED",opens=budget.OpenAttempts,maximum=budget.MaximumOpenCycles,closed=budget.DocumentsClosed,budget.OwnedTitles,
            proof,budgetBefore=beforeIdentity,budgetAfter=AcceptanceFiles.Identity(budgetPath),revision=revision.State.Revision,inspection.RequiresNativeRecovery,nativeAcceptance=false,newOpens=0,newParts=0});
        Console.WriteLine($"Ownership reconciled: {budget.OpenAttempts}/{budget.MaximumOpenCycles} opens, {budget.DocumentsClosed} closes, zero new opens; revision={revision.State.Revision}.");return 0;
    }
    private sealed class DiskOnlyNative : IObservedRevisionNative
    {
        public ExternalEditState CurrentExternal=>throw new InvalidOperationException("Recovery audit never stages native state.");
        public void SaveNative()=>throw new InvalidOperationException("Recovery audit cannot save.");
        public void VerifySavedExternal(ExternalEditState s)=>throw new InvalidOperationException("Recovery audit cannot reopen or certify native geometry.");
    }
    [DllImport("user32.dll")]private static extern uint GetGuiResources(IntPtr handle,uint flags);
}
