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
namespace CadHarness.CurrentRuntimeCompatibility.Tests;
internal sealed record Freeze(string BaselineCommit, Dictionary<string, string> Production, Dictionary<string, string> Historical);
internal static class Program
{
    internal static readonly string[] LiveCases = { "G2", "HeldOut" };
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal static string Output(string root) => Path.Combine(root, "artifacts/milestone9g");
    internal static void Check(bool ok, string message) { if (!ok) throw new TestFailure("COMPATIBILITY_ASSERTION_FAILED", message); }
    internal static void Write(string path, object? value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    private static bool SourceFile(string path) => !path.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj");
    private static Dictionary<string, string> Hashes(string root, IEnumerable<string> paths) => paths.OrderBy(p => p, StringComparer.Ordinal)
        .ToDictionary(p => Path.GetRelativePath(root, p), HashFile, StringComparer.Ordinal);
    private static Dictionary<string, string> Production(string root) => Hashes(root, Directory.GetFiles(Path.Combine(root, "src"), "*", SearchOption.AllDirectories).Where(SourceFile));
    private static Dictionary<string, string> Historical(string root) => Hashes(root,
        new[] { "9d", "9e", "9f" }.SelectMany(m => Directory.GetFiles(Path.Combine(root, "artifacts/milestone" + m), "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "artifacts"), "milestone" + m + "-*.log")))
        .Concat(Directory.GetFiles(Path.Combine(root, "tests"), "*", SearchOption.AllDirectories).Where(SourceFile).Where(p => !p.Contains("CurrentRuntimeCompatibility.Tests", StringComparison.Ordinal)))
        .Concat(Directory.GetFiles(Path.Combine(root, "docs"), "milestone-*-verification.md").Where(p => !p.EndsWith("9g-verification.md", StringComparison.Ordinal)))
        .Concat(Directory.GetFiles(Path.Combine(root, "scripts"), "test-milestone9*.ps1").Where(p => !p.EndsWith("9g.ps1", StringComparison.Ordinal)))
        .Append(Path.Combine(root, "Generalized_CAD_Harness_v0.2_CLEAN_PRD.md")));
    internal static void VerifyFreeze(string root)
    {
        var before = JsonSerializer.Deserialize<Freeze>(File.ReadAllText(Path.Combine(Output(root), "freeze.json")))!;
        Check(before.Production.SequenceEqual(Production(root)), "Production file set/hash changed during M9G.");
        Check(before.Historical.SequenceEqual(Historical(root)), "Historical tests/verification/PRD or M9D/M9E/M9F evidence changed.");
    }
    internal static CadProgram Stable(CadProgram program, bool undo = false) => program with { Operations = program.Operations.Select(o =>
        o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern ? o with { Inputs = o.Inputs.Select(input =>
            input.Name == "seed" ? input : input with { References = input.References.Select(r => r with { Type = undo ? SemanticType.LinearEdge : SemanticType.ReferenceAxis }).ToArray() }).ToArray() } : o).ToArray() };
    internal static PlanningResult Plan(string intent, CadProgram program, IPlanningRuntime runtime)
    {
        var response = JsonSerializer.Serialize(new { outcome = "planned", program = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(program)), reason = "" });
        return new CadPlanner(runtime, new FixturePlanSource(new Dictionary<string, string> { [intent] = response })).PlanAsync(intent).GetAwaiter().GetResult();
    }
    private static void VerifyCaseUnchanged(CaseDefinition definition, CadProgram stable, JsonArray historicalCases)
    {
        Check(JsonNode.DeepEquals(historicalCases.Single(c => c!["Name"]!.GetValue<string>() == definition.Name), JsonSerializer.SerializeToNode(definition)), definition.Name + " fixture/geometry changed from M9D.");
        var restored = Stable(stable, true); var original = definition.Program!;
        Check(restored.ProgramVersion == original.ProgramVersion && restored.Relations.SequenceEqual(original.Relations) &&
            restored.Operations.Count == original.Operations.Count && restored.Operations.Zip(original.Operations).All(pair =>
                pair.First.Id == pair.Second.Id && pair.First.Kind == pair.Second.Kind && pair.First.SemanticId == pair.Second.SemanticId &&
                pair.First.Parameters.SequenceEqual(pair.Second.Parameters) && pair.First.Inputs.Count == pair.Second.Inputs.Count &&
                pair.First.Inputs.Zip(pair.Second.Inputs).All(input => input.First.Name == input.Second.Name && input.First.References.SequenceEqual(input.Second.References))),
            definition.Name + " plan changed beyond PatternDirection reference types.");
    }
    private static int Prepare(string root)
    {
        Directory.CreateDirectory(Output(root));
        foreach (var m in new[] { "9e", "9f" }) Check(File.ReadAllText(Path.Combine(root, $"docs/milestone-{m}-verification.md")).Contains("**COMPLETE**", StringComparison.Ordinal), m + " prerequisite incomplete.");
        var m9f = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9f/m9-final-acceptance.json")))!;
        Check(m9f["Status"]!.GetValue<string>() == "COMPLETE" && m9f["RevalidatedCases"]!.AsArray().All(c => c!["Status"]!.GetValue<string>() == "PASS"), "M9F carry-forward incomplete.");
        var oldProduction = JsonSerializer.Deserialize<Dictionary<string, string>>(JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9f/freeze.json")))!["Production"]!.ToJsonString())!;
        Check(oldProduction.SequenceEqual(Production(root)), "Current production differs from the M9F ReferenceAxis runtime.");
        var fixtureHash = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9d/freeze.json")))!["CaseDefinitions"]!.GetValue<string>();
        Check(fixtureHash == HashFile(Path.Combine(root, "tests/CadHarness.Generalization.Tests/CaseData.cs")), "M9D fixtures changed.");
        var freezePath = Path.Combine(Output(root), "freeze.json");
        if (!File.Exists(freezePath)) Write(freezePath, new Freeze("a491501", Production(root), Historical(root)));
        VerifyFreeze(root);
        var acceptanceSnapshot = Path.Combine(Output(root), "prior-m9-final-acceptance.md");
        if (!File.Exists(acceptanceSnapshot)) File.Copy(Path.Combine(root, "docs/milestone-9-final-acceptance.md"), acceptanceSnapshot);
        var runtime = SolidWorksPlanningRuntime.ForConstruction();
        Check(runtime.Capabilities.Registry.Contracts.Select(c => c.Kind).ToHashSet().SetEquals(new FeatureBackendRegistry().SupportedKinds), "Operations/catalog mismatch.");
        Check(runtime.Capabilities.Profiles.ToHashSet().SetEquals(CreateExtrudeHandler.SupportedProfiles) && runtime.Capabilities.Relations.ToHashSet().SetEquals(new DesignRelationEngine().SupportedKinds), "Profiles/relations catalog mismatch.");
        Check(runtime.Capabilities.Registry.Contracts.SelectMany(c => c.Inputs).Where(i => i.Role == SemanticRole.PatternDirection).All(i => i.AcceptedTypes.SequenceEqual(new[] { SemanticType.ReferenceAxis })), "Direction projection differs from ReferenceAxis.");
        File.WriteAllText(Path.Combine(Output(root), "construction-capabilities.json"), runtime.Capabilities.ToPromptJson());
        File.WriteAllText(Path.Combine(Output(root), "planner-schema.json"), PlannerResponseSchema.Create(runtime.Capabilities));
        var historicalCases = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9d/cases.json")))!.AsArray();
        foreach (var name in LiveCases.Append("G6_Pattern"))
        {
            var definition = CaseData.All().Single(c => c.Name == name); var program = Stable(definition.Program!);
            VerifyCaseUnchanged(definition, program, historicalCases);
            var output = Path.Combine(Output(root), name); Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "program.json"), new CadProgramJson().Serialize(program));
            Write(Path.Combine(output, "expected.json"), definition.Expected);
            var plan = Plan(definition.Intent, program, runtime); Write(Path.Combine(output, "plan.json"), plan);
            var preflight = runtime.Preflight(program);
            Check(plan.ModelCalls == 0, "Fixture planning unexpectedly made a model call.");
            if (name == "G6_Pattern")
            {
                Check(program.Operations.Single(o => o.SemanticId == "layout").Parameter<CountParameter>("count").Value == 1025 && new ProgramValidator().Validate(program).IsValid, "Negative plan is not a well-typed count=1025 IR plan.");
                Check(!plan.Succeeded && plan.Status == PlanningStatus.Rejected && plan.Program is null && plan.Issues.Count > 0 && plan.Issues.All(i => i.Path.EndsWith(".count", StringComparison.Ordinal)), "Planner did not reject solely the finite count contract.");
                Check(!preflight.IsValid && preflight.Issues.Count > 0 && preflight.Issues.All(i => i.Path.EndsWith(".count", StringComparison.Ordinal)), "Runtime preflight rejected for a direction/type reason rather than count.");
                var backend = new RelationBackend().Preflight(program);
                Check(!backend.IsValid && backend.FailureCode == FailureCodes.PreconditionFailed && backend.Message.Contains("1024", StringComparison.Ordinal), "Backend preflight did not reject the native instance limit.");
                Write(Path.Combine(output, "result.json"), new { Case = name, Status = "PASS", Planned = "EXPECTED_REJECTED", CapabilityProjection = "PASS", Executed = "NO_MUTATION",
                    StrictReadback = "NOT_APPLICABLE", Validated = "PASS_PREFLIGHT", Editable = "NOT_APPLICABLE", PersistentState = "UNCHANGED_NO_NATIVE_SESSION",
                    RollbackSafe = "PASS_NO_MUTATION", Plan = plan, RuntimePreflight = preflight, BackendPreflight = backend, NativeConnectionMade = false,
                    MutationStarted = false, RollbackAttempted = false, RollbackSucceeded = false, StateCommitted = false, NativeCalls = 0,
                    PartsCreated = 0, PartsClosed = 0, ProductionCodeChanges = 0, HistoricalEvidenceUnchanged = true });
            }
            else Check(plan.Succeeded && preflight.IsValid && !runtime.Preflight(definition.Program!).IsValid, name + " supported ReferenceAxis plan rejected.");
        }
        VerifyFreeze(root);
        Write(Path.Combine(Output(root), "prepare-result.json"), new { Status = "COMPLETE", BaselineCommit = "a491501", RuntimeMatchesM9F = true,
            ProductionAndHistoricalEvidenceUnchanged = true, OnlyPatternDirectionReferenceTypesChanged = true, G6Pattern = "PASS_COUNT_1025_REJECTED", NativeBudget = 2,
            PartsCreated = 0, MaximumConcurrentParts = 1, PlanningScope = "deterministic fixture IR through production CadPlanner; no LLM generation claim" });
        Console.WriteLine("M9G Prepare PASS: G2/HeldOut accepted; ReferenceAxis count=1025 rejected by Planner/runtime/backend preflight; 0 Parts."); return 0;
    }
    private static int Summary(string root)
    {
        VerifyFreeze(root);
        var live = LiveCases.Select(name => JsonNode.Parse(File.ReadAllText(Path.Combine(Output(root), name, "result.json")))!.AsObject()).ToArray();
        var negative = JsonNode.Parse(File.ReadAllText(Path.Combine(Output(root), "G6_Pattern/result.json")))!;
        var budget = JsonSerializer.Deserialize<BudgetSnapshot>(File.ReadAllText(Path.Combine(Output(root), "native-budget.json")))!;
        var lifecycle = budget.CreationAttempts == 2 && budget.PartsCreated == 2 && budget.PartsClosed == 2 && budget.OpenTestOwnedTitles.Count == 0 &&
            live.All(r => r["PartsCreated"]!.GetValue<int>() == 1 && r["PartsClosed"]!.GetValue<int>() == 1 && r["OriginalActiveRestored"]!.GetValue<bool>() && r["CleanupError"] is null);
        var passed = lifecycle && live.All(r => r["Status"]!.GetValue<string>() == "PASS" &&
            new[] { "Planned", "Executed", "Editable", "StrictReadback", "FinalGeometry", "PersistentState", "RollbackSafe", "CapabilityProjection" }.All(k => r[k]!.GetValue<string>() == "PASS")) &&
            negative["Status"]!.GetValue<string>() == "PASS" && !negative["MutationStarted"]!.GetValue<bool>() && negative["PartsCreated"]!.GetValue<int>() == 0;
        var m9f = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9f/m9-final-acceptance.json")))!;
        var carried = m9f["RevalidatedCases"]!.AsArray();
        Check(carried.Count == 3 && carried.All(r => r!["Status"]!.GetValue<string>() == "PASS"), "M9F cases incomplete.");
        var history = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "artifacts/milestone9d/generalization-matrix.json")))!["Cases"]!.AsArray();
        var unaffectedNames = new[] { "G4", "G6_Fillet", "G6_Placement", "G7" };
        var unaffected = history.Where(c => unaffectedNames.Contains(c!["Case"]!.GetValue<string>(), StringComparer.Ordinal)).ToArray();
        Check(unaffected.Length == 4 && unaffected.All(r => r!["Status"]!.GetValue<string>() == "COMPLETE"), "Unaffected M9D carry-forward incomplete.");
        Write(Path.Combine(Output(root), "m9-final-acceptance.json"), new { Status = passed ? "COMPLETE" : "BLOCKED", M10PrerequisiteSatisfied = passed,
            AllLinearRectangularPatternSupportedAndNegativeCasesVerifiedOnCurrentReferenceAxisRuntime = passed, BaselineCommit = "a491501", Runtime = "solidworks-v0.2-m9e",
            ProductionCodeChanges = 0, ProductionAndHistoricalEvidenceUnchanged = true, LifecycleVerified = lifecycle, NativeBudget = budget,
            CurrentRevalidatedCases = live.Append(negative.AsObject()).ToArray(), ReferenceAxisM9FCarryForward = carried,
            UnaffectedM9DCarryForward = unaffected.Select(c => new { Case = c!["Case"]!.GetValue<string>(), Status = "PASS", Evidence = "artifacts/milestone9d/" + c["Case"]!.GetValue<string>() + "/result.json" }).ToArray(),
            PlanningScope = "production CadPlanner with deterministic fixture responses, not external LLM generation or performance measurement" });
        var lines = new List<string> { "| Case | Planner / capability | Native | Edits | Strict readback | Geometry / state / refs | Rollback safety | Parts |", "|---|---|---|---|---|---|---|---|" };
        lines.AddRange(live.Select(r => $"| {r["Case"]} | {r["Planned"]} / {r["CapabilityProjection"]} | {r["Executed"]} | {r["Editable"]} | {r["StrictReadback"]} | {r["FinalGeometry"]} / {r["PersistentState"]} | {r["RollbackSafe"]} | {r["PartsCreated"]}/{r["PartsClosed"]} |"));
        lines.Add("| G6_Pattern | EXPECTED_REJECTED / PASS | NO_MUTATION | N/A | N/A | PASS_PREFLIGHT | PASS_NO_MUTATION | 0/0 |");
        File.WriteAllLines(Path.Combine(Output(root), "current-runtime-compatibility-matrix.md"), lines);
        Console.WriteLine(string.Join(System.Environment.NewLine, lines)); Console.WriteLine("M9 " + (passed ? "COMPLETE" : "BLOCKED")); return passed ? 0 : 1;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[1] == "--prepare") return Prepare(args[0]);
            if (args.Length is 3 or 4 && args[1] == "--live" && LiveCases.Contains(args[2], StringComparer.Ordinal)) return NativeTests.Run(args[0], args[2], args.Length == 4 ? args[3] : null);
            if (args.Length == 2 && args[1] == "--summary") return Summary(args[0]);
            Console.WriteLine("Usage: <workspace> --prepare | --live G2/HeldOut [template] | --summary"); return 2;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
