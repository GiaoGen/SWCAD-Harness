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
using Environment = System.Environment;

[assembly: SupportedOSPlatform("windows")]
namespace CadHarness.Transactions.Tests;

internal sealed record HoleObservation(double XMm, double YMm, double DiameterMm);
internal sealed record GeometryObservation(int Bodies, double VolumeMm3, IReadOnlyList<HoleObservation> Holes);
internal sealed record TransactionObservation(string Case, MutationResult Result, IReadOnlyList<ValidationReadSet> Reads);
internal sealed class NativeReport
{
    public string Status { get; set; } = "BLOCKED";
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public string? SolidWorksRevision { get; set; }
    public ResourceSnapshot? ResourceGuard { get; set; }
    public CompositionExecutionResult? Creation { get; set; }
    public List<TransactionObservation> Transactions { get; set; } = new();
    public GeometryObservation? Before { get; set; }
    public GeometryObservation? After { get; set; }
    public bool DirtyOnlyVerified { get; set; }
    public bool InvalidEditStateUnchanged { get; set; }
    public bool PostMutationRollbackStateUnchanged { get; set; }
    public bool CommitFailureStateUnchanged { get; set; }
    public bool SessionUsableAfterRollback { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public string? CleanupError { get; set; }
    public BudgetSnapshot? MilestoneBudget { get; set; }
}

// Test-only postcondition failure, after actual native mutation/rebuild. This
// exercises the production rollback path without adding a fault switch to it.
internal sealed class FailingPostcondition : IMutationBackend<NativeEditPreparation, NativeEditRollback>
{
    private readonly TransactionalParameterBackend inner;
    internal FailingPostcondition(TransactionalParameterBackend inner) => this.inner = inner;
    public NativeEditPreparation ResolveInputs(CadState state, OperationNode operation) => inner.ResolveInputs(state, operation);
    public void Preflight(CadState state, NativeEditPreparation prepared) => inner.Preflight(state, prepared);
    public NativeEditRollback CaptureRollback(CadState state, NativeEditPreparation prepared) => inner.CaptureRollback(state, prepared);
    public ChangeSet Execute(NativeEditPreparation prepared) => inner.Execute(prepared);
    public bool Rebuild() => inner.Rebuild();
    public void ValidatePostconditions(NativeEditPreparation prepared)
    { inner.ValidatePostconditions(prepared); throw new StateException("TEST_REQUIRED_POSTCONDITION_FAILED", "Test-only required-postcondition rejection after native edit."); }
    public bool RecoveryAllowed => false;
    public bool Recover(NativeEditPreparation prepared) => false;
    public CadState ValidateFinal(CadState state, NativeEditPreparation prepared, ValidationScope scope) => inner.ValidateFinal(state, prepared, scope);
    public void StageState(NativeEditPreparation prepared, CadState validated) => inner.StageState(prepared, validated);
    public void Rollback(NativeEditRollback rollback) => inner.Rollback(rollback);
    public void ValidateRestored(CadState state, NativeEditRollback rollback, ValidationScope scope) => inner.ValidateRestored(state, rollback, scope);
    public void Invalidate() => inner.Invalidate();
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[1] == "--pure") return PureTests.Run(args[0]);
        if (args.Length is 2 or 3 && args[1] == "--live") return Live(args[0], args.Length == 3 ? args[2] : null);
        Console.WriteLine("Usage: CadHarness.Transactions.Tests <workspace> --pure | --live [part.prtdot]"); return 2;
    }
    private static int Live(string root, string? template)
    {
        var output = Path.Combine(root, "artifacts", "milestone6"); Directory.CreateDirectory(output);
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM6Test_";
        var report = new NativeReport(); NativeTestBudget? budget = null; SolidWorksConnection? connection = null; TestPartScope? part = null;
        try
        {
            var fixture = TestData.Fixture(root); var construction = new RelationBackend();
            var preflight = construction.Preflight(fixture);
            TestData.Check(preflight.IsValid, preflight.Message);
            budget = new(Path.Combine(output, "native-budget.json"), "Milestone 6", 3);
            if (budget.Snapshot.CreationAttempts >= 3) throw new TestFailure("ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED", "M6 Part budget exhausted; no Part created.");
            Console.WriteLine("M6 transaction verification: connecting; no Part created yet.");
            connection = SolidWorksConnection.Connect(); report.SolidWorksRevision = connection.Application.RevisionNumber();
            var partTemplate = connection.ResolvePartTemplate(template);
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget);
            var guard = report.ResourceGuard; Console.WriteLine(JsonSerializer.Serialize(guard));
            var limit = NativeResourceGuard.Evaluate(guard.Responding, guard.GdiCount, guard.OpenTestOwnedParts);
            if (limit is not null) throw new TestFailure(limit, "Resource guard refused Part creation.");
            part = new(connection.Application, budget); var context = part.Create(connection, partTemplate);
            report.Creation = construction.Create(context, fixture); Console.WriteLine(JsonSerializer.Serialize(report.Creation));
            TestData.Check(report.Creation.Succeeded, report.Creation.FailureCode + ": " + report.Creation.Message);
            var path = Path.Combine(output, "native-state.json"); var store = new AtomicStateStore(path);
            store.Commit(context.CaptureBindingState()); report.Before = Observe(context); Verify(report.Before, 40);
            var adapter = new TransactionalParameterBackend(context);
            var transaction = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, adapter);
            void Record(string name, MutationResult result)
            { report.Transactions.Add(new(name, result, adapter.ValidationReads.ToArray())); Console.WriteLine(name + ": " + JsonSerializer.Serialize(result)); }
            var success = transaction.Execute(TestData.Edit(50)); Record("successful targeted edit", success);
            TestData.Check(success.Succeeded && success.StateCommitted && success.Revision == 1, success.FailureCode + ": " + success.Message);
            Verify(Observe(context), 50);
            var snapshots = adapter.ValidationReads.ToArray();
            report.DirtyOnlyVerified = snapshots.Length == 2 && snapshots.All(r => !r.Scope.FullModel &&
                r.ParameterReads.SequenceEqual(new[] { "linear_holes.pattern_spacing" }) &&
                r.EntityReads.All(id => !id.StartsWith("hole_seed", StringComparison.Ordinal) && id != "holes") &&
                r.RelationReads.Count == 5 && r.RelationReads.All(r => r.Subject.StartsWith("linear_", StringComparison.Ordinal)));
            TestData.Check(report.DirtyOnlyVerified, "Ordinary edit scanned unchanged parameters/entities/relations.");
            var bytes = File.ReadAllBytes(path);
            var impossible = transaction.Execute(TestData.Edit(500)); Record("impossible layout rejected", impossible);
            report.InvalidEditStateUnchanged = !impossible.Succeeded && !impossible.MutationStarted && !impossible.RollbackAttempted &&
                !impossible.StateCommitted && File.ReadAllBytes(path).SequenceEqual(bytes);
            TestData.Check(report.InvalidEditStateUnchanged, "Impossible preflight mutated or committed state."); Verify(Observe(context), 50);
            var failure = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, new FailingPostcondition(adapter)).Execute(TestData.Edit(55));
            Record("post-mutation postcondition failure and rollback", failure);
            report.PostMutationRollbackStateUnchanged = failure.FailureCode == "TEST_REQUIRED_POSTCONDITION_FAILED" && failure.MutationStarted &&
                failure.RollbackAttempted && failure.RollbackSucceeded && !failure.StateCommitted && failure.Revision == 1 && File.ReadAllBytes(path).SequenceEqual(bytes);
            TestData.Check(report.PostMutationRollbackStateUnchanged, failure.FailureCode + ": " + failure.Message + " rollback=" + failure.RollbackFailureMessage);
            TestData.Check(adapter.ValidationReads.Last().Scope.Reasons == FullValidationReason.Rollback, "Restoration omitted full validation."); Verify(Observe(context), 50);
            MutationResult commitFailure;
            // Windows permits loading this locked snapshot, but denies atomic
            // replacement. This is an actual File.Replace failure, not a stub.
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                commitFailure = transaction.Execute(TestData.Edit(54));
            Record("atomic file replacement failure and rollback", commitFailure);
            report.CommitFailureStateUnchanged = commitFailure.FailureCode == "STATE_COMMIT_FAILED" && commitFailure.MutationStarted &&
                commitFailure.RollbackSucceeded && !commitFailure.StateCommitted && File.ReadAllBytes(path).SequenceEqual(bytes);
            TestData.Check(report.CommitFailureStateUnchanged, commitFailure.FailureCode + ": " + commitFailure.Message + " rollback=" + commitFailure.RollbackFailureMessage);
            Verify(Observe(context), 50);
            var final = transaction.Execute(TestData.Edit(52), FullValidationReason.ExplicitFullValidate); Record("explicit full validation after rollback", final);
            report.SessionUsableAfterRollback = final.Succeeded && final.StateCommitted && final.Revision == 2 && final.Validation!.FullModel;
            TestData.Check(report.SessionUsableAfterRollback, final.FailureCode + ": " + final.Message);
            report.After = Observe(context); Verify(report.After, 52);
            TestData.Check(store.Load().Revision == 2 && Directory.GetFiles(output, ".native-state.json.*.tmp").Length == 0, "Revision or atomic temporary cleanup differs.");
            report.Status = "COMPLETE"; report.Message = "Successful edit, impossible preflight, post-mutation rollback, actual atomic commit failure, dirty-only reads and explicit full validation passed.";
        }
        catch (TestFailure error) { report.FailureCode = error.Code; report.Message = error.Message; }
        catch (Exception error) { report.FailureCode = "NATIVE_TEST_FAILED"; report.Message = error.GetType().Name + ": " + error.Message; }
        finally
        {
            if (part is not null)
            {
                part.Cleanup(); report.PartsCreated = part.Created ? 1 : 0; report.PartsClosed = part.Closed ? 1 : 0;
                report.OriginalActiveRestored = part.OriginalActiveRestored; report.CleanupError = part.CleanupError;
                if (part.CleanupError is not null) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            }
            if (connection is not null)
                try { connection.Dispose(); }
                catch (Exception error) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; report.CleanupError = error.Message; }
            if (budget is not null) { report.MilestoneBudget = budget.Snapshot; budget.Dispose(); }
            var path = Path.Combine(output, "native-result.json");
            var attempts = File.Exists(path) ? JsonSerializer.Deserialize<List<NativeReport>>(File.ReadAllText(path))! : new();
            attempts.Add(report); File.WriteAllText(path, JsonSerializer.Serialize(attempts, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return report.Status == "COMPLETE" ? 0 : 1;
    }
    private static GeometryObservation Observe(SolidWorksExecutionContext context)
    {
        var bodies = (Array)((IPartDoc)context.Document).GetBodies2(0, false); TestData.Check(bodies.Length == 1, "Expected one solid body.");
        var body = (IBody2)bodies.GetValue(0)!;
        var holes = ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>().Select(f => (ISurface)f.GetSurface()).Where(s => s.IsCylinder()).Select(s =>
        { var c = (double[])s.CylinderParams; return new HoleObservation(c[0] * 1000, c[1] * 1000, c[6] * 2000); }).OrderBy(h => h.XMm).ThenBy(h => h.YMm).ToArray();
        return new(1, ((double[])body.GetMassProperties(1))[3] * 1e9, holes);
    }
    private static void Verify(GeometryObservation geometry, double spacing)
    {
        var linear = geometry.Holes.Where(h => Math.Abs(h.DiameterMm - 8) < 1e-6).ToArray();
        var rectangle = geometry.Holes.Where(h => Math.Abs(h.DiameterMm - 6) < 1e-6).ToArray();
        TestData.Check(linear.Length == 2 && rectangle.Length == 4 && geometry.Holes.Count == 6, "Hole count changed unexpectedly.");
        TestData.Check(Math.Abs(linear[0].XMm + spacing / 2) < 1e-6 && Math.Abs(linear[1].XMm - spacing / 2) < 1e-6 && linear.All(h => Math.Abs(h.YMm) < 1e-6), "Edited linear geometry differs.");
        var expected = new[] { (-30.0, -15.0), (-30.0, 15.0), (30.0, -15.0), (30.0, 15.0) };
        for (var i = 0; i < 4; i++) TestData.Check(Math.Abs(rectangle[i].XMm - expected[i].Item1) < 1e-6 && Math.Abs(rectangle[i].YMm - expected[i].Item2) < 1e-6, "Independent pattern moved.");
        var volume = 100 * 60 * 8 - (2 * Math.PI * 16 + 4 * Math.PI * 9) * 8;
        TestData.Check(Math.Abs(geometry.VolumeMm3 - volume) < 0.001, "Native volume differs after transaction.");
    }
}
