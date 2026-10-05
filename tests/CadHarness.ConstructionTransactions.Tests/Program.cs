using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

[assembly: SupportedOSPlatform("windows")]
namespace CadHarness.ConstructionTransactions.Tests;

internal sealed record CylinderObservation(double XMm, double YMm, double DiameterMm);
internal sealed record ModelObservation(int FeatureCount, IReadOnlyList<string> FeatureTypes, int BodyCount, int FaceCount, int EdgeCount,
    double VolumeMm3, ExtrudeMeasurement? Extents, IReadOnlyList<CylinderObservation> Cylinders, IReadOnlyDictionary<string, string> Properties, bool ActiveSketch, int NeedsRebuild);
internal sealed record StageObservation(string Name, CompositionExecutionResult Result, ModelObservation Before, ModelObservation After,
    bool ModelUnchanged, bool StateFileUnchanged, bool SessionStateUnchanged);
internal sealed class NativeReport
{
    public string Status { get; set; } = "BLOCKED";
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public string? SolidWorksRevision { get; set; }
    public ResourceSnapshot? ResourceGuard { get; set; }
    public List<StageObservation> Stages { get; set; } = new();
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public string? CleanupError { get; set; }
    public BudgetSnapshot? MilestoneBudget { get; set; }
}

// Injection decorates the shared production adapter; it supplies no rollback.
internal sealed class RejectValidatedConstruction : IRequestMutationBackend<CadProgram, ConstructionPreparation, ConstructionRollback>
{
    private readonly TransactionalConstructionBackend inner;
    internal RejectValidatedConstruction(TransactionalConstructionBackend inner) => this.inner = inner;
    public ConstructionPreparation ResolveInputs(CadState state, CadProgram request) => inner.ResolveInputs(state, request);
    public void Preflight(CadState state, ConstructionPreparation prepared) => inner.Preflight(state, prepared);
    public ConstructionRollback CaptureRollback(CadState state, ConstructionPreparation prepared) => inner.CaptureRollback(state, prepared);
    public ChangeSet Execute(ConstructionPreparation prepared) => inner.Execute(prepared);
    public bool Rebuild() => inner.Rebuild();
    public void ValidatePostconditions(ConstructionPreparation prepared)
    { inner.ValidatePostconditions(prepared); throw new StateException("TEST_POSTCONDITION_FAILED", "Test rejection after complete native construction validation."); }
    public bool RecoveryAllowed => false;
    public bool Recover(ConstructionPreparation prepared) => false;
    public CadState ValidateFinal(CadState state, ConstructionPreparation prepared, ValidationScope scope) => inner.ValidateFinal(state, prepared, scope);
    public void StageState(ConstructionPreparation prepared, CadState validated) => inner.StageState(prepared, validated);
    public void Rollback(ConstructionRollback rollback) => inner.Rollback(rollback);
    public void ValidateRestored(CadState state, ConstructionRollback rollback, ValidationScope scope) => inner.ValidateRestored(state, rollback, scope);
    public void Invalidate() => inner.Invalidate();
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[1] == "--pure") return PureTests.Run(args[0]);
        if (args.Length is 2 or 3 && args[1] == "--live") return Live(args[0], args.Length == 3 ? args[2] : null);
        if (args.Length is 2 or 3 && args[1] == "--live-topology") return Live(args[0], args.Length == 3 ? args[2] : null, true);
        Console.WriteLine("Usage: CadHarness.ConstructionTransactions.Tests <workspace> --pure | --live [part.prtdot]"); return 2;
    }
    private static int Live(string root, string? template, bool topology = false)
    {
        var output = Path.Combine(root, "artifacts/milestone9c"); Directory.CreateDirectory(output);
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM9CTest_";
        var report = new NativeReport(); NativeTestBudget? budget = null; SolidWorksConnection? connection = null; TestPartScope? part = null;
        try
        {
            var backend = new RelationBackend();
            TestData.Check(backend.Preflight(TestData.ImpossibleFresh()).IsValid && !backend.Preflight(TestData.InvalidPattern()).IsValid && !backend.Preflight(TestData.InvalidPlacement()).IsValid, "Pure construction preflight differs.");
            budget = new(Path.Combine(output, "native-budget.json"), "M9C", 2);
            if (budget.Snapshot.CreationAttempts >= 2) throw new TestFailure("ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED", "M9C native Part budget exhausted.");
            Console.WriteLine("M9C: connecting; no Part created yet.");
            connection = SolidWorksConnection.Connect(); report.SolidWorksRevision = connection.Application.RevisionNumber();
            var partTemplate = connection.ResolvePartTemplate(template);
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget); Console.WriteLine(JsonSerializer.Serialize(report.ResourceGuard));
            var guard = report.ResourceGuard; var refusal = NativeResourceGuard.Evaluate(guard.Responding, guard.GdiCount, guard.OpenTestOwnedParts);
            if (refusal is not null) throw new TestFailure(refusal, "Native resource guard refused creation.");
            part = new(connection.Application, budget); var context = part.Create(connection, partTemplate);
            // A test-owned original property must survive rollback. Identity
            // properties are deliberately absent from the pristine baseline.
            ((ICustomPropertyManager)context.Document.Extension.CustomPropertyManager[""]).Add3("M9C-original", 30, "preserve me", 0);
            var path = Path.Combine(output, topology ? "topology-state.json" : "native-state.json"); var store = new AtomicStateStore(path); store.Commit(context.CaptureConstructionState());
            if (!topology) File.WriteAllText(Path.Combine(output, "empty-state.json"), File.ReadAllText(path));
            CompositionExecutionResult CheckFailure(string name, Func<CompositionExecutionResult> execute, bool mutated, string? expectedCode = null)
            {
                var before = Observe(context); var stateBytes = File.ReadAllBytes(path); var sessionBefore = JsonSerializer.Serialize(context.CaptureConstructionState());
                var result = execute(); var after = Observe(context);
                var sameModel = Same(before, after); var sameFile = stateBytes.SequenceEqual(File.ReadAllBytes(path));
                var sameSession = sessionBefore == JsonSerializer.Serialize(context.CaptureConstructionState());
                report.Stages.Add(new(name, result, before, after, sameModel, sameFile, sameSession));
                Console.WriteLine(name + ": " + JsonSerializer.Serialize(result));
                TestData.Check(!result.Succeeded && result.MutationStarted == mutated && result.RollbackAttempted == mutated && result.RollbackSucceeded == mutated &&
                    !result.StateCommitted && sameModel && sameFile && sameSession && (expectedCode is null || result.FailureCode == expectedCode),
                    result.FailureCode + ": " + result.Message + "; rollback=" + result.Transaction?.RollbackFailureMessage + $"; model={sameModel}, file={sameFile}, session={sameSession}");
                VerifyBindings(context, store.Load()); return result;
            }
            if (topology)
            {
                var baseResult = backend.Create(context, TestData.Base(), store); TestData.Check(baseResult.Succeeded, baseResult.Message);
                var beforeRound = Observe(context);
                var round = TestData.Fillet() with { Id = "round", SemanticId = "rounded_corners", Parameters = new Dictionary<string, OperationParameter> { ["radiusMm"] = new LengthParameter(1) } };
                var roundResult = backend.Create(context, new("0.2", new[] { round }, Array.Empty<DesignRelation>()), store);
                var rounded = Observe(context);
                report.Stages.Add(new("valid fillet commits intentional consumed-edge health", roundResult, beforeRound, rounded, false, false, false));
                TestData.Check(roundResult.Succeeded && roundResult.StateCommitted && store.Load().Revision == 2 &&
                    store.Load().Entities.Any(e => e.Type == SemanticType.LinearEdge && e.ReferenceHealth != ReferenceHealth.Healthy) &&
                    rounded.BodyCount == 1 && rounded.Cylinders.Count == 5 && Math.Abs(rounded.VolumeMm3 - (80 * 50 - Math.PI * 9 - (4 - Math.PI)) * 8) < 0.001,
                    roundResult.Message + "; rollback=" + roundResult.Transaction?.RollbackFailureMessage);
                VerifyBindings(context, store.Load());
                File.WriteAllText(Path.Combine(output, "original-topology-state.json"), File.ReadAllText(path));
                CheckFailure("consumed original edge rejected before append mutation", () => backend.Create(context,
                    new("0.2", new[] { TestData.Fillet() }, Array.Empty<DesignRelation>()), store), false);
                CheckFailure("complete added-hole construction rolls back on rounded original model", () =>
                {
                    var adapter = new TransactionalConstructionBackend(context);
                    var result = new RequestMutationTransaction<CadProgram, ConstructionPreparation, ConstructionRollback>(store, new RejectValidatedConstruction(adapter)).Execute(TestData.Extension(false));
                    return new(result.Succeeded, result.FailureCode, result.Message, adapter.Operations) { Transaction = result };
                }, true, "TEST_POSTCONDITION_FAILED");
                File.WriteAllText(Path.Combine(output, "restored-topology-state.json"), File.ReadAllText(path));
                report.Status = "COMPLETE"; report.Message = "Successful fillet, consumed-edge preflight rejection, rounded-model topology/persistent-reference/state rollback verified.";
            }
            else
            {
            CheckFailure("invalid pattern rejected before all mutation", () => backend.Create(context, TestData.InvalidPattern(), store), false);
            CheckFailure("invalid placement rejected before all mutation", () => backend.Create(context, TestData.InvalidPlacement(), store), false);
            var freshFailure = CheckFailure("fresh program creates plate/hole then impossible fillet; restores pristine model/state", () => backend.Create(context, TestData.ImpossibleFresh(), store), true);
            TestData.Check(freshFailure.Operations.Count == 3 && freshFailure.Operations.Take(2).All(o => o.Succeeded) && !freshFailure.Operations[2].Succeeded,
                "Impossible fillet did not follow two real successful native operations.");
            var created = backend.Create(context, TestData.Base(), store);
            Console.WriteLine("valid baseline construction: " + JsonSerializer.Serialize(created));
            TestData.Check(created.Succeeded && created.StateCommitted && created.MutationStarted && !created.RollbackAttempted && store.Load().Revision == 1, created.Message + "; rollback=" + created.Transaction?.RollbackFailureMessage);
            var baseline = Observe(context); VerifyGeometry(baseline, 1); VerifyBindings(context, store.Load());
            File.WriteAllText(Path.Combine(output, "original-model-state.json"), File.ReadAllText(path));
            var originalBytes = File.ReadAllBytes(path);
            var extensionFailure = CheckFailure("existing model: added hole succeeds then impossible fillet; restores original model/state", () => backend.Create(context, TestData.Extension(true), store), true);
            TestData.Check(extensionFailure.Operations.Count == 2 && extensionFailure.Operations[0].Succeeded && !extensionFailure.Operations[1].Succeeded, "No real partial extension mutation.");
            CheckFailure("existing model: invalid append placement rejected before mutation", () => backend.Create(context,
                new("0.2", new[] { TestData.Hole("added_hole", 4, new(100, 0)) }, Array.Empty<DesignRelation>()), store), false);
            CheckFailure("complete native construction then required postcondition failure", () =>
            {
                var adapter = new TransactionalConstructionBackend(context);
                var result = new RequestMutationTransaction<CadProgram, ConstructionPreparation, ConstructionRollback>(store, new RejectValidatedConstruction(adapter)).Execute(TestData.Extension(false));
                return new(result.Succeeded, result.FailureCode, result.Message, adapter.Operations) { Transaction = result };
            }, true, "TEST_POSTCONDITION_FAILED");
            CheckFailure("real atomic state commit failure restores added topology and staged session", () =>
            {
                using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                return backend.Create(context, TestData.Extension(false), store);
            }, true, "STATE_COMMIT_FAILED");
            TestData.Check(originalBytes.SequenceEqual(File.ReadAllBytes(path)) && Same(baseline, Observe(context)), "Original model/state changed across rollback cases.");
            File.WriteAllText(Path.Combine(output, "restored-model-state.json"), File.ReadAllText(path));
            var extension = backend.Create(context, TestData.Extension(false), store); Console.WriteLine("valid append after rollback: " + JsonSerializer.Serialize(extension));
            TestData.Check(extension.Succeeded && extension.StateCommitted && store.Load().Revision == 2, extension.Message + "; rollback=" + extension.Transaction?.RollbackFailureMessage);
            VerifyGeometry(Observe(context), 2); VerifyBindings(context, store.Load());
            TestData.Check(extension.Transaction!.Validation is { FullModel: true } && extension.Transaction.Validation.Entities.Count == store.Load().Entities.Count &&
                Directory.GetFiles(output, ".native-state.json.*.tmp").Length == 0, "Final construction scope/atomic cleanup incomplete.");
            report.Status = "COMPLETE"; report.Message = "Preflight rejection, real impossible fillet rollback on pristine/existing models, postcondition/atomic-commit rollback and reusable session verified.";
            }
        }
        catch (TestFailure e) { report.FailureCode = e.Code; report.Message = e.Message; }
        catch (Exception e) { report.FailureCode = "NATIVE_TEST_FAILED"; report.Message = e.GetType().Name + ": " + e.Message; }
        finally
        {
            if (part is not null)
            {
                part.Cleanup(); report.PartsCreated = part.Created ? 1 : 0; report.PartsClosed = part.Closed ? 1 : 0;
                report.OriginalActiveRestored = part.OriginalActiveRestored; report.CleanupError = part.CleanupError;
                if (report.CleanupError is not null) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            }
            if (connection is not null) try { connection.Dispose(); } catch (Exception e) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; report.CleanupError = e.Message; }
            if (budget is not null) { report.MilestoneBudget = budget.Snapshot; budget.Dispose(); }
            var path = Path.Combine(output, "native-result.json"); var reports = File.Exists(path) ? JsonSerializer.Deserialize<List<NativeReport>>(File.ReadAllText(path))! : new();
            reports.Add(report); File.WriteAllText(path, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { report.Status, report.FailureCode, report.Message, report.PartsCreated, report.PartsClosed, report.OriginalActiveRestored, report.CleanupError, Stages = report.Stages.Count }));
        }
        return report.Status == "COMPLETE" ? 0 : 1;
    }
    private static void VerifyBindings(SolidWorksExecutionContext context, CadState state)
    {
        foreach (var entity in state.Entities.Where(e => e.ReferenceHealth == ReferenceHealth.Healthy))
        {
            var bound = new SemanticEntityBinder().Bind(state, new InputContract("test", null, new[] { entity.Type }), new(entity.SemanticId, entity.Type, entity.OwnerFeatureSemanticId));
            TestData.Check(bound.Succeeded && PersistentReferenceAdapter.Resolve<object>(context, bound.Entity!.NativeReference).Health == ReferenceHealth.Healthy, "Restored persisted binding failed: " + entity.SemanticId);
        }
    }
    private static ModelObservation Observe(SolidWorksExecutionContext context)
    {
        var doc = context.Document; var types = new List<string>(); var feature = (IFeature?)doc.FirstFeature();
        for (var i = 0; feature is not null && i < 256; i++, feature = (IFeature?)feature.GetNextFeature()) types.Add(feature.GetTypeName2());
        var props = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in new[] { "", doc.ConfigurationManager.ActiveConfiguration.Name })
        {
            var manager = (ICustomPropertyManager)doc.Extension.CustomPropertyManager[scope];
            if (manager.GetNames() is Array names) foreach (var name in names.Cast<string>())
            { manager.Get6(name, false, out var value, out _, out _, out _); props[scope + ":" + name] = manager.GetType2(name) + ":" + value; }
        }
        var bodies = ((IPartDoc)doc).GetBodies2(0, false) is Array array ? array.Cast<object>().Cast<IBody2>().ToArray() : Array.Empty<IBody2>();
        var cylinders = bodies.SelectMany(body => ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>()).Where(f => ((ISurface)f.GetSurface()).IsCylinder()).Select(f =>
        { var c = (double[])((ISurface)f.GetSurface()).CylinderParams; return new CylinderObservation(c[0] * 1000, c[1] * 1000, c[6] * 2000); }).OrderBy(c => c.XMm).ThenBy(c => c.YMm).ToArray();
        return new(doc.GetFeatureCount(), types, bodies.Length, bodies.Sum(b => b.GetFaceCount()), bodies.Sum(b => b.GetEdgeCount()),
            bodies.Sum(b => ((double[])b.GetMassProperties(1))[3] * 1e9), bodies.Length == 1 ? ExtrudeMeasurementReader.Read(context) : null,
            cylinders, props, doc.GetActiveSketch2() is not null, doc.Extension.NeedsRebuild2);
    }
    private static bool Same(ModelObservation a, ModelObservation b)
    {
        bool Near(double x, double y) => Math.Abs(x - y) <= 1e-6;
        return a.FeatureCount == b.FeatureCount && a.FeatureTypes.SequenceEqual(b.FeatureTypes) && a.BodyCount == b.BodyCount && a.FaceCount == b.FaceCount && a.EdgeCount == b.EdgeCount &&
            Math.Abs(a.VolumeMm3 - b.VolumeMm3) <= 0.001 && a.Properties.SequenceEqual(b.Properties) && a.ActiveSketch == b.ActiveSketch && a.NeedsRebuild == b.NeedsRebuild &&
            (a.Extents == b.Extents || (a.Extents is { } x && b.Extents is { } y && Near(x.WidthMm, y.WidthMm) && Near(x.HeightMm, y.HeightMm) && Near(x.DepthMm, y.DepthMm))) &&
            a.Cylinders.Count == b.Cylinders.Count && a.Cylinders.Zip(b.Cylinders).All(pair => Near(pair.First.XMm, pair.Second.XMm) && Near(pair.First.YMm, pair.Second.YMm) && Near(pair.First.DiameterMm, pair.Second.DiameterMm));
    }
    private static void VerifyGeometry(ModelObservation model, int holes)
    {
        TestData.Check(model.BodyCount == 1 && model.Cylinders.Count == holes && model.Extents is not null &&
            Math.Abs(model.Extents.WidthMm - 80) < 1e-6 && Math.Abs(model.Extents.HeightMm - 50) < 1e-6 && Math.Abs(model.Extents.DepthMm - 8) < 1e-6 &&
            Math.Abs(model.VolumeMm3 - (80 * 50 - Math.PI * (9 + (holes == 2 ? 4 : 0))) * 8) < 0.001, "Independent native geometry differs.");
        TestData.Check(model.Cylinders.Any(c => Math.Abs(c.XMm) < 1e-6 && Math.Abs(c.YMm) < 1e-6 && Math.Abs(c.DiameterMm - 6) < 1e-6), "Original hole changed.");
        if (holes == 2) TestData.Check(model.Cylinders.Any(c => Math.Abs(c.XMm - 20) < 1e-6 && Math.Abs(c.YMm - 10) < 1e-6 && Math.Abs(c.DiameterMm - 4) < 1e-6), "Appended hole differs.");
    }
}
