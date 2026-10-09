using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadHarness.Ir.V03;
using CadHarness.State;

internal sealed record M14AdditionalBudget(string SchemaVersion,bool UserAuthorized,int Milestone,int PreviousMaximumOpens,
    int AdditionalOpenCycles,int MaximumCumulativeOpens,int MaximumNewParts,string HumanAuthorization)
{
    internal void Validate()=>Program.Check(SchemaVersion=="0.3"&&UserAuthorized&&Milestone==14&&
        (PreviousMaximumOpens==12&&AdditionalOpenCycles==8&&MaximumCumulativeOpens==20||PreviousMaximumOpens==20&&AdditionalOpenCycles==3&&MaximumCumulativeOpens==23||PreviousMaximumOpens==23&&AdditionalOpenCycles==4&&MaximumCumulativeOpens==27||
        PreviousMaximumOpens==27&&MaximumCumulativeOpens==int.MaxValue&&AdditionalOpenCycles==int.MaxValue-27&&HumanAuthorization.Contains("No user limit",StringComparison.Ordinal))&&
        MaximumNewParts==0&&!string.IsNullOrWhiteSpace(HumanAuthorization),"Separate explicit M14 authorization required; no reset or Factory transfer.");
}
internal sealed record FrozenFile(string Path,string Sha256,long Bytes);
internal sealed record OracleHole(string Label,double X,double Y,double Diameter);
internal sealed record OraclePattern(string Label,string Seed,int Count,double Spacing);
internal sealed record OracleInput(string Id,string Provenance,FrozenFile Source,string Configuration,double Width,double Height,double Depth,
    IReadOnlyList<OracleHole> Holes,OraclePattern Pattern,IReadOnlyDictionary<string,string> References,string? FactoryReady,
    double BlindDepth,double BlindX,double BlindY,double BlindDiameter,int EquationCount);
internal sealed record AcceptanceScenario(string Id,string Kind,string Input,string Package,string? PreviousPackage,int MaximumOpens,bool PublicEntry);
internal sealed record AcceptanceSchedule(string SchemaVersion,string Run,string Authorization,IReadOnlyList<OracleInput> Inputs,IReadOnlyList<AcceptanceScenario> Scenarios);
internal sealed record AcceptanceFreeze(string SchemaVersion,string Run,DateTime Utc,FrozenFile Schedule,IReadOnlyList<FrozenFile> Files,FrozenFile InitialBudget);
internal static class AcceptanceFiles
{
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow};
    internal static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json)??throw new InvalidDataException("Missing acceptance metadata.");
    internal static FrozenFile Identity(string path)
    {
        var full=Path.GetFullPath(path);var readable=Path.GetFileName(full).Equals("aux.json",StringComparison.OrdinalIgnoreCase)?@"\\?\"+full:full;
        return new(full,ManagedRevisionStore.Hash(readable),new FileInfo(readable).Length);
    }
    internal static void Verify(FrozenFile f)=>Program.Check(Identity(f.Path)==f,"Frozen artifact drift: "+f.Path);
    internal static void Freeze(string path,string executable,string schedule)
    {
        var f=Read<AcceptanceFreeze>(path);Program.Check(f.SchemaVersion=="0.3","Invalid acceptance freeze.");Verify(f.Schedule);
        Program.Check(f.Schedule.Path==Path.GetFullPath(schedule)&&f.Files.Any(x=>x.Path==Path.GetFullPath(executable)),"Schedule/binary not in the frozen run.");foreach(var file in f.Files)Verify(file);
    }
    internal static OracleInput Change(OracleInput s,string label,ParameterKey key,double value)
    {
        Program.Check(double.IsFinite(value)&&value>0,"Invalid independent oracle value.");
        return key switch
        {
            ParameterKey.ExtrusionDepth when label=="host"=>s with{Depth=value},
            ParameterKey.HoleDiameter when s.Holes.Count(h=>h.Label==label)==1=>s with{Holes=s.Holes.Select(h=>h.Label==label?h with{Diameter=value}:h).ToArray()},
            ParameterKey.PatternCount when label==s.Pattern.Label&&value==Math.Truncate(value)=>s with{Pattern=s.Pattern with{Count=checked((int)value)}},
            ParameterKey.PatternSpacing when label==s.Pattern.Label=>s with{Pattern=s.Pattern with{Spacing=value}},
            _=>throw new InvalidDataException("Unscheduled independent oracle parameter.")
        };
    }
    internal static void Validate(AcceptanceSchedule plan)
    {
        Program.Check(plan.SchemaVersion=="0.3"&&plan.Inputs.Select(x=>x.Id).Distinct().Count()==plan.Inputs.Count&&plan.Scenarios.Select(x=>x.Id).Distinct().Count()==plan.Scenarios.Count,"Invalid/duplicate schedule identities.");
        foreach(var i in plan.Inputs)
        {Program.Check(i.Width>0&&i.Height>0&&i.Depth>0&&i.Holes.Count==3&&i.Holes.Select(h=>h.Label).Distinct().Count()==3&&i.Pattern.Count>=2&&i.Pattern.Spacing>0&&i.References.Count>=5,"Incomplete independent input specification.");Verify(i.Source);}
        Program.Check(plan.Scenarios.All(s=>s.MaximumOpens>0&&plan.Inputs.Any(i=>i.Id==s.Input)),"Invalid scheduled open allowance/input.");
        foreach(var s in plan.Scenarios)
        {
            var parent=Path.GetDirectoryName(plan.Authorization)!;var full=Path.GetFullPath(s.Package);
            var relative=Path.GetRelativePath(Path.GetDirectoryName(parent)!,full).Split(Path.DirectorySeparatorChar);
            Program.Check(relative.Length==3&&System.Text.RegularExpressions.Regex.IsMatch(relative[0],"^acceptance-v[0-9]+$")&&relative[1]=="packages"&&!plan.Inputs.Any(i=>i.Source.Path.StartsWith(full+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)),"Scenario must own a separate disposable acceptance package.");
            if(s.PreviousPackage is not null)Program.Check(plan.Scenarios.Any(p=>p.Id==s.PreviousPackage&&p.Package==s.Package),"Missing scheduled package predecessor.");
        }
    }
    internal static void RequireBudget(AcceptanceSchedule plan, NativeTests.Budget budget, string? executionRun = null, string? pendingSlot = null)
    {
        Program.Check(budget.Milestone==14&&budget.MaximumNewParts==0&&budget.OwnedTitles.Count==0&&
            budget.OpenAttempts>=0&&budget.OpenAttempts<=budget.MaximumOpenCycles,"Unresolved or invalid current budget.");
        var run=executionRun??plan.Run;
        Program.Check(plan.Scenarios.Where(s=>s.Id==pendingSlot||!budget.AttemptedSteps.Contains(run+"/"+s.Id)).Sum(s=>(long)s.MaximumOpens)<=budget.MaximumOpenCycles-budget.OpenAttempts,
            "Complete schedule exceeds latest cumulative remaining budget; replan before any native execution.");
    }
}
