using System;
using System.Collections.Generic;
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

namespace CadHarness.SolidWorks;

public sealed record ExternalPartOpenResult(ReopenStatus Status, string? FailureCode, string Message,
    ExternalPartSession? Session, ObservedModel? Observation);
public sealed record ExternalEditResult(MutationResult Transaction, int Checkpoints, System.Collections.Generic.IReadOnlyList<string> PartialNativeSteps,
    ReopenStatus Status, bool SourcePreserved);
public sealed record ExternalNativeEditEvidence(string Phase, DateTime Utc, long Revision,
    string? Target, string? NativeReference, ParameterKey? Parameter, double? BeforeValue,
    double? RequestedValue, double? Readback, ObservedSetterResult? Setter, bool? Rebuilt,
    double ElapsedMilliseconds, int? ExceptionHResult);

public sealed class ExternalPartSession : IExternalEditSession, IDisposable
{
    private readonly SolidWorksConnection connection;
    private readonly IManagedDocumentLifecycle? lifecycle;
    private readonly Action<ExternalEditFault>? editFault;
    private readonly string? originalTitle;
    private readonly int thread = System.Environment.CurrentManagedThreadId;
    private readonly SavedNativeReadProof proof = new();
    private IModelDoc2? owned;
    private bool disposed;
    private readonly List<ExternalPreparedEdit> pendingReadbacks = new();
    // An evidence sink failure propagates through the existing transaction rollback boundary.
    public Action<ExternalNativeEditEvidence>? EvidenceSink { get; set; }
    public ManagedRevisionStore Store { get; }
    public ExternalEditState CurrentExternal { get; private set; } = null!;
    public ReopenStatus Status { get; private set; } = ReopenStatus.Closed;
    public bool OriginalActiveRestored { get; private set; }
    private ExternalPartSession(SolidWorksConnection connection, string root, IManagedDocumentLifecycle? lifecycle,
        Action<DurableFaultPoint>? publishFault, Action<ExternalEditFault>? editFault)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("External editing requires STA.");
        this.connection = connection; this.lifecycle = lifecycle; this.editFault = editFault;
        originalTitle = connection.Application.IActiveDoc2 is IModelDoc2 active ? active.GetTitle() : null;
        Store = new(root, this, publishFault);
    }
    public static ExternalPartOpenResult CreateCopy(SolidWorksConnection connection, string root, string source, string? configuration = null,
        IManagedDocumentLifecycle? lifecycle = null, Action<DurableFaultPoint>? publishFault = null, Action<ExternalEditFault>? editFault = null)
    {
        ExternalPartSession? session = null; ObservedModel? diagnostic = null; FileFingerprint? original = null;
        try
        {
            source = Path.GetFullPath(source); original = ManagedRevisionStore.Fingerprint(source);
            session = new(connection, root, lifecycle, publishFault, editFault); session.Status = ReopenStatus.Opening;
            session.RejectAlreadyOpen(source);
            if (source.StartsWith(session.Store.Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                File.Exists(session.Store.PointerPath) || File.Exists(session.Store.RecoveryPath) || File.Exists(session.Store.WorkingPath))
                throw new StateException("TRANSACTION_BUSY", "Select an untouched source outside a new owned package.");
            session.Store.CopyInitialNative(source); session.OpenOwned(configuration ?? "");
            var config = session.owned!.ConfigurationManager.ActiveConfiguration.Name;
            if (configuration is not null && config != configuration) throw new StateException(V03FailureCodes.ConfigurationMismatch, "Selected configuration differs.");
            var selection = ExternalObservation.Selection(original, ManagedRevisionStore.Fingerprint(session.Store.WorkingPath), config);
            diagnostic = ExternalObservation.Capture(selection, new ExternalPartInspection.NativeSource(session.owned)).Model;
            session.CurrentExternal = ExternalNativeQualification.Qualify(session.owned, selection);
            session.Store.Initialize(ExternalEditPlanning.Adapter(session.CurrentExternal));
            session.VerifyLive(session.CurrentExternal); session.Status = ReopenStatus.Editable;
            return new(session.Status, null, "External observed copy qualified; no construction program reconstructed.", session, session.CurrentExternal.Observation);
        }
        catch (Exception error)
        {
            var code = error is ICadFailure failure ? failure.Code : error is ContractException contract ? contract.Code : "EXTERNAL_INTAKE_FAILED";
            var status = session is not null && File.Exists(session.Store.RecoveryPath) ? ReopenStatus.Quarantined : ReopenStatus.Inspectable;
            try { session?.Dispose(); }
            catch (Exception cleanup) { return new(ReopenStatus.Quarantined, "OWNED_DOCUMENT_CLEANUP_FAILED", cleanup.Message, null, diagnostic); }
            if (original is not null && ManagedRevisionStore.Hash(original.Path) != original.Sha256)
                return new(ReopenStatus.Quarantined, V03FailureCodes.SourceFileDrift, "Original source bytes changed.", null, diagnostic);
            return new(status, code, error.Message, null, diagnostic);
        }
    }
    public static ExternalPartOpenResult Open(SolidWorksConnection connection, string root, string selectedCopy,
        IManagedDocumentLifecycle? lifecycle = null, Action<DurableFaultPoint>? publishFault = null, Action<ExternalEditFault>? editFault = null)
    {
        ExternalPartSession? session = null;
        try
        {
            session = new(connection, root, lifecycle, publishFault, editFault); session.Status = ReopenStatus.Opening;
            session.RejectAlreadyOpen(session.Store.WorkingPath);
            var inspection = session.Store.Inspect(selectedCopy);
            if (inspection.FailureCode is not null || inspection.Revision?.External is null)
                throw new StateException(inspection.FailureCode ?? V03FailureCodes.IncompleteDurablePublish, inspection.Message);
            session.CurrentExternal = inspection.Revision.External;
            session.RejectAlreadyOpen(session.CurrentExternal.Observation.Selection.Source.Path);
            session.CheckSource();
            if (inspection.RequiresNativeRecovery) session.Store.RestoreWorkingCopy(inspection);
            session.OpenOwned(session.CurrentExternal.Observation.Selection.ConfigurationName);
            session.VerifyLive(session.CurrentExternal);
            if (inspection.RequiresNativeRecovery) session.Store.CompleteRecovery(inspection);
            _ = session.Store.Load(); session.Status = ReopenStatus.Editable;
            return new(session.Status, null, "Cold external native/state/manifest verification passed.", session, session.CurrentExternal.Observation);
        }
        catch (Exception error)
        {
            var code = error is ICadFailure failure ? failure.Code : "EXTERNAL_REOPEN_FAILED";
            try { session?.Dispose(); } catch (Exception cleanup) { return new(ReopenStatus.Quarantined, "OWNED_DOCUMENT_CLEANUP_FAILED", cleanup.Message, null, null); }
            return new(ReopenStatus.Quarantined, code, error.Message, null, null);
        }
    }
    public ExternalEditResult Edit(EditSetRequest request) => Edit(new ExternalEditCommand(request, null));
    public ExternalEditResult Edit(ScalarEditRequest request) => Edit(new ExternalEditCommand(null, request));
    private ExternalEditResult Edit(ExternalEditCommand command)
    {
        Check();
        if (Status != ReopenStatus.Editable) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Session is not editable; recover in a fresh controller.");
        // Batch qualification is separate; every target still requires a qualified scalar accessor.
        if (command.Batch is not null)
            NativeQualificationCandidates.RequireExecutable(CurrentExternal.Observation, command.Batch);
        else
        {
            ContractValidation.Edit(command.Scalar!);
            NativeQualificationCandidates.RequireExecutable(CurrentExternal.Observation, command.Scalar!.Edit.Target, command.Scalar.Edit.Parameter);
        }
        var backend = new ExternalEditTransactionBackend(this, editFault);
        var result = new RequestMutationTransaction<ExternalEditCommand, ExternalEditPreparation, ExternalEditRollback>(Store, backend).Execute(command);
        if (File.Exists(Store.RecoveryPath) || result.RollbackAttempted && !result.RollbackSucceeded) Invalidate();
        CheckSource(); return new(result, backend.Checkpoints, backend.PartialNativeSteps, Status, true);
    }
    public void VerifyLive(ExternalEditState expected)
    {
        Check(); Guard(); CheckSource(expected);
        if (owned is null || !string.Equals(owned.GetPathName(), Store.WorkingPath, StringComparison.OrdinalIgnoreCase) ||
            ManagedRevisionStore.Hash(Store.WorkingPath) != expected.Observation.Selection.WorkingCopy.Sha256)
            throw new StateException(V03FailureCodes.SourceFileDrift, "Live path or exact working bytes drifted.");
        ExternalNativeQualification.Verify(owned, expected);
    }
    public void Apply(ExternalPreparedEdit prepared)
    {
        Check(); Guard(); proof.Invalidate();
        var feature = ExternalNativeQualification.Resolve(owned!, prepared.Feature);
        var handler = ParameterMutationRegistry.Default.GetObserved(prepared.Feature.Subtype, prepared.Edit.Parameter);
        var before = handler.ReadObserved(owned!, feature, prepared.Edit.Parameter);
        ExternalEditPlanning.Near(before, prepared.Edit.ExpectedOldValue, prepared.Edit.Parameter);
        Evidence("setter-start", prepared, before: before);
        var timer = Stopwatch.StartNew();
        ObservedSetterResult result;
        try { result = handler.ApplyObserved(owned!, feature, prepared.Edit); }
        catch (Exception error) { Evidence("setter-exception", prepared, before: before, elapsed: timer.Elapsed.TotalMilliseconds, hresult: error.HResult); throw; }
        Evidence("setter-return", prepared, before: before, setter: result, elapsed: timer.Elapsed.TotalMilliseconds);
        if (!result.Succeeded) throw new StateException("PARAMETER_NOT_APPLIED", result.Api + " failed: " + result.ReturnCode);
        var readback = handler.ReadObserved(owned!, ExternalNativeQualification.Resolve(owned!, prepared.Feature), prepared.Edit.Parameter);
        Evidence("setter-readback", prepared, before: before, readback: readback, setter: result, elapsed: timer.Elapsed.TotalMilliseconds);
        ExternalEditPlanning.Near(readback, prepared.Edit.Value, prepared.Edit.Parameter);
        pendingReadbacks.Add(prepared);
    }
    public bool RebuildNative()
    {
        Check(); Guard(); Evidence("rebuild-start"); var timer = Stopwatch.StartNew(); bool result;
        try { result = owned is not null && ExecutionTelemetry.Rebuild(() => owned.ForceRebuild3(false)); }
        catch (Exception error) { Evidence("rebuild-exception", elapsed: timer.Elapsed.TotalMilliseconds, hresult: error.HResult); throw; }
        Evidence("rebuild-return", rebuilt: result, elapsed: timer.Elapsed.TotalMilliseconds);
        if (result && EvidenceSink is not null)
            foreach (var prepared in pendingReadbacks)
            {
                var feature = ExternalNativeQualification.Resolve(owned!, prepared.Feature);
                var value = ParameterMutationRegistry.Default.GetObserved(prepared.Feature.Subtype, prepared.Edit.Parameter).ReadObserved(owned!, feature, prepared.Edit.Parameter);
                Evidence("rebuild-readback", prepared, readback: value, rebuilt: true, elapsed: timer.Elapsed.TotalMilliseconds);
            }
        return result;
    }
    private void Evidence(string phase, ExternalPreparedEdit? prepared = null, double? before = null,
        double? readback = null, ObservedSetterResult? setter = null, bool? rebuilt = null, double elapsed = 0, int? hresult = null) =>
        EvidenceSink?.Invoke(new(phase, DateTime.UtcNow, CurrentExternal.Observation.Selection.ExpectedRevision,
            prepared?.Edit.Target, prepared?.Feature.NativeReference?.Base64, prepared?.Edit.Parameter,
            before, prepared?.Edit.Value, readback, setter, rebuilt, elapsed, hresult));
    public void Stage(ExternalEditState state) { Check(); CurrentExternal = state; }
    public void SaveNative()
    {
        Check(); Guard(); CheckSource();
        if (owned is null || owned.GetPathName() != Store.WorkingPath) throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "External Save As is unsupported.");
        if (owned.GetSaveFlag())
        {
            proof.Invalidate(); var errors = 0; var warnings = 0;
            if (!owned.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) || errors != 0 || warnings != 0 || owned.GetSaveFlag())
                throw new StateException("NATIVE_SAVE_FAILED", $"External save failed/dirty: {errors}/{warnings}.");
        }
        var selection = CurrentExternal.Observation.Selection with { WorkingCopy = ManagedRevisionStore.Fingerprint(Store.WorkingPath) };
        var measured = ExternalObservation.Capture(selection, new ExternalPartInspection.NativeSource(owned, true)).Model;
        CurrentExternal = CurrentExternal with { Observation = ExternalEditPlanning.ReconcileSavedObservation(CurrentExternal.Observation, measured) };
        ExternalEditPlanning.Validate(CurrentExternal);
        pendingReadbacks.Clear();
    }
    public void VerifySavedExternal(ExternalEditState expected)
    {
        Check();
        if (owned is null || !proof.IsCurrent(ManagedRevisionStore.Hash(Store.WorkingPath), owned.GetSaveFlag()))
        {
            CloseOwned(); editFault?.Invoke(ExternalEditFault.Reopen);
            OpenOwned(expected.Observation.Selection.ConfigurationName);
        }
        if (owned!.GetSaveFlag()) throw new StateException("NATIVE_SAVE_FAILED", "Cold saved readback opened dirty.");
        VerifyLive(expected);
    }
    public void Restore(ManagedRecoveryInspection checkpoint)
    {
        Check(); pendingReadbacks.Clear(); CloseOwned(); Store.RestoreWorkingCopy(checkpoint);
        CurrentExternal = checkpoint.Revision!.External ?? throw new StateException(V03FailureCodes.ModeMismatch, "No external batch-start checkpoint.");
        editFault?.Invoke(ExternalEditFault.Reopen); OpenOwned(CurrentExternal.Observation.Selection.ConfigurationName);
        VerifyLive(CurrentExternal); Store.CompleteRecovery(checkpoint);
    }
    public void Invalidate() { Status = ReopenStatus.Quarantined; proof.Invalidate(); }
    private void CheckSource(ExternalEditState? expected = null)
    {
        var source = (expected ?? CurrentExternal).Observation.Selection.Source;
        if (ManagedRevisionStore.Hash(source.Path) != source.Sha256 || new FileInfo(source.Path).Length != source.SizeBytes)
            throw new StateException(V03FailureCodes.SourceFileDrift, "Engineer source differs from its immutable fingerprint.");
    }
    private void RejectAlreadyOpen(string path)
    { if (connection.Application.GetOpenDocumentByName(path) is not null) throw new StateException("DOCUMENT_ALREADY_OPEN", "Never adopt an engineer's open document."); }
    private void OpenOwned(string configuration)
    {
        Check(); if (owned is not null) throw new StateException("TRANSACTION_BUSY", "Only one owned Part may be open.");
        RejectAlreadyOpen(Store.WorkingPath); Guard(); lifecycle?.BeforeOpen(Store.WorkingPath);
        var hash = ManagedRevisionStore.Hash(Store.WorkingPath); var errors = 0; var warnings = 0;
        owned = connection.Application.OpenDoc6(Store.WorkingPath, (int)swDocumentTypes_e.swDocPART, (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
            configuration, ref errors, ref warnings) as IModelDoc2;
        if (owned is not null) lifecycle?.Opened(Store.WorkingPath, owned.GetTitle());
        if (owned is null || errors != 0 || warnings != 0 || owned.IsOpenedReadOnly() || owned.GetSaveFlag())
            throw new StateException("NATIVE_REOPEN_FAILED", $"OpenDoc6 error/warning/read-only/dirty: {errors}/{warnings}.");
        if (ManagedRevisionStore.Hash(Store.WorkingPath) != hash) throw new StateException(V03FailureCodes.SourceFileDrift, "Opening changed saved native bytes.");
        proof.RecordOpened(hash);
    }
    private void CloseOwned()
    {
        Check(); if (owned is null) return; var title = owned.GetTitle(); connection.Application.CloseDoc(title);
        if (connection.Application.GetOpenDocumentByName(Store.WorkingPath) is not null) throw new StateException("OWNED_DOCUMENT_CLEANUP_FAILED", "Owned external copy remains open.");
        owned = null; proof.Invalidate(); lifecycle?.Closed(Store.WorkingPath, title);
    }
    private void Guard()
    {
        using var process = Process.GetProcessById(connection.Application.GetProcessID()); process.Refresh();
        Marshal.SetLastPInvokeError(0); var gdi = GetGuiResources(process.Handle, 0);
        if (!process.Responding || gdi >= 7000 || gdi == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new StateException("TEST_RESOURCE_LIMIT", "Native process/GDI guard refused execution.");
    }
    private void Check()
    { ObjectDisposedException.ThrowIf(disposed, this); if (thread != System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("Keep external session on its owning STA."); }
    public void Dispose()
    {
        if (disposed) return; Check();
        try
        {
            CloseOwned(); var errors = 0;
            if (originalTitle is not null) connection.Application.ActivateDoc3(originalTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref errors);
            OriginalActiveRestored = errors == 0 && (originalTitle is null ? connection.Application.IActiveDoc2 is null :
                connection.Application.IActiveDoc2 is IModelDoc2 active && active.GetTitle() == originalTitle);
            if (!OriginalActiveRestored) throw new StateException("OWNED_DOCUMENT_CLEANUP_FAILED", "Original active document was not restored.");
        }
        finally { disposed = true; Status = ReopenStatus.Closed; Store.Dispose(); }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetGuiResources(IntPtr process, uint flag);
}
