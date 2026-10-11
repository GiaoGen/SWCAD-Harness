using System;
using System.IO;
using System.Linq;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using CadHarness.Ir.V03;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static partial class T1Tests
{
    internal static int Run(string root, string run, string mode)
    {
        Program.Check(ContractValidation.Id(run) && mode is "diagnose" or "diagnose_stages" or "diagnose_cut" or "create" or "edit" or "read" or
            "create_torus" or "edit_torus" or "read_torus" or "fault_save" or "recover_save" or "fault_before" or "recover_before" or
            "fault_after" or "recover_after" or "fault_initial" or "audit", "Unknown T1 run/slot");
        var output = Path.Combine(root, "artifacts/m20-preparation/T1", run, mode);
        Directory.CreateDirectory(output);
        var step = 0; var elapsed = System.Diagnostics.Stopwatch.StartNew();
        void Log(string name, object data) => Program.Write(Path.Combine(output, (++step).ToString("D3") + "-" + name + ".json"),
            new { utc = DateTime.UtcNow, controller = System.Environment.ProcessId, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds, data });
        using var budget = new NativeTestBudget(Path.Combine(root, "artifacts/m20-preparation/T1/native-budget.json"), "T1", int.MaxValue);
        using var connection = SolidWorksConnection.Connect(false);
        NativeResourceGuard.TestTitlePrefix = "CADHarnessT1_";
        var before = NativeResourceGuard.DocumentTitles(connection.Application);
        var resources = NativeResourceGuard.Inspect(connection.Application, budget);
        Program.Check(resources.Responding && resources.GdiCount < 7000 && budget.Snapshot.OpenTestOwnedTitles.Count == 0, "Resource/ownership refusal");
        Log("before", new { resources, budget = budget.Snapshot, before });
        Log("code-version", new { baseline = "b25467d5b3402448f502bf966413213754d600a9", sources = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "tests/CadHarness.FeatureConstruction.Tests"), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(p => new { path = p, sha256 = ManagedRevisionStore.Hash(p) }).ToArray(),
            binaries = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Select(p => new { path = p, sha256 = ManagedRevisionStore.Hash(p) }).ToArray() });
        if (mode == "audit")
        {
            foreach (var id in new[] { "durable_v1", "durable_v2", "initial_failure_v1", "torus_v1" })
            {
                using var store = new ManagedRevisionStore(Path.Combine(root, "artifacts/m20-preparation/T1", id, "package"), new GenericFake());
                var inspection = store.Inspect(store.WorkingPath);
                InspectionLog("package-" + id, inspection, Log);
                Program.Check(id is "durable_v1" or "initial_failure_v1" ? inspection.Status == ReopenStatus.Quarantined && inspection.Revision is null :
                    inspection.FailureCode is null && !inspection.RequiresNativeRecovery, "Package authority unresolved");
            }
            Log("result", new { passed = true, nativeOpens = 0, newParts = 0, documents = before, budget = budget.Snapshot }); return 0;
        }
        if (mode is not ("diagnose" or "diagnose_stages" or "diagnose_cut")) return Durable(root, run, mode, connection, budget, Log);
        var scope = new TestPartScope(connection.Application, budget);
        try
        {
            var path = Path.Combine(output, "CADHarnessT1_diagnose.SLDPRT");
            SolidWorksExecutionContext c;
            if (mode != "diagnose")
            {
                File.Copy(Path.Combine(root, "artifacts/m20-preparation/T1/diag_v1/diagnose/CADHarnessT1_diagnose.SLDPRT"), path, false);
                c = scope.OpenOwnedCopy(path, int.MaxValue);
            }
            else c = scope.Create(connection, connection.ResolvePartTemplate(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot"));
            Snapshot("before-blank-save", c.Document, path, Log);
            int errors = 0, warnings = 0;
            var saved = mode != "diagnose" || c.Document.Extension.SaveAs(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null!, ref errors, ref warnings);
            scope.RefreshOwnedTitle(); Log("blank-save", new { saved, errors, warnings });
            Program.Check(saved && errors == 0 && warnings == 0, "Blank save failed");
            var result = c.ConstructGeneric(mode == "diagnose_stages" ? Cases.Root : Cases.Cut); Log("construction", result);
            Program.Check(result.Succeeded, "Construction failed");
            Snapshot("before-save3", c.Document, path, Log);
            errors = warnings = 0; saved = c.Document.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
            Log("save3", new { saved, errors, warnings, errorNames = ErrorNames(errors) });
            Snapshot("after-save3", c.Document, path, Log);
            if (!saved)
            {
                errors = warnings = 0;
                var same = c.Document.Extension.SaveAs(path, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null!, ref errors, ref warnings);
                Log("same-path-saveas", new { same, errors, warnings });
                Snapshot("after-saveas", c.Document, path, Log);
                var distinct = Path.Combine(output, "distinct.SLDPRT");
                errors = warnings = 0;
                var different = c.Document.Extension.SaveAs(distinct, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null!, ref errors, ref warnings);
                scope.RefreshOwnedTitle(); Log("distinct-saveas", new { different, errors, warnings });
                Snapshot("after-distinct", c.Document, distinct, Log);
            }
            Log("result", new { saved, noColdClaim = true });
        }
        finally
        {
            scope.Cleanup(); Log("cleanup", new { scope.Closed, scope.CleanupError, scope.OriginalActiveRestored, budget = budget.Snapshot });
            Program.Check(scope.Closed && scope.OriginalActiveRestored && scope.CleanupError is null && before.OrderBy(x => x).SequenceEqual(NativeResourceGuard.DocumentTitles(connection.Application).OrderBy(x => x)), "Cleanup failed");
        }
        return 0;
    }
    private static string[] ErrorNames(int errors) => Enum.GetValues<swFileSaveError_e>().Where(e => ((int)e & errors) != 0).Select(e => e.ToString()).ToArray();
    private static void Snapshot(string stage, IModelDoc2 doc, string path, Action<string, object> log) => log(stage, new
    {
        title = doc.GetTitle(), nativePath = doc.GetPathName(), dirty = doc.GetSaveFlag(), openedReadOnly = doc.IsOpenedReadOnly(), rebuild = doc.Extension.NeedsRebuild2,
        activeSketch = doc.GetActiveSketch2() is not null, exists = File.Exists(path),
        readOnly = File.Exists(path) && new FileInfo(path).IsReadOnly,
        sha256 = File.Exists(path) ? ManagedRevisionStore.Hash(path) : null, pathLength = path.Length
    });
    private static int Durable(string root, string run, string mode, SolidWorksConnection connection, NativeTestBudget budget, Action<string, object> log)
    {
        var packageRoot = Path.Combine(root, "artifacts/m20-preparation/T1", run, "package");
        var lifecycle = new T1Lifecycle(budget, log);
        var before = NativeResourceGuard.DocumentTitles(connection.Application);
        GenericPartSession? s = null;
        try
        {
            DurableFaultPoint? point = mode switch { "fault_save" => DurableFaultPoint.AfterNativeSave, "fault_before" => DurableFaultPoint.BeforePointerPublish,
                "fault_after" => DurableFaultPoint.AfterPointerPublish, "fault_initial" => DurableFaultPoint.AfterNativeSave, _ => null };
            void Fault(DurableFaultPoint p) { log("publish-boundary", new { p }); if (p == point) throw new StateException("T1_INJECTED", p.ToString()); }
            if (mode is "create" or "create_torus" or "fault_initial")
                s = GenericPartSession.Create(connection, packageRoot, @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2024\templates\gb_part.prtdot", Fault, lifecycle);
            else
            {
                var opened = GenericPartSession.Open(connection, packageRoot, Path.Combine(packageRoot, "working/CADHarnessManagedPart.SLDPRT"), Fault, lifecycle);
                log("open", new { opened.Status, opened.FailureCode, opened.Message });
                Program.Check(opened.Session is not null, "Durable open failed: " + opened.FailureCode + " " + opened.Message); s = opened.Session!;
            }
            s.SaveEvidenceSink = t => log("save-api", t);
            s.Context.FeatureEvidenceSink = t => log("feature-api", t); s.Context.SketchEvidenceSink = t => log("sketch-api", t);
            if (mode is "create" or "create_torus" or "fault_initial")
            {
                var result = s.Construct(mode == "create_torus" ? Cases.Torus : T1Program(), new("1", "development_requirements", "{\"test\":\"T1 durable development profile\"}"));
                log("construction", new { result, s.Status });
                if (mode == "fault_initial")
                {
                    Program.Check(!result.StateCommitted && s.Status == ReopenStatus.Quarantined && !File.Exists(s.Store.PointerPath), "Initial failure invented authority");
                    var inspection = s.Store.Inspect(s.Store.WorkingPath); InspectionLog("initial-isolation", inspection, log);
                    Program.Check(inspection.Status == ReopenStatus.Quarantined && inspection.Revision is null, "Initial failure not isolated");
                    return 0;
                }
                Program.Check(result.Succeeded && result.StateCommitted && result.Revision == 1 && !File.Exists(s.Store.RecoveryPath), "Durable creation failed: " + result.Message + " " + result.RollbackFailureMessage);
            }
            else if (mode is "edit" or "edit_torus" or "fault_save" or "fault_before" or "fault_after")
            {
                var old = s.Context.CaptureGenericConstructionState(); var p = old.Parameters.Single(p => mode == "edit_torus" ? p.Owner == "revolve" && p.Key == ParameterKey.RevolveRadius : p.Owner == "pocket" && p.Key == ParameterKey.CutDepth);
                var priorPointer = ManagedRevisionStore.Hash(s.Store.PointerPath);
                var result = s.Edit(new(p.Owner, p.Key, p.Value, p.Value + 1, old.Revision));
                log("edit", new { result, s.Status, priorPointer, afterPointer = ManagedRevisionStore.Hash(s.Store.PointerPath) });
                if (mode is "fault_save" or "fault_before")
                {
                    Program.Check(!result.Succeeded && !result.StateCommitted && s.Status == ReopenStatus.Quarantined && File.Exists(s.Store.RecoveryPath) &&
                        ManagedRevisionStore.Hash(s.Store.PointerPath) == priorPointer, "Pre-pointer fault miscommitted");
                    InspectionLog("pending-recovery", s.Store.Inspect(s.Store.WorkingPath), log); return 0;
                }
                Program.Check(result.Succeeded && result.StateCommitted && result.Revision == old.Revision + 1, "Edit not durable: " + result.Message + " " + result.RollbackFailureMessage);
                if (mode == "fault_after")
                {
                    Program.Check(File.Exists(s.Store.RecoveryPath) && s.Store.ReadCurrent().State.Revision == old.Revision + 1, "Post-commit authority missing");
                    InspectionLog("committed-maintenance-pending", s.Store.Inspect(s.Store.WorkingPath), log); return 0;
                }
            }
            var state = s.Context.CaptureGenericConstructionState();
            log("current-state", state);
            if (state.Features.Any(f => f.Kind == ConstructionKind.CreateRevolvedBoss)) TorusOracle(s.Context.Document, state.Parameters.Single().Value, log);
            else T1Oracle(s.Context.Document, state.Parameters.Single(p => p.Owner == "pocket").Value, log);
            Program.Check(s.Store.ReadCurrent().Generic!.State.Revision == state.Revision && s.CurrentGeneric.Provenance is not null, "Companion/revision/provenance differs");
            log("result", new { passed = true, state.Revision, native = ManagedRevisionStore.Hash(s.Store.WorkingPath), recovery = File.Exists(s.Store.RecoveryPath) });
        }
        finally
        {
            s?.Dispose();
            var resource = NativeResourceGuard.Inspect(connection.Application, budget);
            log("cleanup", new { restored = s?.OriginalActiveRestored, budget = budget.Snapshot, resource });
            Program.Check(budget.Snapshot.OpenTestOwnedTitles.Count == 0 && resource.Responding && resource.GdiCount < 7000 && before.OrderBy(x => x).SequenceEqual(NativeResourceGuard.DocumentTitles(connection.Application).OrderBy(x => x)), "Durable cleanup failed");
        }
        return 0;
    }
    private sealed class T1Lifecycle(NativeTestBudget budget, Action<string, object> log) : IGenericDocumentLifecycle
    {
        public void BeforeCreate() { budget.ReserveCreation(); log("reserve-create", budget.Snapshot); }
        public void Created(string title) { budget.RegisterCreated(title); log("created", new { title }); }
        public void Renamed(string before, string after) { budget.RenameOwned(before, after); log("rename", new { before, after }); }
        public void BeforeOpen(string path) { budget.ReserveOpen(int.MaxValue); log("reserve-open", new { path, budget = budget.Snapshot }); }
        public void Opened(string path, string title) { budget.RegisterOpened(title); log("opened", new { path, title }); }
        public void Closed(string path, string title) { budget.RegisterClosed(title); log("closed", new { path, title }); }
    }
    private static void InspectionLog(string name, ManagedRecoveryInspection inspection, Action<string, object> log) => log(name, new
    {
        inspection.Status, inspection.FailureCode, inspection.Message, inspection.RequiresNativeRecovery,
        authorityRevision = inspection.Revision?.State.Revision, manifest = inspection.Revision?.ManifestArtifact,
        generic = inspection.Revision?.Generic
    });
    internal static ConstructionProgram T1Program() => Cases.Cut with { Operations = Cases.Cut.Operations.Concat(new[] {
        Cases.Sketch("second_profile", Cases.Circle(10, 0, 3), 10), Cases.Extrude("second", "second_profile", ConstructionKind.CreateExtrudedCut, 3, true, "pocket"),
        Cases.Sketch("third_profile", Cases.Circle(24, 0, 2), 10), Cases.Extrude("third", "third_profile", ConstructionKind.CreateExtrudedCut, 2, true, "second") }).ToArray() };
    private static void T1Oracle(IModelDoc2 doc, double depth, Action<string, object> log)
    {
        var bodies = NativeItems<IBody2>(((IPartDoc)doc).GetBodies2(0, false)); Program.Check(bodies.Length == 1, "Oracle body count");
        var volume = ((double[])bodies[0].GetMassProperties(1))[3] * 1e9;
        var expected = 80 * 60 * 10 - (8 * 4 + Math.PI * 4) * depth - Math.PI * 9 * 3 - Math.PI * 4 * 2;
        var cylinders = NativeItems<IFace2>(bodies[0].GetFaces()).Select(f => (face: f, surface: (ISurface)f.GetSurface())).Where(p => p.surface.IsCylinder())
            .Select(p => new { native = (double[])p.surface.CylinderParams, uv = (double[])p.face.GetUVBounds(), levels = NativeItems<IEdge>(p.face.GetEdges()).Select(e => (ICurve)e.GetCurve())
                .Where(c => c.IsCircle()).Select(c => ((double[])c.CircleParams)[2] * 1000).OrderBy(z => z).ToArray() }).ToArray();
        var expectedCylinders = new[] { new[] { -22.0, 0, 2, 10 - depth }, new[] { -14.0, 0, 2, 10 - depth }, new[] { 10.0, 0, 3, 7.0 }, new[] { 24.0, 0, 2, 8.0 } };
        var cylinderMatch = cylinders.Length == 4 && expectedCylinders.All(e => cylinders.Count(a => Math.Abs(a.native[0] * 1000 - e[0]) < .01 &&
            Math.Abs(a.native[1] * 1000 - e[1]) < .01 && Math.Abs(a.native[6] * 1000 - e[2]) < .01 && Math.Abs(Math.Abs(a.native[5]) - 1) < 1e-8 &&
            Math.Abs(a.uv[1] - a.uv[0] - (e[0] < 0 ? Math.PI : 2 * Math.PI)) < 1e-8 &&
            a.levels.Length == 2 && Math.Abs(a.levels[0] - e[3]) < .01 && Math.Abs(a.levels[1] - 10) < .01) == 1);
        var straight = NativeItems<IEdge>(bodies[0].GetEdges()).Where(e => ((ICurve)e.GetCurve()).IsLine()).Select(e => new[] {
            ((IVertex)e.GetStartVertex()).GetPoint() as double[] ?? Array.Empty<double>(), ((IVertex)e.GetEndVertex()).GetPoint() as double[] ?? Array.Empty<double>() }).ToArray();
        var tangentLines = new[] { 10.0, 10 - depth }.All(z => new[] { -2.0, 2.0 }.All(y => straight.Count(edge =>
            edge.All(p => p.Length == 3 && Math.Abs(p[1] * 1000 - y) < .01 && Math.Abs(p[2] * 1000 - z) < .01) &&
            Math.Abs(edge.Min(p => p[0]) * 1000 + 22) < .01 && Math.Abs(edge.Max(p => p[0]) * 1000 + 14) < .01) == 1));
        var surfaceKinds = NativeItems<IFace2>(bodies[0].GetFaces()).All(f => ((ISurface)f.GetSurface()).IsPlane() || ((ISurface)f.GetSurface()).IsCylinder());
        var bounds = new double[6];
        for (var i = 0; i < 6; i++) { var axis = i % 3; var sign = i < 3 ? -1 : 1; Program.Check(bodies[0].GetExtremePoint(axis == 0 ? sign : 0, axis == 1 ? sign : 0, axis == 2 ? sign : 0, out var x, out var y, out var z), "Oracle extreme"); bounds[i] = new[] { x, y, z }[axis] * 1000; }
        var passed = surfaceKinds && tangentLines && cylinderMatch && Math.Abs(volume - expected) < .01 && bounds.Zip(new[] { -40.0, -30, 0, 40, 30, 10 }).All(p => Math.Abs(p.First - p.Second) < .01);
        log("independent-oracle", new { passed, volume, expected, difference = volume - expected, bounds, cylinders, expectedCylinders, depth, tangentLines, straight, surfaceKinds });
        Program.Check(passed, "Independent T1 geometry failed");
    }
    private static T[] NativeItems<T>(object value) => value is Array array ? array.Cast<object>().OfType<T>().ToArray() : Array.Empty<T>();
    private static void TorusOracle(IModelDoc2 doc, double radius, Action<string, object> log)
    {
        var body = NativeItems<IBody2>(((IPartDoc)doc).GetBodies2(0, false)).Single();
        var volume = ((double[])body.GetMassProperties(1))[3] * 1e9;
        var faces = NativeItems<IFace2>(body.GetFaces()); Program.Check(faces.Length == 1 && ((ISurface)faces[0].GetSurface()).IsTorus(), "Oracle torus face");
        var t = (double[])((ISurface)faces[0].GetSurface()).TorusParams;
        var expected = 2 * Math.PI * Math.PI * 15 * radius * radius;
        var passed = Math.Abs(volume - expected) < .01 && Math.Abs(t[6] * 1000 - 15) < .01 && Math.Abs(t[7] * 1000 - radius) < .01 &&
            Math.Abs(Math.Abs(t[3]) - 1) < 1e-8 && t.Take(3).All(v => Math.Abs(v) < 1e-8);
        log("independent-torus-oracle", new { passed, volume, expected, difference = volume - expected, radius, native = t }); Program.Check(passed, "Oracle torus differs");
    }
}
