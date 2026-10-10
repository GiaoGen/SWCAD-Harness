using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;

// M14C source screening only; never promotes capability or writes selected originals.
internal static class ExternalCatalog
{
    internal static int Audit(string root,string id)
    {
        var directory=Path.Combine(root,"artifacts","milestone14");var output=Path.Combine(directory,id);
        Program.Check(!Directory.Exists(output),"Never overwrite M14C audit.");
        var budgetFile=AcceptanceFiles.Identity(Path.Combine(directory,"native-budget.json"));
        var budget=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(budgetFile.Path))!;
        Program.Check(budget.OwnedTitles.Count==0&&budget.OpenAttempts==budget.DocumentsClosed&&budget.MaximumNewParts==0,"Unresolved native accounting.");
        var prior=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"acceptance-v18","initial-budget.json")));
        var latest=JsonDocument.Parse(File.ReadAllText(budgetFile.Path));
        var prefix=prior.RootElement.GetProperty("Events").EnumerateArray().Select(e=>e.GetRawText()).ToArray();
        var events=latest.RootElement.GetProperty("Events").EnumerateArray().Select(e=>e.GetRawText()).ToArray();
        Program.Check(events.Take(prefix.Length).SequenceEqual(prefix),"Historical lifecycle events changed.");
        var identities=new List<FrozenFile>();
        foreach(var run in Directory.EnumerateDirectories(directory,"acceptance-v*"))
        {
            var file=Path.Combine(run,"freeze.json");
            if(File.Exists(file)&&int.TryParse(Path.GetFileName(run).Replace("acceptance-v",""),out var version)&&version>=18)
            {
                var freeze=AcceptanceFiles.Read<AcceptanceFreeze>(file);identities.Add(freeze.Schedule);identities.AddRange(freeze.Files);
            }
            var catalog=Path.Combine(run,"catalog.json");
            if(File.Exists(catalog)){var freeze=AcceptanceFiles.Read<Freeze>(catalog);identities.AddRange(freeze.Files);identities.AddRange(freeze.Inputs.Select(i=>i.Source));}
        }
        var registry=Path.Combine(root,"artifacts","fixture-factory","audits","final-v1","fixture-registry.json");
        using var data=JsonDocument.Parse(File.ReadAllText(registry));identities.Add(AcceptanceFiles.Identity(registry));
        foreach(var f in data.RootElement.GetProperty("fixtures").EnumerateArray())foreach(var key in new[]{"manifest","proof","ready","nativePart"})identities.Add(f.GetProperty(key).Deserialize<FrozenFile>(AcceptanceFiles.Json)!);
        var unique=identities.DistinctBy(f=>(f.Path,f.Sha256)).ToArray();foreach(var f in unique)AcceptanceFiles.Verify(f);
        var allMarkers=Directory.EnumerateFiles(directory,"recovery.json",SearchOption.AllDirectories).ToArray();
        var retainedTestMarkers=allMarkers.Where(p=>Path.GetRelativePath(directory,p).Split(Path.DirectorySeparatorChar)[0] is "pure" or "regression").ToArray();
        var markers=allMarkers.Except(retainedTestMarkers).ToArray();
        Program.Check(markers.Length==0,"Unresolved native recovery marker remains.");AcceptanceFiles.Verify(budgetFile);
        Program.WriteNew(Path.Combine(output,"audit.json"),new{utc=DateTime.UtcNow,passed=true,controllerBinary=AcceptanceFiles.Identity(typeof(Program).Assembly.Location),budget=budgetFile,budget.OpenAttempts,budget.DocumentsClosed,budget.MaximumNewParts,budget.OwnedTitles,
            preservedEventPrefix=prefix.Length,verifiedFrozenIdentities=unique.Length,identities=unique,unresolvedRecoveryMarkers=markers,
            retainedPureAndRegressionMarkers=retainedTestMarkers.Select(AcceptanceFiles.Identity).ToArray(),nativeCalls=0});
        Console.WriteLine($"M14C final audit: {unique.Length} frozen identities preserved, {budget.OpenAttempts}/{budget.DocumentsClosed}, no owned documents/recovery markers.");return 0;
    }
    internal sealed record Input(string Id,FrozenFile Source,string Provenance,string? Configuration,bool PreviouslyUsedForTuning);
    internal sealed record Freeze(string Baseline,FrozenFile Authorization,Input[] Inputs,FrozenFile[] Files);
    internal static int Prepare(string root,string run)
    {
        var directory=Path.Combine(root,"artifacts","milestone14",run);Directory.CreateDirectory(directory);
        var budget=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(Path.Combine(root,"artifacts","milestone14","native-budget.json")))!;
        Program.Check(budget.OwnedTitles.Count==0,"Resolve ownership before catalog.");
        var grant=Path.Combine(directory,"authorization.json");
        AcceptancePreparation.Write(grant,new M14AdditionalBudget("0.3",true,14,27,int.MaxValue-27,int.MaxValue,0,
            "No user limit. M14C explicit human authorization in attached final acceptance request: unlimited cumulative opens; bounded scenarios, originals protected, no new Parts masquerading as independent models, stop on repeated stagnation. Separate from M14A/B grants."));
        Input Source(string id,string path,string provenance,string? config=null,bool tuned=true)=>new(id,AcceptanceFiles.Identity(path),provenance,config,tuned);
        var third=Path.Combine(root,"artifacts","milestone14","thirdparty-candidates-v1");
        var inputs=new[]{Source("a1-current",@"D:\document\A1.SLDPRT","User-provided engineer A1; named author unknown; created after intake and used for diagnosis; never held-out"),
            Source("a1-old",Path.Combine(root,"artifacts","milestone14","source-v3","package","working","CADHarnessManagedPart.SLDPRT"),"Preserved byte-identical old engineer A1; source-v3 lineage; never held-out"),
            Source("connector",@"D:\Desktop\solidworks\三爪夹具\2号连接件.SLDPRT","User-provided non-Harness engineering model, previously M13 inspected; precise author/workflow unverified"),
            Source("wrist",@"D:\Desktop\solidworks\三爪夹具\腕关节.SLDPRT","User-provided non-Harness engineering model, previously M13 inspected; precise author/workflow unverified"),
            Source("suppressed-default",@"D:\Download\压制.SLDPRT","User-provided engineer two-configuration model; previously M13 inspected","默认"),
            Source("suppressed-alternate",@"D:\Download\压制.SLDPRT","Same source; native alternate configuration explicitly provided by user","未压缩")}
            .Concat(new[]{"Body Front Plate.SLDPRT","Body Top Plate.SLDPRT","Body Side Plate.SLDPRT"}.Select((name,i)=>Source("thirdparty-"+i,Path.Combine(third,name),
                "Josh Villbrandt Mecanumbot-CAD; Apache-2.0; fixed c50a655aef61ac1a6340b551ec4e837e54e871c4 dated 2017-08-14; third-party native-history candidate, no prior Harness tuning; support unknown before screening",null,false))).ToArray();
        var second=Path.Combine(root,"artifacts","milestone14","thirdparty-candidates-v2");
        if(File.Exists(Path.Combine(second,"plate.SLDPRT")))
            inputs=inputs.Concat(new[]{"plate.SLDPRT","columnPlate_short.SLDPRT"}.Select((name,i)=>Source("thirdparty2-"+i,Path.Combine(second,name),
                "Aubrey / AubreyC coaxial-uav, fixed 75b8fbb7f0f55fe5f08d517074a916808586b818, git author/commit date 2016-05-29; README describes native CAD design; license not stated, local test only; no Harness tuning",null,false))).ToArray();
        var old=AcceptanceFiles.Read<AcceptanceSchedule>(Path.Combine(root,"artifacts","milestone14","acceptance-v5","schedule.json"));
        inputs=inputs.Concat(new[]{"dev_unknown_descendant","dev_equation_driver"}.Select(id=>Source(id,old.Inputs.Single(i=>i.Id==id).Source.Path,"Frozen Factory development source; negative only, never independent"))).ToArray();
        var installed=@"D:\Solidworks Crops\SOLIDWORKS";
        inputs=inputs.Concat(new[]{("vendor-base",Path.Combine(installed,@"data\Structure System - Connection Elements\Sample\Base Plate.SLDPRT")),
            ("vendor-splice",Path.Combine(installed,@"data\Structure System - Connection Elements\Sample\Beam Splice.SLDPRT")),
            ("vendor-cavity",Path.Combine(installed,@"sldBenchmarking\Macro\Mold\Moldbase\sw3dps-hasco metric cavity plate a.sldprt"))}
            .Where(i=>File.Exists(i.Item2)).Select(i=>Source(i.Item1,i.Item2,"Existing installed SOLIDWORKS sample candidate; vendor-directory custody verified, named author/workflow unverified; never Harness generated or tuned",null,false))).ToArray();
        AcceptancePreparation.Write(Path.Combine(directory,"environment.json"),new{utc=DateTime.UtcNow,excelProgIdRegistered=Type.GetTypeFromProgID("Excel.Application") is not null,
            designTableRequirement="https://help.solidworks.com/2024/English/SolidWorks/sldworks/c_Design_Table_Configurations.htm",actualTableDriverVerified=false});
        var files=new[]{AcceptanceFiles.Identity(grant),AcceptanceFiles.Identity(Path.Combine(directory,"environment.json"))}.Concat(inputs.Select(i=>i.Source)).Concat(Directory.EnumerateFiles(Path.Combine(directory,"source"),"*",SearchOption.AllDirectories).Select(AcceptanceFiles.Identity))
            .Concat(Directory.EnumerateFiles(Path.Combine(directory,"bin")).Select(AcceptanceFiles.Identity)).Concat(Directory.EnumerateFiles(third).Select(AcceptanceFiles.Identity)).ToArray();
        AcceptancePreparation.Write(Path.Combine(directory,"catalog.json"),new Freeze("58e5dec",AcceptanceFiles.Identity(grant),inputs,files));
        Console.WriteLine($"M14C catalog frozen: {inputs.Length} sources, one open per immutable named scenario, no original mutation.");return 0;
    }
    internal static int Run(string root,string run,string id,bool qualification=false,bool diagnostic=false)
    {
        var directory=Path.Combine(root,"artifacts","milestone14",run);var frozen=AcceptanceFiles.Read<Freeze>(Path.Combine(directory,"catalog.json"));
        foreach(var file in frozen.Files)AcceptanceFiles.Verify(file);
        foreach(var source in frozen.Inputs)AcceptanceFiles.Verify(source.Source);
        var suffix=diagnostic?"-diagnostic":qualification?"-qualification":"";
        var input=frozen.Inputs.Single(i=>i.Id==id);var output=Path.Combine(directory,"results",id+suffix);Program.Check(!Directory.Exists(output),"Never overwrite catalog attempt.");
        Directory.CreateDirectory(output);using var connection=SolidWorksConnection.Connect(false);
        using var ledger=new NativeTests.Ledger(Path.Combine(root,"artifacts","milestone14"),connection.Application,run+"/"+id+suffix,frozen.Authorization.Path,1);
        var before=ledger.Titles();var journal=new StepJournal(Path.Combine(output,"steps"),run+"/"+id,new{catalog=AcceptanceFiles.Identity(Path.Combine(directory,"catalog.json"))},()=>new{ledger.Data.OpenAttempts,ledger.Data.DocumentsClosed,owned=ledger.Data.OwnedTitles.ToArray()});
        string? failure=null;
        try
        {
            journal.Add(new{step="independent-source",input,nativeVersionHistory=connection.Application.VersionHistory(input.Source.Path),runtimeVersion=connection.Application.RevisionNumber()});
            if(diagnostic)
            {
                var copy=Path.Combine(output,"diagnostic.SLDPRT");File.Copy(input.Source.Path,copy,false);
                var active=(connection.Application.IActiveDoc2 as IModelDoc2)?.GetTitle();
                ledger.BeforeOpen(copy);var errors=0;var warnings=0;
                var doc=connection.Application.OpenDoc6(copy,(int)swDocumentTypes_e.swDocPART,(int)(swOpenDocOptions_e.swOpenDocOptions_Silent|swOpenDocOptions_e.swOpenDocOptions_ReadOnly),input.Configuration??"",ref errors,ref warnings) as IModelDoc2;
                Program.Check(doc is not null,"Diagnostic copy did not open.");ledger.Opened(copy,doc!.GetTitle());
                try
                {
                    Program.Check(errors==0&&warnings==0,"Diagnostic native open warning/error.");
                    void Capture(string phase)
                    {
                        object Describe(IFeature f)
                        {
                            var reference=doc.Extension.GetPersistReference3(f) as byte[];var status=-1;
                            var resolved=reference is null?null:doc.Extension.GetObjectByPersistReference3(reference,out status) as IFeature;
                            var roundtrip=resolved is null?null:doc.Extension.GetPersistReference3(resolved) as byte[];
                            var nativeError=f.GetErrorCode2(out var warning);
                            var sketch=f.GetSpecificFeature2() as ISketch;
                            var segments=sketch is null?Array.Empty<ISketchSegment>():NativeEditOracle.Items<ISketchSegment>(sketch.GetSketchSegments()).Where(s=>!s.ConstructionGeometry).ToArray();
                            var extrude=f.GetDefinition() as IExtrudeFeatureData2;
                            var dimensions=new System.Collections.Generic.List<object>();var d=f.GetFirstDisplayDimension() as IDisplayDimension;var n=0;
                            while(d is not null)
                            {
                                Program.Check(++n<=64,"Diagnostic dimension bound exceeded.");var dimension=(IDimension)d.GetDimension2(0);
                                dimensions.Add(new{type=d.Type2,dimension.SystemValue,designTableDriven=dimension.IsDesignTableDimension(),dimension.ReadOnly,dimension.DrivenState,
                                    reference=doc.Extension.GetPersistReference3(dimension) is byte[] dr?Convert.ToBase64String(dr):null});
                                d=f.GetNextDisplayDimension(d) as IDisplayDimension;
                            }
                            return new{name=f.Name,nativeType=f.GetTypeName2(),underlyingType=f.GetTypeName(),reference=reference is null?null:Convert.ToBase64String(reference),
                                referenceStatus=status,referenceRoundtrips=reference is not null&&status==0&&roundtrip is not null&&reference.SequenceEqual(roundtrip),
                                nativeError,warning,specificSketch=sketch is not null,activeSegments=segments.Length,
                                activeCircles=segments.OfType<ISketchArc>().Count(a=>a.IsCircle()==1),
                                extrusion=extrude is null?null:new{boss=extrude.IsBossFeature(),baseExtrude=extrude.IsBaseExtrude(),endCondition=extrude.GetEndCondition(true),
                                    thin=extrude.IsThinFeature(),bothDirections=extrude.BothDirections,depthMeters=extrude.GetDepth(true)},dimensions};
                        }
                        var features=new System.Collections.Generic.List<object>();var visits=0;
                        for(var f=doc.FirstFeature() as IFeature;f is not null;f=f.GetNextFeature() as IFeature)
                        {
                            Program.Check(++visits<=512,"Diagnostic feature bound exceeded.");
                            var parents=f.GetParents();var children=f.GetChildren();
                            features.Add(new{feature=Describe(f),suppressed=f.IsSuppressed(),parentArrayReturned=parents is Array,childArrayReturned=children is Array,
                                parents=NativeEditOracle.Items<IFeature>(parents).Select(Describe).ToArray(),children=NativeEditOracle.Items<IFeature>(children).Select(Describe).ToArray()});
                        }
                        journal.Add(new{step=phase,configuration=doc.ConfigurationManager.ActiveConfiguration.Name,configurations=doc.GetConfigurationNames(),hasDesignTable=doc.Extension.HasDesignTable(),
                            features,equations=((IEquationMgr)doc.GetEquationMgr()).GetCount()});
                    }
                    Capture("actual-native-dependency-inventory");
                    if(id=="vendor-cavity")
                    {
                        IFeature? host=null;
                        for(var f=doc.FirstFeature() as IFeature;f is not null;f=f.GetNextFeature() as IFeature)
                            if(f.GetTypeName2()=="Extrusion"){host=f;break;}
                        Program.Check(host is not null&&host.GetDefinition() is IExtrudeFeatureData2,"Actual table host definition missing.");
                        object ReadDriver()
                        {
                            var data=(IExtrudeFeatureData2)host!.GetDefinition();var d=host.GetFirstDisplayDimension() as IDisplayDimension;
                            Program.Check(d is not null,"Actual table host depth dimension missing.");var dimension=(IDimension)d!.GetDimension2(0);
                            Program.Check(dimension.IsDesignTableDimension()&&Math.Abs(dimension.SystemValue-data.GetDepth(true))<1e-9,"Actual table depth driver/definition disagree.");
                            return new{configuration=doc.ConfigurationManager.ActiveConfiguration.Name,featureReference=Convert.ToBase64String((byte[])doc.Extension.GetPersistReference3(host)),
                                hasDesignTable=doc.Extension.HasDesignTable(),designTableDriven=dimension.IsDesignTableDimension(),depthMeters=data.GetDepth(true),dimensionMeters=dimension.SystemValue};
                        }
                        var beforeDepth=((IExtrudeFeatureData2)host!.GetDefinition()).GetDepth(true);var driverBefore=ReadDriver();
                        var switched=doc.ShowConfiguration2("K 20-095 095-9");var rebuilt=doc.ForceRebuild3(false);var driverAfter=ReadDriver();
                        journal.Add(new{step="actual-design-table-depth-driver",driverBefore,switched,rebuilt,driverAfter});
                        Program.Check(switched&&rebuilt&&doc.Extension.HasDesignTable()&&Math.Abs(((IExtrudeFeatureData2)host.GetDefinition()).GetDepth(true)-beforeDepth)>1e-6,"Actual table configuration failed to change controlled depth.");
                        Capture("table-driven-configuration-native-inventory");
                    }
                    if(id=="suppressed-default")
                    {
                        Program.Check(doc.ShowConfiguration2("未压缩"),"Native configuration switch failed.");
                        Program.Check(doc.ConfigurationManager.ActiveConfiguration.Name=="未压缩","Actual active configuration did not change.");Capture("actual-configuration-switch");
                    }
                }
                finally
                {
                    var title=doc.GetTitle();connection.Application.CloseDoc(title);Program.Check(connection.Application.GetOpenDocumentByName(copy) is null,"Diagnostic copy remained open.");ledger.Closed(copy,title);
                    if(active is not null){var e=0;connection.Application.ActivateDoc3(active,false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref e);Program.Check(e==0,"Active restoration failed.");}
                }
                Program.Check(ManagedRevisionStore.Hash(copy)==input.Source.Sha256,"Diagnostic native copy bytes changed.");
            }
            else if(qualification)
            {
                var opened=ExternalPartSession.CreateCopy(connection,Path.Combine(output,"package"),input.Source.Path,input.Configuration,lifecycle:ledger);
                journal.Add(new{step="production-source-qualification",opened.Status,opened.FailureCode,opened.Message,opened.Observation});
                opened.Session?.Dispose();
                Program.Check(opened.Session is null&&opened.Status==ReopenStatus.Inspectable&&!File.Exists(Path.Combine(output,"package","current.json"))&&!File.Exists(Path.Combine(output,"package","recovery.json")),"Frozen unsupported-source prediction did not remain inspectable before Setter.");
            }
            else
            {
            var result=ExternalPartInspection.Inspect(connection,input.Source.Path,Path.Combine(output,"inspection.SLDPRT"),input.Configuration,lifecycle:ledger);
            journal.Add(new{step="read-only-intake",result});
            Program.Check(result.OriginalPreserved&&result.CopyPreserved&&result.NoOwnedDocumentRemains&&result.OriginalActiveRestored,"Catalog lifecycle/source failure.");
            }
        }
        catch(Exception e){failure=e.ToString();journal.Add(new{step="catalog-failure",failure});}
        foreach(var source in frozen.Inputs)AcceptanceFiles.Verify(source.Source);
        var after=ledger.Titles();Program.Check(before.OrderBy(x=>x).SequenceEqual(after.OrderBy(x=>x))&&ledger.Data.OwnedTitles.Count==0,"Catalog ownership mismatch.");
        Program.WriteNew(Path.Combine(output,"result.json"),new{input,passed=failure is null,failure,before,after,controller=System.Environment.ProcessId,reports=journal});
        Console.WriteLine($"M14C catalog {id}: {(failure is null?"PASS":"FAIL")}");return failure is null?0:2;
    }
}
