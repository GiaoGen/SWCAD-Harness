using System;
using System.IO;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;

internal static partial class T2Tests
{
    internal static int Run(string root, string run, string model, string phase, string? packageRun = null)
    {
        Program.Check(ContractValidation.Id(run) && (packageRun is null || ContractValidation.Id(packageRun)) && model is "shaft" or "sleeve" && phase is "candidate_create" or "candidate_edit" or "candidate_cold_edit" or "candidate_reverse" or "candidate_fault" or "create" or "edit" or "cold_edit" or "fault" or "read" or "recover" or "legacy_read" or "audit", "Unknown T2 slot");
        var directory = Path.Combine(root, "artifacts/m20-preparation/T2", run, model, phase);
        Directory.CreateDirectory(directory); var step = 0; var timer = System.Diagnostics.Stopwatch.StartNew();
        void Log(string name, object data) => Program.Write(Path.Combine(directory, (++step).ToString("D3") + "-" + name + ".json"), new { utc = DateTime.UtcNow, controller = System.Environment.ProcessId, elapsedMilliseconds = timer.Elapsed.TotalMilliseconds, data });
        using var budget = new NativeTestBudget(Path.Combine(root, "artifacts/m20-preparation/T2/native-budget.json"), "T2", int.MaxValue);
        using var connection = SolidWorksConnection.Connect(false);
        var before = NativeResourceGuard.DocumentTitles(connection.Application); var resource = NativeResourceGuard.Inspect(connection.Application, budget);
        Program.Check(resource.Responding && resource.GdiCount < 7000 && budget.Snapshot.OpenTestOwnedTitles.Count == 0, "Resource/ownership refusal");
        Log("before", new { resource, budget = budget.Snapshot, before });
        Log("code-version", new { baseline = "145534d", sources = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "tests/CadHarness.FeatureConstruction.Tests"), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(p => new { path = p, sha256 = ManagedRevisionStore.Hash(p) }).ToArray(), binaries = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Select(p => new { path = p, sha256 = ManagedRevisionStore.Hash(p) }).ToArray() });
        if (phase == "audit")
        {
            using var registry = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"artifacts/fixture-factory/audits/m14-preflight/fixture-registry.json")));
            var originals = registry.RootElement.GetProperty("fixtures").EnumerateArray().Select(f => {
                var native = f.GetProperty("nativePart"); var path = native.GetProperty("path").GetString()!; var hash = ManagedRevisionStore.Hash(path);
                return new { id = f.GetProperty("fixtureId").GetString(), path, hash, unchanged = hash == native.GetProperty("sha256").GetString() };
            }).ToArray();
            var manual = ManagedRevisionStore.Hash(Path.Combine(root,"M14测试用夹具.SLDPRT"));
            using var stop = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"artifacts/milestone20/acceptance_v3/stop-audit-20261011T145238147.json")));
            var history = stop.RootElement.GetProperty("preservedEvidence").EnumerateArray().Select(e => new {
                path = e.GetProperty("path").GetString()!, expected = e.GetProperty("sha256").GetString()!
            }).ToArray();
            var drift = history.Where(e => !File.Exists(e.path) || ManagedRevisionStore.Hash(e.path) != e.expected).ToArray();
            Log("preservation", new { originals, manual, historicalRecords = history.Length, drift });
            foreach (var failed in new[]{"candidate_v1","candidate_v2","candidate_v3"})
            {
                var package = Path.Combine(root,"artifacts/m20-preparation/T2",failed,"shaft/package");
                using var store = new ManagedRevisionStore(package,new MetadataReader()); var inspection = store.Inspect(store.WorkingPath);
                Log("initial-quarantine-"+failed,new { inspection.Status,inspection.FailureCode,inspection.RequiresNativeRecovery, authority=inspection.Revision?.State.Revision,
                    marker = File.Exists(store.RecoveryPath), markerHash=File.Exists(store.RecoveryPath)?ManagedRevisionStore.Hash(store.RecoveryPath):null, leaseReacquired=true });
                Program.Check(inspection.Status==ReopenStatus.Quarantined && inspection.Revision is null,"Initial failure invented authority");
            }
            foreach (var id in new[]{"shaft","sleeve"})
            {
                var package = Path.Combine(root,"artifacts/m20-preparation/T2",packageRun??"candidate_v4",id,"package");
                using var store = new ManagedRevisionStore(package,new MetadataReader()); var inspection = store.Inspect(store.WorkingPath);
                Log("authority-"+id,new { inspection.Status,inspection.FailureCode,inspection.RequiresNativeRecovery,revision=inspection.Revision?.State.Revision,native=ManagedRevisionStore.Hash(store.WorkingPath),leaseReacquired=true });
                Program.Check(inspection.FailureCode is null && !inspection.RequiresNativeRecovery && !File.Exists(store.RecoveryPath),"Active authority/recovery unresolved");
            }
            Program.Check(originals.All(o=>o.unchanged) && manual=="60fbd4101e4c1f3c5ca129c7e5d44b1eadf28c50b6483e0259d06b4b716f9a8b" && drift.Length==0,"Original or historical evidence drift");
            Log("result", new { passed = true, nativeOpens=0, creations=0, budget = budget.Snapshot, documents = before }); return 0;
        }
        var packageRoot = phase == "legacy_read" ? Path.Combine(root,"artifacts/m20-preparation/T1/torus_v1/package") : Path.Combine(root, "artifacts/m20-preparation/T2", packageRun ?? run, model, "package");
        var lifecycle = new Lifecycle(budget, Log); GenericPartSession? s = null; var sleeve = model == "sleeve";
        var candidate = phase.StartsWith("candidate_", StringComparison.Ordinal); var name = candidate ? phase[10..] : phase;
        try
        {
            if (name == "create") s = GenericPartSession.Create(connection, packageRoot, @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot", lifecycle: lifecycle);
            else
            {
                var opened = GenericPartSession.Open(connection, packageRoot, Path.Combine(packageRoot, "working/CADHarnessManagedPart.SLDPRT"), lifecycle: lifecycle);
                Log("open", new { opened.Status, opened.FailureCode, opened.Message });
                Program.Check(opened.Session is not null, "Cold restore failed: " + opened.Message); s = opened.Session!;
            }
            var setters = 0; var checkpoints = 0; var saveCalls = 0;
            s.SaveEvidenceSink = t => { Log("save-api", t); if (t.Stage == "before-save3") saveCalls++; };
            s.Context.SketchEvidenceSink = t => Log("sketch-api", t);
            s.Context.FeatureEvidenceSink = t => {
                Log("feature-api", t);
                if (t.Stage == "radial-driver-applied") { setters++; if (name == "fault" && setters == 1) throw new StateException("T2_RELATION_FAULT", "Actual first radius Setter succeeded; propagation interrupted."); }
            };
            if (name == "create")
            {
                var requested = T2Cases.Program(sleeve); Log("request", requested);
                var result = candidate ? s.ConstructCandidate(requested) : s.Construct(requested);
                Log("construction", new { result, s.Status });
                Program.Check(result.Succeeded && result.StateCommitted && result.Revision == 1, "Creation failed: " + result.Message + " " + result.RollbackFailureMessage);
            }
            else if (name is "edit" or "cold_edit" or "reverse" or "fault")
            {
                var state = s.Context.CaptureGenericConstructionState(); Log("before-state", state);
                var param = state.Parameters.Single(p => p.SemanticId == "rotation.radius." + (sleeve ? "large_bore" : "middle"));
                var pointer = ManagedRevisionStore.Hash(s.Store.PointerPath); var native = ManagedRevisionStore.Hash(s.Store.WorkingPath);
                var prepare = s.Context.GenericDurableCheckpoint!;
                s.Context.GenericDurableCheckpoint = () => { checkpoints++; Log("checkpoint", new { checkpoints }); prepare(); };
                var negatives = new[] {
                    new GenericScalarCommand(param.Owner, param.Key, param.Value, param.Value + 1, state.Revision),
                    new(param.Owner, param.Key, param.Value - 1, param.Value + 1, state.Revision, param.SemanticId),
                    new(param.Owner, param.Key, param.Value, -1, state.Revision, param.SemanticId),
                    new(param.Owner, param.Key, param.Value, 25, state.Revision, param.SemanticId),
                    new(param.Owner, ParameterKey.RevolveAxialSpan, 1, 2, state.Revision, param.SemanticId) };
                if (!candidate) negatives = new[]{negatives[0],negatives[^1]}.Concat(sleeve ? Array.Empty<GenericScalarCommand>() : new[]{
                    new GenericScalarCommand(param.Owner,param.Key,state.Parameters.Single(p=>p.SemanticId=="rotation.radius.groove").Value,
                        state.Parameters.Single(p=>p.SemanticId=="rotation.radius.groove").Value+.5,state.Revision,"rotation.radius.groove") }).ToArray();
                foreach (var negative in name == "edit" ? negatives : Array.Empty<GenericScalarCommand>())
                {
                    // Shaft 25 remains a legal envelope, so use a collapsing radius instead.
                    var input = negative.Value == 25 && !sleeve ? negative with { Value = 1 } : negative;
                    var refused = candidate ? s.EditCandidate(input) : s.Edit(input); Log("preflight-negative", new { input, refused, setters, checkpoints, saveCalls });
                    Program.Check(!refused.Succeeded && !refused.MutationStarted && !refused.RollbackAttempted && setters == 0 && checkpoints == 0 && saveCalls == 0 &&
                        ManagedRevisionStore.Hash(s.Store.PointerPath) == pointer && ManagedRevisionStore.Hash(s.Store.WorkingPath) == native, "Unsafe preflight rejection");
                }
                var nextValue = name == "fault" ? param.Value + .5 : name == "reverse" ? param.Value - 2 : !candidate && name == "cold_edit" ? param.Value - 2 : param.Value + 1;
                var request = new GenericScalarCommand(param.Owner, param.Key, param.Value, nextValue, state.Revision, param.SemanticId); Log("request", request);
                var result = candidate ? s.EditCandidate(request) : s.Edit(request);
                Log("edit", new { result, s.Status, setters, checkpoints, saveCalls, beforeRevision = state.Revision, beforePointer = pointer, afterPointer = ManagedRevisionStore.Hash(s.Store.PointerPath) });
                if (name == "fault")
                {
                    Program.Check(!result.StateCommitted && result.RollbackAttempted && result.RollbackSucceeded && checkpoints == 1 && setters == 1 && saveCalls == 0 &&
                        ManagedRevisionStore.Hash(s.Store.PointerPath) == pointer && ManagedRevisionStore.Hash(s.Store.WorkingPath) == native, "Relation failure did not atomically rollback");
                    // Finish deliberately quarantines a pending journal even
                    // after validated in-memory rollback. Never bypass it to
                    // reuse the production context; a new controller recovers.
                    Oracle(s.Context.Document, sleeve, param.Value, Log);
                    Log("result", new { passed = true, rollback = true, state.Revision, quarantine = s.Status, recoveryMarker = File.Exists(s.Store.RecoveryPath) }); return 0;
                }
                Program.Check(result.Succeeded && result.StateCommitted && result.Revision == state.Revision + 1 && checkpoints == 1 &&
                    setters == (sleeve ? 1 : 2) && saveCalls == 1, "Edit failed or aggregate semantics differ: " + result.Message + " " + result.RollbackFailureMessage);
                Program.Check(result.Changes.ChangedParameters.ToHashSet().SetEquals(sleeve ? new[]{param.SemanticId} : new[]{param.SemanticId,"rotation.radius.groove"}), "Unchanged driver was marked modified");
            }
            var current = s.Context.CaptureGenericConstructionState(); Log("current-state", current);
            if (phase=="legacy_read")
            {
                T1Tests.TorusOracle(s.Context.Document,current.Parameters.Single().Value,Log);
                Program.Check(s.CurrentGeneric.FormatVersion==1 && current.Revision==2 && !File.Exists(s.Store.RecoveryPath),"Format 1 compatibility drift");
                Log("result",new { passed=true,current.Revision,format=1,native=ManagedRevisionStore.Hash(s.Store.WorkingPath),nativeModification=false }); return 0;
            }
            var primary = current.Parameters.Single(p => p.SemanticId == "rotation.radius." + (sleeve ? "large_bore" : "middle"));
            Oracle(s.Context.Document, sleeve, primary.Value, Log);
            Program.Check(s.Store.ReadCurrent().Generic!.FormatVersion == 2 && s.Store.ReadCurrent().State.Revision == current.Revision && !File.Exists(s.Store.RecoveryPath), "Durable package/recovery mismatch");
            Log("result", new { passed = true, current.Revision, primary.Value, native = ManagedRevisionStore.Hash(s.Store.WorkingPath), format = s.CurrentGeneric.FormatVersion, candidate, setters, checkpoints, saveCalls });
        }
        catch (Exception e) { Log("failure", new { error = e.ToString() }); throw; }
        finally
        {
            s?.Dispose(); var after = NativeResourceGuard.Inspect(connection.Application, budget);
            Log("cleanup", new { originalActiveRestored = s?.OriginalActiveRestored, budget = budget.Snapshot, resource = after });
            Program.Check(budget.Snapshot.OpenTestOwnedTitles.Count == 0 && after.Responding && after.GdiCount < 7000 && before.OrderBy(x => x).SequenceEqual(NativeResourceGuard.DocumentTitles(connection.Application).OrderBy(x => x)), "T2 resource cleanup failed");
        }
        return 0;
    }
    private sealed class Lifecycle(NativeTestBudget budget, Action<string, object> log) : IGenericDocumentLifecycle
    {
        public void BeforeCreate() { budget.ReserveCreation(); log("reserve-create", budget.Snapshot); }
        public void Created(string title) { budget.RegisterCreated(title); log("created", new { title }); }
        public void Renamed(string before, string after) { budget.RenameOwned(before, after); log("renamed", new { before, after }); }
        public void BeforeOpen(string path) { budget.ReserveOpen(int.MaxValue); log("reserve-open", new { path, budget = budget.Snapshot }); }
        public void Opened(string path, string title) { budget.RegisterOpened(title); log("opened", new { path, title }); }
        public void Closed(string path, string title) { budget.RegisterClosed(title); log("closed", new { path, title }); }
    }
    private sealed class MetadataReader : IGenericRevisionNative
    {
        public GenericRevisionPackage CurrentGeneric => throw new NotSupportedException("Audit never mutates native state.");
        public void SaveNative() => throw new NotSupportedException("Audit never saves.");
        public void VerifySavedGeneric(GenericRevisionPackage package) => throw new NotSupportedException("Metadata audit is not native verification.");
    }
    private static T[] Items<T>(object value) => value is Array a ? a.Cast<object>().OfType<T>().ToArray() : Array.Empty<T>();
    private static void Oracle(IModelDoc2 doc, bool sleeve, double primary, Action<string, object> log)
    {
        // Expected stations/radii and integrals belong to the frozen test only;
        // no production material calculator, binder or state supplies this oracle.
        var expected = sleeve ? new[] { new[] { 16d, 0, 30 }, new[] { 24d, 30, 36 }, new[] { 10d, 0, 20 }, new[] { primary, 20, 36 } } :
            new[] { new[] { 15d, 0, 20 }, new[] { primary, 20, 35 }, new[] { primary - 2, 35, 39 }, new[] { primary, 39, 60 }, new[] { 9d, 60, 80 } };
        var expectedVolume = sleeve ? Math.PI * (16 * 16 * 30 + 24 * 24 * 6 - 10 * 10 * 20 - primary * primary * 16) :
            Math.PI * (15 * 15 * 20 + primary * primary * 36 + (primary - 2) * (primary - 2) * 4 + 9 * 9 * 20);
        var bodies = Items<IBody2>(((IPartDoc)doc).GetBodies2(0, false)); Program.Check(bodies.Length == 1, "Oracle body count");
        var body = bodies.Single(); var volume = ((double[])body.GetMassProperties(1))[3] * 1e9;
        var faces = Items<IFace2>(body.GetFaces()); var native = faces.Where(f => ((ISurface)f.GetSurface()).IsCylinder()).Select(f => new {
            cylinder = (double[])((ISurface)f.GetSurface()).CylinderParams,
            rings = Items<IEdge>(f.GetEdges()).Select(e => (ICurve)e.GetCurve()).Where(c => c.IsCircle()).Select(c => ((double[])c.CircleParams)[0] * 1000).OrderBy(v => v).ToArray(),
            uv = (double[])f.GetUVBounds() }).ToArray();
        var match = native.Length == expected.Length && expected.All(e => native.Count(n => Math.Abs(n.cylinder[6] * 1000 - e[0]) < .01 &&
            Math.Abs(Math.Abs(n.cylinder[3]) - 1) < 1e-8 && Math.Abs(n.cylinder[1]) < 1e-8 && Math.Abs(n.cylinder[2]) < 1e-8 && n.rings.Length == 2 &&
            Math.Abs(n.rings[0] - e[1]) < .01 && Math.Abs(n.rings[1] - e[2]) < .01 && Math.Abs(n.uv[1] - n.uv[0] - 2 * Math.PI) < 1e-8) == 1);
        var bounds = new double[6];
        for (var i = 0; i < 6; i++) { var axis = i % 3; var sign = i < 3 ? -1 : 1; Program.Check(body.GetExtremePoint(axis == 0 ? sign : 0, axis == 1 ? sign : 0, axis == 2 ? sign : 0, out var x, out var y, out var z), "Oracle extreme"); bounds[i] = new[] { x, y, z }[axis] * 1000; }
        var limits = sleeve ? new[] { 0d, -24, -24, 36, 24, 24 } : new[] { 0d, -15, -15, 80, 15, 15 };
        var surfaceTypes = faces.All(f => ((ISurface)f.GetSurface()).IsPlane() || ((ISurface)f.GetSurface()).IsCylinder());
        var passed = match && surfaceTypes && Math.Abs(volume - expectedVolume) < .01 && bounds.Zip(limits).All(p => Math.Abs(p.First - p.Second) < .01);
        log("independent-oracle", new { passed, primary, sleeve, volume, expectedVolume, difference = volume - expectedVolume, expected, native, bounds, match, surfaceTypes }); Program.Check(passed, "Independent radial geometry failed");
    }
}
