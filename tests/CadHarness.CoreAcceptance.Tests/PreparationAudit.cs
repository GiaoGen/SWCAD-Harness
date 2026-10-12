using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using CadHarness.State.V03;

internal static partial class Program
{
    private static void TestTask(TaskDefinition task,List<object> tests)
    {
        void T(string name,bool condition){Check(condition,task.Id+" "+name);tests.Add(new{task.Id,name,passed=true});}
        T("Complete program",task.Program.Operations.Count>0);
        var d=ContractJson.Read<SemanticConstructionDraftV2>(task.DraftJson,SemanticConstructionV2.ValidateDraft);
        var compiled=SemanticConstructionV2.Compile(task.Request,d);var role=compiled.Evidence.EditTargets.Single();
        T("Exact parameter identity",role.ParameterSemanticId.StartsWith(role.Owner+".",StringComparison.Ordinal));
        var facts=ConstructionRequirements.Normalize(task.Request);
        T("Default disclosure",facts.Requirements.Any(r=>r.Source==RequirementSource.Defaulted));
        foreach(var phase in new[]{1,2})
        {
            var value=Tasks.Geometry(task,phase).Value;var changed=task.Program;
            if(role.Parameter==ParameterKey.RevolveRadius)
            {
                var owner=changed.Operations.Single(o=>o.SemanticId==role.Owner);var profile=changed.Operations.Single(o=>o.SemanticId+".profile"==owner.Profile!.SemanticId);
                var edited=RadialProfilePolicy.Edit(profile.Sketch!,role.ParameterSemanticId[(role.Owner+".radius.").Length..],role.Before,value);
                changed=changed with{Operations=changed.Operations.Select(o=>o==profile?o with{Sketch=edited}:o).ToArray()};
            }
            else changed=changed with{Operations=changed.Operations.Select(o=>o.SemanticId==role.Owner?o with{Depth=new(value)}:o).ToArray()};
            T("Phase "+phase+" independent volume",Math.Abs(ConstructionPreflight.EvaluateGeometry(changed).VolumeMm3-Tasks.Geometry(task,phase).Volume)<1e-6);
        }
        var map=d.Nodes.ToDictionary(n=>n.Id,n=>"alternate_"+n.Id);
        var renamed=d with{Nodes=d.Nodes.Select(n=>n with{Id=map[n.Id],Host=n.Host is null?null:map[n.Host],Edge=n.Edge is null?null:n.Edge with{Host=map[n.Edge.Host]}}).ToArray()};
        var other=SemanticConstructionV2.Compile(task.Request,renamed);
        T("Equivalent named history accepted",other.Evidence.EditTargets.Single().Owner!=role.Owner&&Math.Abs(ConstructionPreflight.EvaluateGeometry(other.Response.Program!).VolumeMm3-Tasks.Geometry(task,0).Volume)<1e-6);
        T("C3 post-edit bounds not initial",task.Id!="C3"||Tasks.Geometry(task,1).Bounds[5]==52);
    }
    private sealed class AuditOnly:IManagedRevisionNative
    {
        public CadProgram CurrentProgram=>throw new NotSupportedException();
        public void SaveNative()=>throw new NotSupportedException();
        public void VerifySavedRevision(CadState state,CadProgram program)=>throw new NotSupportedException();
    }
    private sealed class GenericAuditOnly:IGenericRevisionNative
    {
        public GenericRevisionPackage CurrentGeneric=>throw new NotSupportedException();
        public void SaveNative()=>throw new NotSupportedException();
        public void VerifySavedGeneric(GenericRevisionPackage package)=>throw new NotSupportedException();
    }
    private sealed class ExternalAuditOnly:IObservedRevisionNative
    {
        public ExternalEditState CurrentExternal=>throw new NotSupportedException();
        public void SaveNative()=>throw new NotSupportedException();
        public void VerifySavedExternal(ExternalEditState state)=>throw new NotSupportedException();
    }
    private static int PreparationAudit()
    {
        using var connection=SolidWorksConnection.Connect(false);using var budget=new NativeTestBudget(Ledger,"T5",int.MaxValue);
        var resource=NativeResourceGuard.Inspect(connection.Application,budget);Check(resource.Responding&&resource.GdiCount<7000&&budget.Snapshot.OpenTestOwnedTitles.Count==0,"Audit resources/ownership refused");
        var originals=new List<FrozenFile>();using(var registry=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"artifacts/fixture-factory/audits/m14-preflight/fixture-registry.json"))))
        foreach(var row in registry.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var f=row.GetProperty("nativePart");var path=f.GetProperty("path").GetString()!;var current=Identity(path);Check(current.Sha256==f.GetProperty("sha256").GetString(),"Factory original drift");
            Check(connection.Application.GetOpenDocumentByName(path) is null,"Original open; do not take ownership");originals.Add(current);
        }
        var manual=Identity(Path.Combine(Root,"M14测试用夹具.SLDPRT"));Check(manual.Sha256=="60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b"&&connection.Application.GetOpenDocumentByName(manual.Path) is null,"Manual original drift/open");originals.Add(manual);
        var ledgers=Directory.EnumerateFiles(Path.Combine(Root,"artifacts/m20-preparation"),"native-budget.json",SearchOption.AllDirectories).Concat(new[]{Path.Combine(Root,"artifacts/milestone20/native-budget.json"),Path.Combine(Root,"artifacts/milestone14/native-budget.json")}).Distinct()
            .Select(p=>{var readable=string.Equals(Path.GetFullPath(p),Path.GetFullPath(Ledger),StringComparison.OrdinalIgnoreCase)?WriteAuditLedgerCopy(budget.Snapshot):p;return new{path=p,identity=Identity(readable),snapshot=AuditBudget(File.ReadAllText(readable))};}).ToArray();
        Check(ledgers.All(l=>l.snapshot.OpenTestOwnedTitles.Count==0),"Historical unresolved ownership");
        using var stop=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"artifacts/milestone20/acceptance_v3/stop-audit-20261011T145238147.json")));var historical=0;
        foreach(var f in stop.RootElement.GetProperty("preservedEvidence").EnumerateArray()){Check(ManagedRevisionStore.Hash(f.GetProperty("path").GetString()!)==f.GetProperty("sha256").GetString(),"Stopped M20 evidence drift");historical++;}
        var markers=new List<object>();
        foreach(var p in Directory.EnumerateFiles(Path.Combine(Root,"artifacts/m20-preparation"),"recovery.json",SearchOption.AllDirectories))
        {
            using var store=new ManagedRevisionStore(Path.GetDirectoryName(p)!,new AuditOnly());var inspection=store.Inspect(store.WorkingPath);
            if(File.Exists(store.WorkingPath)&&new FileInfo(store.WorkingPath).Length<100)
            {
                var bytes=File.ReadAllText(store.WorkingPath);
                Check(bytes=="blank"||System.Text.RegularExpressions.Regex.IsMatch(bytes,"^native-revision-[0-9]+$"),"Unknown short native file; cannot classify recovery");
                markers.Add(new{identity=Identity(p),syntheticNative=Identity(store.WorkingPath),disposition="Pure-test fake native bytes, not a SOLIDWORKS session or native recovery. Preserve."});continue;
            }
            Check(inspection.Status==ReopenStatus.Quarantined&&inspection.Revision is null&&!File.Exists(store.PointerPath),"Unresolved authoritative recovery: "+p);
            markers.Add(new{identity=Identity(p),inspection.Status,inspection.FailureCode,authorityExists=false,disposition="Preserve initial-failure quarantine; never reuse"});
        }
        Write(Path.Combine(Output,"native-audit.json"),new{utc=DateTime.UtcNow,controller=System.Environment.ProcessId,resource,documents=Documents(connection.Application),budget=budget.Snapshot,ledgers,originals,preservedM20Files=historical,isolatedMarkers=markers,unresolvedAuthoritativeRecovery=0,noNativeOpen=true});return 0;
    }
    private static string WriteAuditLedgerCopy(BudgetSnapshot snapshot)
    {var path=Path.Combine(Output,"t5-ledger-snapshot.json");Write(path,snapshot);return path;}
    private static BudgetSnapshot AuditBudget(string json)
    {
        var result=System.Text.Json.JsonSerializer.Deserialize<BudgetSnapshot>(json,Json)!;
        using var d=System.Text.Json.JsonDocument.Parse(json);
        if(d.RootElement.TryGetProperty("OwnedTitles",out var owned))
        {result.PartsClosed=d.RootElement.GetProperty("DocumentsClosed").GetInt32();result.OpenTestOwnedTitles.AddRange(owned.EnumerateArray().Select(p=>p.GetString()!));}
        return result;
    }
}
