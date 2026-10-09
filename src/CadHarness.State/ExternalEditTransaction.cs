using System;
using System.Collections.Generic;
using CadHarness.Ir.V03;

namespace CadHarness.State.V03;

public enum ExternalEditFault { Preparation, FirstEdit, LastEdit, Rebuild, Postcondition, Rollback, Reopen }
public sealed record ExternalEditCommand(EditSetRequest? Batch, ScalarEditRequest? Scalar);
public interface IExternalEditSession : IObservedRevisionNative
{
    ManagedRevisionStore Store { get; }
    void VerifyLive(ExternalEditState expected);
    void Apply(ExternalPreparedEdit edit);
    bool RebuildNative();
    void Stage(ExternalEditState state);
    void Restore(ManagedRecoveryInspection checkpoint);
    void Invalidate();
}
public sealed record ExternalEditRollback(ExternalEditState State, ManagedRecoveryInspection Checkpoint);

public sealed class ExternalEditTransactionBackend : IRequestMutationBackend<ExternalEditCommand, ExternalEditPreparation, ExternalEditRollback>
{
    private readonly IExternalEditSession session;
    private readonly Action<ExternalEditFault>? fault;
    private readonly List<string> steps = new();
    private bool restoring;
    public IReadOnlyList<string> PartialNativeSteps => steps.AsReadOnly();
    public int Checkpoints { get; private set; }
    public bool RecoveryAllowed => false;
    public bool Recover(ExternalEditPreparation prepared) => false;
    public ExternalEditTransactionBackend(IExternalEditSession session, Action<ExternalEditFault>? fault = null)
    { this.session = session; this.fault = fault; }
    public ExternalEditPreparation ResolveInputs(CadState state, ExternalEditCommand request)
    {
        steps.Clear(); restoring = false; Checkpoints = 0;
        if ((request.Batch is null) == (request.Scalar is null)) throw new StateException(V03FailureCodes.ModeMismatch, "Exactly one typed edit mode is required.");
        fault?.Invoke(ExternalEditFault.Preparation);
        var external = session.Store.ReadCurrent().External ?? throw new StateException(V03FailureCodes.ModeMismatch, "Expected external companion.");
        if (state.Revision != external.Observation.Selection.ExpectedRevision) throw new StateException("STALE_REFERENCE", "Coordinator revision drifted.");
        return request.Batch is not null ? ExternalEditPlanning.Prepare(external, request.Batch) : ExternalEditPlanning.Prepare(external, request.Scalar!);
    }
    public void Preflight(CadState state, ExternalEditPreparation prepared)
    { session.VerifyLive(prepared.Before); ExternalEditPlanning.Validate(prepared.Proposed); }
    public ExternalEditRollback CaptureRollback(CadState state, ExternalEditPreparation prepared)
    {
        // All fallible native resolution and proposal validation precede the durable marker.
        var previous = session.Store.ReadCurrent();
        var inspection = new ManagedRecoveryInspection(ReopenStatus.Inspectable, null, "Batch-start revision checkpoint.", previous, true);
        session.Store.PrepareCheckpoint(); Checkpoints++;
        return new(prepared.Before, inspection);
    }
    public ChangeSet Execute(ExternalEditPreparation prepared)
    {
        for (var i = 0; i < prepared.Ordered.Count; i++)
        {
            session.Apply(prepared.Ordered[i]); steps.Add(prepared.Ordered[i].Edit.Target + ":" + prepared.Ordered[i].Edit.Parameter);
            if (i == 0) fault?.Invoke(ExternalEditFault.FirstEdit);
            if (i == prepared.Ordered.Count - 1) fault?.Invoke(ExternalEditFault.LastEdit);
            if (!session.RebuildNative()) throw new StateException("FEATURE_REBUILD_FAILED", "Intermediate native edit failed rebuild.");
        }
        return prepared.Changes;
    }
    public bool Rebuild()
    { if (!restoring) fault?.Invoke(ExternalEditFault.Rebuild); return restoring || session.RebuildNative(); }
    public void ValidatePostconditions(ExternalEditPreparation prepared)
    { fault?.Invoke(ExternalEditFault.Postcondition); session.VerifyLive(prepared.Proposed); }
    public CadState ValidateFinal(CadState state, ExternalEditPreparation prepared, ValidationScope scope)
    { session.VerifyLive(prepared.Proposed); return ExternalEditPlanning.Adapter(prepared.Proposed); }
    public void StageState(ExternalEditPreparation prepared, CadState validated) => session.Stage(prepared.Proposed);
    public void Rollback(ExternalEditRollback rollback)
    { fault?.Invoke(ExternalEditFault.Rollback); restoring = true; session.Restore(rollback.Checkpoint); }
    public void ValidateRestored(CadState state, ExternalEditRollback rollback, ValidationScope scope)
    { session.VerifyLive(rollback.State); if (session.Store.Load().Revision != state.Revision) throw new StateException("ROLLBACK_VALIDATION_FAILED", "Restored revision differs."); }
    public void Invalidate() => session.Invalidate();
}
