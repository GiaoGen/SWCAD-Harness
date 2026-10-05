using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using CadHarness.SolidWorks.Tests;

[assembly: SupportedOSPlatform("windows")]
namespace CadHarness.Benchmark.Tests;
internal sealed record BenchmarkManifest(string BaselineCommit, string Model, string Endpoint, int Temperature, int MaximumOutputTokens, int TimeoutSeconds,
    int MaximumDecisions, int MaximumParts, int MaximumConcurrentParts, int Warmups, int MeasuredRuns, BenchmarkTask[] Tasks, RunSlot[] Schedule,
    Dictionary<string, string> Sources, Dictionary<string, string> Binaries, Dictionary<string, string> Historical);
internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string evidenceName = "milestone10";
    internal static string Output(string root) => Path.Combine(root, "artifacts", evidenceName);
    internal static void Check(bool ok, string message) { if (!ok) throw new TestFailure("BENCHMARK_ASSERTION_FAILED", message); }
    internal static void Write(string path, object? value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool Source(string path) => !path.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj");
    private static Dictionary<string, string> Hashes(string root, IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p), Hash, StringComparer.Ordinal);
    private static Dictionary<string, string> Sources(string root) => Hashes(root,
        Directory.GetFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories).Where(Source)
        .Concat(Directory.GetFiles(Path.Combine(root, "tests/CadHarness.Benchmark.Tests"), "*", SearchOption.AllDirectories).Where(Source))
        .Concat(new[] { "scripts/test-milestone10.ps1", "tests/CadHarness.SolidWorks.Tests/NativeResourceGuard.cs", "tests/CadHarness.SolidWorks.Tests/NativeTestBudget.cs", "tests/CadHarness.SolidWorks.Tests/TestPartScope.cs" }.Select(p => Path.Combine(root, p))));
    private static Dictionary<string, string> Binaries(string root) => Hashes(root, Directory.GetFiles(AppContext.BaseDirectory, "*.dll"));
    private static Dictionary<string, string> Historical(string root) => Hashes(root,
        new[] { "9d", "9e", "9f", "9g" }.SelectMany(m => Directory.GetFiles(Path.Combine(root, "artifacts/milestone" + m), "*", SearchOption.AllDirectories))
        .Concat(Directory.GetFiles(Path.Combine(root, "tests"), "*", SearchOption.AllDirectories).Where(Source).Where(p => !p.Contains("CadHarness.Benchmark.Tests", StringComparison.Ordinal)))
        .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "milestone-9*.md"))
        .Append(Path.Combine(root, "Generalized_CAD_Harness_v0.2_CLEAN_PRD.md")));
    internal static void VerifyFreeze(string root)
    {
        var path = Path.Combine(Output(root), "manifest.json"); var manifest = JsonSerializer.Deserialize<BenchmarkManifest>(File.ReadAllText(path))!;
        Check(File.ReadAllText(Path.Combine(Output(root), "manifest.sha256")).Trim() == Hash(path), "Frozen benchmark manifest changed.");
        Check(manifest.Sources.SequenceEqual(Sources(root)) && manifest.Binaries.SequenceEqual(Binaries(root)), "Production/benchmark source or binary changed after freeze.");
        Check(manifest.Historical.SequenceEqual(Historical(root)), "Historical evidence, tests, M9 acceptance or PRD changed.");
        Check(JsonSerializer.Serialize(manifest.Schedule) == JsonSerializer.Serialize(BenchmarkData.Schedule()) && JsonSerializer.Serialize(manifest.Tasks) == JsonSerializer.Serialize(BenchmarkData.Tasks()), "Frozen tasks/schedule changed.");
    }
    private static int Prepare(string root)
    {
        var output = Output(root); Directory.CreateDirectory(output);
        Check(!File.Exists(Path.Combine(output, "manifest.json")), "M10 already frozen. Do not replace evidence or expand budget.");
        Check(File.ReadAllText(Path.Combine(root, "docs/milestone-9-final-acceptance.md")).Contains("M10 prerequisite satisfied=true", StringComparison.Ordinal), "M9 prerequisite missing.");
        Check(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY")), "Authorized model key missing.");
        var manifest = new BenchmarkManifest("c6c3ef6", "deepseek-chat", "https://api.deepseek.com/chat/completions", 0, 8192, 120,
            CadHarness.Planning.StepwisePlanner.MaximumDecisions, BenchmarkData.MaximumParts, 1, 1, 5, BenchmarkData.Tasks(), BenchmarkData.Schedule(), Sources(root), Binaries(root), Historical(root));
        Write(Path.Combine(output, "manifest.json"), manifest); File.WriteAllText(Path.Combine(output, "manifest.sha256"), Hash(Path.Combine(output, "manifest.json")));
        Console.WriteLine("M10 frozen: 2 tasks x 2 modes x (1 warm-up + 5 measured) = 24 Parts maximum. No native calls."); return 0;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var names = args.Where(a => a.StartsWith("--evidence-name=", StringComparison.Ordinal)).ToArray();
            if (names.Length > 1) throw new ArgumentException("Exactly one evidence directory name is allowed.");
            if (names.Length == 1)
            {
                evidenceName = names[0]["--evidence-name=".Length..];
                if (!System.Text.RegularExpressions.Regex.IsMatch(evidenceName, "^milestone10(?:-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) || evidenceName.Length > 64)
                    throw new ArgumentException("Evidence name must be a single milestone10 directory name.");
                args = args.Where(a => !a.StartsWith("--evidence-name=", StringComparison.Ordinal)).ToArray();
            }
            if (args.Length < 2) return 2; var root = Path.GetFullPath(args[0]);
            return args[1] switch
            {
                "--pure" => PureTests.Run(root), "--prepare" => Prepare(root), "--summary" => BenchmarkSummary.Run(root),
                "--live" when args.Length >= 3 => NativeBenchmark.Run(root, args[2], args.Length >= 4 ? args[3] : null),
                _ => 2
            };
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
    }
}
