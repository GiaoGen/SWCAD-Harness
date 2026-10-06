using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using CadHarness.Planning;
using CadHarness.SolidWorks.Tests;

[assembly: SupportedOSPlatform("windows")]
namespace CadHarness.Benchmark.Tests;
internal sealed record QualificationManifest(string Baseline, string ProviderFormat, string Model, string Endpoint, int MaximumParts,
    BenchmarkTask[] Tasks, Dictionary<string, string> Sources, Dictionary<string, string> Binaries, Dictionary<string, string> Historical);
internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static string Output(string root) => Path.Combine(root, "artifacts/milestone10b");
    internal static void Check(bool ok, string message) { if (!ok) throw new TestFailure("QUALIFICATION_ASSERTION_FAILED", message); }
    internal static void Write(string path, object? value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool IsSource(string path) => !path.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj");
    private static Dictionary<string, string> Hashes(string root, IEnumerable<string> files) => files.OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p), Hash, StringComparer.Ordinal);
    private static Dictionary<string, string> Sources(string root) => Hashes(root, Directory.GetFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories).Where(IsSource)
        .Concat(Directory.GetFiles(Path.Combine(root, "tests"), "*", SearchOption.AllDirectories).Where(IsSource))
        .Concat(new[] { "scripts/test-milestone10b.ps1", "scripts/solidworks-interop.props" }.Select(p => Path.Combine(root, p))));
    private static Dictionary<string, string> Binaries(string root) => Hashes(root, Directory.GetFiles(AppContext.BaseDirectory, "*.dll"));
    private static Dictionary<string, string> Historical(string root) => Hashes(root, new[] { "milestone9d", "milestone9e", "milestone9f", "milestone9g", "milestone10", "milestone10-contract-fix", "milestone10a" }
        .SelectMany(m => Directory.GetFiles(Path.Combine(root, "artifacts", m), "*", SearchOption.AllDirectories))
        .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "milestone-9*.md"))
        .Concat(new[] { "docs/milestone-10-verification.md", "docs/milestone-10a-verification.md", "Generalized_CAD_Harness_v0.2_CLEAN_PRD.md" }.Select(p => Path.Combine(root, p))));
    internal static RunSlot[] SmokeSchedule() => BenchmarkData.Tasks().SelectMany(t => new[] { "Harness", "Stepwise" }.Select(mode => (t.Name, mode)))
        .Select((s, i) => new RunSlot(i + 1, s.Name, s.mode, 1, false)).ToArray();
    internal static void VerifyFreeze(string root)
    {
        var path = Path.Combine(Output(root), "manifest.json");
        Check(File.ReadAllText(Path.Combine(Output(root), "manifest.sha256")).Trim() == Hash(path), "Manifest changed.");
        var manifest = JsonSerializer.Deserialize<QualificationManifest>(File.ReadAllText(path))!;
        Check(manifest.Sources.SequenceEqual(Sources(root)) && manifest.Binaries.SequenceEqual(Binaries(root)) && manifest.Historical.SequenceEqual(Historical(root)), "Frozen sources/binaries or historical evidence changed.");
        Check(manifest.MaximumParts == 4 && JsonSerializer.Serialize(manifest.Tasks) == JsonSerializer.Serialize(BenchmarkData.Tasks()), "M10B scope changed.");
    }
    internal static void VerifyGate(string root)
    {
        VerifyFreeze(root);
        var output = Output(root); var path = Path.Combine(output, "qualification-result.json");
        Check(File.Exists(path) && File.ReadAllText(path + ".sha256").Trim() == Hash(path), "Qualification result absent or changed.");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Check(doc.RootElement.GetProperty("GatePassed").GetBoolean() && doc.RootElement.GetProperty("PartsCreated").GetInt32() == 0, "Zero-Part gate failed; no native connection permitted.");
        var rows = JsonSerializer.Deserialize<List<QualificationRow>>(File.ReadAllText(Path.Combine(output, "qualification-matrix.json")))!;
        Check(rows.Count == 7 && rows.All(r => r.Status == "PASS"), "All five Harness and both completed Stepwise tasks are required.");
        Check(doc.RootElement.GetProperty("MatrixHash").GetString() == Hash(Path.Combine(output, "qualification-matrix.json")), "Qualification matrix changed.");
    }
    private static int Prepare(string root)
    {
        Directory.CreateDirectory(Output(root)); var path = Path.Combine(Output(root), "manifest.json"); Check(!File.Exists(path), "M10B already frozen; do not overwrite/retry evidence.");
        foreach (var task in BenchmarkData.Tasks()) { _ = new VerifiedObservation(root, task.Name); }
        _ = new VerifiedObservation(root, "G2", "thickness-10");
        Write(path, new QualificationManifest("2c2c68b99be09ebb28768ebd5df04261568358ca", DeepSeekPlanSource.Format, "deepseek-chat", DeepSeekPlanSource.Endpoint.ToString(), 4, BenchmarkData.Tasks(), Sources(root), Binaries(root), Historical(root)));
        File.WriteAllText(Path.Combine(Output(root), "manifest.sha256"), Hash(path));
        Write(Path.Combine(Output(root), "native-budget.json"), new BudgetSnapshot());
        Console.WriteLine("M10B frozen; zero-Part gate mandatory; separate maximum 4 Parts; no benchmark."); return 0;
    }
    private static int Qualify(string root)
    {
        VerifyFreeze(root); var output = Output(root);
        Check(!File.Exists(Path.Combine(output, "qualification-attempt.json")), "Real qualification already attempted; no retries.");
        var key = Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY"); Check(!string.IsNullOrWhiteSpace(key), "Authorized key missing.");
        Write(Path.Combine(output, "qualification-attempt.json"), new { StartedUtc = DateTimeOffset.UtcNow, NativeConnectionPermitted = false, Parts = 0 });
        using var client = OpenAiPlanSource.CreateClient();
        var source = new RecordedSource(new AuditedSource(new DeepSeekPlanSource(client, "deepseek-chat", key!), output), output);
        var rows = Qualification.InitialRows(); Write(Path.Combine(output, "qualification-matrix.json"), rows);
        bool gate = false; string? unexpected = null;
        try { gate = Qualification.Run(root, source, rows); }
        catch (Exception e) { unexpected = e is TestFailure f ? f.Code : e.GetType().Name; }
        VerifyFreeze(root);
        var result = new { Status = gate ? "ZERO_PART_PASS" : "M10B BLOCKED", GatePassed = gate, NativeSmokePermitted = gate, FormalBenchmarkExecuted = false,
            LlmCalls = source.Calls.Count, source.InputTokens, source.OutputTokens, Calls = source.Calls, Rows = rows,
            IdentifierRejections = rows.Count(r => r.Status == "FAIL" && r.Reason is not null && (r.Reason.Contains("identifier", StringComparison.OrdinalIgnoreCase) || r.Reason.Contains("semantic ID", StringComparison.OrdinalIgnoreCase))),
            RelationContractRejections = rows.Count(r => r.Status == "FAIL" && r.FailureStage is not "provider_structured_output" && r.Reason is not null && (r.Reason.Contains("relation", StringComparison.OrdinalIgnoreCase) || r.Reason.Contains("reference", StringComparison.OrdinalIgnoreCase))),
            SemanticContractRejections = rows.Count(r => r.Status == "FAIL" && r.FailureStage is not "provider_structured_output" && r.Reason is not null && (r.Reason.Contains("type", StringComparison.OrdinalIgnoreCase) || r.Reason.Contains("typed", StringComparison.OrdinalIgnoreCase))),
            ProviderSchemaRejections = source.Calls.Count(c => c.FailureCode == "PLANNER_PROVIDER_ERROR" || c.FailureCode == "PLANNER_PROVIDER_SCHEMA_INVALID"),
            PartsCreated = 0, PartsClosed = 0, MaximumNativeParts = 4, HistoricalEvidenceUnchanged = true, MatrixHash = Hash(Path.Combine(output, "qualification-matrix.json")), UnexpectedFailure = unexpected };
        var path = Path.Combine(output, "qualification-result.json"); Write(path, result); File.WriteAllText(path + ".sha256", Hash(path));
        Console.WriteLine(JsonSerializer.Serialize(result, Json)); return gate ? 0 : 1;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) return 2; var root = Path.GetFullPath(args[0]);
            return args[1] switch {
                "--pure" => PureQualification.Run(root), "--prepare" => Prepare(root), "--qualify" => Qualify(root),
                "--smoke" when args.Length >= 3 => NativeSmoke.Run(root, args[2], args.Length >= 4 ? args[3] : null),
                "--summary" => AcceptanceSummary.Run(root), _ => 2
            };
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
    }
}
