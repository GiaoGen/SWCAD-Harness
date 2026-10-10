using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.State;

internal static class AcceptancePreparation
{
    internal static int BatchB(string root,string run,bool publicEntry)
    {
        var output=Path.Combine(root,"artifacts","milestone14",run);
        var original=AcceptanceFiles.Read<AcceptanceSchedule>(Path.Combine(root,"artifacts","milestone14","acceptance-v5","schedule.json"));
        var budgetPath=Path.Combine(root,"artifacts","milestone14","native-budget.json");var budget=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(budgetPath))!;
        Program.Check(budget.OwnedTitles.Count==0&&budget.MaximumNewParts==0,"Resolve native ownership before M14B.");
        var grant=Path.Combine(output,"authorization.json");
        Write(grant,new M14AdditionalBudget("0.3",true,14,27,int.MaxValue-27,int.MaxValue,0,
            "No user limit. Explicit human authorization: M14B unlimited opens, bounded frozen slots, stop on repeated stagnation. Retain all prior cumulative counts; no new Parts, M14C or Factory extension."));
        var core=original.Inputs.Single(i=>i.Id=="dev_core");
        var final=AcceptanceFiles.Change(AcceptanceFiles.Change(AcceptanceFiles.Change(core,"hole_b",CadHarness.Ir.V03.ParameterKey.HoleDiameter,11),"hole_c",CadHarness.Ir.V03.ParameterKey.HoleDiameter,8),"pattern",CadHarness.Ir.V03.ParameterKey.PatternSpacing,24) with{Id="batch-final-r2"};
        var published=AcceptanceFiles.Change(AcceptanceFiles.Change(core,"hole_b",CadHarness.Ir.V03.ParameterKey.HoleDiameter,14),"hole_c",CadHarness.Ir.V03.ParameterKey.HoleDiameter,10) with{Id="batch-published-r1"};
        var inputs=original.Inputs.Concat(new[]{final,published,core with{Id="batch-restored-r0"}}).ToArray();
        string Package(string id)=>Path.Combine(output,"packages",id);
        var scenarios=publicEntry?new[]{new AcceptanceScenario("public","batch-sequence","dev_core",Package("public"),null,4,true),new AcceptanceScenario("public-cold","batch-cold","batch-final-r2",Package("public"),"public",1,false),
                new AcceptanceScenario("boundaries","batch-boundary-faults","dev_core",Package("boundaries"),null,3,true),new AcceptanceScenario("rollback-recovery","batch-cold","batch-restored-r0",Package("boundaries"),"boundaries",1,false)}:
            new[]{new AcceptanceScenario("candidate","batch-sequence","dev_core",Package("candidate"),null,3,false),new AcceptanceScenario("candidate-cold","batch-cold","batch-final-r2",Package("candidate"),"candidate",1,false),
                new AcceptanceScenario("faults","batch-publication-faults","dev_core",Package("faults"),null,10,false),new AcceptanceScenario("postpointer-recovery","batch-cold","batch-published-r1",Package("faults"),"faults",1,false),
                new AcceptanceScenario("native-rebuild","batch-native-rebuild","dev_core",Package("native-rebuild"),null,2,false),
                new AcceptanceScenario("interrupted","batch-interrupted","dev_core",Package("interrupted"),null,1,false),new AcceptanceScenario("interrupted-recovery","batch-cold","batch-restored-r0",Package("interrupted"),"interrupted",1,false)};
        var schedule=Path.Combine(output,"schedule.json");var plan=new AcceptanceSchedule("0.3",run,grant,inputs,scenarios);AcceptanceFiles.Validate(plan);Write(schedule,plan);
        File.Copy(budgetPath,Path.Combine(output,"initial-budget.json"),false);
        var registry=Path.Combine(root,"artifacts","fixture-factory","audits","final-v1","fixture-registry.json");using var data=JsonDocument.Parse(File.ReadAllText(registry));
        var files=new List<FrozenFile>{AcceptanceFiles.Identity(grant),AcceptanceFiles.Identity(Path.Combine(output,"initial-budget.json")),AcceptanceFiles.Identity(registry)};
        foreach(var f in data.RootElement.GetProperty("fixtures").EnumerateArray())foreach(var k in new[]{"manifest","proof","ready","nativePart"}){var file=f.GetProperty(k).Deserialize<FrozenFile>(AcceptanceFiles.Json)!;AcceptanceFiles.Verify(file);files.Add(file);}
        foreach(var dir in new[]{"source","bin"})files.AddRange(Directory.EnumerateFiles(Path.Combine(output,dir),"*",SearchOption.AllDirectories).Select(AcceptanceFiles.Identity));
        foreach(var input in inputs)files.Add(input.Source);
        var m14a=Path.Combine(root,"artifacts","milestone14","acceptance-v13");
        foreach(var slot in new[]{"scalar-public","public-cold"})
        {
            var proof=Path.Combine(m14a,"results",slot,"result.json");using var report=JsonDocument.Parse(File.ReadAllText(proof));
            Program.Check(report.RootElement.GetProperty("passed").GetBoolean(),"M14A qualification must remain accepted.");files.Add(AcceptanceFiles.Identity(proof));
        }
        var corePackage=original.Scenarios.Single(s=>s.Id=="core").Package;using var pointer=JsonDocument.Parse(File.ReadAllText(Path.Combine(corePackage,"current.json")));
        var manifest=pointer.RootElement.GetProperty("manifest").Deserialize<CadHarness.State.V03.ArtifactIdentity>(AcceptanceFiles.Json)!;
        Program.Check(ManagedRevisionStore.Hash(manifest.Path)==manifest.Sha256&&!File.Exists(Path.Combine(corePackage,"recovery.json")),"M14A authority drifted/unresolved.");
        using var authority=JsonDocument.Parse(File.ReadAllText(manifest.Path));Program.Check(authority.RootElement.GetProperty("revision").GetInt64()==11,"M14A current authority is not revision 11.");
        Write(Path.Combine(output,"baseline.json"),new{head="458b6e1",budget.OpenAttempts,budget.DocumentsClosed,coreRevision=11,manifest=AcceptanceFiles.Identity(manifest.Path),working=AcceptanceFiles.Identity(Path.Combine(corePackage,"working","CADHarnessManagedPart.SLDPRT")),scope="M14B only; new isolated packages, M14A core is not edited"});
        files.Add(AcceptanceFiles.Identity(Path.Combine(output,"baseline.json")));
        Write(Path.Combine(output,"freeze.json"),new AcceptanceFreeze("0.3",run,DateTime.UtcNow,AcceptanceFiles.Identity(schedule),files.DistinctBy(f=>f.Path).ToArray(),AcceptanceFiles.Identity(budgetPath)));
        Console.WriteLine($"M14B freeze: {scenarios.Sum(s=>s.MaximumOpens)} bounded opens; no new Parts; existing authority/counters retained.");return 0;
    }
    internal static int ScalarA(string root,string run,bool publicEntry,string phase="candidate")
    {
        var output=Path.Combine(root,"artifacts","milestone14",run);
        var original=AcceptanceFiles.Read<AcceptanceSchedule>(Path.Combine(root,"artifacts","milestone14","acceptance-v5","schedule.json"));
        var budgetPath=Path.Combine(root,"artifacts","milestone14","native-budget.json");var budget=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(budgetPath))!;
        Program.Check(budget.OwnedTitles.Count==0&&budget.MaximumNewParts==0,"Resolve native ownership before freezing.");
        var grant=Path.Combine(output,"authorization.json");
        Write(grant,new M14AdditionalBudget("0.3",true,14,27,int.MaxValue-27,int.MaxValue,0,
            "No user limit. Explicit human authorization: no budget limit for M14A; stop on repeated stagnation, preserve all cumulative counts. No new Parts; only scalar qualification, no M14B/M14C execution."));
        var core=NativeAcceptance.CoreFinal(original.Inputs.Single(i=>i.Id=="dev_core")) with{Id="core-r4"};
        var final=NativeAcceptance.ScalarFinal(core) with{Id="core-r7"};
        var origin=original.Inputs.Single(i=>i.Id=="dev_origin");
        var originFinal=AcceptanceFiles.Change(origin,"hole_b",CadHarness.Ir.V03.ParameterKey.HoleDiameter,11) with{Id="origin-r1"};
        var corePackage=original.Scenarios.Single(s=>s.Id=="core").Package;
        var inputs=original.Inputs.Concat(new[]{core,final,originFinal,NativeAcceptance.PublicFinal(final) with{Id="core-public-r11"}}).ToArray();
        string Package(string id)=>Path.Combine(output,"packages",id);
        var scenarios=phase=="boundaries"?new[]{new AcceptanceScenario("boundaries","scalar-native-boundaries","dev_core",Package("boundaries"),null,3,false)}:
            phase=="probe"?new[]{new AcceptanceScenario("probe","origin-driver-probe","dev_origin",Package("probe"),null,1,false)}:
            publicEntry?new[]{new AcceptanceScenario("scalar-public","scalar-public","core-r7",corePackage,null,5,true),new AcceptanceScenario("public-cold","scalar-cold","core-public-r11",corePackage,"scalar-public",1,false)}:
            new[]{new AcceptanceScenario("core","scalar-candidate-v4","core-r4",corePackage,null,5,false),new AcceptanceScenario("core-cold","scalar-cold","core-r7",corePackage,"core",1,false),
                new AcceptanceScenario("origin","scalar-origin","dev_origin",Package("origin"),null,2,false),new AcceptanceScenario("origin-cold","scalar-cold","origin-r1",Package("origin"),"origin",1,false),
                new AcceptanceScenario("equation","mandatory-refusal","dev_equation_driver",Package("equation"),null,1,false),new AcceptanceScenario("unknown","mandatory-refusal","dev_unknown_descendant",Package("unknown"),null,1,false)};
        if(phase=="remaining")scenarios=scenarios.Where(s=>s.Id is not "core" and not "core-cold").ToArray();
        var plan=new AcceptanceSchedule("0.3",run,grant,inputs,scenarios);AcceptanceFiles.Validate(plan);
        if(!publicEntry&&phase=="candidate")Program.Check(budget.OpenAttempts==14&&budget.MaximumOpenCycles==27,"Recheck changed starting authority/budget before candidate run.");
        var schedule=Path.Combine(output,"schedule.json");Write(schedule,plan);File.Copy(budgetPath,Path.Combine(output,"initial-budget.json"),false);
        var registry=Path.Combine(root,"artifacts","fixture-factory","audits","final-v1","fixture-registry.json");using var data=JsonDocument.Parse(File.ReadAllText(registry));
        var files=new List<FrozenFile>{AcceptanceFiles.Identity(grant),AcceptanceFiles.Identity(Path.Combine(output,"initial-budget.json")),AcceptanceFiles.Identity(registry)};
        foreach(var f in data.RootElement.GetProperty("fixtures").EnumerateArray())foreach(var k in new[]{"manifest","proof","ready","nativePart"}){var file=f.GetProperty(k).Deserialize<FrozenFile>(AcceptanceFiles.Json)!;AcceptanceFiles.Verify(file);files.Add(file);}
        foreach(var dir in new[]{"source","bin"})files.AddRange(Directory.EnumerateFiles(Path.Combine(output,dir),"*",SearchOption.AllDirectories).Select(AcceptanceFiles.Identity));
        foreach(var input in inputs){AcceptanceFiles.Verify(input.Source);files.Add(input.Source);}
        files.Add(AcceptanceFiles.Identity(Path.Combine(root,"artifacts","milestone14","acceptance-v3","results","core","result.json")));
        Write(Path.Combine(output,"freeze.json"),new AcceptanceFreeze("0.3",run,DateTime.UtcNow,AcceptanceFiles.Identity(schedule),files.DistinctBy(f=>f.Path).ToArray(),AcceptanceFiles.Identity(budgetPath)));
        Console.WriteLine($"M14A scalar-only freeze: {scenarios.Sum(s=>s.MaximumOpens)} bounded opens, cumulative counters preserved, no new Parts or batch/fault matrix.");return 0;
    }
    internal static int Run(string root,string run)
    {
        var output=Path.Combine(root,"artifacts","milestone14",run);Directory.CreateDirectory(output);
        var grant=Path.Combine(output,"authorization.json");
        Write(grant,new M14AdditionalBudget("0.3",true,14,12,8,20,0,"Human reply: 授权新增 8 次打开. Existing 2/12 ledger preserved; cumulative 20; no new Parts; M14 only."));
        var registryPath=Path.Combine(root,"artifacts","fixture-factory","audits","final-v1","fixture-registry.json");
        using var registry=JsonDocument.Parse(File.ReadAllText(registryPath));var inputs=new List<OracleInput>();var identities=new List<FrozenFile>{AcceptanceFiles.Identity(registryPath),AcceptanceFiles.Identity(grant)};
        foreach(var fixture in registry.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            foreach(var key in new[]{"manifest","proof","ready","nativePart"})
            {var f=fixture.GetProperty(key).Deserialize<FrozenFile>(AcceptanceFiles.Json)!;AcceptanceFiles.Verify(f);identities.Add(f);}
            var readyPath=fixture.GetProperty("ready").GetProperty("path").GetString()!;
            using var ready=JsonDocument.Parse(File.ReadAllText(readyPath));
            Program.Check(ready.RootElement.GetProperty("status").GetString()=="PREPARATION_SELF_CHECKED_NOT_M14_ACCEPTANCE","Factory cannot certify M14 edits.");
            foreach(var key in new[]{"manifest","proof","nativePart","readerFreeze"})
            {var f=ready.RootElement.GetProperty(key).Deserialize<FrozenFile>(AcceptanceFiles.Json)!;AcceptanceFiles.Verify(f);identities.Add(f);}
            using var proof=JsonDocument.Parse(File.ReadAllText(fixture.GetProperty("proof").GetProperty("path").GetString()!));
            Program.Check(proof.RootElement.GetProperty("passed").GetBoolean()&&proof.RootElement.GetProperty("activeDocumentRestored").GetBoolean(),"Fixture preparation not independently self-checked.");
            using var manifest=JsonDocument.Parse(File.ReadAllText(fixture.GetProperty("manifest").GetProperty("path").GetString()!));
            var source=fixture.GetProperty("nativePart").Deserialize<FrozenFile>(AcceptanceFiles.Json)!;
            Program.Check(manifest.RootElement.GetProperty("nativePart").Deserialize<FrozenFile>(AcceptanceFiles.Json)==source&&proof.RootElement.GetProperty("nativePart").Deserialize<FrozenFile>(AcceptanceFiles.Json)==source,"Manifest/Reader identity disagreement.");
            var refs=manifest.RootElement.GetProperty("features").EnumerateArray().ToDictionary(f=>f.GetProperty("label").GetString()!,f=>f.GetProperty("persistentReference").GetString()!);
            var id=fixture.GetProperty("fixtureId").GetString()!;
            inputs.Add(new(id,"Independent API development fixture; never held-out",source,fixture.GetProperty("configuration").GetString()!,120,80,10,
                new[]{new OracleHole("seed",-40,20,8),new OracleHole("hole_b",id=="dev_origin"?0:-30,id=="dev_origin"?0:-20,10),new OracleHole("hole_c",35,-20,6)},
                new("pattern","seed",3,25),refs,readyPath,id=="dev_unknown_descendant"?2:0,45,25,6,id=="dev_equation_driver"?1:0));
        }
        var engineerRefs=new Dictionary<string,string>{{"host","aEIAAAEAAAD//v8AAAAAAB0AAAA="},{"seed","aEIAAAEAAAD//v8AAAAAADEAAAA="},{"pattern","aEIAAAEAAAD//v8AAAAAADgAAAA="},{"hole_b","aEIAAAEAAAD//v8AAAAAAEUAAAA="},{"hole_c","aEIAAAEAAAD//v8AAAAAAFUAAAA="}};
        foreach(var (id,path) in new[]{("engineer_current",@"D:\document\A1.SLDPRT"),("engineer_old",Path.Combine(root,"artifacts","milestone14","source-v3","package","working","CADHarnessManagedPart.SLDPRT"))})
            inputs.Add(new(id,"Engineer authored A1; previously diagnosed, NOT held-out. Frozen expected geometry from preserved source-v3; current source must match before mutation.",AcceptanceFiles.Identity(path),"默认",80,40,10,new[]{new OracleHole("seed",-20,10,10),new OracleHole("hole_b",0,-5,15),new OracleHole("hole_c",35,-15,5)},new("pattern","seed",2,40),engineerRefs,null,0,0,0,0,0));
        string Package(string id)=>Path.Combine(output,"packages",id);
        var scenarios=new[]{new AcceptanceScenario("core","candidate-sequence","dev_core",Package("core"),null,9,false),
            new AcceptanceScenario("recovery","interrupted-reopen-fresh-controller","dev_core",Package("core"),"core",1,false),
            new AcceptanceScenario("cold","fresh-controller-read","dev_core",Package("core"),"recovery",1,false),
            new AcceptanceScenario("unknown","mandatory-refusal","dev_unknown_descendant",Package("unknown"),null,1,false),
            new AcceptanceScenario("equation","mandatory-refusal","dev_equation_driver",Package("equation"),null,1,false),
            new AcceptanceScenario("old","engineer-history-comparison","engineer_old",Package("old"),null,1,false),
            new AcceptanceScenario("origin","public-edit-identity-robustness","dev_origin",Package("origin"),null,2,true),
            new AcceptanceScenario("engineer","public-engineer-four-row-batch","engineer_current",Package("engineer"),null,2,true)};
        var plan=new AcceptanceSchedule("0.3",run,grant,inputs,scenarios);AcceptanceFiles.Validate(plan);
        var schedule=Path.Combine(output,"schedule.json");Write(schedule,plan);
        var budget=AcceptanceFiles.Identity(Path.Combine(root,"artifacts","milestone14","native-budget.json"));
        var existing=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(budget.Path))!;
        Program.Check(existing.OpenAttempts==2&&existing.MaximumOpenCycles==12&&existing.OwnedTitles.Count==0,"Initial budget is not preserved 2/12.");
        File.Copy(budget.Path,Path.Combine(output,"initial-budget.json"),false);identities.Add(AcceptanceFiles.Identity(Path.Combine(output,"initial-budget.json")));
        identities.AddRange(inputs.Select(i=>i.Source));
        foreach(var dir in new[]{"source","bin"})identities.AddRange(Directory.EnumerateFiles(Path.Combine(output,dir),"*",SearchOption.AllDirectories).Select(AcceptanceFiles.Identity));
        foreach(var dir in new[]{"source-v2","source-v3"})identities.AddRange(Directory.EnumerateFiles(Path.Combine(root,"artifacts","milestone14",dir),"*",SearchOption.AllDirectories).Where(p=>!p.EndsWith(".lock")).Select(AcceptanceFiles.Identity));
        Write(Path.Combine(output,"freeze.json"),new AcceptanceFreeze("0.3",run,DateTime.UtcNow,AcceptanceFiles.Identity(schedule),identities.DistinctBy(f=>f.Path).ToArray(),budget));
        Console.WriteLine("Frozen M14 schedule: 18 additional opens maximum, original ledger 2/12; authorized cumulative 20; no native call.");return 0;
    }
    internal static int Stage(string root,string run,string schedule)
    {
        var output=Path.Combine(root,"artifacts","milestone14",run);
        var previous=AcceptanceFiles.Read<AcceptanceFreeze>(Path.Combine(Path.GetDirectoryName(schedule)!,"freeze.json"));
        AcceptanceFiles.Verify(previous.Schedule);foreach(var f in previous.Files)AcceptanceFiles.Verify(f);
        var files=previous.Files.Concat(new[]{"source","bin"}.SelectMany(d=>Directory.EnumerateFiles(Path.Combine(output,d),"*",SearchOption.AllDirectories)).Select(AcceptanceFiles.Identity)).ToArray();
        Write(Path.Combine(output,"freeze.json"),new AcceptanceFreeze("0.3",run,DateTime.UtcNow,previous.Schedule,files,previous.InitialBudget));
        Console.WriteLine("New source stage frozen against unchanged authorized schedule and retained previous evidence.");return 0;
    }
    internal static int Replan(string root,string run,string schedule,bool resumeCore=false)
    {
        var output=Path.Combine(root,"artifacts","milestone14",run);var original=AcceptanceFiles.Read<AcceptanceSchedule>(schedule);
        var previous=AcceptanceFiles.Read<AcceptanceFreeze>(Path.Combine(Path.GetDirectoryName(schedule)!,"freeze.json"));
        AcceptanceFiles.Verify(previous.Schedule);foreach(var f in previous.Files)AcceptanceFiles.Verify(f);
        var budgetPath=Path.Combine(root,"artifacts","milestone14","native-budget.json");var budget=JsonSerializer.Deserialize<NativeTests.Budget>(File.ReadAllText(budgetPath))!;
        Program.Check(budget.OwnedTitles.Count==0&&budget.MaximumOpenCycles is 20 or 23,"Replan cannot reset ownership or budget.");
        Write(Path.Combine(output,"authorization.json"),new M14AdditionalBudget("0.3",true,14,23,4,27,0,"Human reply: 授权追加 4 次打开. Prior authorizations and all failed attempts preserved; cumulative 27; no new Parts; M14 only."));
        var revised=original with{Run=run,Authorization=Path.Combine(output,"authorization.json"),Scenarios=original.Scenarios.Where(s=>s.Id!="cold").Select(s=>s with{Package=Path.Combine(output,"packages",s.Input=="dev_core"?"core":s.Id)}).ToArray()};
        if(resumeCore)
        {
            using var result=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(schedule)!,"results","core","result.json")));
            Program.Check(!result.RootElement.GetProperty("passed").GetBoolean()&&result.RootElement.GetProperty("reports").EnumerateArray().Any(r=>r.TryGetProperty("result",out var t)&&t.GetProperty("Revision").GetInt64()==1&&
                (r.GetProperty("step").GetString()=="depth"&&t.GetProperty("Succeeded").GetBoolean()||r.GetProperty("step").GetString()=="diameter"&&t.GetProperty("RollbackSucceeded").GetBoolean())),"Continuation requires preserved accepted depth and a recorded later failure.");
            var existingPackage=original.Scenarios.Single(s=>s.Id=="core").Package;
            revised=revised with{Scenarios=revised.Scenarios.Select(s=>s.Input=="dev_core"?s with{Package=existingPackage,Kind=s.Id=="core"?"candidate-continuation":s.Kind,MaximumOpens=s.Id=="core"?8:s.MaximumOpens}:s).ToArray()};
        }
        AcceptanceFiles.Validate(revised);Program.Check(revised.Scenarios.Sum(s=>s.MaximumOpens)<=27-budget.OpenAttempts,"Revised schedule exceeds actual remaining budget.");
        var schedulePath=Path.Combine(output,"schedule.json");Write(schedulePath,revised);
        File.Copy(budgetPath,Path.Combine(output,"initial-budget.json"),false);
        Write(Path.Combine(output,"schedule-change.json"),new{reason="Preserved depth success at revision 1; both diameter failures rolled back to that revision. Native setter/cache and saved companion association fixes retain full geometry validation, canonical intent and raw native scalar evidence. Continue existing owned package without replaying depth. +4 explicit user authorization; prior budget/extensions/failures retained.",previous=AcceptanceFiles.Identity(schedule),initialOpens=budget.OpenAttempts,maximum=27,newParts=0});
        var files=previous.Files.Concat(new[]{"source","bin"}.SelectMany(d=>Directory.EnumerateFiles(Path.Combine(output,d),"*",SearchOption.AllDirectories)).Select(AcceptanceFiles.Identity)).Concat(new[]{AcceptanceFiles.Identity(Path.Combine(output,"authorization.json")),AcceptanceFiles.Identity(Path.Combine(output,"initial-budget.json")),AcceptanceFiles.Identity(Path.Combine(output,"schedule-change.json"))}).Concat(Directory.EnumerateFiles(Path.Combine(Path.GetDirectoryName(schedule)!,"results"),"*.json",SearchOption.AllDirectories).Select(AcceptanceFiles.Identity)).ToArray();
        Write(Path.Combine(output,"freeze.json"),new AcceptanceFreeze("0.3",run,DateTime.UtcNow,AcceptanceFiles.Identity(schedulePath),files,AcceptanceFiles.Identity(budgetPath)));
        Console.WriteLine($"Revised schedule frozen: {revised.Scenarios.Sum(s=>s.MaximumOpens)} additional opens maximum; prior failures retained; cumulative ceiling 27.");return 0;
    }
    internal static void Write(string path,object value)
    {Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var stream=new FileStream(path,FileMode.CreateNew);JsonSerializer.Serialize(stream,value,AcceptanceFiles.Json);stream.Flush(true);}
}
