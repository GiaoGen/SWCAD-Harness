using System;
using System.Collections.Generic;
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

internal sealed record LedgerEvent(string Kind, string Path, string Title, int ControllerPid, DateTime Utc, int Gdi);
internal sealed class Ledger
{
    public int Milestone { get; set; } = 13;
    public int MaximumNewParts { get; set; } = 0;
    public int MaximumOpenCycles { get; set; } = 5;
    public int NewParts { get; set; }
    public int Opens { get; set; }
    public int Closes { get; set; }
    public List<string> Attempts { get; set; } = new();
    public List<LedgerEvent> Events { get; set; } = new();
}
internal sealed class NativeLifecycle : IManagedDocumentLifecycle, IDisposable
{
    private readonly string ledgerPath;
    private readonly FileStream lease;
    private readonly ISldWorks application;
    internal Ledger Data { get; }
    internal Dictionary<string, (string NativeType, bool Suppressed, Dictionary<ParameterKey,double> Scalars)> Oracle { get; } = new();
    internal int NativeArrayCount;
    internal int OpenedCount, ClosedCount;
    internal NativeLifecycle(string common, string slot, ISldWorks application)
    {
        ledgerPath=Path.Combine(common,"native-budget.json");
        lease=new(Path.Combine(common,"native-budget.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        this.application=application;
        Data=JsonSerializer.Deserialize<Ledger>(File.ReadAllText(ledgerPath), new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        if(Data.Attempts.Contains(slot))throw new InvalidOperationException("Slot was already attempted; no hidden rerun.");
        Data.Attempts.Add(slot);Event("controller-start", "",slot);
    }
    public void BeforeOpen(string path)
    {
        if(Data.Opens>=Data.MaximumOpenCycles||Data.NewParts!=0)throw new InvalidOperationException("M13 native budget exhausted.");
        Data.Opens++;Event("reserve-open",path,"");
    }
    public void Opened(string path,string title)
    {
        OpenedCount++;Event("opened-read-only-copy",path,title);
        var document=(IModelDoc2)application.GetOpenDocumentByName(path);
        Program.Require(document.IsOpenedReadOnly(),"Oracle requires read-only native document.");
        // Independent inventory API, not the production traversal algorithm.
        if(document.FeatureManager.GetFeatures(false) is not Array features)throw new InvalidOperationException("Independent GetFeatures(false) inventory unavailable.");
        NativeArrayCount=features.Length;
        if(features.Length>ContractLimits.Dependencies)throw new InvalidOperationException("Oracle inventory exceeds its safety bound.");
        foreach(var feature in features.Cast<object>().OfType<IFeature>())
        {
            byte[]? reference;
            try { reference=document.Extension.GetPersistReference3(feature) as byte[]; }
            catch(COMException) { continue; }
            if(reference is null||reference.Length==0)continue;
            var key=Convert.ToBase64String(reference);var values=new Dictionary<ParameterKey,double>();
            var suppressed=feature.IsSuppressed();
            if(!suppressed)
            {
                object? definition;
                try { definition=feature.GetDefinition(); } catch(COMException) { definition=null; }
                if(definition is IExtrudeFeatureData2 extrusion && (extrusion.IsBossFeature()||extrusion.IsBaseExtrude()))
                    values[ParameterKey.ExtrusionDepth]=extrusion.GetDepth(true)*1000;
                if(definition is ILinearPatternFeatureData pattern)
                {values[ParameterKey.PatternCount]=pattern.D1TotalInstances;values[ParameterKey.PatternSpacing]=pattern.D1Spacing*1000;}
                Array? faces;
                try { faces=feature.GetFaces() as Array; } catch(COMException) { faces=null; }
                if(faces is not null)
                {
                    Program.Require(faces.Length<=ContractLimits.Geometry,"Oracle face count exceeds its safety bound.");
                    var radii=faces.Cast<object>().OfType<IFace2>().Select(f=>f.GetSurface()).OfType<ISurface>()
                        .Where(s=>s.IsCylinder()).Select(s=>((double[])s.CylinderParams)[6]*2000).Distinct().ToArray();
                    if(radii.Length==1)values[ParameterKey.HoleDiameter]=radii[0];
                }
            }
            Oracle[key]=(feature.GetTypeName2(),suppressed,values);
        }
    }
    public void Closed(string path,string title){ClosedCount++;Data.Closes++;Event("closed-owned",path,title);}
    private void Event(string kind,string path,string title)
    {
        using var p=Process.GetProcessById(application.GetProcessID());p.Refresh();
        var gdi=GetGuiResources(p.Handle,0);Program.Require(p.Responding&&gdi>0&&gdi<7000,"Native process/GDI guard failed.");
        Data.Events.Add(new(kind,path,title,System.Environment.ProcessId,DateTime.UtcNow,gdi));
        var temp=ledgerPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        Program.WriteNew(temp,Data);File.Replace(temp,ledgerPath,null);
    }
    public void Dispose(){try{Event("controller-exit","","");}finally{lease.Dispose();}}
    [DllImport("user32.dll")]private static extern int GetGuiResources(IntPtr handle,int flag);
}
internal static class NativeTests
{
    internal static int Run(string root,string run,string slot,string sourcePath,string? configuration)
    {
        var common=Path.Combine(root,"artifacts","milestone13");var output=Path.Combine(common,run);
        var reportPath=Path.Combine(output,slot+"-result.json");Program.Require(!File.Exists(reportPath),"Slot report already exists.");
        VerifyFreeze(root,output,sourcePath);
        var status="PARTIAL";string? error=null;ExternalPartInspectionResult? inspection=null;
        var oracleVerified=false;int? nativeArrayCount=null;int? oracleScalars=null;
        NativeLifecycle? lifecycle=null;SolidWorksConnection? connection=null;
        var started=DateTime.UtcNow;
        try
        {
            connection=SolidWorksConnection.Connect();
            lifecycle=new(common,run+"/"+slot,connection.Application);
            var copy=Path.Combine(output,"copies",slot,Path.GetFileName(sourcePath));
            inspection=ExternalPartInspection.Inspect(connection,sourcePath,copy,configuration,
                slot=="overflow"?new ExternalObservationLimits(Features:1):null,lifecycle);
            var model=inspection.Observation.Model;var count=inspection.Observation.Inventory;
            Program.Require(inspection.ReadOnlyNative&&inspection.OriginalPreserved&&inspection.CopyPreserved&&inspection.OriginalActiveRestored&&
                inspection.NoOwnedDocumentRemains&&inspection.DirtyOnOpen==inspection.DirtyAfterInspection,"Read-only preservation/cleanup failed.");
            Program.Require(model.Features.All(f=>f.EditSupport==EditSupport.ReadOnly),"M13 exposed mutation.");
            nativeArrayCount=lifecycle.NativeArrayCount;oracleScalars=0;
            if(slot=="overflow")Program.Require(!model.InventoryComplete&&model.LimitOutcome==V03FailureCodes.ObservationLimitExceeded&&count.InventoryCount==1&&count.NativeInventoryCountLowerBound==2,"Overflow masqueraded as complete.");
            else
            {
                Program.Require(model.InventoryComplete&&count.NativeInventoryCountExact,"Normal fixture observation incomplete.");
                Program.Require(count.InventoryCount==lifecycle.NativeArrayCount,"Production traversal count differs from independent GetFeatures(false) count.");
                foreach(var f in model.Features.Where(f=>f.NativeReference is not null))
                {
                    Program.Require(lifecycle.Oracle.TryGetValue(f.NativeReference!.Base64,out var native),"Observed reference not in independent native inventory.");
                    Program.Require(native.NativeType==f.NativeType&&native.Suppressed==(f.Health==ObservationHealth.Suppressed),"Native kind/suppression readback differs.");
                    foreach(var scalar in f.Parameters)
                    {
                        Program.Require(native.Scalars.TryGetValue(scalar.Key,out var value)&&Math.Abs(value-scalar.Value)<0.001,"Observed scalar disagrees with native definition/topology oracle.");
                        oracleScalars++;
                    }
                }
                if(slot=="reopen")
                {
                    var initial=ContractJson.Read<ObservedModel>(File.ReadAllText(Path.Combine(output,"history-a-overlay.json")),ObservedStateValidation.Validate);
                    Program.Require(initial.Selection.ConfigurationId==model.Selection.ConfigurationId&&initial.Selection.Source.Sha256==model.Selection.Source.Sha256&&
                        initial.Features.Select(f=>f.SemanticId).SequenceEqual(model.Features.Select(f=>f.SemanticId)),"Stable identities changed on unchanged cold reopen.");
                }
                if(slot=="configuration")
                {
                    var initial=ContractJson.Read<ObservedModel>(File.ReadAllText(Path.Combine(output,"history-b-overlay.json")),ObservedStateValidation.Validate);
                    Program.Require(initial.Selection.ConfigurationId!=model.Selection.ConfigurationId&&initial.Selection.Source.Sha256==model.Selection.Source.Sha256,"Native configuration change reused identity or changed source.");
                }
            }
            oracleVerified=true;
            using(var stream=new FileStream(Path.Combine(output,slot+"-overlay.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {var bytes=System.Text.Encoding.UTF8.GetBytes(ContractJson.Write(model,ObservedStateValidation.Validate));stream.Write(bytes);stream.Flush(true);}
            status="COMPLETE";
        }
        catch(Exception e){error=e.ToString();}
        finally
        {
            try{lifecycle?.Dispose();connection?.Dispose();}catch(Exception e){status="PARTIAL";error=(error??"")+"\n"+e;}
            Program.WriteNew(reportPath,new{milestone=13,slot,status,error,controllerPid=System.Environment.ProcessId,startedUtc=started,finishedUtc=DateTime.UtcNow,
                inspection,oracleVerified,nativeArrayCount,oracleScalars,opens=lifecycle?.OpenedCount??0,closes=lifecycle?.ClosedCount??0});
        }
        Console.WriteLine($"M13 {slot}: {status}; {inspection?.Observation.Inventory.InventoryCount} features, {oracleScalars} scalars; {reportPath}");
        if(error is not null)Console.Error.WriteLine(error);return status=="COMPLETE"?0:1;
    }
    private static void VerifyFreeze(string root,string output,string source)
    {
        var path=Path.Combine(output,"native-freeze.json");Program.Require(ManagedRevisionStore.Hash(path)==File.ReadAllText(path+".sha256").Trim(),"Native freeze drift.");
        using var json=JsonDocument.Parse(File.ReadAllText(path));
        foreach(var file in json.RootElement.GetProperty("files").EnumerateArray())
            Program.Require(ManagedRevisionStore.Hash(Path.Combine(root,file.GetProperty("path").GetString()!))==file.GetProperty("sha256").GetString(),"Frozen source/binary drift.");
        var originals=json.RootElement.GetProperty("originals").EnumerateArray().ToArray();
        Program.Require(originals.Any(f=>string.Equals(f.GetProperty("Path").GetString(),source,StringComparison.OrdinalIgnoreCase)),"Source not explicitly frozen.");
        foreach(var file in originals)Program.Require(ManagedRevisionStore.Hash(file.GetProperty("Path").GetString()!)==file.GetProperty("Sha256").GetString(),"Original drift.");
    }
}
