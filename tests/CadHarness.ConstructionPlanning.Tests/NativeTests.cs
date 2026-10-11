using System;
using System.IO;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class Program
{
    private static int Native(string root, string run, string? blank = null, bool replayAll = false)
    {
        Check(ContractValidation.Id(run), "Invalid run ID");
        var output = Path.Combine(root, "artifacts/milestone17", run);
        Check(!Directory.Exists(output), "Never overwrite a run"); Directory.CreateDirectory(output);
        var protectedFiles = ProtectedFiles(root);
        Write(Path.Combine(output, "originals-before.json"), protectedFiles);
        Write(Path.Combine(output, "schedule.json"), new { frozenSource = SourceHashes(root), root = Cases.Plate(), continuation = Cases.Hole(1),
            maximumNewParts = blank is null ? 1 : 0, maximumOpens = blank is null ? 0 : 1,
            provenance = replayAll ? "fixture_replay_of_preserved_live_responses" : blank is null ? "live_provider" : "recorded_live_root_then_live_continuation", expectedVolumes = new[] { 62 * 46 * 9.0, (62 * 46 - Math.PI * 9) * 9 },
            authorization = "2026-10-11 user explicitly authorizes unlimited M17 native budget; accounting and lifecycle remain mandatory." });
        NativeTestBudget? ledger = null; SolidWorksConnection? connection = null; TestPartScope? scope = null; string? failure = null;
        string[] before = Array.Empty<string>(), after = Array.Empty<string>(); var step = 0;
        void Evidence(string name, object data) { Write(Path.Combine(output, "steps", (++step).ToString("D3") + "-" + name + ".json"), new { utc = DateTime.UtcNow, controller = System.Environment.ProcessId, name, data }); Console.WriteLine("STEP " + name); }
        try
        {
            ledger = new(Path.Combine(root, "artifacts/milestone17/native-budget.json"), "M17", int.MaxValue);
            connection = SolidWorksConnection.Connect(false); NativeResourceGuard.TestTitlePrefix = "CADHarnessM17Test_";
            var app = connection.Application; before = NativeResourceGuard.DocumentTitles(app);
            var resource = NativeResourceGuard.Inspect(app, ledger);
            Check(resource.Responding && resource.GdiCount < 7000 && resource.OpenTestOwnedParts == 0, "Resource guard refused");
            scope = new(app, ledger);
            var path = Path.Combine(output, "working.SLDPRT"); int errors = 0, warnings = 0;
            SolidWorksExecutionContext c;
            if (blank is null)
            {
                c = scope.Create(connection, connection.ResolvePartTemplate(System.Environment.GetEnvironmentVariable("CAD_HARNESS_PART_TEMPLATE")));
                Check(c.Document.Extension.SaveAs(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null!, ref errors, ref warnings) && errors == 0 && warnings == 0, "Blank save failed"); scope.RefreshOwnedTitle();
                Write(Path.Combine(output, "blank-source.json"), new { path, sha256 = ManagedRevisionStore.Hash(path) });
            }
            else
            {
                var sourcePath = Path.GetFullPath(blank);
                Check(sourcePath.StartsWith(Path.GetFullPath(Path.Combine(root, "artifacts/milestone17")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Only an owned M17 blank source may be reused");
                using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourcePath)!, "blank-source.json")));
                Check(string.Equals(Path.GetFullPath(record.RootElement.GetProperty("path").GetString()!), sourcePath, StringComparison.OrdinalIgnoreCase) && record.RootElement.GetProperty("sha256").GetString() == ManagedRevisionStore.Hash(sourcePath), "Owned blank identity changed");
                File.Copy(sourcePath, path, false); c = scope.OpenOwnedCopy(path, int.MaxValue);
                protectedFiles = protectedFiles.Append(new ProtectedFile(sourcePath, ManagedRevisionStore.Hash(sourcePath))).ToArray();
            }
            c.FeatureEvidenceSink = trace => Evidence("feature-api", trace); c.SketchEvidenceSink = trace => Evidence("sketch-api", trace);
            using var client = OpenAiPlanSource.CreateClient(); var source = new EvidenceSource(Provider(client), output);
            void RunPlan(string name, ConstructionPlanRequest request, double volume, int expectedRevision, IStructuredPlanSource? selected = null)
            {
                var old = c.CaptureGenericConstructionState(); var runtime = SolidWorksConstructionPlanningRuntime.ForManagedSession(c);
                var plan = new ConstructionPlanner(runtime, selected ?? source).PlanAsync(request).GetAwaiter().GetResult();
                Evidence(name + "-plan", plan);
                Check(GenericConstructionStatePolicy.MatchesNativeSnapshot(old, c.CaptureGenericConstructionState()), "Planning mutated native state");
                Check(plan.Status == ConstructionPlanningStatus.Planned && plan.Preview is not null, "Plan failed: " + plan.FailureCode + " " + plan.Message);
                var approval = plan.Preview!.Approve(plan.Preview.Sha256);
                var result = c.ExecuteConstructionPlan(approval); Evidence(name + "-execution", new { result, state = c.CaptureGenericConstructionState() });
                Check(result.Succeeded && result.StateCommitted && result.RebuildSucceeded && result.Revision == expectedRevision, "Execution failed: " + result.FailureCode + " " + result.Message);
                Oracle(c, volume, expectedRevision == 2, Evidence);
                try { c.ExecuteConstructionPlan(approval); throw new Exception("Reused approval accepted"); } catch (ContractException e) { Check(e.Code == V03FailureCodes.PreviewExpired, e.Message); Evidence("reused-approval-refused", new { e.Code }); }
                var now = NativeResourceGuard.Inspect(app, ledger);
                Check(now.Responding && now.GdiCount < 7000 && now.OpenTestOwnedParts == 1, "Native resource guard failed");
            }
            IStructuredPlanSource? replay = null;
            IStructuredPlanSource Replay(string path)
            {
                using var recorded = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
                Evidence("reused-live-plan", new { path, sha256 = ManagedRevisionStore.Hash(path), originalSource = "live_provider", currentSource = "fixture_replay", newModelInferenceClaimed = false });
                return new RecordingSource(recorded.RootElement.GetProperty("response").GetProperty("Json").GetString()!);
            }
            if (blank is not null)
            {
                var evidencePath = Path.Combine(root, "artifacts/milestone17/live_v3/call-02-response.json");
                replay = Replay(evidencePath);
            }
            RunPlan("new-part", Cases.Plate(), 62 * 46 * 9, 1, replay);
            RunPlan("continuation", Cases.Hole(c.CaptureGenericConstructionState().Revision), (62 * 46 - Math.PI * 9) * 9, 2,
                replayAll ? Replay(Path.Combine(root, "artifacts/milestone17/native_v2/call-01-response.json")) : null);
            var state = c.CaptureGenericConstructionState();
            var q = Cases.Hole(2) with { Input = Cases.Hole(2).Input with { Requirements = new[] { Cases.Given("diameter", null) } }, Rules = Cases.Hole(2).Rules.Where(r => r.RuleId != "half_dimension").ToArray() };
            var refusal = new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForManagedSession(c), replayAll ? Replay(Path.Combine(root, "artifacts/milestone17/native_v2/call-02-response.json")) : source).PlanAsync(q).GetAwaiter().GetResult();
            Evidence("critical-refusal-no-mutation", new { refusal, unchanged = GenericConstructionStatePolicy.MatchesNativeSnapshot(state, c.CaptureGenericConstructionState()) });
            Check(refusal.Status == ConstructionPlanningStatus.NeedsClarification && refusal.Preview is null && GenericConstructionStatePolicy.MatchesNativeSnapshot(state, c.CaptureGenericConstructionState()), "Critical unknown not safely refused");
            errors = 0; warnings = 0;
            Check(c.Document.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) && errors == 0 && warnings == 0, "Native save failed");
            Evidence("saved", new { path, sha256 = ManagedRevisionStore.Hash(path), state = c.CaptureGenericConstructionState(), coldReopenClaimed = false });
        }
        catch (Exception e) { failure = e.ToString(); Write(Path.Combine(output, "failure.json"), new { failure, step, utc = DateTime.UtcNow }); Console.Error.WriteLine(failure); }
        finally
        {
            scope?.Cleanup();
            if (connection is not null) after = NativeResourceGuard.DocumentTitles(connection.Application);
            var cleanup = scope is not null && scope.Closed && scope.OriginalActiveRestored && scope.CleanupError is null;
            if (!cleanup && failure is null) failure = "Native lifecycle cleanup not confirmed";
            var preserved = protectedFiles.All(p => ManagedRevisionStore.Hash(p.Path) == p.Hash);
            Write(Path.Combine(output, "originals-after.json"), new { preserved, files = ProtectedFiles(root) });
            if (!preserved && failure is null) failure = "Protected source/evidence drifted";
            if (!before.OrderBy(x => x).SequenceEqual(after.OrderBy(x => x)) && failure is null) failure = "User document inventory changed";
            Write(Path.Combine(output, "result.json"), new { passed = failure is null && before.OrderBy(x => x).SequenceEqual(after.OrderBy(x => x)), failure, cleanup, before, after, budget = ledger?.Snapshot, step });
            ledger?.Dispose(); connection?.Dispose();
        }
        return failure is null ? 0 : 1;
    }
    private sealed record ProtectedFile(string Path, string Hash);
    private static ProtectedFile[] ProtectedFiles(string root)
    {
        var paths = new System.Collections.Generic.List<string> { Path.Combine(root, "SWCAD_Harness_v0.3_PRD.md"), Path.Combine(root, "M14测试用夹具.SLDPRT") };
        var registryPath = Path.Combine(root, "artifacts/fixture-factory/audits/final-v1/fixture-registry.json"); paths.Add(registryPath);
        using var registry = System.Text.Json.JsonDocument.Parse(File.ReadAllText(registryPath));
        foreach (var fixture in registry.RootElement.GetProperty("fixtures").EnumerateArray())
            foreach (var key in new[] { "nativePart", "manifest", "proof", "ready" })
            {
                var item = fixture.GetProperty(key); var path = item.GetProperty("path").GetString()!;
                Check(ManagedRevisionStore.Hash(path) == item.GetProperty("sha256").GetString(), "Frozen fixture drifted before native execution"); paths.Add(path);
            }
        foreach (var milestone in new[] { "milestone14", "milestone15", "milestone16" }) paths.Add(Path.Combine(root, "artifacts", milestone, "native-budget.json"));
        return paths.Select(p => new ProtectedFile(p, ManagedRevisionStore.Hash(p))).ToArray();
    }
    private static void Oracle(SolidWorksExecutionContext c, double expected, bool cut, Action<string, object> evidence)
    {
        var bodies = ((IPartDoc)c.Document).GetBodies2(0, false) as Array; Check(bodies is { Length: 1 }, "Independent body count differs");
        var b = (IBody2)bodies!.GetValue(0)!; var mass = (double[])b.GetMassProperties(1); var volume = mass[3] * 1e9;
        var supports = new double[6]; var bounds = new[] { -31.0, -23, 0, 31, 23, 9 };
        for (var i = 0; i < 6; i++)
        {
            var axis = i % 3; var sign = i < 3 ? -1 : 1;
            Check(b.GetExtremePoint(axis == 0 ? sign : 0, axis == 1 ? sign : 0, axis == 2 ? sign : 0, out var x, out var y, out var z), "Independent support missing");
            supports[i] = new[] { x, y, z }[axis] * 1000;
        }
        var cylinders = 0; var cylinderOk = true;
        foreach (IFace2 face in (Array)b.GetFaces())
        {
            var s = (ISurface)face.GetSurface();
            if (s.IsCylinder())
            {
                cylinders++; var p = (double[])s.CylinderParams;
                cylinderOk &= Math.Abs(p[0] * 1000) < .01 && Math.Abs(p[1] * 1000) < .01 && Math.Abs(p[6] * 1000 - 3) < .01 && Math.Abs(Math.Abs(p[5]) - 1) < 1e-8;
            }
            else cylinderOk &= s.IsPlane();
        }
        var passed = Math.Abs(volume - expected) <= Math.Max(1e-3, expected * 1e-8) && supports.Zip(bounds).All(p => Math.Abs(p.First - p.Second) < .01) &&
            Math.Abs(mass[0] * 1000) < .01 && Math.Abs(mass[1] * 1000) < .01 && Math.Abs(mass[2] * 1000 - 4.5) < .01 && cylinders == (cut ? 1 : 0) && cylinderOk;
        evidence("independent-oracle", new { passed, expectedVolumeMm3 = expected, actualVolumeMm3 = volume, difference = volume - expected, supports, bounds, cylinders, cylinderOk, centroidSi = mass.Take(3) });
        Check(passed, "Independent native Oracle failed");
    }
}
