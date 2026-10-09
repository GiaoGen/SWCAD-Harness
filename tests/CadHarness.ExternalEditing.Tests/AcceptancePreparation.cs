using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class AcceptancePreparation
{
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
