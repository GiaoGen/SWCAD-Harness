using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;

internal static class PureTests
{
    private sealed class FakeNative : IManagedRevisionNative
    {
        internal string Path = "";
        internal double Depth = 8;
        internal bool SaveFails, ReadFails;
        internal bool DriftDuringRead;
        internal int Saves, Reads;
        public CadProgram CurrentProgram => Program(Depth);
        public void SaveNative()
        { Saves++; if (SaveFails) throw new StateException("NATIVE_SAVE_FAILED", "injected native failure"); File.WriteAllText(Path, Bytes(Depth)); }
        public void VerifySavedRevision(CadState state, CadProgram program)
        {
            Reads++;
            if (ReadFails || File.ReadAllText(Path) != Bytes(state.Parameters.Single().Value))
                throw new StateException("NATIVE_REOPEN_FAILED", "independent fake saved read refusal");
            if (DriftDuringRead) File.AppendAllText(Path, "drift during native read");
        }
    }
    private sealed class Package : IDisposable
    {
        internal readonly FakeNative Native = new();
        internal readonly string Root;
        internal ManagedRevisionStore Store;
        internal DurableFaultPoint? Fault;
        internal readonly Guid DocumentId = Guid.NewGuid(), ConfigurationId = Guid.NewGuid();
        internal Package(string parent)
        {
            Root = System.IO.Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Store = NewStore(); Native.Path = Store.WorkingPath;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Native.Path)!);
            File.WriteAllText(Native.Path, Bytes(8)); Store.Initialize(State(0, 8));
        }
        private ManagedRevisionStore NewStore() => new(Root, Native, point =>
        { if (Fault == point) throw new StateException("INJECTED_DURABLE_FAILURE", point.ToString()); });
        internal void Restart() { Store.Dispose(); Store = NewStore(); }
        internal CadState State(long revision, double depth) => new()
        {
            SchemaVersion = "0.2", Document = new(DocumentId, ConfigurationId, "Default", Store.WorkingPath), Revision = revision,
            Features = new[] { new FeatureNode("plate", OperationKind.CreateExtrude, new("AQID"), ReferenceHealth.Healthy) },
            Entities = new[] { new SemanticEntityNode("plate", SemanticType.FeatureRef, "plate", new("AQID"), ReferenceHealth.Healthy) },
            Parameters = new[] { new ParameterNode("plate.extrusion_depth", ParameterKind.Length, depth) },
            Bindings = new[] { new ParameterBinding("plate.extrusion_depth", "plate", EditableParameter.ExtrusionDepth) },
            Relations = Array.Empty<JsonElement>(), Dependencies = Array.Empty<JsonElement>()
        };
        internal void Edit() { Store.PrepareCheckpoint(); Native.Depth = 10; Store.Commit(State(1, 10)); }
        public void Dispose() => Store.Dispose();
    }
    private static CadProgram Program(double depth) => new CadProgramJson().Parse(
        "{\"programVersion\":\"0.2\",\"operations\":[{\"id\":\"base\",\"kind\":\"create_extrude\",\"semanticId\":\"plate\",\"profile\":{\"kind\":\"centered_rectangle\",\"widthMm\":100,\"heightMm\":60},\"depthMm\":" + depth.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}],\"relations\":[]}").Program!;
    private static string Bytes(double depth) => "fake-native-depth=" + depth.ToString(System.Globalization.CultureInfo.InvariantCulture);
    internal static int Run(string workspace)
    {
        var output = Path.Combine(workspace, "artifacts", "milestone12", "pure", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));
        Directory.CreateDirectory(output);
        var tests = new List<(string Name, Action Run)>();
        void Add(string name, Action<Package> run) => tests.Add((name, () => { using var p = new Package(output); run(p); }));
        tests.Add(("native initial base and boss are separately recognized", () =>
        {
            var blind = (int)SolidWorks.Interop.swconst.swEndConditions_e.swEndCondBlind;
            Check(ManagedHistoryQualification.Extrude(OperationKind.CreateExtrude, false, true, false, blind));
            Check(ManagedHistoryQualification.Extrude(OperationKind.CreateExtrude, true, false, false, blind));
            Check(!ManagedHistoryQualification.Extrude(OperationKind.CreateExtrude, false, false, false, blind));
        }));
        tests.Add(("thin through-all bosses and base-as-cut histories refuse", () =>
        {
            var blind = (int)SolidWorks.Interop.swconst.swEndConditions_e.swEndCondBlind;
            var through = (int)SolidWorks.Interop.swconst.swEndConditions_e.swEndCondThroughAll;
            Check(!ManagedHistoryQualification.Extrude(OperationKind.CreateExtrude, false, true, true, blind));
            Check(!ManagedHistoryQualification.Extrude(OperationKind.CreateExtrude, true, false, false, through));
            Check(!ManagedHistoryQualification.Extrude(OperationKind.CreateThroughHole, false, true, false, through));
            Check(ManagedHistoryQualification.Extrude(OperationKind.CreateThroughHole, false, false, false, through));
        }));
        tests.Add(("native edit invalidates saved-read proof even with identical bytes and clean flag", () =>
        {
            var proof = new SavedNativeReadProof(); var hash = new string('a', 64);
            proof.RecordOpened(hash); Check(proof.IsCurrent(hash, false)); proof.Invalidate();
            Check(!proof.IsCurrent(hash, false)); proof.RecordOpened(hash); Check(proof.IsCurrent(hash, false));
        }));
        tests.Add(("dirty native document and hash drift cannot reuse opened proof", () =>
        {
            var proof = new SavedNativeReadProof(); var hash = new string('a', 64); proof.RecordOpened(hash);
            Check(!proof.IsCurrent(hash, true)); Check(!proof.IsCurrent(new string('b', 64), false));
        }));
        Add("initial package exact hashes and companion versions", p =>
        {
            var current = p.Store.ReadCurrent(); Check(current.State.Revision == 0 && current.Manifest.State.SchemaVersion == "0.2" &&
                current.Manifest.Program!.SchemaVersion == "0.2" && current.Manifest.SchemaVersion == "0.3" && p.Native.Reads == 1);
            Check(ManagedRevisionStore.Hash(current.Manifest.NativePart.Path) == current.Manifest.NativePart.Sha256);
        });
        Add("successful scalar commit retains prior complete package", p =>
        {
            var old = p.Store.ReadCurrent(); p.Edit(); var now = p.Store.ReadCurrent();
            Check(now.State.Revision == 1 && now.Manifest.PreviousManifestSha256 == old.ManifestArtifact.Sha256 &&
                p.Store.ReadRevision(old.ManifestArtifact).State.Revision == 0 && !File.Exists(p.Store.RecoveryPath));
        });
        Add("saved native hash permits existing writable application handle", p =>
        {
            using var applicationHandle = new FileStream(p.Store.WorkingPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            Check(ManagedRevisionStore.Hash(p.Store.WorkingPath) == p.Store.ReadCurrent().Manifest.NativePart.Sha256);
            p.Store.PrepareCheckpoint(); Check(p.Store.Inspect(p.Store.WorkingPath).Revision is not null);
        });
        foreach (var point in Enum.GetValues<DurableFaultPoint>())
        {
            var captured = point;
            Add("restart authority after " + point, p =>
            {
                var before = p.Store.ReadCurrent(); p.Fault = captured;
                if (captured == DurableFaultPoint.AfterPointerPublish) p.Edit(); else Refuse(p.Edit, "INJECTED_DURABLE_FAILURE");
                p.Restart(); var inspection = p.Store.Inspect(p.Store.WorkingPath);
                var expected = captured == DurableFaultPoint.AfterPointerPublish ? 1 : 0;
                Check(inspection.FailureCode is null && inspection.RequiresNativeRecovery && inspection.Revision!.State.Revision == expected);
                Refuse(() => p.Store.Load(), V03FailureCodes.IncompleteDurablePublish);
                p.Store.RestoreWorkingCopy(inspection); p.Fault = null; p.Native.Depth = expected == 1 ? 10 : 8;
                p.Store.CompleteRecovery(inspection); Check(p.Store.Load().Revision == expected);
                Check(File.Exists(before.ManifestArtifact.Path));
            });
        }
        Add("native save failure preserves old pointer", p =>
        { var before = File.ReadAllBytes(p.Store.PointerPath); p.Native.SaveFails = true; Refuse(p.Edit, "NATIVE_SAVE_FAILED"); Check(before.SequenceEqual(File.ReadAllBytes(p.Store.PointerPath))); });
        Add("independent saved read failure cannot publish", p =>
        { p.Native.ReadFails = true; Refuse(p.Edit, "NATIVE_REOPEN_FAILED"); Check(p.Store.ReadCurrent().State.Revision == 0); });
        Add("saved bytes drifting during independent read cannot publish", p =>
        { p.Native.DriftDuringRead = true; Refuse(p.Edit, V03FailureCodes.SourceFileDrift); Check(p.Store.ReadCurrent().State.Revision == 0); });
        foreach (var revision in new long[] { 0, 7 })
        {
            var initialRevision = revision;
            Add("initial post-pointer recovery retains starting revision " + revision, p =>
            {
                var root = Path.Combine(p.Root, "initial"); var native = new FakeNative();
                using var store = new ManagedRevisionStore(root, native, point =>
                { if (point == DurableFaultPoint.AfterPointerPublish) throw new StateException("INJECTED_DURABLE_FAILURE", "initial pointer published"); });
                native.Path = store.WorkingPath;
                Directory.CreateDirectory(Path.GetDirectoryName(native.Path)!); File.WriteAllText(native.Path, Bytes(8));
                var state = p.State(initialRevision, 8); state = state with { Document = state.Document with { SavedPath = store.WorkingPath } };
                store.Initialize(state); var result = store.Inspect(store.WorkingPath);
                Check(result.Revision!.State.Revision == initialRevision && result.FailureCode is null && result.RequiresNativeRecovery);
                store.RestoreWorkingCopy(result); store.CompleteRecovery(result); Check(store.Load().Revision == initialRevision);
            });
        }
        Add("initial interruption without complete pointer remains quarantined", p =>
        {
            var native = new FakeNative(); using var store = new ManagedRevisionStore(Path.Combine(p.Root, "initial"), native, point =>
            { if (point == DurableFaultPoint.BeforePointerPublish) throw new StateException("INJECTED_DURABLE_FAILURE", "no initial pointer"); });
            native.Path = store.WorkingPath; Directory.CreateDirectory(Path.GetDirectoryName(native.Path)!); File.WriteAllText(native.Path, Bytes(8));
            var state = p.State(7, 8); state = state with { Document = state.Document with { SavedPath = store.WorkingPath } };
            Refuse(() => store.Initialize(state), "INJECTED_DURABLE_FAILURE");
            Check(store.Inspect(store.WorkingPath).Status == ReopenStatus.Quarantined && !File.Exists(store.PointerPath));
        });
        Add("working file drift refused without recovery marker", p =>
        { File.AppendAllText(p.Store.WorkingPath, "external"); var result = p.Store.Inspect(p.Store.WorkingPath); Check(result.FailureCode == V03FailureCodes.SourceFileDrift && result.Status == ReopenStatus.Inspectable); });
        Add("Save As different selected path refused", p => Check(p.Store.Inspect(Path.Combine(p.Root, "other.SLDPRT")).FailureCode == "DOCUMENT_IDENTITY_MISMATCH"));
        Add("missing native file refused", p => { File.Delete(p.Store.WorkingPath); Check(p.Store.Inspect(p.Store.WorkingPath).FailureCode == V03FailureCodes.SourceFileDrift); });
        foreach (var sidecar in new[] { "state", "program", "manifest", "native" })
        {
            var which = sidecar;
            Add("missing committed " + which + " refused", p =>
            { File.Delete(ArtifactPath(p.Store.ReadCurrent(), which)); Check(p.Store.Inspect(p.Store.WorkingPath).Revision is null); });
            Add("hash drift in " + which + " refused", p =>
            { File.AppendAllText(ArtifactPath(p.Store.ReadCurrent(), which), "tampered"); Check(p.Store.Inspect(p.Store.WorkingPath).FailureCode == V03FailureCodes.SourceFileDrift); });
        }
        Add("wrong document GUID refuses publish before save", p =>
        { p.Store.PrepareCheckpoint(); var s = p.State(1, 10); Refuse(() => p.Store.Commit(s with { Document = s.Document with { DocumentId = Guid.NewGuid() } }), "TRANSACTION_INTEGRITY_FAILED"); Check(p.Native.Saves == 1); });
        Add("wrong configuration GUID refuses publish before save", p =>
        { p.Store.PrepareCheckpoint(); var s = p.State(1, 10); Refuse(() => p.Store.Commit(s with { Document = s.Document with { ConfigurationId = Guid.NewGuid() } }), "TRANSACTION_INTEGRITY_FAILED"); });
        Add("configuration name mismatch refuses publish", p =>
        { p.Store.PrepareCheckpoint(); var s = p.State(1, 10); Refuse(() => p.Store.Commit(s with { Document = s.Document with { ConfigurationName = "Other" } }), "TRANSACTION_INTEGRITY_FAILED"); });
        Add("revision jump refuses publish", p => { p.Store.PrepareCheckpoint(); Refuse(() => p.Store.Commit(p.State(2, 10)), "TRANSACTION_INTEGRITY_FAILED"); });
        Add("no native checkpoint refuses publish", p => Refuse(() => p.Store.Commit(p.State(1, 10)), V03FailureCodes.IncompleteDurablePublish));
        Add("pending checkpoint blocks another checkpoint", p => { p.Store.PrepareCheckpoint(); Refuse(p.Store.PrepareCheckpoint, V03FailureCodes.IncompleteDurablePublish); });
        Add("exclusive controller lease", p => Refuse(() => { using var other = new ManagedRevisionStore(p.Root, p.Native); }, "TRANSACTION_BUSY"));
        Add("unknown pointer field refused", p =>
        { var node = JsonNode.Parse(File.ReadAllText(p.Store.PointerPath))!; node["extra"] = true; File.WriteAllText(p.Store.PointerPath, node.ToJsonString()); Check(p.Store.Inspect(p.Store.WorkingPath).Revision is null); });
        Add("pointer cannot escape owned revisions", p =>
        { var node = JsonNode.Parse(File.ReadAllText(p.Store.PointerPath))!; node["manifest"]!["path"] = Path.Combine(output, "outside.json"); File.WriteAllText(p.Store.PointerPath, node.ToJsonString()); Check(p.Store.Inspect(p.Store.WorkingPath).Revision is null); });
        Add("unknown recovery field quarantines", p =>
        { p.Store.PrepareCheckpoint(); var node = JsonNode.Parse(File.ReadAllText(p.Store.RecoveryPath))!; node["extra"] = true; File.WriteAllText(p.Store.RecoveryPath, node.ToJsonString()); Check(p.Store.Inspect(p.Store.WorkingPath).Status == ReopenStatus.Quarantined); });
        Add("journal path escape quarantines", p =>
        { p.Store.PrepareCheckpoint(); var node = JsonNode.Parse(File.ReadAllText(p.Store.RecoveryPath))!; node["candidateDirectory"] = output; File.WriteAllText(p.Store.RecoveryPath, node.ToJsonString()); Check(p.Store.Inspect(p.Store.WorkingPath).Status == ReopenStatus.Quarantined); });
        Add("corrupt pointer with journal restores exact previous", p =>
        { p.Store.PrepareCheckpoint(); File.WriteAllText(p.Store.PointerPath, "broken"); var result = p.Store.Inspect(p.Store.WorkingPath); Check(result.Revision!.State.Revision == 0); p.Store.RestoreWorkingCopy(result); p.Store.CompleteRecovery(result); Check(p.Store.Load().Revision == 0); });
        Add("no provable current or previous quarantines", p =>
        { p.Store.PrepareCheckpoint(); File.Delete(p.Store.ReadCurrent().Manifest.State.Path); Check(p.Store.Inspect(p.Store.WorkingPath).Status == ReopenStatus.Quarantined); Check(File.Exists(p.Store.RecoveryPath)); });
        Add("unpublished candidate never discovered by directory order", p =>
        { p.Fault = DurableFaultPoint.BeforePointerPublish; Refuse(p.Edit, "INJECTED_DURABLE_FAILURE"); Check(p.Store.Inspect(p.Store.WorkingPath).Revision!.State.Revision == 0); });
        Add("forged recovery authority cannot restore", p =>
        { var old = p.Store.ReadCurrent(); p.Edit(); p.Store.PrepareCheckpoint(); var result = p.Store.Inspect(p.Store.WorkingPath) with { Revision = old }; Refuse(() => p.Store.RestoreWorkingCopy(result), V03FailureCodes.IncompleteDurablePublish); });
        Add("native recovery verification failure retains quarantine", p =>
        { p.Store.PrepareCheckpoint(); var result = p.Store.Inspect(p.Store.WorkingPath); p.Store.RestoreWorkingCopy(result); p.Native.ReadFails = true; Refuse(() => p.Store.CompleteRecovery(result), "NATIVE_REOPEN_FAILED"); Check(File.Exists(p.Store.RecoveryPath)); });
        Add("state program ownership association cannot invent feature", p =>
        { var s = p.State(0, 8); Refuse(() => ManagedRevisionStore.ValidateAssociation(s with { Features = s.Features.Select(f => f with { Kind = OperationKind.ApplyFillet }).ToArray() }, Program(8)), "STATE_DRIFT_DETECTED"); });
        Add("migration evidence cannot claim native proof from pure read", p =>
        { var r = p.Store.ReadCurrent(); var evidence = new ManagedMigrationEvidence("0.3", "0.2", "0.3", "0.2", r.Manifest.NativePart, r.Manifest.State, r.Manifest.Program!, r.Manifest.State.Path, false, true, "MIGRATED_COPY_NATIVE_VERIFIED"); Refuse(() => ManagedPartSession.ValidateMigration(evidence), V03FailureCodes.MigrationFailed); });
        var results = new List<object>(); var failed = 0;
        foreach (var test in tests)
        {
            string? error = null;
            try { test.Run(); } catch (Exception e) { error = e.ToString(); failed++; }
            Console.WriteLine((error is null ? "PASS " : "FAIL ") + test.Name + (error is null ? "" : ": " + error));
            results.Add(new { name = test.Name, passed = error is null, error });
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        { milestone = 12, passed = tests.Count - failed, failed, nativeParts = 0, nativeOpens = 0, results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"M12 pure {tests.Count - failed}/{tests.Count}; results: {output}"); return failed == 0 ? 0 : 1;
    }
    private static string ArtifactPath(ManagedRevision revision, string kind) => kind switch
    { "state" => revision.Manifest.State.Path, "program" => revision.Manifest.Program!.Path, "manifest" => revision.ManifestArtifact.Path, _ => revision.Manifest.NativePart.Path };
    internal static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Acceptance assertion failed."); }
    internal static void Refuse(Action action, string code)
    {
        try { action(); } catch (Exception e) when (e is ICadFailure failure && failure.Code == code) { return; }
        catch (ContractException e) when (e.Code == code) { return; }
        throw new InvalidOperationException("Expected typed refusal: " + code);
    }
}
