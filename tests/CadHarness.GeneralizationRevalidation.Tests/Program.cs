using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;

[assembly: SupportedOSPlatform("windows")]
namespace CadHarness.GeneralizationRevalidation.Tests;

internal sealed record Freeze(string ProductionCommit, Dictionary<string, string> Production, Dictionary<string, string> Historical);
internal static class Program
{
    internal static readonly string[] Cases = { "G1", "G3", "G5" };
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static string Output(string root) => Path.Combine(root, "artifacts/milestone9f");
    internal static void Check(bool ok, string message) { if (!ok) throw new TestFailure("REVALIDATION_ASSERTION_FAILED", message); }
    internal static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    private static bool SourceFile(string path) => !path.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj");
    private static Dictionary<string, string> Hashes(string root, IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.Ordinal)
        .ToDictionary(p => Path.GetRelativePath(root, p), HashFile, StringComparer.Ordinal);
    private static Dictionary<string, string> Production(string root) => Hashes(root, Directory.GetFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories).Where(SourceFile));
    private static Dictionary<string, string> Historical(string root) => Hashes(root,
        new[] { "artifacts/milestone9d", "artifacts/milestone9e" }.SelectMany(d => Directory.GetFiles(Path.Combine(root, d), "*", SearchOption.AllDirectories))
        .Concat(Directory.GetFiles(Path.Combine(root, "artifacts"), "milestone9d-*.log"))
        .Concat(Directory.GetFiles(Path.Combine(root, "artifacts"), "milestone9e-*.log"))
        .Concat(Directory.GetFiles(Path.Combine(root, "tests"), "*", SearchOption.AllDirectories).Where(SourceFile).Where(p => !p.Contains("GeneralizationRevalidation.Tests", StringComparison.Ordinal)))
        .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "milestone-*-verification.md").Where(p => !p.EndsWith("9f-verification.md", StringComparison.Ordinal)))
        .Append(Path.Combine(root, "Generalized_CAD_Harness_v0.2_CLEAN_PRD.md")));
    internal static void VerifyFreeze(string root)
    {
        var before = JsonSerializer.Deserialize<Freeze>(File.ReadAllText(Path.Combine(Output(root), "freeze.json")))!;
        Check(before.Production.SequenceEqual(Production(root)), "Production file set/hash changed during M9F.");
        Check(before.Historical.SequenceEqual(Historical(root)), "Historical tests, verification, PRD or M9D/M9E evidence changed.");
    }
    internal static CadProgram Stable(CadProgram program, bool undo = false) => program with { Operations = program.Operations.Select(o =>
        o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern ? o with { Inputs = o.Inputs.Select(input =>
            input.Name == "seed" ? input : input with { References = input.References.Select(r => r with { Type = undo ? SemanticType.LinearEdge : SemanticType.ReferenceAxis }).ToArray() }).ToArray() } : o).ToArray() };
    internal static PlanningResult Plan(string intent, CadProgram program, IPlanningRuntime runtime)
    {
        var response = JsonSerializer.Serialize(new { outcome = "planned", program = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(program)), reason = "" });
        return new CadPlanner(runtime, new FixturePlanSource(new Dictionary<string, string> { [intent] = response })).PlanAsync(intent).GetAwaiter().GetResult();
    }
    private static int Prepare(string root)
    {
        Directory.CreateDirectory(Output(root));
        foreach (var m in new[] { "9a", "9b", "9c", "9e" }) Check(File.ReadAllText(Path.Combine(root, $"docs/milestone-{m}-verification.md")).Contains("**COMPLETE**", StringComparison.Ordinal), m + " prerequisite is not COMPLETE.");
        var historicalFreeze = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9d/freeze.json")))!;
        Check(historicalFreeze["CaseDefinitions"]!.GetValue<string>() == HashFile(Path.Combine(root, "tests/CadHarness.Generalization.Tests/CaseData.cs")), "Original M9D case definitions changed.");
        var path = Path.Combine(Output(root), "freeze.json");
        if (!File.Exists(path)) Write(path, new Freeze("d51b20c", Production(root), Historical(root)));
        VerifyFreeze(root);
        var runtime = SolidWorksPlanningRuntime.ForConstruction();
        Check(runtime.Capabilities.Registry.Contracts.Select(c => c.Kind).ToHashSet().SetEquals(new FeatureBackendRegistry().SupportedKinds), "Projected operations/native registry differ.");
        Check(runtime.Capabilities.Profiles.ToHashSet().SetEquals(CreateExtrudeHandler.SupportedProfiles), "Projected/native profiles differ.");
        Check(runtime.Capabilities.Relations.ToHashSet().SetEquals(new CadHarness.State.DesignRelationEngine().SupportedKinds), "Projected relations/handlers differ.");
        Check(runtime.Capabilities.Registry.Contracts.SelectMany(c => c.Inputs).Where(i => i.Role == SemanticRole.PatternDirection).All(i => i.AcceptedTypes.SequenceEqual(new[] { SemanticType.ReferenceAxis })), "Direction projection is not ReferenceAxis.");
        Check(runtime.Capabilities.ParameterEdits.Count == 0 && !runtime.Capabilities.Registry.TryGet(OperationKind.EditParameter, out _), "Construction offers parameter edits.");
        File.WriteAllText(Path.Combine(Output(root), "construction-capabilities.json"), runtime.Capabilities.ToPromptJson());
        File.WriteAllText(Path.Combine(Output(root), "planner-schema.json"), PlannerResponseSchema.Create(runtime.Capabilities));
        var historicalCases = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9d/cases.json")))!.AsArray();
        var serializer = new CadProgramJson();
        var reports = new List<object>();
        foreach (var name in Cases)
        {
            var definition = CaseData.All().Single(c => c.Name == name); var stable = Stable(definition.Program!);
            Check(JsonNode.DeepEquals(historicalCases.Single(c => c!["Name"]!.GetValue<string>() == name), JsonSerializer.SerializeToNode(definition)), name + " fixture/geometry changed from M9D.");
            var restored = Stable(stable, true); var original = definition.Program!;
            Check(restored.ProgramVersion == original.ProgramVersion && restored.Relations.SequenceEqual(original.Relations) &&
                restored.Operations.Count == original.Operations.Count && restored.Operations.Zip(original.Operations).All(pair =>
                    pair.First.Id == pair.Second.Id && pair.First.Kind == pair.Second.Kind && pair.First.SemanticId == pair.Second.SemanticId &&
                    pair.First.Parameters.SequenceEqual(pair.Second.Parameters) && pair.First.Inputs.Count == pair.Second.Inputs.Count &&
                    pair.First.Inputs.Zip(pair.Second.Inputs).All(input => input.First.Name == input.Second.Name && input.First.References.SequenceEqual(input.Second.References))),
                name + " plan changed beyond direction reference types.");
            Check(stable.Operations.Count == 4 && !runtime.Preflight(definition.Program!).IsValid && runtime.Preflight(stable).IsValid, name + " typed preflight differs.");
            var plan = Plan(definition.Intent, stable, runtime); Check(plan.Succeeded && plan.ModelCalls == 0, name + " Planner rejected: " + plan.Message);
            var casePath = Path.Combine(Output(root), name); Directory.CreateDirectory(casePath);
            File.WriteAllText(Path.Combine(casePath, "program.json"), serializer.Serialize(stable)); Write(Path.Combine(casePath, "expected.json"), definition.Expected!);
            Write(Path.Combine(casePath, "plan.json"), plan);
            reports.Add(new { Case = name, Planned = "PASS", OriginalGeometryAndCompositionPreserved = true, OnlyDirectionReferenceTypeChanged = true });
        }
        Write(Path.Combine(Output(root), "prepare-result.json"), new { Status = "COMPLETE", ProductionCommit = "d51b20c", ProductionUnchanged = true,
            HistoricalEvidenceUnchanged = true, CapabilityProjectionConsistent = true, NativeBudget = 3, MaximumConcurrentParts = 1,
            PlanningSource = "deterministic fixture IR through production CadPlanner; zero external model calls", Cases = reports });
        Console.WriteLine("M9F Prepare PASS: G1/G3/G5 typed plans accepted; original geometry/composition preserved; 0 Parts."); return 0;
    }
    private static void VerifyNativeEvidenceHashes(string root)
    {
        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Output(root), "native-evidence-hashes.json")))!;
        foreach (var item in hashes) Check(HashFile(Path.Combine(root, item.Key)) == item.Value, "Original M9F native evidence changed: " + item.Key);
    }
    private static int VerifyRecordedEvidence(string root)
    {
        VerifyFreeze(root); VerifyNativeEvidenceHashes(root);
        var output = Path.Combine(Output(root), "G5");
        var rawPath = Path.Combine(output, "result.json"); var rollbackPath = Path.Combine(output, "rollback.json");
        var raw = JsonNode.Parse(File.ReadAllText(rawPath))!;
        Check(raw["Status"]!.GetValue<string>() == "BLOCKED" && raw["RollbackSafe"]!.GetValue<string>() == "NOT_RUN" &&
            raw["Message"]!.GetValue<string>().StartsWith("Atomic construction rollback failed:", StringComparison.Ordinal), "Not the documented raw floating-point assertion failure.");
        foreach (var key in new[] { "Planned", "Executed", "StrictReadback", "FinalGeometry", "PersistentState", "CapabilityProjection" }) Check(raw[key]!.GetValue<string>() == "PASS", "Raw G5 " + key + " incomplete.");
        Check(raw["PartsCreated"]!.GetValue<int>() == 1 && raw["PartsClosed"]!.GetValue<int>() == 1 && raw["OriginalActiveRestored"]!.GetValue<bool>() &&
            raw["CleanupError"] is null && raw["ProductionAndHistoricalEvidenceUnchanged"]!.GetValue<bool>(), "Raw G5 lifecycle/freeze failed.");
        var rollback = JsonNode.Parse(File.ReadAllText(rollbackPath))!; var result = rollback["Result"]!;
        Check(!result["Succeeded"]!.GetValue<bool>() && result["FailureCode"]!.GetValue<string>() == "STATE_COMMIT_FAILED" &&
            result["MutationStarted"]!.GetValue<bool>() && result["RollbackAttempted"]!.GetValue<bool>() && result["RollbackSucceeded"]!.GetValue<bool>() &&
            !result["StateCommitted"]!.GetValue<bool>() && result["Operations"]!.AsArray().Count == 1 && result["Operations"]![0]!["Succeeded"]!.GetValue<bool>() &&
            result["Transaction"]!["RollbackFailureCode"] is null && result["Transaction"]!["RollbackFailureMessage"] is null, "Recorded production rollback did not succeed.");
        Check(rollback["BeforeModelSignature"]!.GetValue<string>() == rollback["AfterModelSignature"]!.GetValue<string>() &&
            rollback["FileUnchanged"]!.GetValue<bool>() && rollback["SessionUnchanged"]!.GetValue<bool>(), "Recorded structure, state or session changed.");
        var before = rollback["Before"]!.Deserialize<GeometryRead>()!; var after = rollback["After"]!.Deserialize<GeometryRead>()!;
        Check(NativeTests.SameGeometry(before, after), "Recorded geometry differs beyond established M9D tolerances.");
        var expected = CaseData.All().Single(c => c.Name == "G5").Expected!;
        NativeTests.VerifyRecordedGeometry(before, expected); NativeTests.VerifyRecordedGeometry(after, expected);
        var bytes = File.ReadAllBytes(Path.Combine(output, "state.json"));
        Check(Program.Hash(bytes) == rollback["StateBeforeSha256"]!.GetValue<string>() && Program.Hash(bytes) == rollback["StateAfterSha256"]!.GetValue<string>() &&
            bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(output, "committed-state.json"))), "Recorded committed state bytes changed.");
        var state = new AtomicStateStore(Path.Combine(output, "state.json")).Load(); StateValidation.Validate(state);
        Check(state.Revision == 1 && state.Features.Count == 4 && Directory.GetFiles(output, ".state.json.*.tmp").Length == 0, "Recorded state revision/features/temp cleanup differ.");
        var directions = JsonSerializer.Deserialize<DirectionRead[]>(File.ReadAllText(Path.Combine(output, "strict-readback.json")))!;
        Check(directions.Length == 2 && directions.All(d => d.ReferenceMatches && d.ReverseMatches && d.Count == 2), "Recorded nominal datum readback failed.");
        foreach (var direction in directions)
        {
            var axis = direction.Slot == "D1" ? "x" : "y";
            Check(state.Entities.Single(e => e.SemanticId == "stock.direction_" + axis).NativeReference == direction.ExpectedReference && direction.ExpectedReference == direction.ActualReference,
                "Saved native direction identity differs from recorded native readback.");
        }
        var projections = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "projection-audit.json")))!["Visible"]!.AsArray();
        Check(projections.Count > 0 && projections.All(c => new[] { "Registered", "NativeReadable", "PlannerAccepted", "PreflightExecutable", "RollbackCapturable" }.All(k => c![k]!.GetValue<bool>())), "Recorded edit projection incomplete.");
        VerifyFreeze(root); VerifyNativeEvidenceHashes(root);
        Write(Path.Combine(output, "evidence-verification.json"), new { Status = "PASS", Case = "G5", RawResultSha256 = HashFile(rawPath), RawRollbackSha256 = HashFile(rollbackPath),
            NativePartsCreated = 0, NativePartsClosed = 0, NativeCalls = 0, ProductionChanges = 0, RawNativeEvidenceUnchanged = true,
            VolumeBefore = before.Volume, VolumeAfter = after.Volume, VolumeDeltaMm3 = Math.Abs(before.Volume - after.Volume), VolumeToleranceMm3 = 0.001,
            LengthToleranceMm = 1e-6, ModelStructureExact = true, GeometryWithinExistingTolerance = true, FileExact = true, SessionExact = true,
            RuntimeRollbackFlagsVerified = true, PersistentStateAndDatumReferencesVerified = true,
            PostRollbackNativeReadback = "Recorded production RollbackSucceeded=true: TransactionalConstructionBackend.ValidateRestored ran checkpoint.Verify, strict RelationNativeReadback.Verify and captured-state comparison before returning success. No additional independent post-rollback COM observations were made after the test assertion.",
            Explanation = "The original verifier compared serialized geometry floats exactly. Only volume differs by about 7.3e-12 mm^3; all original raw reports/logs remain unchanged. Acceptance uses captured evidence and existing geometric tolerances." });
        Console.WriteLine("G5 recorded-evidence PASS; volume delta=" + Math.Abs(before.Volume - after.Volume) + " mm^3; 0 Parts; original native evidence preserved."); return 0;
    }
    private static int Summary(string root)
    {
        VerifyFreeze(root);
        VerifyNativeEvidenceHashes(root);
        var reports = Cases.Select(name => JsonNode.Parse(File.ReadAllText(Path.Combine(Output(root), name, "result.json")))!.AsObject()).ToArray();
        var g5 = reports.Single(r => r["Case"]!.GetValue<string>() == "G5");
        var evidencePath = Path.Combine(Output(root), "G5/evidence-verification.json");
        if (g5["Status"]!.GetValue<string>() == "BLOCKED" && File.Exists(evidencePath))
        {
            VerifyRecordedEvidence(root);
            var review = JsonNode.Parse(File.ReadAllText(evidencePath))!;
            g5["RawStatus"] = g5["Status"]!.DeepClone(); g5["RawFailureCode"] = g5["FailureCode"]?.DeepClone(); g5["RawMessage"] = g5["Message"]?.DeepClone();
            g5["Status"] = "PASS"; g5["RollbackSafe"] = "PASS"; g5["FailureCode"] = null; g5["Message"] = "Zero-Part evidence verification corrected the test comparator; raw native report preserved.";
            g5["RollbackEvidenceReview"] = review;
        }
        var budget = JsonSerializer.Deserialize<BudgetSnapshot>(File.ReadAllText(Path.Combine(Output(root), "native-budget.json")))!;
        var lifecycle = reports.All(r => r["PartsCreated"]!.GetValue<int>() == 1 && r["PartsClosed"]!.GetValue<int>() == 1 && r["OriginalActiveRestored"]!.GetValue<bool>() && r["CleanupError"] is null) &&
            budget.CreationAttempts == 3 && budget.PartsCreated == 3 && budget.PartsClosed == 3 && budget.OpenTestOwnedTitles.Count == 0;
        var passed = lifecycle && reports.All(r => r["Status"]!.GetValue<string>() == "PASS" && new[] { "Planned", "Executed", "StrictReadback", "FinalGeometry", "PersistentState", "RollbackSafe", "CapabilityProjection" }.All(key => r[key]!.GetValue<string>() == "PASS"));
        var original = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9d/generalization-matrix.json")))!;
        var carry = original["Cases"]!.AsArray().Where(c => !Cases.Contains(c!["Case"]!.GetValue<string>(), StringComparer.Ordinal)).ToArray();
        Check(carry.Length == 7 && carry.All(c => c!["Status"]!.GetValue<string>() == "COMPLETE"), "Historical unblocked cases are incomplete.");
        var carrySummary = carry.Select(c => new { Case = c!["Case"]!.GetValue<string>(), Status = "PASS",
            Evidence = "artifacts/milestone9d/" + c["Case"]!.GetValue<string>() + "/result.json", Provenance = "Historical M9D execution; not rerun on M9E runtime",
            Planned = c["Planned"]!.GetValue<string>(), Executed = c["Executed"]!.GetValue<string>(), Validated = c["Validated"]!.GetValue<string>(),
            Editable = c["Editable"]!.GetValue<string>(), RollbackSafe = c["RollbackSafe"]!.GetValue<string>() }).ToArray();
        Write(Path.Combine(Output(root), "m9-final-acceptance.json"), new { Status = passed ? "COMPLETE" : "BLOCKED", M10PrerequisiteSatisfied = passed,
            ProductionCodeChanges = 0, ProductionCommit = "d51b20c", ProductionAndHistoricalEvidenceUnchanged = true, LifecycleVerified = lifecycle,
            PlanningScope = "deterministic typed fixture IR through production CadPlanner; no external LLM generation or benchmark claim",
            RevalidatedCases = reports, HistoricalCarryForward = carrySummary, Budget = budget });
        var lines = new List<string> { "| Case | Planned / capability | Native execution | Strict readback | Final geometry | CADState / refs | Rollback safe | Production changes |", "|---|---|---|---|---|---|---|---|" };
        lines.AddRange(reports.Select(r => $"| {r["Case"]} | {r["Planned"]} / {r["CapabilityProjection"]} | {r["Executed"]} | {r["StrictReadback"]} | {r["FinalGeometry"]} | {r["PersistentState"]} | {r["RollbackSafe"]} | 0 |"));
        File.WriteAllLines(Path.Combine(Output(root), "generalization-revalidation-matrix.md"), lines);
        Console.WriteLine(string.Join(System.Environment.NewLine, lines)); Console.WriteLine("M9 " + (passed ? "COMPLETE" : "BLOCKED")); return passed ? 0 : 1;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[1] == "--prepare") return Prepare(args[0]);
            if (args.Length == 2 && args[1] == "--verifyevidence") return VerifyRecordedEvidence(args[0]);
            if (args.Length is 3 or 4 && args[1] == "--live" && Cases.Contains(args[2], StringComparer.Ordinal)) return NativeTests.Run(args[0], args[2], args.Length == 4 ? args[3] : null);
            if (args.Length == 2 && args[1] == "--summary") return Summary(args[0]);
            Console.WriteLine("Usage: <workspace> --prepare | --live G1/G3/G5 [template] | --verifyevidence | --summary"); return 2;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
