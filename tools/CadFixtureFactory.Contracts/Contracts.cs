using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CadFixtureFactory;

public enum Dataset { Development, HeldOut }
public enum NegativeHistory { None, BlindCutDescendant, EquationDrivenDepth }
public sealed record HoleSpec(string Label, double XMm, double YMm, double DiameterMm, bool CoincidentOrigin);
public sealed record PatternSpec(string Label, string Seed, int Count, double SpacingMm);
public sealed record BlindCutSpec(double XMm, double YMm, double DiameterMm, double DepthMm);
public sealed record FixtureSpec(string SchemaVersion, string FixtureId, Dataset Dataset, string Recipe,
    double WidthMm, double HeightMm, double DepthMm, IReadOnlyList<HoleSpec> Holes, PatternSpec Pattern, NegativeHistory Negative, BlindCutSpec? BlindCut);
public sealed record FileIdentity(string Path, string Sha256, long Bytes);
public sealed record SourceFreeze(string SchemaVersion, string Purpose, DateTime Utc, FileIdentity Baseline, IReadOnlyList<FileIdentity> Files);
public sealed record FeatureFact(string Label, string Name, string NativeType, string UnderlyingType, string PersistentReference,
    IReadOnlyList<string> Parents, IReadOnlyList<string> Children, IReadOnlyList<DimensionFact> Dimensions,
    bool Suppressed, int ErrorCode, bool Warning, DefinitionFact? Definition);
public sealed record DefinitionFact(string InterfaceName, bool? Thin, bool? BothDirections, bool? Draft,
    int? EndConditionD1, double? DepthMm, int? CountD1, double? SpacingMm, bool? Direction2,
    bool? GeometryPattern, int? SkippedInstances);
public sealed record DimensionFact(string Name, double SystemValue, int DrivenState, bool ReadOnly, bool DesignTable);
public sealed record GeometryExpectation(double WidthMm, double HeightMm, double DepthMm, int SolidBodies,
    int ThroughCylinders, int BlindCylinders, double VolumeMm3);
public sealed record FixtureManifest(string SchemaVersion, string FixtureId, Dataset Dataset, string Provenance,
    string GeneratorVersion, string SolidWorksVersion, FileIdentity Specification, FileIdentity BuilderBinary,
    FileIdentity Template, FileIdentity SourceFreeze, FileIdentity NativePart, string Configuration, IReadOnlyList<FeatureFact> Features,
    GeometryExpectation Expected, string BudgetLedger, string Status);
public sealed record ReaderProof(string SchemaVersion, string FixtureId, FileIdentity NativePart, FileIdentity ReaderBinary,
    bool Passed, string? Failure, string Configuration, int FeatureCount, int SolidBodies, int Cylinders,
    double VolumeMm3, IReadOnlyList<string> Checks, IReadOnlyList<CylinderFact> CylinderMeasurements,
    int EquationCount, int ControllerPid, DateTime Utc, bool ActiveDocumentRestored);
public sealed record CylinderFact(double XMm, double YMm, double RadiusMm, double AxisZ, IReadOnlyList<double> BoundaryZMm);
public sealed record PreparationAuthorization(string SchemaVersion, bool UserAuthorized, int MaximumCreationAttempts,
    int MaximumOpenAttempts, bool GenerateHeldOut, int M14MaximumCumulativeOpens, string HumanAuthorization);
public sealed record AdditionalPreparationAuthorization(string SchemaVersion, bool UserAuthorized, string Purpose,
    FileIdentity OriginalAuthorization, int AdditionalCreationAttempts, int AdditionalOpenAttempts,
    int MaximumCreationAttempts, int MaximumOpenAttempts, bool GenerateHeldOut, bool M14AcceptanceAuthorized, string HumanAuthorization);
public sealed class PreparationBudget
{
    public int CreationAttempts { get; set; }
    public int OpenAttempts { get; set; }
    public int ClosedDocuments { get; set; }
    public List<string> AttemptSlots { get; set; } = new();
    public List<string> OwnedTitles { get; set; } = new();
    public List<LifecycleEvent> Events { get; set; } = new();
}
public sealed record LifecycleEvent(string Kind, string Detail, int ControllerPid, int NativePid, uint Gdi, DateTime Utc);
public static class FixtureFiles
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false) } };
    public static T Read<T>(string path)
    {
        if (new FileInfo(path).Length > 1048576) throw new InvalidDataException("Fixture metadata byte limit.");
        using var parsed = JsonDocument.Parse(File.ReadAllText(path));
        CheckFields(parsed.RootElement, typeof(T));
        return JsonSerializer.Deserialize<T>(parsed.RootElement, Json) ?? throw new InvalidDataException("Missing metadata.");
    }
    private static void CheckFields(JsonElement node, Type type)
    {
        if (node.ValueKind == JsonValueKind.Null) return;
        if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)||type.GetGenericTypeDefinition()==typeof(List<>)))
        { foreach (var item in node.EnumerateArray()) CheckFields(item,type.GetGenericArguments()[0]); return; }
        if (type.Namespace != typeof(FixtureSpec).Namespace || type.IsEnum) return;
        var properties=type.GetProperties().ToDictionary(p=>JsonNamingPolicy.CamelCase.ConvertName(p.Name));
        var seen=new HashSet<string>();
        foreach(var p in node.EnumerateObject())
        { if(!seen.Add(p.Name)||!properties.TryGetValue(p.Name,out var field)) throw new InvalidDataException("Unknown/duplicate fixture field: "+p.Name); CheckFields(p.Value,field.PropertyType); }
        if (seen.Count!=properties.Count) throw new InvalidDataException("Missing fixture fields.");
    }
    public static void WriteNew<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, Json); stream.Flush(true);
    }
    public static void Atomic<T>(string path, T value)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        WriteNew(temp, value); if (File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
    }
    public static FileIdentity Identity(string path)
    {
        path=Path.GetFullPath(path); using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        return new(path,Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),stream.Length);
    }
    public static void Verify(FileIdentity file)
    { if (Identity(file.Path)!=file) throw new InvalidDataException("Frozen fixture artifact drift: "+file.Path); }
    public static PreparationAuthorization ApplyAdditionalAuthorization(PreparationAuthorization original,AdditionalPreparationAuthorization grant)
    {
        if(grant.SchemaVersion!="1.0"||!grant.UserAuthorized||grant.Purpose!="development-fixture-preparation-only"||grant.GenerateHeldOut||grant.M14AcceptanceAuthorized||
            grant.AdditionalCreationAttempts is <0 or >100||grant.AdditionalOpenAttempts is <0 or >100||grant.AdditionalCreationAttempts+grant.AdditionalOpenAttempts<=0||
            grant.MaximumCreationAttempts!=original.MaximumCreationAttempts+grant.AdditionalCreationAttempts||grant.MaximumOpenAttempts!=original.MaximumOpenAttempts+grant.AdditionalOpenAttempts||
            grant.MaximumCreationAttempts>100||grant.MaximumOpenAttempts>100||string.IsNullOrWhiteSpace(grant.HumanAuthorization))
            throw new InvalidDataException("Invalid separate additional preparation authorization; never reset counters or transfer M14 budget.");
        return original with{MaximumCreationAttempts=grant.MaximumCreationAttempts,MaximumOpenAttempts=grant.MaximumOpenAttempts};
    }
    public static SourceFreeze VerifyFreeze(string path,string executable,string spec)
    {
        var freeze=Read<SourceFreeze>(path);
        if(freeze.SchemaVersion!="1.0"||freeze.Purpose!="development-fixture-preparation-only"||freeze.Files is null||freeze.Files.Count==0)
            throw new InvalidDataException("Invalid Factory source freeze.");
        Verify(freeze.Baseline);foreach(var f in freeze.Files)Verify(f);
        foreach(var required in new[]{executable,spec})
            if(!freeze.Files.Any(f=>string.Equals(f.Path,System.IO.Path.GetFullPath(required),StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Execution artifact not frozen: "+required);
        return freeze;
    }
    public static void Validate(FixtureSpec spec)
    {
        bool Positive(double n)=>double.IsFinite(n)&&n>0&&n<=1000;
        if (spec.SchemaVersion!="1.0"||!Regex.IsMatch(spec.FixtureId??"",@"^dev_[a-z0-9_]{1,40}$")||spec.Dataset!=Dataset.Development||
            spec.Recipe!="rectangular_plate_hole_pattern_v1"||!Enum.IsDefined(spec.Negative)||!Positive(spec.WidthMm)||!Positive(spec.HeightMm)||!Positive(spec.DepthMm)||
            spec.Holes is null||spec.Holes.Count!=3||spec.Holes.Any(h=>h is null)||spec.Holes.Select(h=>h.Label).Distinct().Count()!=3||spec.Pattern is null||
            spec.Pattern.Count is <2 or >8||!Positive(spec.Pattern.SpacingMm)||!Regex.IsMatch(spec.Pattern.Label??"",@"^[a-z][a-z0-9_]{0,30}$")||
            spec.Holes.Any(h=>h.Label==spec.Pattern.Label||h.Label is "host" or "blind_cut")||!spec.Holes.Any(h=>h.Label==spec.Pattern.Seed)||
            spec.Holes.Any(h=>!Regex.IsMatch(h.Label??"",@"^[a-z][a-z0-9_]{0,30}$")||!Positive(h.DiameterMm)||!double.IsFinite(h.XMm)||!double.IsFinite(h.YMm)||h.CoincidentOrigin&&(h.XMm!=0||h.YMm!=0)))
            throw new InvalidDataException("Invalid development fixture spec; Held-out generation is not authorized.");
        if((spec.Negative==NegativeHistory.BlindCutDescendant)!=(spec.BlindCut is not null))throw new InvalidDataException("Blind-cut negative must have an explicit specification.");
        if(spec.BlindCut is {} b&&(!Positive(b.DiameterMm)||!Positive(b.DepthMm)||b.DepthMm>=spec.DepthMm||!double.IsFinite(b.XMm)||!double.IsFinite(b.YMm)))
            throw new InvalidDataException("Invalid blind-cut negative.");
        var circles=new List<(double X,double Y,double R)>();
        foreach(var h in spec.Holes) for(var i=0;i<(h.Label==spec.Pattern.Seed?spec.Pattern.Count:1);i++) circles.Add((h.XMm+i*spec.Pattern.SpacingMm,h.YMm,h.DiameterMm/2));
        if(spec.BlindCut is {} blind)circles.Add((blind.XMm,blind.YMm,blind.DiameterMm/2));
        for(var i=0;i<circles.Count;i++)
        { var c=circles[i]; if(Math.Abs(c.X)+c.R>=spec.WidthMm/2||Math.Abs(c.Y)+c.R>=spec.HeightMm/2) throw new InvalidDataException("Fixture hole crosses host.");
          for(var j=0;j<i;j++) if(Math.Sqrt(Math.Pow(c.X-circles[j].X,2)+Math.Pow(c.Y-circles[j].Y,2))<=c.R+circles[j].R+0.1) throw new InvalidDataException("Fixture holes overlap/touch."); }
    }
}
