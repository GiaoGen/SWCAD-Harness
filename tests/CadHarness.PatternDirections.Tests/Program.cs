using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;

[assembly: SupportedOSPlatform("windows")]
namespace CadHarness.PatternDirections.Tests;
internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    internal static Dictionary<string, string> EvidenceHashes(string root) => Directory.GetFiles(Path.Combine(root, "artifacts/milestone9d"), "*", SearchOption.AllDirectories)
        .OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    internal static void VerifyEvidence(string root)
    {
        var before = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "artifacts/milestone9e/m9d-evidence-hashes.json")))!;
        Check(before.SequenceEqual(EvidenceHashes(root)), "M9D evidence changed.");
    }
    [STAThread]
    private static int Main(string[] args)
    {
        var root = args[0]; var output = Path.Combine(root, "artifacts/milestone9e"); Directory.CreateDirectory(output);
        if (args[1] == "--pure") return PureTests.Run(root);
        if (args[1] is "--linear" or "--rectangular") return NativeTests.Run(root, args[1], args.Length > 2 ? args[2] : null);
        if (args[1] != "--diagnose") return 2;
        var evidencePath = Path.Combine(output, "m9d-evidence-hashes.json");
        if (!File.Exists(evidencePath)) Write(evidencePath, EvidenceHashes(root)); VerifyEvidence(root);
        Check(!File.Exists(Path.Combine(output, "diagnosis.json")), "Diagnostic Part already attempted.");
        Check(OperationRegistry.Default.Get(OperationKind.CreateExtrude).Outputs.Single(o => o.Suffix == ".direction_x").Type == SemanticType.LinearEdge,
            "Baseline diagnosis requires the pre-fix edge backend. This runtime already uses datum axes; no diagnostic Part was created.");
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM9ETest_";
        using var budget = new NativeTestBudget(Path.Combine(output, "native-budget.json"), "M9E", 3);
        SolidWorksConnection? connection = null; TestPartScope? part = null; CompositionExecutionResult? result = null;
        ResourceSnapshot? guard = null; string? failure = null;
        try
        {
            connection = SolidWorksConnection.Connect(); guard = NativeResourceGuard.Inspect(connection.Application, budget);
            Check(NativeResourceGuard.Evaluate(guard.Responding, guard.GdiCount, guard.OpenTestOwnedParts) is null, "Resource guard rejected Part.");
            part = new(connection.Application, budget); var context = part.Create(connection, connection.ResolvePartTemplate(args.Length > 2 ? args[2] : null));
            var path = Path.Combine(output, "diagnostic-state.json"); var store = new AtomicStateStore(path); store.Commit(context.CaptureConstructionState());
            var bytes = File.ReadAllBytes(path);
            result = new RelationBackend().Create(context, CaseData.All().Single(c => c.Name == "G1").Program!, store);
            Check(!result.Succeeded && result.FailureCode == "RELATION_VIOLATED" && result.Message.Contains("axisReferenceMatches=") &&
                result.Operations.Count == 4 && result.Operations.All(o => o.Succeeded) && result.RollbackSucceeded && !result.StateCommitted && bytes.SequenceEqual(File.ReadAllBytes(path)), "Diagnosis was not a clean four-operation reproduction: " + result.Message);
        }
        catch (Exception e) { failure = e.Message; }
        finally
        {
            part?.Cleanup(); connection?.Dispose(); VerifyEvidence(root);
            Write(Path.Combine(output, "diagnosis.json"), new { Status = failure is null && part?.CleanupError is null ? "COMPLETE" : "BLOCKED", Result = result, ResourceGuard = guard,
                Failure = failure, PartsCreated = part?.Created == true ? 1 : 0, PartsClosed = part?.Closed == true ? 1 : 0,
                OriginalActiveRestored = part?.OriginalActiveRestored, CleanupError = part?.CleanupError, Budget = budget.Snapshot });
            Console.WriteLine(result?.Message); Console.WriteLine("Diagnostic failure: " + failure);
        }
        return failure is null && part?.CleanupError is null ? 0 : 1;
    }
}
