using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

[assembly: SupportedOSPlatform("windows")]
namespace CadHarness.Generalization.Tests;

internal sealed record Cylinder(double X, double Y, double Diameter, double[] Levels);
internal sealed record ModelSnapshot(int Features, string[] FeatureTypes, int Bodies, int Faces, int Edges,
    double Volume, ExtrudeMeasurement? Extents, Cylinder[] Cylinders, SortedDictionary<string, string> Properties, int NeedsRebuild);
internal sealed record Freeze(Dictionary<string, string> Production, string CaseDefinitions, Dictionary<string, string> PriorTests);
internal sealed record ProjectionCheck(string Target, EditableParameter Parameter, bool Registered, bool NativeReadable,
    bool PreflightExecutable, string? Code, string? Message);
internal sealed record FailureEvidence(string Name, object Result, ModelSnapshot Before, ModelSnapshot After,
    string StateBeforeSha256, string StateAfterSha256, bool ModelUnchanged, bool FileUnchanged, bool SessionUnchanged);
internal sealed class CaseReport
{
    public string Case { get; set; } = "";
    public string Status { get; set; } = "NOT_RUN";
    public string Planned { get; set; } = "NOT_RUN";
    public string Executed { get; set; } = "NOT_RUN";
    public string Validated { get; set; } = "NOT_RUN";
    public string Editable { get; set; } = "NOT_APPLICABLE";
    public string RollbackSafe { get; set; } = "NOT_RUN";
    public string ProductionCodeChangesRequired { get; set; } = "NONE";
    public PlanningResult? Plan { get; set; }
    public CompositionExecutionResult? Construction { get; set; }
    public List<MutationResult> Edits { get; set; } = new();
    public List<ProjectionCheck> EditProjection { get; set; } = new();
    public List<FailureEvidence> Failures { get; set; } = new();
    public List<ModelSnapshot> Measurements { get; set; } = new();
    public bool CapabilityProjectionConsistent { get; set; } = true;
    public bool ProductionUnchanged { get; set; }
    public bool StatePersistedAndRebound { get; set; }
    public ResourceSnapshot? ResourceGuard { get; set; }
    public string? SolidWorksRevision { get; set; }
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public string? CleanupError { get; set; }
}

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static void Check(bool ok, string message) { if (!ok) throw new TestFailure("EVALUATION_ASSERTION_FAILED", message); }
    private static void Near(double actual, double expected, double tolerance = 1e-6) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"Native {actual} differs from expected {expected}.");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path) => Hash(File.ReadAllBytes(path));
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    private static string Output(string root) => Path.Combine(root, "artifacts/milestone9d");
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[1] == "--prepare") return Prepare(args[0]);
            if (args.Length is 3 or 4 && args[1] == "--live") return Live(args[0], args[2], args.Length == 4 ? args[3] : null);
            if (args.Length == 2 && args[1] == "--matrix") return Matrix(args[0]);
            Console.WriteLine("Usage: <workspace> --prepare | --live <case> [template] | --matrix"); return 2;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
    private static Dictionary<string, string> SourceHashes(string root, string directory) => Directory.GetFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories)
        .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
        .Where(p => Path.GetExtension(p) is ".cs" or ".csproj" or ".json")
        .OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(root, p), HashFile, StringComparer.Ordinal);
    private static void VerifyFreeze(string root)
    {
        var freeze = JsonSerializer.Deserialize<Freeze>(File.ReadAllText(Path.Combine(Output(root), "freeze.json")))!;
        Check(freeze.Production.SequenceEqual(SourceHashes(root, "src")), "Production code changed after M9D freeze.");
        Check(freeze.CaseDefinitions == HashFile(Path.Combine(root, "tests/CadHarness.Generalization.Tests/CaseData.cs")), "Frozen held-out/case definitions changed.");
    }
    private static string Envelope(CadProgram? program) => program is null ? JsonSerializer.Serialize(new { outcome = "unsupported", program = (object?)null,
        reason = "The executable catalog has no turbine blade optimization, freeform airfoil or internal cooling passage operations." }) :
        JsonSerializer.Serialize(new { outcome = "planned", program = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(program)), reason = "" });
    private static PlanningResult Plan(string intent, CadProgram? program, IPlanningRuntime runtime) =>
        new CadPlanner(runtime, new FixturePlanSource(new Dictionary<string, string> { [intent] = Envelope(program) })).PlanAsync(intent).GetAwaiter().GetResult();
    private static int Prepare(string root)
    {
        var output = Output(root); Directory.CreateDirectory(output);
        foreach (var m in new[] { "9a", "9b", "9c" }) Check(File.ReadAllText(Path.Combine(root, $"docs/milestone-{m}-verification.md")).Contains("**COMPLETE**", StringComparison.Ordinal), "Prerequisite " + m + " is not COMPLETE.");
        var freezePath = Path.Combine(output, "freeze.json");
        if (!File.Exists(freezePath)) Write(freezePath, new Freeze(SourceHashes(root, "src"), HashFile(Path.Combine(root, "tests/CadHarness.Generalization.Tests/CaseData.cs")),
            SourceHashes(root, "tests").Where(p => !p.Key.Contains("Generalization.Tests", StringComparison.Ordinal)).ToDictionary(p => p.Key, p => p.Value)));
        VerifyFreeze(root);
        var runtime = SolidWorksPlanningRuntime.ForConstruction();
        Check(runtime.Capabilities.Registry.Contracts.Select(c => c.Kind).ToHashSet().SetEquals(new FeatureBackendRegistry().SupportedKinds), "Construction catalog/native registry differ.");
        Check(runtime.Capabilities.Profiles.ToHashSet().SetEquals(CreateExtrudeHandler.SupportedProfiles), "Profile catalog/native support differ.");
        Check(runtime.Capabilities.Relations.ToHashSet().SetEquals(new DesignRelationEngine().SupportedKinds), "Relation catalog/handlers differ.");
        Check(!runtime.Capabilities.Registry.TryGet(OperationKind.EditParameter, out _) && runtime.Capabilities.ParameterEdits.Count == 0, "Construction advertises edit operations.");
        Write(Path.Combine(output, "cases.json"), CaseData.All());
        File.WriteAllText(Path.Combine(output, "construction-capabilities.json"), runtime.Capabilities.ToPromptJson());
        File.WriteAllText(Path.Combine(output, "planner-schema.json"), PlannerResponseSchema.Create(runtime.Capabilities));
        var plans = new Dictionary<string, PlanningResult>();
        foreach (var test in CaseData.All())
        {
            var result = Plan(test.Intent, test.Program, runtime); plans[test.Name] = result;
            var expected = test.Outcome switch { "unsupported" => result.Status == PlanningStatus.Unsupported,
                "preflight_failure" => !result.Succeeded && result.Status is PlanningStatus.Rejected or PlanningStatus.Unsupported,
                _ => result.Succeeded };
            Check(expected, test.Name + " planning/preflight differs: " + result.FailureCode + ": " + result.Message);
            Console.WriteLine(test.Name + ": " + result.Status + " " + result.FailureCode);
        }
        Write(Path.Combine(output, "plans.json"), plans);
        Write(Path.Combine(output, "prepare-result.json"), new { Status = "COMPLETE", Cases = plans.Count, CapabilityProjectionConsistent = true,
            ProductionUnchanged = true, PlanningSource = "deterministic fixture IR responses through production CadPlanner; zero external model calls", NativeBudget = 10, MaximumConcurrentParts = 1,
            HeldOut = "Frozen before the first native M9D run; new 137x91x13 mixed through/blind composition, later through diameter7->9. Existing test corpus hashes captured in freeze.json." });
        return 0;
    }
    private static int Live(string root, string name, string? template)
    {
        VerifyFreeze(root);
        var test = CaseData.All().Single(c => c.Name == name); var output = Path.Combine(Output(root), name); Directory.CreateDirectory(output);
        var resultPath = Path.Combine(output, "result.json"); Check(!File.Exists(resultPath), "Case already attempted; no automatic native rerun.");
        var report = new CaseReport { Case = name, Status = "RUNNING" };
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM9DTest_";
        using var budget = new NativeTestBudget(Path.Combine(Output(root), "native-budget.json"), "M9D", 10);
        SolidWorksConnection? connection = null; TestPartScope? part = null;
        try
        {
            report.Plan = Plan(test.Intent, test.Program, SolidWorksPlanningRuntime.ForConstruction());
            report.Planned = report.Plan.Succeeded ? "PASS" : "EXPECTED_REJECTED";
            connection = SolidWorksConnection.Connect(); report.SolidWorksRevision = connection.Application.RevisionNumber();
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget);
            var refusal = NativeResourceGuard.Evaluate(report.ResourceGuard.Responding, report.ResourceGuard.GdiCount, report.ResourceGuard.OpenTestOwnedParts);
            if (refusal is not null) throw new TestFailure(refusal, "Native resource guard refused case creation.");
            part = new(connection.Application, budget); var context = part.Create(connection, connection.ResolvePartTemplate(template));
            var path = Path.Combine(output, "state.json"); var store = new AtomicStateStore(path); store.Commit(context.CaptureConstructionState());
            var backend = new RelationBackend();
            if (test.Outcome != "supported")
            {
                var before = Observe(context); var bytes = File.ReadAllBytes(path); var session = JsonSerializer.Serialize(context.CaptureConstructionState());
                if (test.Program is null)
                {
                    Check(report.Plan.Status == PlanningStatus.Unsupported && report.Plan.Program is null, "Unsupported intent yielded a program.");
                    report.Failures.Add(Evidence("unsupported planner; backend not called", report.Plan, before, bytes, session, context, path));
                    report.Executed = "EXPECTED_NO_MUTATION";
                }
                else
                {
                    report.Construction = backend.Create(context, report.Plan.Program ?? test.Program, store);
                    var mutated = test.Outcome == "native_failure";
                    Check(!report.Construction.Succeeded && report.Construction.MutationStarted == mutated && report.Construction.RollbackAttempted == mutated &&
                        report.Construction.RollbackSucceeded == mutated && !report.Construction.StateCommitted, "Negative failure flags differ: " + report.Construction.Message);
                    Check(report.Construction.FailureCode == (mutated ? "GEOMETRY_IMPOSSIBLE" : FailureCodes.PreconditionFailed), "Negative error code differs.");
                    if (mutated) Check(report.Construction.Operations.Count == 3 && report.Construction.Operations.Take(2).All(o => o.Succeeded), "Impossible fillet did not follow real modifications.");
                    report.Failures.Add(Evidence("negative construction", report.Construction, before, bytes, session, context, path));
                    report.Executed = mutated ? "EXPECTED_FAILED_ROLLED_BACK" : "EXPECTED_NO_MUTATION";
                }
                Check(report.Failures.All(f => f.ModelUnchanged && f.FileUnchanged && f.SessionUnchanged), "Negative case changed baseline.");
                report.Validated = "PASS_UNCHANGED"; report.RollbackSafe = test.Outcome == "native_failure" ? "PASS" : "PASS_NO_MUTATION";
                report.Status = "COMPLETE";
            }
            else
            {
                Check(report.Plan.Succeeded, "Supported plan rejected.");
                var constructionBefore = Observe(context); var constructionBytes = File.ReadAllBytes(path);
                var constructionSession = JsonSerializer.Serialize(context.CaptureConstructionState());
                report.Construction = backend.Create(context, report.Plan.Program!, store);
                if (!report.Construction.Succeeded)
                {
                    report.Failures.Add(Evidence("supported construction failed", report.Construction, constructionBefore, constructionBytes, constructionSession, context, path));
                    report.Status = "BLOCKED_CAPABILITY"; report.Executed = "NATIVE_OPERATIONS_THEN_ROLLBACK";
                    report.Validated = "BLOCKED_CAPABILITY"; report.Editable = "BLOCKED_CAPABILITY";
                    report.RollbackSafe = report.Construction.RollbackSucceeded && report.Failures.All(f => f.ModelUnchanged && f.FileUnchanged && f.SessionUnchanged) ? "PASS" : "FAILED";
                    report.ProductionCodeChangesRequired = "REQUIRED_FOR_CONSTRUCTION";
                    report.FailureCode = report.Construction.FailureCode; report.Message = report.Construction.Message;
                    report.CapabilityProjectionConsistent = false; return 0;
                }
                report.Executed = "PASS";
                var expected = test.Expected!; var geometry = Observe(context); VerifyModel(geometry, expected); report.Measurements.Add(geometry);
                VerifyState(context, store.Load(), report.Plan.Program!); report.StatePersistedAndRebound = true; report.Validated = "PASS";
                File.Copy(path, Path.Combine(output, "initial-state.json"), false);
                AuditProjection(context, store, output, report);
                if (name == "G2")
                {
                    var resized = ResizeThickness(expected, 10);
                    if (EvaluateEdit(context, store, output, report, CaseData.Edit("stock", EditableParameter.ExtrusionDepth, 10), resized)) expected = resized;
                    resized = ResizeThroughHoles(expected, 8);
                    if (EvaluateEdit(context, store, output, report, CaseData.Edit("seed", EditableParameter.HoleDiameter, 8), resized)) expected = resized;
                }
                else
                {
                    var diameter = name == "HeldOut" ? 9 : expected.Holes.First(h => h.Diameter < 20).Diameter + 1;
                    var changed = ResizeThroughHoles(expected, diameter);
                    var worked = EvaluateEdit(context, store, output, report, CaseData.Edit("seed", EditableParameter.HoleDiameter, diameter), changed);
                    if (worked && name == "HeldOut") expected = changed;
                    else if (worked)
                    {
                        EvaluateEdit(context, store, output, report, CaseData.Edit("seed", EditableParameter.HoleDiameter, expected.Holes.First(h => h.Diameter < 20).Diameter), expected);
                    }
                }
                var before = Observe(context); var bytes = File.ReadAllBytes(path); var session = JsonSerializer.Serialize(context.CaptureConstructionState());
                var extra = new CadProgram("0.2", new[] { CaseData.Hole("probe", 3, expected.Disk ? 15 : 0, expected.Disk ? 0 : -20) },
                    new[] { new DesignRelation(RelationKind.HostedOn, "probe", "stock.top_face") });
                CompositionExecutionResult failed;
                using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) failed = backend.Create(context, extra, store);
                var evidence = Evidence("real atomic commit failure after appended native hole", failed, before, bytes, session, context, path); report.Failures.Add(evidence);
                Check(failed.FailureCode == "STATE_COMMIT_FAILED" && failed.MutationStarted && failed.RollbackAttempted && failed.RollbackSucceeded && !failed.StateCommitted &&
                    failed.Operations.Count == 1 && failed.Operations[0].Succeeded && evidence.ModelUnchanged && evidence.FileUnchanged && evidence.SessionUnchanged,
                    "Construction atomic rollback failed: " + failed.FailureCode + ": " + failed.Message + "; " + failed.Transaction?.RollbackFailureMessage);
                VerifyModel(Observe(context), expected); VerifyState(context, store.Load(), CurrentProgram(context, store.Load())); report.RollbackSafe = "PASS";
                Check(Directory.GetFiles(output, ".state.json.*.tmp").Length == 0, "State commit temporary file leaked.");
                report.Status = report.Editable == "BLOCKED_CAPABILITY" || !report.CapabilityProjectionConsistent ? "BLOCKED_CAPABILITY" : "COMPLETE";
                if (!report.CapabilityProjectionConsistent && report.ProductionCodeChangesRequired == "NONE") report.ProductionCodeChangesRequired = "REQUIRED_FOR_PROJECTION";
            }
        }
        catch (Exception e) { report.Status = "BLOCKED"; report.FailureCode = e is TestFailure tf ? tf.Code : "EVALUATION_FAILED"; report.Message = e.Message; }
        finally
        {
            if (part is not null)
            {
                part.Cleanup(); report.PartsCreated = part.Created ? 1 : 0; report.PartsClosed = part.Closed ? 1 : 0;
                report.OriginalActiveRestored = part.OriginalActiveRestored; report.CleanupError = part.CleanupError;
                if (part.CleanupError is not null) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            }
            if (connection is not null) try { connection.Dispose(); } catch (Exception e) { report.Status = "BLOCKED"; report.CleanupError = e.Message; }
            try { VerifyFreeze(root); report.ProductionUnchanged = true; } catch (Exception e) { report.Status = "BLOCKED"; report.Message = e.Message; }
            Write(resultPath, report);
            Console.WriteLine(JsonSerializer.Serialize(new { report.Case, report.Status, report.Planned, report.Executed, report.Validated, report.Editable, report.RollbackSafe,
                report.CapabilityProjectionConsistent, report.FailureCode, report.Message, report.PartsCreated, report.PartsClosed, report.CleanupError }, Json));
        }
        return report.Status == "BLOCKED" ? 1 : 0;
    }
    private static void AuditProjection(SolidWorksExecutionContext context, AtomicStateStore store, string output, CaseReport report)
    {
        var state = store.Load(); var runtime = SolidWorksPlanningRuntime.ForEdit(context, state);
        File.WriteAllText(Path.Combine(output, "edit-capabilities.json"), runtime.Capabilities.ToPromptJson());
        foreach (var cap in runtime.Capabilities.ParameterEdits)
        {
            var owner = CurrentProgram(context, state).Operations.Single(o => o.SemanticId == cap.Target);
            var registered = ParameterMutationRegistry.Default.TryGet(owner, cap.Parameter, out var handler); var readable = false; var executable = false;
            string? code = null; string? message = null;
            try
            {
                Check(registered, "Catalog offers missing handler.");
                var value = handler.Read(context, owner, cap.Parameter); Near(value, ParameterMutationRegistry.Default.Expected(owner, cap.Parameter)); readable = true;
                var edit = CaseData.Edit(cap.Target, cap.Parameter, value);
                var adapter = new TransactionalParameterBackend(context); var prepared = adapter.ResolveInputs(state, edit); adapter.Preflight(state, prepared); adapter.CaptureRollback(state, prepared); executable = true;
            }
            catch (Exception e) { code = e is StateException se ? se.Code : "CAPABILITY_AUDIT_FAILED"; message = e.Message; }
            report.EditProjection.Add(new(cap.Target, cap.Parameter, registered, readable, executable, code, message));
        }
        report.CapabilityProjectionConsistent &= report.EditProjection.All(p => p.Registered && p.NativeReadable && p.PreflightExecutable);
        // Every omitted bound pair is checked against the projected schema, even if IR has that enum.
        var hidden = state.Bindings.Where(b => !runtime.Capabilities.ParameterEdits.Any(p => p.Target == b.OwnerFeatureSemanticId && p.Parameter == b.Parameter)).ToArray();
        foreach (var binding in hidden)
        {
            var value = state.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value;
            Check(!runtime.Capabilities.Validate(new("0.2", new[] { CaseData.Edit(binding.OwnerFeatureSemanticId, binding.Parameter, value) }, Array.Empty<DesignRelation>())).IsValid, "Unsupported bound edit leaked into catalog.");
        }
        Write(Path.Combine(output, "hidden-edits.json"), hidden);
    }
    private static bool EvaluateEdit(SolidWorksExecutionContext context, AtomicStateStore store, string output, CaseReport report, OperationNode edit, ExpectedModel expected)
    {
        var runtime = SolidWorksPlanningRuntime.ForEdit(context, store.Load()); var plan = Plan("Edit " + edit.Input("target")!.References[0].SemanticId + " " + edit.Parameter<ParameterNameParameter>("parameter").Value,
            new("0.2", new[] { edit }, Array.Empty<DesignRelation>()), runtime);
        if (!plan.Succeeded)
        {
            report.Editable = "BLOCKED_CAPABILITY"; report.ProductionCodeChangesRequired = "REQUIRED_FOR_EDIT"; report.Message = plan.FailureCode + ": " + plan.Message; return false;
        }
        var adapter = new TransactionalParameterBackend(context); var result = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, adapter).Execute(plan.Program!.Operations.Single());
        report.Edits.Add(result); Write(Path.Combine(output, "edit-" + report.Edits.Count + ".json"), new { Plan = plan, Result = result, Reads = adapter.ValidationReads });
        if (!result.Succeeded)
        {
            report.Editable = "BLOCKED_CAPABILITY"; report.ProductionCodeChangesRequired = "REQUIRED_FOR_EDIT"; report.CapabilityProjectionConsistent = false;
            report.Message = result.FailureCode + ": " + result.Message;
            Check(!result.MutationStarted || result.RollbackSucceeded, "Failed edit could not restore model: " + result.RollbackFailureMessage); return false;
        }
        Check(result.StateCommitted, "Successful edit was not committed."); var geometry = Observe(context); VerifyModel(geometry, expected); report.Measurements.Add(geometry);
        VerifyState(context, store.Load(), CurrentProgram(context, store.Load()));
        File.Copy(Path.Combine(output, "state.json"), Path.Combine(output, "edited-state-" + report.Edits.Count + ".json"), false);
        if (report.Editable != "BLOCKED_CAPABILITY") report.Editable = "PASS"; return true;
    }
    private static CadProgram CurrentProgram(SolidWorksExecutionContext context, CadState state) =>
        new CadProgramJson().Parse(SolidWorksPlanningRuntime.ForEdit(context, state).ModelContextJson).Program!;
    private static ExpectedModel ResizeThickness(ExpectedModel m, double t) => m with { Thickness = t, Volume = m.Volume / m.Thickness * t, Holes = m.Holes.Select(h => h with { Top = t }).ToArray() };
    private static ExpectedModel ResizeThroughHoles(ExpectedModel m, double diameter)
    {
        var changed = m.Holes.Where(h => h.Bottom == 0 && h.Diameter < 20).ToArray();
        return m with { Volume = m.Volume - changed.Sum(h => Math.PI * (diameter * diameter - h.Diameter * h.Diameter) / 4 * m.Thickness),
            Holes = m.Holes.Select(h => h.Bottom == 0 && h.Diameter < 20 ? h with { Diameter = diameter } : h).ToArray() };
    }
    private static FailureEvidence Evidence(string name, object result, ModelSnapshot before, byte[] bytes, string session, SolidWorksExecutionContext context, string path)
    {
        var after = Observe(context); var final = File.ReadAllBytes(path);
        return new(name, result, before, after, Hash(bytes), Hash(final), Same(before, after), bytes.SequenceEqual(final), session == JsonSerializer.Serialize(context.CaptureConstructionState()));
    }
    private static ModelSnapshot Observe(SolidWorksExecutionContext context)
    {
        var doc = context.Document; var types = new List<string>(); var feature = (IFeature?)doc.FirstFeature();
        for (var i = 0; feature is not null && i < 256; i++, feature = (IFeature?)feature.GetNextFeature()) types.Add(feature.GetTypeName2());
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in new[] { "", doc.ConfigurationManager.ActiveConfiguration.Name })
        {
            var manager = (ICustomPropertyManager)doc.Extension.CustomPropertyManager[scope];
            if (manager.GetNames() is Array names) foreach (var name in names.Cast<string>())
            { manager.Get6(name, false, out var value, out _, out _, out _); properties[scope + ":" + name] = manager.GetType2(name) + ":" + value; }
        }
        var bodies = ((IPartDoc)doc).GetBodies2(0, false) is Array array ? array.Cast<object>().Cast<IBody2>().ToArray() : Array.Empty<IBody2>();
        var cylinders = bodies.SelectMany(b => ((Array)b.GetFaces()).Cast<object>().Cast<IFace2>()).Where(f => ((ISurface)f.GetSurface()).IsCylinder()).Select(f =>
        {
            var c = (double[])((ISurface)f.GetSurface()).CylinderParams; Near(Math.Abs(c[5]), 1);
            var levels = ((Array)f.GetEdges()).Cast<object>().Cast<IEdge>().Select(e => (ICurve)e.GetCurve()).Where(curve => curve.IsCircle())
                .Select(curve => ((double[])curve.CircleParams)[2] * 1000).OrderBy(z => z).ToArray();
            return new Cylinder(c[0] * 1000, c[1] * 1000, c[6] * 2000, levels);
        }).OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Diameter).ToArray();
        return new(doc.GetFeatureCount(), types.ToArray(), bodies.Length, bodies.Sum(b => b.GetFaceCount()), bodies.Sum(b => b.GetEdgeCount()),
            bodies.Sum(b => ((double[])b.GetMassProperties(1))[3] * 1e9), bodies.Length == 1 ? ExtrudeMeasurementReader.Read(context) : null, cylinders, properties, doc.Extension.NeedsRebuild2);
    }
    private static bool Same(ModelSnapshot a, ModelSnapshot b) => a.Features == b.Features && a.FeatureTypes.SequenceEqual(b.FeatureTypes) && a.Bodies == b.Bodies && a.Faces == b.Faces && a.Edges == b.Edges &&
        Math.Abs(a.Volume - b.Volume) < 0.001 && a.Properties.SequenceEqual(b.Properties) && a.NeedsRebuild == b.NeedsRebuild && a.Cylinders.Length == b.Cylinders.Length &&
        a.Cylinders.Zip(b.Cylinders).All(p => Math.Abs(p.First.X - p.Second.X) < 1e-6 && Math.Abs(p.First.Y - p.Second.Y) < 1e-6 && Math.Abs(p.First.Diameter - p.Second.Diameter) < 1e-6 &&
            p.First.Levels.Length == p.Second.Levels.Length && p.First.Levels.Zip(p.Second.Levels).All(z => Math.Abs(z.First - z.Second) < 1e-6)) &&
        (a.Extents == b.Extents || (a.Extents is { } x && b.Extents is { } y && Math.Abs(x.WidthMm - y.WidthMm) < 1e-6 && Math.Abs(x.HeightMm - y.HeightMm) < 1e-6 && Math.Abs(x.DepthMm - y.DepthMm) < 1e-6));
    private static void VerifyModel(ModelSnapshot model, ExpectedModel expected)
    {
        Check(model.Bodies == 1 && model.NeedsRebuild == 0 && model.Extents is not null, "Model body/rebuild invalid.");
        Near(model.Extents!.WidthMm, expected.Width); Near(model.Extents.HeightMm, expected.Height); Near(model.Extents.DepthMm, expected.Thickness); Near(model.Volume, expected.Volume, 0.001);
        var extra = (expected.Disk ? 1 : 0) + (expected.Fillet > 0 ? 4 : 0); Check(model.Cylinders.Length == expected.Holes.Count + extra, "Cylinder/hole count differs.");
        foreach (var h in expected.Holes)
        {
            var matches = model.Cylinders.Where(c => Math.Abs(c.X - h.X) < 1e-6 && Math.Abs(c.Y - h.Y) < 1e-6 && Math.Abs(c.Diameter - h.Diameter) < 1e-6).ToArray();
            Check(matches.Length == 1, $"Hole ({h.X},{h.Y}) diameter {h.Diameter} not uniquely present.");
            Check(matches[0].Levels.Length == 2, "Hole wall needs two circle boundaries."); Near(matches[0].Levels[0], h.Bottom); Near(matches[0].Levels[1], h.Top);
        }
        if (expected.Fillet > 0) Check(model.Cylinders.Count(c => Math.Abs(c.Diameter - 2 * expected.Fillet) < 1e-6) == 4, "Four outer native fillet cylinders missing.");
    }
    private static void VerifyState(SolidWorksExecutionContext context, CadState state, CadProgram program)
    {
        StateValidation.Validate(state); Check(state.Features.Count == program.Operations.Count && state.Revision > 0, "Managed feature/revision differs.");
        Check(StateRelationData.Relations(state).ToHashSet().SetEquals(program.Relations), "Persisted relations differ.");
        foreach (var entity in state.Entities)
        {
            var bound = new SemanticEntityBinder().Bind(state, new InputContract("evaluation", null, new[] { entity.Type }), new(entity.SemanticId, entity.Type, entity.OwnerFeatureSemanticId));
            if (entity.ReferenceHealth == ReferenceHealth.Healthy)
                Check(bound.Succeeded && PersistentReferenceAdapter.Resolve<object>(context, bound.Entity!.NativeReference).Health == ReferenceHealth.Healthy, "Healthy persistent reference cannot bind: " + entity.SemanticId);
            else Check(!bound.Succeeded, "Consumed entity remained bindable.");
        }
        foreach (var feature in state.Features) Check(PersistentReferenceAdapter.Resolve<IFeature>(context, feature.NativeReference).Health == ReferenceHealth.Healthy, "Feature ref failed.");
        foreach (var binding in state.Bindings)
        {
            var owner = program.Operations.Single(o => o.SemanticId == binding.OwnerFeatureSemanticId);
            Near(state.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value, ParameterMutationRegistry.Default.Expected(owner, binding.Parameter));
        }
    }
    private static int Matrix(string root)
    {
        VerifyFreeze(root); var output = Output(root); var reports = CaseData.All().Select(c =>
            JsonNode.Parse(File.ReadAllText(Path.Combine(output, c.Name, "result.json")))!.AsObject()).ToArray();
        Check(reports.All(r => r["PartsCreated"]!.GetValue<int>() == 1 && r["PartsClosed"]!.GetValue<int>() == 1 && r["OriginalActiveRestored"]!.GetValue<bool>() &&
            r["CleanupError"] is null && r["ProductionUnchanged"]!.GetValue<bool>()), "Lifecycle or production freeze failed.");
        foreach (var report in reports)
        {
            // Preserve raw case reports. The first G1 run used the initial
            // reporter, which did not populate final-validation/rollback columns
            // on early construction failure. Derive only facts present in its
            // production transaction result; do not invent native measurements.
            if (report["Construction"] is JsonObject construction && !construction["Succeeded"]!.GetValue<bool>() && construction["RollbackSucceeded"]!.GetValue<bool>() && report["Validated"]!.GetValue<string>() == "NOT_RUN")
            {
                Check(construction["MutationStarted"]!.GetValue<bool>() && construction["RollbackAttempted"]!.GetValue<bool>() && !construction["StateCommitted"]!.GetValue<bool>(),
                    "Incomplete production rollback flags.");
                var state = new AtomicStateStore(Path.Combine(output, report["Case"]!.GetValue<string>(), "state.json")).Load();
                Check(state.Features.Count == 0 && state.Entities.Count == 0 && state.Revision == 0, "Failed initial construction left committed state.");
                report["Executed"] = "NATIVE_OPERATIONS_THEN_ROLLBACK"; report["Validated"] = "BLOCKED_CAPABILITY"; report["Editable"] = "BLOCKED_CAPABILITY";
                report["RollbackSafe"] = "PASS_RUNTIME_VERIFIED";
            }
        }
        var budget = JsonSerializer.Deserialize<BudgetSnapshot>(File.ReadAllText(Path.Combine(output, "native-budget.json")))!;
        Check(budget.PartsCreated == 10 && budget.PartsClosed == 10 && budget.OpenTestOwnedTitles.Count == 0, "Case Part accounting differs.");
        Write(Path.Combine(output, "generalization-matrix.json"), new { Status = reports.All(r => r["Status"]!.GetValue<string>() == "COMPLETE") ? "COMPLETE" : "EVALUATION_COMPLETE_WITH_BLOCKERS",
            PlanningSource = "deterministic fixture IR through production planner; no model-backed text-generation claim", ProductionUnchanged = true,
            CapabilityProjectionConsistent = reports.All(r => r["CapabilityProjectionConsistent"]!.GetValue<bool>()), Budget = budget, Cases = reports });
        var lines = new List<string> { "| Case | Planned | Executed | Validated | Editable | Rollback safe | Production-code changes required | Status |", "|---|---|---|---|---|---|---|---|" };
        lines.AddRange(reports.Select(r => "| " + string.Join(" | ", new[] { "Case", "Planned", "Executed", "Validated", "Editable", "RollbackSafe", "ProductionCodeChangesRequired", "Status" }.Select(key => r[key]!.GetValue<string>())) + " |"));
        File.WriteAllLines(Path.Combine(output, "generalization-matrix.md"), lines); Console.WriteLine(string.Join(System.Environment.NewLine, lines)); return 0;
    }
}
