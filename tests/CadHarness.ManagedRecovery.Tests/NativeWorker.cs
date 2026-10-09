using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal sealed record HoleObservation(double X, double Y, double Diameter, double[] BoundaryZ);
internal sealed record NativeObservation(double DefinitionDepth, ExtrudeMeasurement Extents, double Volume,
    IReadOnlyList<HoleObservation> Holes, long Revision, int Relations, int Dependencies);
internal sealed class WorkerReport
{
    public string Status { get; set; } = "BLOCKED";
    public string? FailureCode { get; set; }
    public string? Error { get; set; }
    public string Step { get; set; } = "";
    public int ControllerPid { get; set; } = System.Environment.ProcessId;
    public string? SolidWorksVersion { get; set; }
    public bool StartedApplication { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public bool NoOwnedDocumentRemains { get; set; }
    public bool OriginalsPreserved { get; set; }
    public ManagedMigrationEvidence? Migration { get; set; }
    public CompositionExecutionResult? Creation { get; set; }
    public MutationResult? Edit { get; set; }
    public NativeObservation? Before { get; set; }
    public NativeObservation? After { get; set; }
    public Dictionary<string, string> Refusals { get; set; } = new();
    public string? PointerBefore { get; set; }
    public string? PointerAfter { get; set; }
    public bool? SavedDiskDiffersFromAuthoritative { get; set; }
    public bool? JournalRetained { get; set; }
    public bool? TargetPersistentReferencePreserved { get; set; }
    public JsonElement? RetainedRecoveryMarker { get; set; }
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime FinishedUtc { get; set; }
}

internal static class NativeWorker
{
    internal static int Run(string root, string step, string? template)
    {
        root = Path.GetFullPath(root);
        var common = Path.Combine(root, "artifacts", "milestone12");
        var selectedEvidence = System.Environment.GetEnvironmentVariable("CAD_HARNESS_M12_EVIDENCE_DIR");
        var output = selectedEvidence is null ? common : Path.GetFullPath(selectedEvidence);
        if (selectedEvidence is not null && !string.Equals(output, Path.Combine(common, "authorized-v3"), StringComparison.OrdinalIgnoreCase))
            throw new StateException("NATIVE_BUDGET_EXHAUSTED", "Additional budget requires its exact new evidence namespace.");
        var authorization = selectedEvidence is null ? null : Path.Combine(output, "authorization.json");
        var reportPath = Path.Combine(output, "worker-" + step + ".json");
        if (File.Exists(reportPath)) { Console.WriteLine("Slot already recorded; no rerun."); return 2; }
        var packageRoot = Path.Combine(output, "managed");
        var originalPart = Path.Combine(common, "legacy", "CADHarnessM12Original.SLDPRT");
        var originalState = Path.Combine(common, "legacy", "state-v02.json");
        var originalProgram = Path.Combine(common, "legacy", "program-v02.json");
        var report = new WorkerReport { Step = step };
        SolidWorksConnection? connection = null; NativeLedger? ledger = null; ManagedPartSession? session = null;
        IModelDoc2? created = null; string? originalActive = null; string? createdTitle = null;
        try
        {
            VerifyFreeze(root, output);
            if (step != "create") CheckPrerequisite(output, step);
            connection = SolidWorksConnection.Connect();
            report.SolidWorksVersion = connection.Application.RevisionNumber(); report.StartedApplication = connection.StartedApplication;
            originalActive = connection.Application.IActiveDoc2 is IModelDoc2 active ? active.GetTitle() : null;
            ledger = new(common, connection.Application, selectedEvidence is null ? step : "authorized-v3/" + step, authorization);
            if (selectedEvidence is not null) Check(CheckOriginals(common), "Preserved initial originals changed before authorized continuation.");
            if (step is "create" or "migrate")
            {
                if (Directory.Exists(packageRoot) || step == "create" && Directory.Exists(Path.Combine(output, "legacy")))
                    throw new StateException("NATIVE_SLOT_ALREADY_ATTEMPTED", "Native destination must be new.");
                CadState state;
                if (step == "create")
                {
                var fixturePath = Path.Combine(root, "tests", "CadHarness.Relations.Tests", "Fixtures", "rect2x2.json");
                var parsed = new CadProgramJson().Parse(File.ReadAllText(fixturePath)); Check(parsed.IsValid, "Frozen G2-like fixture is invalid.");
                var backend = new RelationBackend(); Check(backend.Preflight(parsed.Program!).IsValid, "G2-like pure preflight failed.");
                var partTemplate = connection.ResolvePartTemplate(template);
                ledger.ReserveCreation(); var context = connection.CreatePart(partTemplate);
                created = context.Document; createdTitle = created.GetTitle(); ledger.Created(created);
                if (!created.SetTitle2("CADHarnessM12Test_" + Guid.NewGuid().ToString("N"))) throw new StateException("NATIVE_CREATE_FAILED", "Could not label owned Part.");
                ledger.RefreshTitle(createdTitle, created.GetTitle()); createdTitle = created.GetTitle();
                report.Creation = backend.Create(context, parsed.Program!); Check(report.Creation.Succeeded, "Construction: " + report.Creation.Message);
                var configuration = created.ConfigurationManager.ActiveConfiguration.Name;
                var alternate = created.ConfigurationManager.AddConfiguration2("M12Other", "negative configuration only", "", 0, "", "", false);
                Check(alternate is not null && created.ShowConfiguration2(configuration) && created.ForceRebuild3(false), "Could not freeze alternate owned configuration.");
                Directory.CreateDirectory(Path.GetDirectoryName(originalPart)!);
                var errors = 0; var warnings = 0;
                var saved = created.Extension.SaveAs(originalPart, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
                ledger.RefreshTitle(createdTitle, created.GetTitle()); createdTitle = created.GetTitle();
                Check(saved && errors == 0 && warnings == 0 && !created.GetSaveFlag(), $"Original native save errors={errors}, warnings={warnings}");
                state = context.CaptureBindingState();
                var program = new DesignRelationEngine().Solve(parsed.Program!).Program;
                new AtomicStateStore(originalState).Commit(state); File.WriteAllText(originalProgram, new CadProgramJson().Serialize(program));
                report.Before = Observe(context, state); Verify(report.Before, 8, state.Revision);
                CloseCreated();
                }
                else state = new AtomicStateStore(originalState).Load();
                var fingerprints = new[] { ManagedRevisionStore.Fingerprint(originalPart), ManagedRevisionStore.Fingerprint(originalState), ManagedRevisionStore.Fingerprint(originalProgram) };
                WriteNew(Path.Combine(output, "original-hashes.json"), JsonSerializer.Serialize(fingerprints));
                var migrated = ManagedPartSession.MigrateCopy(connection, packageRoot, originalPart, originalState, originalProgram, ledger);
                Check(migrated.Session is not null, "Native migration: " + migrated.FailureCode + ": " + migrated.Message); session = migrated.Session;
                report.Migration = session!.Migration;
                report.After = Observe(session.Context, session.Store.Load()); Verify(report.After, 8, state.Revision);
                Check(report.Migration is { NativeReopenVerified: true, OriginalsPreserved: true }, "Native copy migration audit missing.");
            }
            else
            {
                Action<DurableFaultPoint>? fault = step == "interrupt" ? point =>
                { if (point == DurableFaultPoint.AfterNativeSave) throw new StateException("INJECTED_INTERRUPTED_PUBLISH", "AfterNativeSave before any state/package publication"); } : null;
                var opened = ManagedPartSession.Open(connection, packageRoot, Path.Combine(packageRoot, "working", "CADHarnessManagedPart.SLDPRT"), fault, ledger);
                Check(opened.Session is not null, "Cold reopen: " + opened.FailureCode + ": " + opened.Message); session = opened.Session!;
                var state = session.Store.Load(); var originalRevision = new AtomicStateStore(originalState).Load().Revision;
                var depth = step == "readback" ? 10 : 8;
                report.Before = Observe(session.Context, state); Verify(report.Before, depth, step == "readback" ? originalRevision + 1 : originalRevision);
                Check(session.PlanningRuntime().Capabilities.ParameterEdits.Any(e => e.Target == "plate" && e.Parameter == EditableParameter.ExtrusionDepth), "Cold context does not offer qualified depth edit.");
                if (step is "interrupt" or "recover-edit")
                {
                    if (step == "interrupt") NativeRefusals(session, state, report);
                    var priorFeature = state.Features.Single(f => f.SemanticId == "plate").NativeReference;
                    report.PointerBefore = ManagedRevisionStore.Hash(session.Store.PointerPath);
                    report.Edit = session.Edit(DepthEdit(10), state.Revision);
                    report.PointerAfter = ManagedRevisionStore.Hash(session.Store.PointerPath);
                    report.JournalRetained = File.Exists(session.Store.RecoveryPath);
                    if (report.JournalRetained == true)
                        report.RetainedRecoveryMarker = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(session.Store.RecoveryPath));
                    if (step == "interrupt")
                    {
                        Check(!report.Edit.Succeeded && report.Edit.FailureCode == "INJECTED_INTERRUPTED_PUBLISH" && report.Edit.RollbackSucceeded && !report.Edit.StateCommitted,
                            "Injected save boundary failure was not contained: " + JsonSerializer.Serialize(report.Edit));
                        report.SavedDiskDiffersFromAuthoritative = ManagedRevisionStore.Hash(session.Store.WorkingPath) != session.Store.ReadCurrent().Manifest.NativePart.Sha256;
                        Check(report.PointerBefore == report.PointerAfter && report.JournalRetained == true && report.SavedDiskDiffersFromAuthoritative == true && session.Status == ReopenStatus.Quarantined,
                            "Interrupted disk save must preserve prior pointer/checkpoint and require recovery.");
                        report.After = Observe(session.Context, state); Verify(report.After, 8, state.Revision);
                        Refusal("dispatch-after-interruption", () => session.PlanningRuntime(), V03FailureCodes.IncompleteDurablePublish, report);
                    }
                    else
                    {
                        Check(report.Edit.Succeeded && report.Edit.StateCommitted && report.Edit.Revision == state.Revision + 1,
                            "Recovered edit failed: " + JsonSerializer.Serialize(report.Edit));
                        var after = session.Store.Load(); report.After = Observe(session.Context, after); Verify(report.After, 10, after.Revision);
                        report.TargetPersistentReferencePreserved = priorFeature == after.Features.Single(f => f.SemanticId == "plate").NativeReference;
                        Check(report.TargetPersistentReferencePreserved == true && report.JournalRetained == false, "Identity or committed recovery journal is wrong.");
                    }
                }
                else
                {
                    report.After = report.Before;
                    Check(session.Context.Document.ShowConfiguration2("M12Other"), "Could not switch owned test configuration.");
                    Refusal("actual-native-configuration-switch", () => session.Context.VerifyManagedIdentity(state.Document), V03FailureCodes.ConfigurationMismatch, report);
                }
            }
            report.OriginalsPreserved = CheckOriginals(output); Check(report.OriginalsPreserved, "Legacy original hashes changed.");
            report.Status = "COMPLETE";
        }
        catch (Exception e) { report.FailureCode = e is ICadFailure cad ? cad.Code : e is ContractException contract ? contract.Code : "NATIVE_ACCEPTANCE_FAILED"; report.Error = e.ToString(); }
        finally
        {
            try
            {
                if (session is not null) { session.Dispose(); report.OriginalActiveRestored = session.OriginalActiveRestored; }
                CloseCreated();
                if (connection is not null && session is null)
                {
                    var error = 0;
                    if (originalActive is not null) connection.Application.ActivateDoc3(originalActive, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error);
                    report.OriginalActiveRestored = error == 0 && (originalActive is null ? connection.Application.IActiveDoc2 is null :
                        connection.Application.IActiveDoc2 is IModelDoc2 active && active.GetTitle() == originalActive);
                }
                report.NoOwnedDocumentRemains = ledger is not null && ledger.Data.OwnedTitles.Count == 0 &&
                    !ledger.Titles().Any(t => t.StartsWith("CADHarnessM12", StringComparison.Ordinal));
                if (!report.NoOwnedDocumentRemains || !report.OriginalActiveRestored) throw new StateException("TEST_CLEANUP_FAILED", "Owned document cleanup/active restoration incomplete.");
            }
            catch (Exception e) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; report.Error = (report.Error ?? "") + "\n" + e; }
            try { ledger?.Dispose(); connection?.Dispose(); }
            catch (Exception e) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; report.Error = (report.Error ?? "") + "\n" + e; }
            report.FinishedUtc = DateTime.UtcNow;
            WriteNew(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return report.Status == "COMPLETE" ? 0 : 1;
        void CloseCreated()
        {
            if (created is null || connection is null || ledger is null) return;
            var title = created.GetTitle(); connection.Application.CloseDoc(title);
            Check(!ledger.Titles().Contains(title), "Created owned Part did not close."); ledger.Closed(originalPart, title); created = null;
        }
    }
    private static void NativeRefusals(ManagedPartSession session, CadState state, WorkerReport report)
    {
        var program = session.Store.ReadCurrent().Program;
        void Restore(CadState changed) => new SolidWorksExecutionContext(session.Context.Document).RestoreManagedState(changed, program);
        Refusal("native-document-guid", () => Restore(state with { Document = state.Document with { DocumentId = Guid.NewGuid() } }), "DOCUMENT_IDENTITY_MISMATCH", report);
        Refusal("native-configuration-guid", () => Restore(state with { Document = state.Document with { ConfigurationId = Guid.NewGuid() } }), V03FailureCodes.ConfigurationMismatch, report);
        Refusal("native-configuration-name", () => Restore(state with { Document = state.Document with { ConfigurationName = "Other" } }), V03FailureCodes.ConfigurationMismatch, report);
        Refusal("native-save-as-lineage", () => Restore(state with { Document = state.Document with { SavedPath = Path.Combine(session.Store.Root, "another.SLDPRT") } }), "DOCUMENT_IDENTITY_MISMATCH", report);
        Refusal("native-stale-persistent-reference", () => Restore(state with { Features = state.Features.Select(f => f.SemanticId == "plate" ? f with { NativeReference = new("AQIDBA==") } : f).ToArray() }), "STALE_REFERENCE", report);
        var drifted = state with { Parameters = state.Parameters.Select(p => p.SemanticId == "plate.extrusion_depth" ? p with { Value = p.Value + 1 } : p).ToArray() };
        Refusal("native-state-scalar-drift", () => Restore(drifted), "RELATION_VIOLATED", report);
    }
    private static void Refusal(string name, Action action, string code, WorkerReport report)
    { PureTests.Refuse(action, code); report.Refusals.Add(name, code); }
    private static OperationNode DepthEdit(double value) => new("depth_edit", OperationKind.EditParameter, null,
        new[] { new OperationInput("target", new[] { new SemanticReference("plate", SemanticType.FeatureRef) }) },
        new Dictionary<string, OperationParameter> { ["parameter"] = new ParameterNameParameter(EditableParameter.ExtrusionDepth), ["value"] = new EditValueParameter(new LengthParameter(value)) });
    private static NativeObservation Observe(SolidWorksExecutionContext context, CadState state)
    {
        var body = ((Array)((IPartDoc)context.Document).GetBodies2(0, false)).Cast<object>().Cast<IBody2>().Single();
        var holes = new List<HoleObservation>();
        foreach (var face in ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>())
        {
            var surface = (ISurface)face.GetSurface(); if (!surface.IsCylinder()) continue;
            var c = (double[])surface.CylinderParams;
            var z = ((Array)face.GetEdges()).Cast<object>().Cast<IEdge>().Select(e => (ICurve)e.GetCurve()).Where(e => e.IsCircle())
                .Select(e => ((double[])e.CircleParams)[2] * 1000).Distinct().OrderBy(v => v).ToArray();
            holes.Add(new(c[0] * 1000, c[1] * 1000, c[6] * 2000, z));
        }
        var feature = (IFeature)context.NativeFeature("plate");
        return new(((IExtrudeFeatureData2)feature.GetDefinition()).GetDepth(true) * 1000, ExtrudeMeasurementReader.Read(context),
            ((double[])body.GetMassProperties(1))[3] * 1e9, holes.OrderBy(h => h.X).ThenBy(h => h.Y).ToArray(),
            state.Revision, state.Relations.Count, state.Dependencies.Count);
    }
    private static void Verify(NativeObservation native, double depth, long revision)
    {
        Near(native.DefinitionDepth, depth); Near(native.Extents.WidthMm, 100); Near(native.Extents.HeightMm, 60); Near(native.Extents.DepthMm, depth);
        Check(native.Extents.SolidBodyCount == 1 && native.Holes.Count == 4 && native.Relations == 5 && native.Dependencies > 0 && native.Revision == revision, "Native hole/relation/revision inventory differs.");
        var positions = new[] { (-30.0, -15.0), (-30.0, 15.0), (30.0, -15.0), (30.0, 15.0) };
        for (var i = 0; i < positions.Length; i++)
        {
            var h = native.Holes[i]; Near(h.X, positions[i].Item1); Near(h.Y, positions[i].Item2); Near(h.Diameter, 6);
            Check(h.BoundaryZ.Length == 2, "Through-hole native boundaries missing."); Near(h.BoundaryZ[0], 0); Near(h.BoundaryZ[1], depth);
        }
        Near(native.Holes.Average(h => h.X), 0); Near(native.Holes.Average(h => h.Y), 0);
        Near(native.Volume, (100 * 60 - 4 * Math.PI * 9) * depth);
    }
    private static bool CheckOriginals(string output)
    {
        var fingerprints = JsonSerializer.Deserialize<FileFingerprint[]>(File.ReadAllText(Path.Combine(output, "original-hashes.json")))!;
        return fingerprints.All(f => ManagedRevisionStore.Hash(f.Path) == f.Sha256 && new FileInfo(f.Path).Length == f.SizeBytes);
    }
    private static void CheckPrerequisite(string output, string step)
    {
        var previous = step == "migrate" ? "create" : step == "interrupt" ? (File.Exists(Path.Combine(output, "worker-migrate.json")) ? "migrate" : "create") : step == "recover-edit" ? "interrupt" : "recover-edit";
        var previousPath = Path.Combine(output, "worker-" + previous + ".json");
        if (step == "migrate" && !File.Exists(previousPath)) previousPath = Path.Combine(Path.GetDirectoryName(output)!, "worker-create.json");
        var report = JsonSerializer.Deserialize<WorkerReport>(File.ReadAllText(previousPath))!;
        if (step == "migrate")
        {
            Check(report.Creation?.Succeeded == true && report.Before is { DefinitionDepth: 8, Revision: 1, Relations: 5 } &&
                report.NoOwnedDocumentRemains && report.OriginalActiveRestored && report.ControllerPid != System.Environment.ProcessId,
                "Continuation requires the already saved/closed owned original, not a repeated creation attempt.");
            return;
        }
        Check(report.Status == "COMPLETE" && report.ControllerPid != System.Environment.ProcessId, "Previous frozen controller did not complete or process was reused.");
    }
    private static void VerifyFreeze(string root, string output)
    {
        var continuation = Path.Combine(output, "continuation-v2", "native-freeze.json");
        var path = File.Exists(continuation) ? continuation : Path.Combine(output, "native-freeze.json");
        Check(ManagedRevisionStore.Hash(path) == File.ReadAllText(path + ".sha256").Trim(), "Native freeze checksum drift.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("authorizationSha256", out var authorization))
            Check(ManagedRevisionStore.Hash(Path.Combine(output, "authorization.json")) == authorization.GetString(), "Explicit budget authorization drift.");
        foreach (var file in document.RootElement.GetProperty("files").EnumerateArray())
            Check(ManagedRevisionStore.Hash(Path.Combine(root, file.GetProperty("path").GetString()!)) == file.GetProperty("sha256").GetString(), "Source/binary drift after formal native freeze.");
        var template = document.RootElement.GetProperty("template");
        Check(ManagedRevisionStore.Hash(template.GetProperty("path").GetString()!) == template.GetProperty("sha256").GetString(), "Native template drift after freeze.");
    }
    private static void Near(double a, double b) => Check(double.IsFinite(a) && Math.Abs(a - b) < 0.001, $"Native oracle differs: {a} vs {b}");
    private static void Check(bool condition, string message) { if (!condition) throw new StateException("NATIVE_ACCEPTANCE_FAILED", message); }
    private static void WriteNew(string path, string text)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); var bytes = System.Text.Encoding.UTF8.GetBytes(text); stream.Write(bytes); stream.Flush(true); }
}
