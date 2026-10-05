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
namespace CadHarness.CircularGeometry.Tests;

internal sealed record CylinderObservation(double XMm, double YMm, double DiameterMm, IReadOnlyList<double> LevelsMm);
internal sealed record GeometryObservation(ExtrudeMeasurement Extents, double VolumeMm3, IReadOnlyList<CylinderObservation> Cylinders);
internal sealed record StageObservation(string Name, MutationResult? Transaction, GeometryObservation Geometry, IReadOnlyList<ValidationReadSet> Reads);
internal sealed class NativeReport
{
    public string Status { get; set; } = "BLOCKED";
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public string? SolidWorksRevision { get; set; }
    public ResourceSnapshot? ResourceGuard { get; set; }
    public CompositionExecutionResult? Creation { get; set; }
    public GeometryObservation? G4 { get; set; }
    public List<StageObservation> Stages { get; set; } = new();
    public bool PersistentReferencesRebound { get; set; }
    public bool RealNativeRotationAxisVerified { get; set; }
    public bool AtomicStateRoundTripVerified { get; set; }
    public bool FinalG4Verified { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public string? CleanupError { get; set; }
    public BudgetSnapshot? MilestoneBudget { get; set; }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[1] == "--pure") return PureTests.Run(args[0]);
        if (args.Length is 2 or 3 && args[1] == "--live") return Live(args[0], args.Length == 3 ? args[2] : null);
        Console.WriteLine("Usage: CadHarness.CircularGeometry.Tests <workspace> --pure | --live [part.prtdot]"); return 2;
    }
    private static int Live(string root, string? template)
    {
        var output = Path.Combine(root, "artifacts/milestone9b"); Directory.CreateDirectory(output);
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM9BTest_";
        var report = new NativeReport(); NativeTestBudget? budget = null; SolidWorksConnection? connection = null; TestPartScope? part = null;
        try
        {
            var fixture = TestData.Fixture(root); var backend = new RelationBackend();
            var preflight = backend.Preflight(fixture); TestData.Check(preflight.IsValid, preflight.Message);
            budget = new(Path.Combine(output, "native-budget.json"), "M9B", 2);
            if (budget.Snapshot.CreationAttempts >= 2) throw new TestFailure("ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED", "M9B native Part budget exhausted.");
            Console.WriteLine("M9B: connecting; no Part created yet.");
            connection = SolidWorksConnection.Connect(); report.SolidWorksRevision = connection.Application.RevisionNumber();
            var partTemplate = connection.ResolvePartTemplate(template);
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget);
            Console.WriteLine(JsonSerializer.Serialize(report.ResourceGuard));
            var guard = report.ResourceGuard; var refusal = NativeResourceGuard.Evaluate(guard.Responding, guard.GdiCount, guard.OpenTestOwnedParts);
            if (refusal is not null) throw new TestFailure(refusal, "Resource guard refused native test creation.");
            part = new(connection.Application, budget); var context = part.Create(connection, partTemplate);
            report.Creation = backend.Create(context, fixture); TestData.Check(report.Creation.Succeeded, report.Creation.FailureCode + ": " + report.Creation.Message);
            report.G4 = Observe(context); Verify(report.G4, 8, 6, 360);
            var path = Path.Combine(output, "native-state.json"); var store = new AtomicStateStore(path);
            var initial = context.CaptureBindingState(); store.Commit(initial);
            File.WriteAllText(Path.Combine(output, "initial-state.json"), File.ReadAllText(path));
            var roundTrip = store.Load(); VerifyReferences(context, roundTrip);
            report.PersistentReferencesRebound = true; report.AtomicStateRoundTripVerified = true;
            var axis = roundTrip.Entities.Single(e => e.SemanticId == "disk.rotational_reference");
            TestData.Check(axis.Type == SemanticType.CylindricalFace && axis.Geometry is { RadiusMm: not null, Direction: not null }, "Rotational geometry is absent.");
            TestData.Near(axis.Geometry!.RadiusMm!.Value, 50); TestData.Near(Math.Abs(axis.Geometry.Direction!.Z), 1);
            var axisFace = PersistentReferenceAdapter.Resolve<IFace2>(context, axis.NativeReference).NativeObject;
            TestData.Check(axisFace is not null && ((ISurface)axisFace.GetSurface()).IsCylinder(), "Axis is not native cylindrical topology.");
            VerifyDefinitionReferences(context, roundTrip, 6, 360); report.RealNativeRotationAxisVerified = true;
            foreach (var (id, expected) in new[] { ("disk.profile_diameter", 100.0), ("disk.extrusion_depth", 12.0), ("bolt_ring.pattern_count", 6.0), ("bolt_ring.pattern_angle", 360.0) })
                TestData.Near(roundTrip.Parameters.Single(p => p.SemanticId == id).Value, expected);
            var catalog = SolidWorksPlanningRuntime.ForEdit(context, roundTrip).Capabilities;
            TestData.Check(catalog.ParameterEdits.Count == 2 && catalog.ParameterEdits.All(e => e.Parameter == EditableParameter.HoleDiameter), "Projection overpromises unverified circular edits.");
            File.WriteAllText(Path.Combine(output, "native-edit-capabilities.json"), catalog.ToPromptJson());
            var adapter = new TransactionalParameterBackend(context); var transaction = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, adapter);
            void Record(string name, MutationResult? result, double diameter, int count = 6, double angle = 360)
            {
                var geometry = Observe(context); Verify(geometry, diameter, count, angle);
                report.Stages.Add(new(name, result, geometry, adapter.ValidationReads.ToArray()));
                Console.WriteLine(name + ": " + JsonSerializer.Serialize(result));
            }
            // Test-only external definition change establishes native partial-span
            // orientation/count and proves drift detection before any transaction.
            var committedBytes = File.ReadAllBytes(path);
            try
            {
                SetNativePattern(context, 4, 210);
                VerifyDefinitionReferences(context, roundTrip, 4, 210);
                var partial = context.CaptureBindingState();
                TestData.Near(partial.Parameters.Single(p => p.SemanticId == "bolt_ring.pattern_count").Value, 4);
                TestData.Near(partial.Parameters.Single(p => p.SemanticId == "bolt_ring.pattern_angle").Value, 210);
                var drift = transaction.Execute(TestData.Edit(8));
                TestData.Check(drift.FailureCode == "STATE_DRIFT_DETECTED" && !drift.MutationStarted && !drift.StateCommitted &&
                    File.ReadAllBytes(path).SequenceEqual(committedBytes) && adapter.ValidationReads.Any(r => r.Scope.FullModel), "Circular drift was not rejected/escalated.");
                Record("native partial span 4 instances / 210 degrees; drift rejected", drift, 8, 4, 210);
            }
            finally { SetNativePattern(context, 6, 360); }
            Verify(Observe(context), 8, 6, 360); VerifyReferences(context, store.Load());
            var enlarged = transaction.Execute(TestData.Edit(9));
            TestData.Check(enlarged.Succeeded && enlarged.StateCommitted && enlarged.Revision == 1 && enlarged.Validation is { FullModel: false } &&
                enlarged.Validation.Parameters.SequenceEqual(new[] { "bolt_seed.hole_diameter" }) && enlarged.Dirty!.Features.Contains("bolt_ring") &&
                enlarged.Dirty.Entities.Contains("disk.rotational_reference"), enlarged.FailureCode + ": " + enlarged.Message + "; rollback=" + enlarged.RollbackFailureMessage);
            Record("existing HoleDiameter handler propagates 8->9 to all six circular instances", enlarged, 9);
            var stable = File.ReadAllBytes(path); MutationResult commitFailure;
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) commitFailure = transaction.Execute(TestData.Edit(9.5));
            TestData.Check(commitFailure.FailureCode == "STATE_COMMIT_FAILED" && commitFailure.MutationStarted && commitFailure.RollbackSucceeded &&
                !commitFailure.StateCommitted && File.ReadAllBytes(path).SequenceEqual(stable), commitFailure.FailureCode + ": " + commitFailure.Message + "; rollback=" + commitFailure.RollbackFailureMessage);
            Record("atomic commit failure restores six circular holes and state", commitFailure, 9);
            var restored = transaction.Execute(TestData.Edit(8));
            TestData.Check(restored.Succeeded && restored.StateCommitted && restored.Revision == 2, restored.Message + "; rollback=" + restored.RollbackFailureMessage);
            Record("committed return to nominal G4 diameter", restored, 8);
            var final = store.Load(); VerifyReferences(context, final); VerifyDefinitionReferences(context, final, 6, 360);
            TestData.Check(final.Document.Matches(initial.Document) && final.Features.SequenceEqual(initial.Features) && final.Revision == 2 &&
                StateRelationData.Relations(final).SequenceEqual(StateRelationData.Relations(initial)) && final.Entities.All(e => e.ReferenceHealth == ReferenceHealth.Healthy), "Final identity/relations differ.");
            TestData.Near(final.Parameters.Single(p => p.SemanticId == "bolt_seed.hole_diameter").Value, 8);
            TestData.Check(Directory.GetFiles(output, ".native-state.json.*.tmp").Length == 0, "Atomic commit left temporary files.");
            report.FinalG4Verified = true; report.Status = "COMPLETE"; report.Message = "G4 generic construction, native cylinder axis, partial-span readback/drift, circular hole propagation and atomic rollback verified.";
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
            var path = Path.Combine(output, "native-result.json");
            var reports = File.Exists(path) ? JsonSerializer.Deserialize<List<NativeReport>>(File.ReadAllText(path))! : new();
            reports.Add(report); File.WriteAllText(path, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return report.Status == "COMPLETE" ? 0 : 1;
    }
    private static void VerifyReferences(SolidWorksExecutionContext context, CadState state)
    {
        foreach (var entity in state.Entities)
        {
            var slot = new InputContract("test", null, new[] { entity.Type });
            var binding = new SemanticEntityBinder().Bind(state, slot, new(entity.SemanticId, entity.Type, entity.OwnerFeatureSemanticId));
            TestData.Check(binding.Succeeded && PersistentReferenceAdapter.Resolve<object>(context, binding.Entity!.NativeReference).Health == ReferenceHealth.Healthy,
                "Persisted typed binding failed: " + entity.SemanticId);
        }
    }
    private static void VerifyDefinitionReferences(SolidWorksExecutionContext context, CadState state, int count, double angle)
    {
        var data = (ICircularPatternFeatureData)((IFeature)context.NativeFeature("bolt_ring")).GetDefinition();
        TestData.Check(data.TotalInstances == count && data.EqualSpacing && !data.VarySketch && !data.Direction2 && data.GetSkippedItemCount() == 0, "Circular definition modes differ.");
        TestData.Near(data.Spacing * 180 / Math.PI, angle);
        TestData.Check(data.AccessSelections(context.Document, null), "Cannot read circular definition references.");
        try
        {
            TestData.Check(data.Axis is IFace2 && PersistentReferenceAdapter.Capture(context, data.Axis) == state.Entities.Single(e => e.SemanticId == "disk.rotational_reference").NativeReference, "Circular axis not the real bound face.");
            var seeds = (Array)data.PatternFeatureArray;
            TestData.Check(seeds.Length == 1 && PersistentReferenceAdapter.Capture(context, seeds.GetValue(0)!) == state.Features.Single(f => f.SemanticId == "bolt_seed").NativeReference, "Native circular seed differs.");
        }
        finally { data.ReleaseSelectionAccess(); context.Document.ClearSelection2(true); }
    }
    private static void SetNativePattern(SolidWorksExecutionContext context, int count, double angle)
    {
        var feature = (IFeature)context.NativeFeature("bolt_ring"); var data = (ICircularPatternFeatureData)feature.GetDefinition();
        TestData.Check(data.AccessSelections(context.Document, null), "Cannot modify test circular definition.");
        var modified = false;
        try { data.TotalInstances = count; data.Spacing = angle * Math.PI / 180; modified = feature.ModifyDefinition(data, context.Document, null); TestData.Check(modified, "Test circular ModifyDefinition failed."); }
        finally { if (!modified) data.ReleaseSelectionAccess(); context.Document.ClearSelection2(true); }
        TestData.Check(context.Document.ForceRebuild3(false) && feature.GetErrorCode2(out var warning) == 0 && !warning && context.Document.Extension.NeedsRebuild2 == 0, "Test circular rebuild failed.");
    }
    private static GeometryObservation Observe(SolidWorksExecutionContext context)
    {
        var bodies = (Array)((IPartDoc)context.Document).GetBodies2(0, false); TestData.Check(bodies.Length == 1, "Expected one solid body.");
        var body = (IBody2)bodies.GetValue(0)!;
        var cylinders = ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>().Where(f => ((ISurface)f.GetSurface()).IsCylinder()).Select(f =>
        {
            var c = (double[])((ISurface)f.GetSurface()).CylinderParams;
            TestData.Near(Math.Abs(c[5]), 1);
            var levels = ((Array)f.GetEdges()).Cast<object>().Cast<IEdge>().Select(e => (ICurve)e.GetCurve()).Where(curve => curve.IsCircle())
                .Select(curve => ((double[])curve.CircleParams)[2] * 1000).Distinct().OrderBy(z => z).ToArray();
            return new CylinderObservation(c[0] * 1000, c[1] * 1000, c[6] * 2000, levels);
        }).ToArray();
        return new(ExtrudeMeasurementReader.Read(context), ((double[])body.GetMassProperties(1))[3] * 1e9, cylinders);
    }
    private static void Verify(GeometryObservation geometry, double diameter, int count, double angle)
    {
        TestData.Near(geometry.Extents.WidthMm, 100); TestData.Near(geometry.Extents.HeightMm, 100); TestData.Near(geometry.Extents.DepthMm, 12);
        TestData.Check(geometry.Extents.SolidBodyCount == 1 && geometry.Cylinders.Count == count + 2, "Cylinder/instance count differs.");
        var expected = new List<(double X, double Y, double Diameter)> { (0, 0, 100), (0, 0, 20) };
        for (var i = 0; i < count; i++)
        {
            var theta = i * angle * Math.PI / 180 / (angle == 360 ? count : count - 1);
            expected.Add((35 * Math.Cos(theta), 35 * Math.Sin(theta), diameter));
        }
        foreach (var (x, y, d) in expected)
        {
            var candidates = geometry.Cylinders.Where(c => Math.Abs(c.XMm - x) <= 1e-6 && Math.Abs(c.YMm - y) <= 1e-6 && Math.Abs(c.DiameterMm - d) <= 1e-6).ToArray();
            TestData.Check(candidates.Length == 1, $"Expected unique native cylinder at ({x}, {y}), diameter {d}.");
            var c = candidates[0]; TestData.Check(c.LevelsMm.Count == 2, "Cylinder lacks two circular boundaries.");
            TestData.Near(c.LevelsMm[0], 0); TestData.Near(c.LevelsMm[1], 12);
        }
        TestData.Check(Math.Abs(geometry.VolumeMm3 - Math.PI * (50 * 50 - 10 * 10 - count * diameter * diameter / 4) * 12) <= 0.001, "Native volume differs from analytic disk/bores.");
    }
}
