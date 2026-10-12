using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

namespace CadHarness.SolidWorks;

public interface IGenericDocumentLifecycle : IManagedDocumentLifecycle
{
    void BeforeCreate();
    void Created(string title);
    void Renamed(string before, string after);
}
public sealed record GenericOpenResult(ReopenStatus Status, string? FailureCode, string Message, GenericPartSession? Session);
public sealed record GenericSaveTrace(string Stage, string Path, string Title, bool Dirty, bool ReadOnly, int NeedsRebuild,
    bool ActiveSketch, bool? Result, int Errors, int Warnings, string? Sha256);

// Uses the same journal, artifact hashes and atomic pointer as managed/external
// revisions; only the explicitly versioned Generic companion and restore differ.
public sealed class GenericPartSession : IGenericRevisionNative, IDisposable
{
    private readonly SolidWorksConnection connection;
    private readonly IGenericDocumentLifecycle? lifecycle;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private readonly string? originalTitle;
    private IModelDoc2? owned;
    private bool disposed;
    private GenericProvenance? provenance;
    public ManagedRevisionStore Store { get; }
    public SolidWorksExecutionContext Context { get; private set; } = null!;
    public GenericRevisionPackage CurrentGeneric => Context.CaptureGenericRevisionPackage(provenance);
    public ReopenStatus Status { get; private set; } = ReopenStatus.Closed;
    public bool OriginalActiveRestored { get; private set; }
    public Action<GenericSaveTrace>? SaveEvidenceSink { get; set; }
    private GenericPartSession(SolidWorksConnection connection, string root, Action<DurableFaultPoint>? fault, IGenericDocumentLifecycle? lifecycle)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("Generic controller requires STA.");
        this.connection = connection; this.lifecycle = lifecycle;
        originalTitle = connection.Application.IActiveDoc2 is IModelDoc2 original ? original.GetTitle() : null;
        Store = new(root, this, fault);
    }
    public static GenericPartSession Create(SolidWorksConnection connection, string root, string template,
        Action<DurableFaultPoint>? fault = null, IGenericDocumentLifecycle? lifecycle = null)
    {
        var session = new GenericPartSession(connection, root, fault, lifecycle);
        try
        {
            if (File.Exists(session.Store.PointerPath) || File.Exists(session.Store.RecoveryPath) || File.Exists(session.Store.WorkingPath))
                throw new StateException("TRANSACTION_BUSY", "Creation never replaces an existing package.");
            session.Store.PrepareNewGenericWorkingPath();
            session.RejectCollision(); session.Guard(); lifecycle?.BeforeCreate();
            session.Context = connection.CreatePart(template); session.owned = session.Context.Document;
            lifecycle?.Created(session.owned.GetTitle());
            Directory.CreateDirectory(Path.GetDirectoryName(session.Store.WorkingPath)!);
            var title = session.owned.GetTitle(); int errors = 0, warnings = 0;
            var result = session.owned.Extension.SaveAs(session.Store.WorkingPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null!, ref errors, ref warnings);
            lifecycle?.Renamed(title, session.owned.GetTitle()); session.Trace("initial-saveas", result, errors, warnings);
            if (!result || errors != 0 || warnings != 0 || session.owned.GetSaveFlag() || session.owned.GetPathName() != session.Store.WorkingPath)
                throw new StateException("NATIVE_SAVE_FAILED", "Initial owned blank SaveAs failed.");
            // Qualify the native file lifecycle before any feature mutation.
            // A live NewDocument/first-SaveAs context can fail later Save3 even
            // though it reports a writable, rebuilt, saved blank Part.
            var blankHash = ManagedRevisionStore.Hash(session.Store.WorkingPath);
            session.CloseOwned(); session.RejectCollision(); session.Guard(); lifecycle?.BeforeOpen(session.Store.WorkingPath);
            errors = warnings = 0;
            session.owned = (IModelDoc2?)connection.Application.OpenDoc6(session.Store.WorkingPath, (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref errors, ref warnings);
            if (session.owned is not null) lifecycle?.Opened(session.Store.WorkingPath, session.owned.GetTitle());
            if (session.owned is null || errors != 0 || warnings != 0 || session.owned.IsOpenedReadOnly() || session.owned.GetSaveFlag() ||
                ManagedRevisionStore.Hash(session.Store.WorkingPath) != blankHash)
                throw new StateException("NATIVE_REOPEN_FAILED", "Initial blank must independently reopen clean and byte-identical.");
            session.Context = new(session.owned, connection.Application);
            _ = session.Context.CaptureGenericConstructionState();
            session.Attach(); session.Status = ReopenStatus.Editable; return session;
        }
        catch { session.Dispose(); throw; }
    }
    public static GenericOpenResult Open(SolidWorksConnection connection, string root, string selectedPart,
        Action<DurableFaultPoint>? fault = null, IGenericDocumentLifecycle? lifecycle = null)
    {
        GenericPartSession? s = null;
        try
        {
            s = new(connection, root, fault, lifecycle); s.Status = ReopenStatus.Opening; s.RejectCollision();
            var inspection = s.Store.Inspect(selectedPart);
            if (inspection.FailureCode is not null || inspection.Revision?.Generic is null)
                throw new StateException(inspection.FailureCode ?? V03FailureCodes.ModeMismatch, inspection.Message);
            if (inspection.RequiresNativeRecovery) s.Store.RestoreWorkingCopy(inspection);
            s.OpenOwned(inspection.Revision.Generic);
            if (inspection.RequiresNativeRecovery) s.Store.CompleteRecovery(inspection);
            _ = s.Store.Load(); s.Status = ReopenStatus.Editable;
            return new(s.Status, null, "Generic native/state/driver/revision cold verification passed.", s);
        }
        catch (Exception e)
        {
            var code = e is ICadFailure f ? f.Code : e is ContractException c ? c.Code : "GENERIC_REOPEN_FAILED";
            try { s?.Dispose(); } catch (Exception cleanup) { return new(ReopenStatus.Quarantined, "OWNED_DOCUMENT_CLEANUP_FAILED", cleanup.Message, null); }
            return new(ReopenStatus.Quarantined, code, e.Message, null);
        }
    }
    private void Attach()
    {
        Context.GenericDurableCheckpoint = () =>
        {
            Guard();
            if (File.Exists(Store.PointerPath)) Store.PrepareCheckpoint();
            else Store.PrepareInitialGenericCheckpoint(checked(Context.CaptureGenericConstructionState().Revision + 1));
        };
        Context.GenericDurableCommit = state =>
        {
            if (File.Exists(Store.PointerPath)) Store.Commit(GenericRevisionValidation.Envelope(state));
            else Store.Initialize(GenericRevisionValidation.Envelope(state));
        };
    }
    public MutationResult Construct(ConstructionProgram program, GenericProvenance? acceptedProvenance = null)
    {
        RequireEditable(); var previous = provenance; var next=acceptedProvenance ?? provenance;
        try { var before=Context.CaptureGenericConstructionState();GenericPlanningProvenance.Verify(next,before with {Program=program,Revision=checked(before.Revision+1)}); }
        catch(ContractException e){return new(false,e.Code,e.Message,"provenance preflight",false,false,false,false,false,false,null,ChangeSet.Empty,null,null);}
        provenance=next;
        var result = Context.ConstructGeneric(program);
        if (!result.StateCommitted) provenance = previous;
        Finish(result); return result;
    }
    public MutationResult Edit(GenericScalarCommand command)
    {
        RequireEditable();var previous=provenance;
        try {provenance=GenericPlanningProvenance.AppendEdit(provenance,Context.CaptureGenericConstructionState(),command);}
        catch(ContractException e){return new(false,e.Code,e.Message,"provenance preflight",false,false,false,false,false,false,null,ChangeSet.Empty,null,null);}
        var result=Context.EditGenericScalar(command);if(!result.StateCommitted)provenance=previous;Finish(result);return result;
    }
    public MutationResult ExecuteConstructionPlan(ConstructionExecutionApproval approval)
    {
        RequireEditable();var before=Context.CaptureGenericConstructionState();var current=SolidWorksConstructionPlanningRuntime.ForManagedSession(Context);
        var accepted=approval.ConsumeAccepted(current);var record=GenericPlanningProvenance.AppendPlan(provenance,before,accepted);
        return Construct(accepted.ReadResponse().Program!,record);
    }
    internal MutationResult ConstructCandidate(ConstructionProgram program)
    { RequireEditable(); var result = Context.ConstructGenericCandidate(program); Finish(result); return result; }
    internal MutationResult EditCandidate(GenericScalarCommand command)
    { RequireEditable(); var result = Context.EditGenericScalarCandidate(command); Finish(result); return result; }
    private void Finish(MutationResult result)
    {
        if (File.Exists(Store.RecoveryPath) || result.RollbackAttempted && !result.RollbackSucceeded)
        { Status = ReopenStatus.Quarantined; Context.RelationContextUsable = false; }
    }
    private void RequireEditable()
    { Check(); Guard(); if (Status != ReopenStatus.Editable) throw new StateException(V03FailureCodes.IncompleteDurablePublish, "Fresh recovery controller required."); }
    public void SaveNative()
    {
        Check(); Guard();
        if (owned is null || owned.GetPathName() != Store.WorkingPath || owned.IsOpenedReadOnly() || owned.GetActiveSketch2() is not null || owned.Extension.NeedsRebuild2 != 0)
            throw new StateException("NATIVE_SAVE_FAILED", "Owned native path/readonly/rebuild/sketch precondition failed.");
        Context.VerifyManagedIdentity(CurrentGeneric.State.Document);
        Trace("before-save3", null, 0, 0);
        int errors = 0, warnings = 0;
        var result = owned.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);
        Trace("after-save3", result, errors, warnings);
        if (!result || errors != 0 || warnings != 0 || owned.GetSaveFlag() || !File.Exists(Store.WorkingPath))
            throw new StateException("NATIVE_SAVE_FAILED", $"Save3 result={result}, errors={errors}, warnings={warnings}.");
    }
    public void VerifySavedGeneric(GenericRevisionPackage package)
    {
        Check(); CloseOwned(); OpenOwned(package);
        if (owned!.GetSaveFlag()) throw new StateException("NATIVE_SAVE_FAILED", "Independent native readback is dirty.");
    }
    private void OpenOwned(GenericRevisionPackage package)
    {
        RejectCollision(); Guard(); lifecycle?.BeforeOpen(Store.WorkingPath);
        var before = ManagedRevisionStore.Hash(Store.WorkingPath); int errors = 0, warnings = 0;
        owned = (IModelDoc2?)connection.Application.OpenDoc6(Store.WorkingPath, (int)swDocumentTypes_e.swDocPART,
            (int)swOpenDocOptions_e.swOpenDocOptions_Silent, package.State.Document.ConfigurationName, ref errors, ref warnings);
        if (owned is not null) lifecycle?.Opened(Store.WorkingPath, owned.GetTitle());
        if (owned is null || errors != 0 || warnings != 0 || owned.IsOpenedReadOnly())
            throw new StateException("NATIVE_REOPEN_FAILED", $"OpenDoc6 errors={errors}, warnings={warnings}.");
        Context = new(owned, connection.Application); Context.RestoreGenericRevisionPackage(package); provenance = package.Provenance; Attach();
        if (ManagedRevisionStore.Hash(Store.WorkingPath) != before) throw new StateException(V03FailureCodes.SourceFileDrift, "Native bytes changed during cold readback.");
    }
    private void RejectCollision()
    {
        if (connection.Application.GetDocuments() is Array docs && docs.Cast<object>().Cast<IModelDoc2>().Any(d =>
            string.Equals(d.GetPathName(), Store.WorkingPath, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(d.GetPathName()), Path.GetFileName(Store.WorkingPath), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.GetTitle(), Path.GetFileName(Store.WorkingPath), StringComparison.OrdinalIgnoreCase)))
            throw new StateException("DOCUMENT_ALREADY_OPEN", "Never take ownership of an open document or collide with its native filename.");
    }
    private void CloseOwned()
    {
        Check(); if (owned is null) return;
        if (owned.GetPathName() != Store.WorkingPath && owned.GetPathName().Length != 0)
            throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "Owned native path changed; close requires manual reconciliation.");
        var title = owned.GetTitle(); connection.Application.CloseDoc(title);
        if (connection.Application.GetOpenDocumentByName(Store.WorkingPath) is not null)
            throw new StateException("OWNED_DOCUMENT_CLEANUP_FAILED", "Owned Part remains open.");
        owned = null; lifecycle?.Closed(Store.WorkingPath, title);
    }
    private void Trace(string stage, bool? result, int errors, int warnings)
    {
        if (owned is null) return;
        SaveEvidenceSink?.Invoke(new(stage, owned.GetPathName(), owned.GetTitle(), owned.GetSaveFlag(), owned.IsOpenedReadOnly(), owned.Extension.NeedsRebuild2,
            owned.GetActiveSketch2() is not null, result, errors, warnings, File.Exists(Store.WorkingPath) ? ManagedRevisionStore.Hash(Store.WorkingPath) : null));
    }
    private void Guard()
    {
        using var p = Process.GetProcessById(connection.Application.GetProcessID()); p.Refresh(); Marshal.SetLastPInvokeError(0);
        var gdi = GetGuiResources(p.Handle, 0);
        if (!p.Responding || gdi >= 7000 || gdi == 0 && Marshal.GetLastPInvokeError() != 0) throw new StateException("TEST_RESOURCE_LIMIT", "Response/GDI guard refused.");
    }
    private void Check()
    { ObjectDisposedException.ThrowIf(disposed, this); if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Generic session requires its owning STA."); }
    public void Dispose()
    {
        if (disposed) return; Check();
        try
        {
            CloseOwned();
            if (originalTitle is null) OriginalActiveRestored = connection.Application.IActiveDoc2 is null;
            else
            {
                var error = 0;
                var active = connection.Application.ActivateDoc3(originalTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as IModelDoc2;
                OriginalActiveRestored = error == 0 && active?.GetTitle() == originalTitle &&
                    connection.Application.IActiveDoc2 is IModelDoc2 current && current.GetTitle() == originalTitle;
            }
            if (!OriginalActiveRestored) throw new StateException("OWNED_DOCUMENT_CLEANUP_FAILED", "Original active document was not restored.");
        }
        finally { disposed = true; Status = ReopenStatus.Closed; Store.Dispose(); }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetGuiResources(IntPtr process, uint flag);
}
