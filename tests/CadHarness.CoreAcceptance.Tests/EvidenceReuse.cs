using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.State;

internal static partial class Program
{
    private static string OwnedEvidencePath(string path)
    {
        var full=Path.GetFullPath(path);Check(full.StartsWith(Path.GetFullPath(Path.Combine(Root,"artifacts/m20-preparation/T5"))+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Reuse is restricted to owned T5 evidence");return full;
    }
    private static CoreEvidence[] LoadCoreReuse()
    {
        var run=Environment.GetEnvironmentVariable("CAD_HARNESS_T5_REUSE_RUN");if(string.IsNullOrWhiteSpace(run))return Array.Empty<CoreEvidence>();
        var directory=OwnedEvidencePath(Path.Combine(Root,"artifacts/m20-preparation/T5",run));
        var freeze=Read<FreezeRecord>(Path.Combine(directory,"freeze.json"));
        foreach(var f in freeze.Files.Where(f=>f.Path.StartsWith(Path.Combine(Root,"src"),StringComparison.OrdinalIgnoreCase)))Check(Identity(f.Path)==f,"Production changed; previous native chains require compatibility review");
        return Definitions().Select(t=>
        {
            var slot=t.Id+"-deterministic";var p=Path.Combine(directory,"slots",slot);
            var n=Identity(Path.Combine(p,"native-result.json"));var c=Identity(Path.Combine(p,"cold-result.json"));var r=Identity(Path.Combine(p,"read-result.json"));
            Check(CompleteStatus(Stage(n.Path),Stage(c.Path),Stage(r.Path),true)=="PASS","Cannot reuse an incomplete native task");
            var read=Stage(r.Path)!;Check(ManagedRevisionStore.Hash(Path.Combine(read.Package!,"working/CADHarnessManagedPart.SLDPRT"))==read.NativeSha&&ManagedRevisionStore.Hash(Path.Combine(read.Package!,"current.json"))==read.ManifestSha,"Reused package authority drift");return new CoreEvidence(slot,n,c,r);
        }).ToArray();
    }
    private static CompletedExternal? LoadCompletedExternal()
    {
        var input=Environment.GetEnvironmentVariable("CAD_HARNESS_T5_EXTERNAL_CONTINUATION");if(string.IsNullOrWhiteSpace(input))return null;
        var package=OwnedEvidencePath(input);var result=Identity(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(package))!,"slots/core-external/native-result.json"));
        var row=Read<JsonElement>(result.Path);
        Check(row.GetProperty("status").GetString()=="FAIL"&&row.GetProperty("failure").GetString()!.Contains("Revision origin differs from the native controller mode",StringComparison.Ordinal),"Only the documented post-operation reporting failure may reconcile");
        foreach(var field in new[]{"nativePassed","editablePassed","followUpPassed","saved","cleanup"})Check(row.GetProperty(field).GetBoolean(),"Native operations did not finish before reporting failure");
        using var store=new ManagedRevisionStore(package,new ExternalAuditOnly());var current=store.ReadCurrent();
        Check(!File.Exists(store.RecoveryPath)&&current.Manifest.Revision==Read<JsonElement>(Path.Combine(package,"controller.json")).GetProperty("revision").GetInt64(),"No valid completed authority");
        Check(ManagedRevisionStore.Hash(store.WorkingPath)==row.GetProperty("nativeSha").GetString()&&ManagedRevisionStore.Hash(store.PointerPath)==row.GetProperty("manifestSha").GetString(),"Reporting failure concealed native drift");
        var steps=Path.Combine(Path.GetDirectoryName(result.Path)!,"steps/native");
        var scalars=Directory.GetFiles(steps,"*-external-scalar.json");Check(scalars.Length==4,"Four public Scalars not evidenced");
        foreach(var path in scalars)
        {var t=Read<JsonElement>(path).GetProperty("data").GetProperty("result").GetProperty("transaction");Check(t.GetProperty("succeeded").GetBoolean()&&t.GetProperty("stateCommitted").GetBoolean()&&t.GetProperty("rebuildSucceeded").GetBoolean(),"Failed public Scalar cannot reconcile");}
        var batch=Read<JsonElement>(Directory.GetFiles(steps,"*-external-batch.json").Single()).GetProperty("data");
        Check(batch.GetProperty("transaction").GetProperty("succeeded").GetBoolean()&&batch.GetProperty("checkpoints").GetInt32()==1,"Public batch not evidenced");
        var fault=Read<JsonElement>(Directory.GetFiles(steps,"*-external-first-edit-fault.json").Single()).GetProperty("data").GetProperty("transaction");
        Check(!fault.GetProperty("succeeded").GetBoolean()&&fault.GetProperty("rollbackSucceeded").GetBoolean()&&!fault.GetProperty("stateCommitted").GetBoolean(),"Atomic restoration not evidenced");
        return new("core",package,result,Identity(Path.Combine(package,"expected.json")),Identity(store.PointerPath),Identity(store.WorkingPath));
    }
    private static void ReconcileCompletedExternal(CompletedExternal evidence)
    {
        using var store=new ManagedRevisionStore(evidence.Package,new ExternalAuditOnly());var previous=Read<JsonElement>(evidence.PriorResult.Path);var revision=store.ReadCurrent().Manifest.Revision;
        Write(Path.Combine(Output,"slots/core-external/reconciled-successful-steps.json"),new{status="PASS",reconciledSuccessfulSteps=true,originalControllerStatus="FAIL",originalFailureRetained=evidence.PriorResult,
            basis="Frozen successful public operations/Oracle/restoration; only post-disposal test metadata mode failed. New independent cold controller still required.",controller=previous.GetProperty("controller").GetInt32(),package=evidence.Package,revision,
            nativeSha=evidence.Native.Sha256,manifestSha=evidence.Pointer.Sha256,nativePassed=true,editablePassed=true,followUpPassed=true,reopenPassed=false,saved=true,cleanup=true,elapsedMilliseconds=previous.GetProperty("elapsedMilliseconds").GetDouble()});
    }
}
