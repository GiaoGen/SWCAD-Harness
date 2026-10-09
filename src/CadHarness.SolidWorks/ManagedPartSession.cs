using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

namespace CadHarness.SolidWorks;

public interface IManagedDocumentLifecycle
{
    void BeforeOpen(string path);
    void Opened(string path, string title);
    void Closed(string path, string title);
}
public sealed record ManagedOpenResult(ReopenStatus Status, string? FailureCode, string Message, ManagedPartSession? Session);

// Owns only documents it opened, never an existing engineer document. The
// connection's application lifetime remains with its caller.
public sealed class ManagedPartSession : IManagedRevisionNative, IDisposable
{
    private readonly SolidWorksConnection connection;
    private readonly IManagedDocumentLifecycle? lifecycle;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly string? originalTitle;
    private IModelDoc2? owned;
    private readonly SavedNativeReadProof savedReadProof = new();
    private bool disposed;
    public ManagedRevisionStore Store { get; }
    public SolidWorksExecutionContext Context { get; private set; } = null!;
    public CadProgram CurrentProgram => Context.RelationProgram ?? throw new StateException("STALE_REFERENCE", "No restored managed program.");
    public ReopenStatus Status { get; private set; } = ReopenStatus.Closed;
    public bool OriginalActiveRestored { get; private set; }
    public ManagedMigrationEvidence? Migration { get; private set; }

    private ManagedPartSession(SolidWorksConnection connection, string root, Action<DurableFaultPoint>? fault, IManagedDocumentLifecycle? lifecycle)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("Managed controller requires STA.");
        this.connection = connection; this.lifecycle = lifecycle;
        originalTitle = connection.Application.IActiveDoc2 is IModelDoc2 original ? original.GetTitle() : null;
        Store = new(root, this, fault);
    }
    public static ManagedOpenResult Open(SolidWorksConnection connection, string root, string selectedPart,
        Action<DurableFaultPoint>? fault = null, IManagedDocumentLifecycle? lifecycle = null)
    {
        ManagedPartSession? session = null;
        try
        {
            session = new(connection, root, fault, lifecycle); session.Status = ReopenStatus.Opening;
            session.RejectAlreadyOpen();
            var inspection = session.Store.Inspect(selectedPart);
            if (inspection.FailureCode is not null || inspection.Revision is null)
                throw new StateException(inspection.FailureCode ?? V03FailureCodes.IncompleteDurablePublish, inspection.Message);
            if (inspection.RequiresNativeRecovery) session.Store.RestoreWorkingCopy(inspection);
            session.OpenOwned(inspection.Revision.State, inspection.Revision.Program);
            session.Status = ReopenStatus.Inspectable;
            if (inspection.RequiresNativeRecovery) session.Store.CompleteRecovery(inspection);
            session.EnsureCurrent(); session.Status = ReopenStatus.Editable;
            return new(session.Status, null, "Cold managed native readback and package validation passed.", session);
        }
        catch (Exception error)
        {
            var status = session is not null && File.Exists(session.Store.RecoveryPath) ? ReopenStatus.Quarantined : ReopenStatus.Inspectable;
            var failure = Failure(error);
            try { session?.Dispose(); } catch (Exception cleanup) { return new(ReopenStatus.Quarantined, "OWNED_DOCUMENT_CLEANUP_FAILED", cleanup.Message, null); }
            return new(status, failure.Code, failure.Message, null);
        }
    }

    public static ManagedOpenResult MigrateCopy(SolidWorksConnection connection, string root, string originalPart,
        string originalState, string originalProgram, IManagedDocumentLifecycle? lifecycle = null)
    {
        ManagedPartSession? session = null;
        try
        {
            originalPart = Path.GetFullPath(originalPart);
            var rollbackPath = Path.GetFullPath(originalState);
            var legacy = LegacyMigrationReader.Read(originalState, originalProgram, rollbackPath);
            var partHash = ManagedRevisionStore.Fingerprint(originalPart);
            var stateHash = ManagedRevisionStore.Artifact(originalState, "0.2");
            var programHash = ManagedRevisionStore.Artifact(originalProgram, "0.2");
            if (!string.Equals(originalPart, legacy.State.Document.SavedPath, StringComparison.OrdinalIgnoreCase))
                throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "Explicit legacy Part differs from its state path.");
            session = new(connection, root, null, lifecycle); session.Status = ReopenStatus.Opening;
            session.RejectAlreadyOpen();
            if (File.Exists(session.Store.PointerPath) || File.Exists(session.Store.RecoveryPath) || File.Exists(session.Store.WorkingPath) ||
                File.Exists(Path.Combine(session.Store.Root, "migration.json")))
                throw new StateException("TRANSACTION_BUSY", "Migration destination must be new; originals are never overwritten.");
            if (new[] { originalPart, Path.GetFullPath(originalState), Path.GetFullPath(originalProgram) }.Any(p =>
                p.StartsWith(session.Store.Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                throw new StateException(V03FailureCodes.MigrationFailed, "Original files must be outside the owned copy package.");
            session.Store.CopyInitialNative(originalPart);
            var state = legacy.State with { Document = legacy.State.Document with { SavedPath = session.Store.WorkingPath } };
            session.OpenOwned(state, legacy.Program);
            session.Status = ReopenStatus.Inspectable;
            PreserveOriginals();
            session.Store.Initialize(state);
            PreserveOriginals();
            session.Migration = new("0.3", "0.2", "0.3", "0.2", partHash, stateHash, programHash,
                rollbackPath, true, true, "MIGRATED_COPY_NATIVE_VERIFIED");
            using (var audit = new FileStream(Path.Combine(session.Store.Root, "migration.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(ContractJson.Write(session.Migration, ValidateMigration));
                audit.Write(bytes); audit.Flush(true);
            }
            session.EnsureCurrent(); session.Status = ReopenStatus.Editable;
            return new(session.Status, null, "Legacy originals preserved; migrated copy independently reopened and native verified.", session);
            void PreserveOriginals()
            {
                if (ManagedRevisionStore.Hash(originalPart) != partHash.Sha256 || ManagedRevisionStore.Hash(originalState) != stateHash.Sha256 ||
                    ManagedRevisionStore.Hash(originalProgram) != programHash.Sha256)
                    throw new StateException(V03FailureCodes.SourceFileDrift, "Legacy originals changed during copy migration.");
            }
        }
        catch (Exception error)
        {
            var failure = Failure(error);
            try { session?.Dispose(); } catch (Exception cleanup) { return new(ReopenStatus.Quarantined, "OWNED_DOCUMENT_CLEANUP_FAILED", cleanup.Message, null); }
            return new(ReopenStatus.Quarantined, failure.Code, failure.Message, null);
        }
    }

    public SolidWorksPlanningRuntime PlanningRuntime()
    {
        Check(); RequireEditable(); var revision = EnsureCurrent();
        return SolidWorksPlanningRuntime.ForEdit(Context, revision.State);
    }
    public MutationResult Edit(OperationNode edit, long expectedRevision)
    {
        Check(); RequireEditable();
        var revision = EnsureCurrent();
        if (revision.State.Revision != expectedRevision) throw new StateException("STALE_REFERENCE", "Requested revision is no longer current.");
        savedReadProof.Invalidate();
        var backend = new ManagedParameterBackend(this);
        var result = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(Store, backend).Execute(edit, FullValidationReason.StateDriftSuspicion);
        if (File.Exists(Store.RecoveryPath) || !Context.RelationContextUsable) Status = ReopenStatus.Quarantined;
        return result;
    }
    private ManagedRevision EnsureCurrent()
    {
        Check(); _ = Store.Load(); var revision = Store.ReadCurrent();
        if (owned is null || owned.GetSaveFlag()) throw new StateException("STATE_DRIFT_DETECTED", "Native document has unsaved edits; durable dispatch is blocked.");
        Context.VerifyManagedReadback(revision.State, revision.Program);
        GuardResources(); return revision;
    }
    private void RequireEditable()
    { if (Status != ReopenStatus.Editable) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Session is not editable; a fresh recovery controller is required."); }
    public void SaveNative()
    {
        Check(); GuardResources();
        if (owned is null || !string.Equals(Path.GetFullPath(owned.GetPathName()), Store.WorkingPath, StringComparison.OrdinalIgnoreCase))
            throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "Native Save As is unsupported.");
        if (File.Exists(Store.PointerPath)) Context.VerifyManagedIdentity(Store.ReadCurrent().State.Document);
        if (owned.GetSaveFlag())
        {
            savedReadProof.Invalidate();
            var errors = 0; var warnings = 0;
            if (!owned.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) || errors != 0 || warnings != 0 || owned.GetSaveFlag())
                throw new StateException("NATIVE_SAVE_FAILED", $"Native save failed or remains dirty. errors={errors}, warnings={warnings}");
        }
        if (!File.Exists(Store.WorkingPath)) throw new StateException("NATIVE_SAVE_FAILED", "Native saved file is missing.");
    }
    public void VerifySavedRevision(CadState state, CadProgram program)
    {
        Check();
        // A byte-identical, clean file opened independently by this controller
        // already has a saved-file read proof (initial migration/recovery).
        if (owned is not null && savedReadProof.IsCurrent(ManagedRevisionStore.Hash(Store.WorkingPath), owned.GetSaveFlag()))
        { Context.VerifyManagedReadback(state, program); return; }
        CloseOwned(); OpenOwned(state, program);
        if (owned!.GetSaveFlag()) throw new StateException("NATIVE_SAVE_FAILED", "Independent saved readback opened a dirty native document.");
    }
    private void RejectAlreadyOpen()
    {
        if (connection.Application.GetOpenDocumentByName(Store.WorkingPath) is not null)
            throw new StateException("DOCUMENT_ALREADY_OPEN", "Selected Part is already open and is not owned by this controller.");
    }
    private void OpenOwned(CadState state, CadProgram program)
    {
        Check(); if (owned is not null) throw new StateException("TRANSACTION_BUSY", "Only one owned native document may be open.");
        RejectAlreadyOpen(); GuardResources(); lifecycle?.BeforeOpen(Store.WorkingPath);
        var before = ManagedRevisionStore.Hash(Store.WorkingPath);
        var errors = 0; var warnings = 0;
        owned = (IModelDoc2?)connection.Application.OpenDoc6(Store.WorkingPath, (int)swDocumentTypes_e.swDocPART,
            (int)swOpenDocOptions_e.swOpenDocOptions_Silent, state.Document.ConfigurationName, ref errors, ref warnings);
        // Ownership precedes restoration or any native mutation, even if the
        // returned document accompanies an OpenDoc error/warning.
        if (owned is not null) lifecycle?.Opened(Store.WorkingPath, owned.GetTitle());
        if (owned is null || errors != 0 || warnings != 0) throw new StateException("NATIVE_REOPEN_FAILED", $"OpenDoc6 errors={errors}, warnings={warnings}");
        Context = new(owned); Context.RestoreManagedState(state, program);
        if (ManagedRevisionStore.Hash(Store.WorkingPath) != before) throw new StateException(V03FailureCodes.SourceFileDrift, "Saved bytes changed during native open.");
        savedReadProof.RecordOpened(before); GuardResources();
    }
    private void CloseOwned()
    {
        Check(); if (owned is null) return;
        var title = owned.GetTitle();
        connection.Application.CloseDoc(title);
        if (connection.Application.GetOpenDocumentByName(Store.WorkingPath) is not null ||
            connection.Application.GetDocuments() is Array docs && docs.Cast<object>().Cast<IModelDoc2>().Any(d => d.GetTitle() == title))
            throw new StateException("OWNED_DOCUMENT_CLEANUP_FAILED", "Owned document remained open after CloseDoc.");
        owned = null; savedReadProof.Invalidate(); lifecycle?.Closed(Store.WorkingPath, title);
    }
    private void GuardResources()
    {
        using var process = Process.GetProcessById(connection.Application.GetProcessID()); process.Refresh();
        Marshal.SetLastPInvokeError(0); var gdi = GetGuiResources(process.Handle, 0);
        if (!process.Responding || gdi >= 7000 || gdi == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new StateException("TEST_RESOURCE_LIMIT", "SOLIDWORKS is unresponsive, GDI is unknown, or GDI >= 7000.");
    }
    private void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Managed session must remain on its owning STA thread.");
    }
    public void Dispose()
    {
        if (disposed) return;
        Check();
        try
        {
            CloseOwned();
            if (originalTitle is null) OriginalActiveRestored = connection.Application.IActiveDoc2 is null;
            else
            {
                var error = 0;
                var restored = connection.Application.ActivateDoc3(originalTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as IModelDoc2;
                OriginalActiveRestored = error == 0 && restored?.GetTitle() == originalTitle &&
                    connection.Application.IActiveDoc2 is IModelDoc2 active && active.GetTitle() == originalTitle;
            }
            if (!OriginalActiveRestored) throw new StateException("OWNED_DOCUMENT_CLEANUP_FAILED", "Engineer's original active document was not restored.");
        }
        finally { disposed = true; Status = ReopenStatus.Closed; Store.Dispose(); }
    }
    private static (string Code, string Message) Failure(Exception error) => error switch
    { ICadFailure e => (e.Code, error.Message), ContractException e => (e.Code, error.Message), _ => ("MANAGED_REOPEN_FAILED", error.Message) };
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetGuiResources(IntPtr process, uint flag);
    public static void ValidateMigration(ManagedMigrationEvidence evidence)
    {
        ContractValidation.Version(evidence.SchemaVersion);
        ContractValidation.Fingerprint(evidence.OriginalPart);
        ObservedStateValidation.Artifact(evidence.OriginalState); ObservedStateValidation.Artifact(evidence.OriginalProgram);
        if (evidence.OldStateVersion != "0.2" || evidence.NewManifestVersion != "0.3" || evidence.PreservedStateVersion != "0.2" ||
            evidence.OriginalState.SchemaVersion != "0.2" || evidence.OriginalProgram.SchemaVersion != "0.2" ||
            !Path.IsPathFullyQualified(evidence.RollbackPath) || !evidence.NativeReopenVerified || !evidence.OriginalsPreserved ||
            evidence.Result != "MIGRATED_COPY_NATIVE_VERIFIED")
            throw new ContractException(V03FailureCodes.MigrationFailed, "Migration requires native saved-copy proof and preserved legacy originals.");
    }
}

public sealed class SavedNativeReadProof
{
    private string? independentlyOpenedHash;
    public void RecordOpened(string hash) => independentlyOpenedHash = hash;
    public void Invalidate() => independentlyOpenedHash = null;
    public bool IsCurrent(string hash, bool dirty) => !dirty && independentlyOpenedHash is not null && independentlyOpenedHash == hash;
}

internal sealed class ManagedParameterBackend : IMutationBackend<NativeEditPreparation, NativeEditRollback>
{
    private readonly ManagedPartSession session;
    // Saved verification can replace the native context. Rollback always uses
    // the current context, never COM handles belonging to a closed document.
    private TransactionalParameterBackend Current => new(session.Context);
    internal ManagedParameterBackend(ManagedPartSession session) => this.session = session;
    public NativeEditPreparation ResolveInputs(CadState state, OperationNode request) => Current.ResolveInputs(state, request);
    public void Preflight(CadState state, NativeEditPreparation prepared) => Current.Preflight(state, prepared);
    public NativeEditRollback CaptureRollback(CadState state, NativeEditPreparation prepared)
    { var rollback = Current.CaptureRollback(state, prepared); session.Store.PrepareCheckpoint(); return rollback; }
    public ChangeSet Execute(NativeEditPreparation prepared) => Current.Execute(prepared);
    public bool Rebuild() => Current.Rebuild();
    public void ValidatePostconditions(NativeEditPreparation prepared) => Current.ValidatePostconditions(prepared);
    public bool RecoveryAllowed => false;
    public bool Recover(NativeEditPreparation prepared) => false;
    public CadState ValidateFinal(CadState state, NativeEditPreparation prepared, ValidationScope scope) => Current.ValidateFinal(state, prepared, scope);
    public void StageState(NativeEditPreparation prepared, CadState validated) => Current.StageState(prepared, validated);
    public void Rollback(NativeEditRollback rollback) => Current.Rollback(rollback);
    public void ValidateRestored(CadState state, NativeEditRollback rollback, ValidationScope scope) => Current.ValidateRestored(state, rollback, scope);
    public void Invalidate() => Current.Invalidate();
}
