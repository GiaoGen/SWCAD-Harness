using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State.V03;

namespace CadHarness.State;

public enum DurableFaultPoint { BeforeNativeSave, AfterNativeSave, AfterStateFlush, AfterPackageVerification, BeforePointerPublish, AfterPointerPublish }
public sealed record ManagedRevisionPointer(string SchemaVersion, ArtifactIdentity Manifest);
public sealed record ManagedRecoveryMarker(string SchemaVersion, ArtifactIdentity? PreviousManifest, long PreviousRevision,
    string WorkingPath, string CandidateDirectory, long CandidateRevision);
public sealed record ManagedRevision(RevisionManifest Manifest, ArtifactIdentity ManifestArtifact, CadState State,
    CadProgram? ConstructionProgram, ExternalEditState? External = null)
{
    public CadProgram Program => ConstructionProgram ?? throw new StateException(V03FailureCodes.ModeMismatch,
        "An external observed revision has no construction program.");
}
public sealed record ManagedRecoveryInspection(ReopenStatus Status, string? FailureCode, string Message,
    ManagedRevision? Revision, bool RequiresNativeRecovery);
public sealed record ManagedMigrationEvidence(string SchemaVersion, string OldStateVersion, string NewManifestVersion,
    string PreservedStateVersion, FileFingerprint OriginalPart, ArtifactIdentity OriginalState, ArtifactIdentity OriginalProgram,
    string RollbackPath, bool NativeReopenVerified, bool OriginalsPreserved, string Result);

public interface IManagedRevisionNative
{
    CadProgram CurrentProgram { get; }
    void SaveNative();
    void VerifySavedRevision(CadState state, CadProgram program);
}

public interface IObservedRevisionNative : IManagedRevisionNative
{
    ExternalEditState CurrentExternal { get; }
    void VerifySavedExternal(ExternalEditState state);
    CadProgram IManagedRevisionNative.CurrentProgram => throw new StateException(V03FailureCodes.ModeMismatch, "External state has no CadProgram.");
    void IManagedRevisionNative.VerifySavedRevision(CadState state, CadProgram program) =>
        throw new StateException(V03FailureCodes.ModeMismatch, "Cannot reinterpret an external revision as managed.");
}

// Native save and JSON state are not atomic together. Only the small pointer is
// replaced atomically, after a saved native revision has been independently read.
public sealed class ManagedRevisionStore : ICadStateStore, IDisposable
{
    private const int MaximumMetadataBytes = 1048576;
    private readonly FileStream lease;
    private readonly IManagedRevisionNative native;
    private readonly Action<DurableFaultPoint>? fault;
    private bool disposed;
    public string Root { get; }
    public string WorkingPath => Path.Combine(Root, "working", "CADHarnessManagedPart.SLDPRT");
    public string PointerPath => Path.Combine(Root, "current.json");
    public string RecoveryPath => Path.Combine(Root, "recovery.json");
    private string RevisionsPath => Path.Combine(Root, "revisions");

    public ManagedRevisionStore(string root, IManagedRevisionNative native, Action<DurableFaultPoint>? fault = null)
    {
        Root = Path.GetFullPath(root); this.native = native; this.fault = fault;
        Directory.CreateDirectory(Root); RejectReparse(Root);
        try { lease = new FileStream(Path.Combine(Root, "controller.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new StateException("TRANSACTION_BUSY", "Another controller owns this managed package.", e); }
    }
    public CadState Load()
    {
        Check();
        if (File.Exists(RecoveryPath)) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Reconcile the durable recovery marker before dispatch.");
        var revision = ReadCurrent(); VerifyWorking(revision); return revision.State;
    }
    public ManagedRevision ReadCurrent()
    {
        Check();
        try
        {
            var pointer = ContractJson.Read<ManagedRevisionPointer>(ReadText(PointerPath), ValidatePointer, MaximumMetadataBytes);
            return ReadRevision(pointer.Manifest);
        }
        catch (ContractException e) { throw new StateException(V03FailureCodes.IncompleteDurablePublish, e.Message, e); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Managed revision pointer is absent or unreadable.", e); }
    }
    public void PrepareCheckpoint()
    {
        var previous = ReadCurrent(); VerifyWorking(previous);
        if (File.Exists(RecoveryPath)) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Unreconciled previous publish.");
        WriteMarker(previous);
    }
    public void Initialize(CadState state)
    {
        Check();
        if (File.Exists(PointerPath) || File.Exists(RecoveryPath)) throw new StateException("TRANSACTION_BUSY", "A package already exists or needs recovery.");
        ValidateStatePath(state); WriteMarker(null, state.Revision); Publish(state, null);
    }
    public void Commit(CadState state)
    {
        Check();
        var previous = ReadCurrent();
        ValidateStatePath(state);
        if (!state.Document.Matches(previous.State.Document) || state.Revision != checked(previous.State.Revision + 1))
            throw new StateException("TRANSACTION_INTEGRITY_FAILED", "Publish must preserve identity and advance one revision.");
        if (!File.Exists(RecoveryPath)) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "No batch-start file checkpoint was captured.");
        var marker = ReadMarker();
        if (marker.PreviousManifest != previous.ManifestArtifact || marker.PreviousRevision != previous.State.Revision)
            throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Checkpoint does not name the authoritative revision.");
        Publish(state, previous);
    }
    private void Publish(CadState state, ManagedRevision? previous)
    {
        var marker = ReadMarker(); var directory = marker.CandidateDirectory;
        Directory.CreateDirectory(directory); RejectReparse(directory);
        fault?.Invoke(DurableFaultPoint.BeforeNativeSave);
        if (previous is not null) VerifyWorking(previous);
        ValidateNativeAssociation(state);
        native.SaveNative();
        fault?.Invoke(DurableFaultPoint.AfterNativeSave);
        var nativePath = Path.Combine(directory, "part.SLDPRT"); CopyFlushed(WorkingPath, nativePath);
        var statePath = Path.Combine(directory, "state.json");
        ArtifactIdentity? programArtifact = null;
        if (native is IObservedRevisionNative observed)
        {
            ValidateNativeAssociation(state);
            WriteNew(statePath, ContractJson.Write(observed.CurrentExternal, ExternalEditPlanning.Validate));
        }
        else
        {
            new AtomicStateStore(statePath).Commit(state);
            var programPath = Path.Combine(directory, "program.json");
            var programJson = new CadProgramJson().Serialize(native.CurrentProgram);
            var parsed = new CadProgramJson().Parse(programJson);
            if (!parsed.IsValid) throw new StateException("PROGRAM_SCHEMA_INVALID", "Durable program is not strict v0.2.");
            WriteNew(programPath, programJson); programArtifact = Artifact(programPath, "0.2");
        }
        fault?.Invoke(DurableFaultPoint.AfterStateFlush);
        if (native is IObservedRevisionNative saved) saved.VerifySavedExternal(saved.CurrentExternal);
        else native.VerifySavedRevision(state, native.CurrentProgram);
        if (Hash(WorkingPath) != Hash(nativePath)) throw new StateException(V03FailureCodes.SourceFileDrift, "Native bytes changed during saved-package verification.");
        var manifest = new RevisionManifest("0.3", native is IObservedRevisionNative ? ModelOrigin.External : ModelOrigin.Harness, state.Document.DocumentId, state.Document.ConfigurationId,
            state.Document.ConfigurationName, state.Revision, Fingerprint(nativePath), Artifact(statePath, native is IObservedRevisionNative ? "0.3" : "0.2"), programArtifact,
            previous?.ManifestArtifact.Sha256, PublishStage.PointerPublished);
        var manifestPath = Path.Combine(directory, "manifest.json");
        WriteNew(manifestPath, ContractJson.Write(manifest, ObservedStateValidation.Manifest));
        var manifestArtifact = Artifact(manifestPath, "0.3");
        _ = ReadRevision(manifestArtifact);
        fault?.Invoke(DurableFaultPoint.AfterPackageVerification);
        fault?.Invoke(DurableFaultPoint.BeforePointerPublish);
        WritePointer(manifestArtifact);
        // Publication is the commit point. Subsequent maintenance cannot turn a
        // committed revision into an ordinary failed scalar transaction.
        try { fault?.Invoke(DurableFaultPoint.AfterPointerPublish); File.Delete(RecoveryPath); }
        catch (Exception) { /* A retained marker is reconciled before the next dispatch. */ }
    }
    private void WriteMarker(ManagedRevision? previous, long? initialRevision = null)
    {
        var marker = new ManagedRecoveryMarker("0.3", previous?.ManifestArtifact, previous?.State.Revision ?? -1,
            WorkingPath, Path.Combine(RevisionsPath, "candidate_" + Guid.NewGuid().ToString("N")),
            previous is null ? initialRevision!.Value : checked(previous.State.Revision + 1));
        WriteNew(RecoveryPath, ContractJson.Write(marker, ValidateMarker));
    }
    public ManagedRecoveryInspection Inspect(string selectedPartPath)
    {
        try
        {
            Check();
            if (!SamePath(selectedPartPath, WorkingPath)) throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "Save As or a different selected Part is not this package's working copy.");
            if (!File.Exists(RecoveryPath))
            { var complete = ReadCurrent(); VerifyWorking(complete); return new(ReopenStatus.Inspectable, null, "Package hashes verified; native readback is required.", complete, false); }
            var marker = ReadMarker();
            ManagedRevision? current = null;
            try { current = ReadCurrent(); } catch (StateException) { }
            if (current is not null && (current.ManifestArtifact == marker.PreviousManifest ||
                SamePath(current.ManifestArtifact.Path, Path.Combine(marker.CandidateDirectory, "manifest.json")) &&
                current.Manifest.PreviousManifestSha256 == marker.PreviousManifest?.Sha256 && current.State.Revision == marker.CandidateRevision))
                return new(ReopenStatus.Inspectable, null, "Journaled publish requires native recovery verification.", current, true);
            if (marker.PreviousManifest is not null)
            { var previous = ReadRevision(marker.PreviousManifest); return new(ReopenStatus.Inspectable, null, "Only the journaled previous complete revision is provable.", previous, true); }
            return new(ReopenStatus.Quarantined, V03FailureCodes.IncompleteDurablePublish, "No complete authoritative revision exists; preserve recovery evidence.", null, true);
        }
        catch (Exception e) when (e is StateException or ContractException or IOException or UnauthorizedAccessException)
        {
            var code = e is StateException failure ? failure.Code : V03FailureCodes.IncompleteDurablePublish;
            return new(File.Exists(RecoveryPath) ? ReopenStatus.Quarantined : ReopenStatus.Inspectable, code, e.Message, null, File.Exists(RecoveryPath));
        }
    }
    // Restore only the exact manifest named by the journal, never discover a
    // revision by directory order, displayed feature name, or nearest geometry.
    public void RestoreWorkingCopy(ManagedRecoveryInspection inspection)
    {
        Check();
        if (!inspection.RequiresNativeRecovery || inspection.Revision is null || inspection.FailureCode is not null)
            throw new StateException(V03FailureCodes.IncompleteDurablePublish, "No proven journaled checkpoint is available.");
        RequireRecoveryAuthority(inspection);
        var revision = ReadRevision(inspection.Revision.ManifestArtifact);
        PrepareWorkingDirectory();
        ReplaceCopy(revision.Manifest.NativePart.Path, WorkingPath);
        VerifyWorking(revision);
    }
    public void CompleteRecovery(ManagedRecoveryInspection inspection)
    {
        Check(); var revision = inspection.Revision ?? throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Missing recovery revision.");
        if (!inspection.RequiresNativeRecovery) return;
        RequireRecoveryAuthority(inspection);
        VerifyWorking(revision);
        if (native is IObservedRevisionNative observed && revision.External is not null) observed.VerifySavedExternal(revision.External);
        else native.VerifySavedRevision(revision.State, revision.Program);
        VerifyWorking(revision);
        WritePointer(revision.ManifestArtifact); File.Delete(RecoveryPath);
    }
    private void RequireRecoveryAuthority(ManagedRecoveryInspection inspection)
    {
        var current = Inspect(WorkingPath);
        if (!current.RequiresNativeRecovery || current.FailureCode is not null || current.Revision is null ||
            current.Revision.ManifestArtifact != inspection.Revision?.ManifestArtifact)
            throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Recovery authority changed or was not journaled.");
    }
    public void CopyInitialNative(string source)
    {
        Check();
        if (File.Exists(PointerPath) || File.Exists(RecoveryPath)) throw new StateException("TRANSACTION_BUSY", "Initial copy cannot replace a managed package.");
        PrepareWorkingDirectory(); CopyFlushed(source, WorkingPath);
    }
    private void PrepareWorkingDirectory()
    {
        GuardPath(WorkingPath, Root);
        Directory.CreateDirectory(Path.GetDirectoryName(WorkingPath)!);
        GuardPath(WorkingPath, Root);
    }
    public ManagedRevision ReadRevision(ArtifactIdentity artifact)
    {
        Check(); GuardArtifact(artifact, RevisionsPath);
        if (artifact.SchemaVersion != "0.3") throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Manifest version is not v0.3.");
        VerifyArtifact(artifact);
        var manifest = ContractJson.Read<RevisionManifest>(ReadText(artifact.Path), ObservedStateValidation.Manifest, MaximumMetadataBytes);
        if (manifest.Stage != PublishStage.PointerPublished || (manifest.Origin == ModelOrigin.External) != (native is IObservedRevisionNative))
            throw new StateException(V03FailureCodes.ModeMismatch, "Revision origin differs from the native controller mode.");
        var directory = Path.GetDirectoryName(artifact.Path)!;
        GuardPath(manifest.NativePart.Path, directory); GuardArtifact(manifest.State, directory);
        if (manifest.Origin == ModelOrigin.External)
        {
            if (manifest.Program is not null || manifest.State.SchemaVersion != "0.3")
                throw new StateException(V03FailureCodes.ModeMismatch, "External packages contain only observed state, never a synthetic program.");
            VerifyFingerprint(manifest.NativePart); VerifyArtifact(manifest.State);
            var external = ContractJson.Read<ExternalEditState>(ReadText(manifest.State.Path), ExternalEditPlanning.Validate, MaximumMetadataBytes);
            var adapter = ExternalEditPlanning.Adapter(external); ValidateStatePath(adapter);
            VerifyManifestIdentity(manifest, adapter);
            if (external.Observation.Selection.WorkingCopy.Sha256 != manifest.NativePart.Sha256 ||
                external.Observation.Selection.WorkingCopy.SizeBytes != manifest.NativePart.SizeBytes)
                throw new StateException(V03FailureCodes.SourceFileDrift, "Observed working hash differs from native snapshot.");
            return new(manifest, artifact, adapter, null, external);
        }
        if (manifest.Program is null) throw new StateException(V03FailureCodes.ModeMismatch, "Managed revision requires a program.");
        GuardArtifact(manifest.Program, directory);
        if (manifest.State.SchemaVersion != "0.2" || manifest.Program.SchemaVersion != "0.2")
            throw new StateException(FailureCodes.OperationUnsupported, "M12 reopens qualified v0.2 programs/state with a v0.3 manifest.");
        VerifyFingerprint(manifest.NativePart); VerifyArtifact(manifest.State); VerifyArtifact(manifest.Program);
        var state = new AtomicStateStore(manifest.State.Path).Load(); ValidateStatePath(state);
        var parsed = new CadProgramJson().Parse(ReadText(manifest.Program.Path));
        if (!parsed.IsValid) throw new StateException("PROGRAM_SCHEMA_INVALID", "Committed program is invalid.");
        VerifyManifestIdentity(manifest, state);
        ValidateAssociation(state, parsed.Program!);
        return new(manifest, artifact, state, parsed.Program!);
    }
    private static void VerifyManifestIdentity(RevisionManifest manifest, CadState state)
    {
        if (state.Document.DocumentId != manifest.DocumentId || state.Document.ConfigurationId != manifest.ConfigurationId ||
            state.Document.ConfigurationName != manifest.ConfigurationName || state.Revision != manifest.Revision)
            throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "Manifest identity/configuration/revision disagrees with state.");
    }
    private void ValidateNativeAssociation(CadState state)
    {
        if (native is not IObservedRevisionNative observed) { ValidateAssociation(state, native.CurrentProgram); return; }
        var adapter = ExternalEditPlanning.Adapter(observed.CurrentExternal);
        if (!state.Document.Matches(adapter.Document) || state.Revision != adapter.Revision ||
            !state.Features.SequenceEqual(adapter.Features) || !state.Parameters.SequenceEqual(adapter.Parameters) ||
            !state.Bindings.SequenceEqual(adapter.Bindings) || state.Entities.Count != 0 || state.Relations.Count != 0 ||
            !StateRelationData.Dependencies(state).SequenceEqual(StateRelationData.Dependencies(adapter)))
            throw new StateException("STATE_DRIFT_DETECTED", "Coordinator state differs from the complete external companion.");
    }
    public static void ValidateAssociation(CadState state, CadProgram program)
    {
        StateValidation.Validate(state); var check = new ProgramValidator().Validate(program);
        if (!check.IsValid || program.Operations.Any(o => o.Kind == OperationKind.EditParameter)) throw new StateException("PROGRAM_SCHEMA_INVALID", "Expected a committed construction program.");
        var map = program.Operations.ToDictionary(o => o.SemanticId!, StringComparer.Ordinal);
        if (state.Features.Count != map.Count || state.Features.Any(f => !map.TryGetValue(f.SemanticId, out var o) || o.Kind != f.Kind))
            throw new StateException("STATE_DRIFT_DETECTED", "State feature ownership differs from program.");
        var solved = new DesignRelationEngine().Solve(program);
        if (!StateRelationData.Relations(state).ToHashSet().SetEquals(program.Relations) ||
            !StateRelationData.Dependencies(state).ToHashSet().SetEquals(solved.Dependencies.Edges) ||
            program.Operations.Zip(solved.Program.Operations).Any(pair => !OperationSemanticComparer.EqualsDefinition(pair.First, pair.Second)))
            throw new StateException("STATE_DRIFT_DETECTED", "Committed relation/dependency graph or solved geometry differs.");
    }
    // SOLIDWORKS retains a writable handle even on a clean, saved Part. Shared
    // read access does not mutate it; publication cross-checks the bytes again
    // after independent native readback before moving the pointer.
    public static string Hash(string path) { using var stream = OpenSavedRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public static FileFingerprint Fingerprint(string path) => new(Path.GetFullPath(path), Hash(path), new FileInfo(path).Length);
    public static ArtifactIdentity Artifact(string path, string version) => new(Path.GetFullPath(path), Hash(path), version);
    private void ValidateStatePath(CadState state)
    { StateValidation.Validate(state); if (!SamePath(state.Document.SavedPath, WorkingPath)) throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "State path is not the explicit package working copy."); }
    private void VerifyWorking(ManagedRevision revision)
    { GuardPath(WorkingPath, Root); if (!File.Exists(WorkingPath) || Hash(WorkingPath) != revision.Manifest.NativePart.Sha256 || new FileInfo(WorkingPath).Length != revision.Manifest.NativePart.SizeBytes) throw new StateException(V03FailureCodes.SourceFileDrift, "Selected working Part bytes differ from the authoritative manifest."); }
    private void ValidatePointer(ManagedRevisionPointer pointer)
    { ContractValidation.Version(pointer.SchemaVersion); GuardArtifact(pointer.Manifest, RevisionsPath); if (pointer.Manifest.SchemaVersion != "0.3") throw new ContractException(V03FailureCodes.ContractInvalid, "Pointer requires a v0.3 manifest."); }
    private void ValidateMarker(ManagedRecoveryMarker marker)
    {
        ContractValidation.Version(marker.SchemaVersion); GuardPath(marker.CandidateDirectory, RevisionsPath);
        if (!SamePath(marker.WorkingPath, WorkingPath) || marker.PreviousRevision < -1 || marker.CandidateRevision < 0 ||
            marker.PreviousManifest is not null && marker.CandidateRevision != marker.PreviousRevision + 1 ||
            (marker.PreviousManifest is null) != (marker.PreviousRevision == -1))
            throw new ContractException(V03FailureCodes.ContractInvalid, "Recovery identity is invalid.");
        if (marker.PreviousManifest is not null) GuardArtifact(marker.PreviousManifest, RevisionsPath);
    }
    private ManagedRecoveryMarker ReadMarker() => ContractJson.Read<ManagedRecoveryMarker>(ReadText(RecoveryPath), ValidateMarker, MaximumMetadataBytes);
    private void WritePointer(ArtifactIdentity artifact) => AtomicText(PointerPath, ContractJson.Write(new ManagedRevisionPointer("0.3", artifact), ValidatePointer));
    private void GuardArtifact(ArtifactIdentity artifact, string parent) { ObservedStateValidation.Artifact(artifact); GuardPath(artifact.Path, parent); }
    private void GuardPath(string path, string parent)
    {
        var full = Path.GetFullPath(path); var prefix = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Package artifact escapes its owned directory.");
        for (var cursor = Path.GetDirectoryName(full); cursor is not null && cursor.StartsWith(Root, StringComparison.OrdinalIgnoreCase); cursor = Path.GetDirectoryName(cursor))
            if (Directory.Exists(cursor)) RejectReparse(cursor);
        if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Package file is a reparse point.");
    }
    private static void RejectReparse(string path) { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Package directory is a reparse point."); }
    private static void VerifyArtifact(ArtifactIdentity artifact) { if (Hash(artifact.Path) != artifact.Sha256) throw new StateException(V03FailureCodes.SourceFileDrift, "Artifact hash mismatch: " + Path.GetFileName(artifact.Path)); }
    private static void VerifyFingerprint(FileFingerprint file) { ContractValidation.Fingerprint(file); if (new FileInfo(file.Path).Length != file.SizeBytes || Hash(file.Path) != file.Sha256) throw new StateException(V03FailureCodes.SourceFileDrift, "Native snapshot hash/size mismatch."); }
    private static string ReadText(string path) { if (new FileInfo(path).Length > MaximumMetadataBytes) throw new StateException("STATE_SCHEMA_INVALID", "Metadata exceeds byte bound."); return File.ReadAllText(path, Encoding.UTF8); }
    private static void WriteNew(string path, string text) { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); var bytes = Encoding.UTF8.GetBytes(text); if (bytes.Length > MaximumMetadataBytes) throw new StateException("STATE_SCHEMA_INVALID", "Metadata byte limit exceeded."); stream.Write(bytes); stream.Flush(true); }
    private static FileStream OpenSavedRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    private static void CopyFlushed(string source, string target) { using var input = OpenSavedRead(source); using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output); output.Flush(true); }
    private static void ReplaceCopy(string source, string target) { var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { CopyFlushed(source, temp); if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target); } finally { if (File.Exists(temp)) File.Delete(temp); } }
    private static void AtomicText(string path, string text) { var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { WriteNew(temp, text); if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); } finally { if (File.Exists(temp)) File.Delete(temp); } }
    private static bool SamePath(string a, string b) => Path.IsPathFullyQualified(a) && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    private void Check() => ObjectDisposedException.ThrowIf(disposed, this);
    public void Dispose() { if (!disposed) { disposed = true; lease.Dispose(); } }
}
