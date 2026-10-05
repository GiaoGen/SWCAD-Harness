using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;

namespace CadHarness.State;

public interface ICadStateStore
{
    CadState Load();
    void Commit(CadState state);
}

public abstract record MutationPreparation(ChangeSet Changes, FullValidationReason ValidationReasons);

// Native API mechanics and rollback payloads stay in the adapter. No layout
// formula, native interface, or display name belongs in this coordinator.
public interface IMutationBackend<TPrepared, TRollback> : IRequestMutationBackend<OperationNode, TPrepared, TRollback>
    where TPrepared : MutationPreparation { }

public interface IRequestMutationBackend<TRequest, TPrepared, TRollback> where TPrepared : MutationPreparation
{
    TPrepared ResolveInputs(CadState state, TRequest request);
    void Preflight(CadState state, TPrepared prepared);
    TRollback CaptureRollback(CadState state, TPrepared prepared);
    ChangeSet Execute(TPrepared prepared);
    bool Rebuild();
    void ValidatePostconditions(TPrepared prepared);
    bool RecoveryAllowed { get; }
    bool Recover(TPrepared prepared);
    CadState ValidateFinal(CadState state, TPrepared prepared, ValidationScope scope);
    // Stage session metadata before the atomic commit; rollback must restore it
    // if the disk commit fails. Nothing fallible runs after a successful commit.
    void StageState(TPrepared prepared, CadState validated);
    void Rollback(TRollback rollback);
    void ValidateRestored(CadState state, TRollback rollback, ValidationScope scope);
    void Invalidate();
}

public sealed record MutationResult(bool Succeeded, string? FailureCode, string Message, string Stage,
    bool MutationStarted, bool RebuildSucceeded, bool RollbackAttempted, bool RollbackSucceeded, bool StateCommitted,
    bool RecoveryAttempted, long? Revision, ChangeSet Changes, DirtySet? Dirty, ValidationScope? Validation,
    string? RollbackFailureCode = null, string? RollbackFailureMessage = null);

public sealed class MutationTransaction<TPrepared, TRollback> where TPrepared : MutationPreparation
{
    private readonly RequestMutationTransaction<OperationNode, TPrepared, TRollback> transaction;
    public MutationTransaction(ICadStateStore store, IMutationBackend<TPrepared, TRollback> backend) => transaction = new(store, backend);
    public MutationResult Execute(OperationNode operation, FullValidationReason requested = FullValidationReason.None) => transaction.Execute(operation, requested);
}

// The same coordinator owns operation edits and whole construction programs.
public sealed class RequestMutationTransaction<TRequest, TPrepared, TRollback> where TPrepared : MutationPreparation
{
    private readonly ICadStateStore store;
    private readonly IRequestMutationBackend<TRequest, TPrepared, TRollback> backend;
    private bool running;
    public RequestMutationTransaction(ICadStateStore store, IRequestMutationBackend<TRequest, TPrepared, TRollback> backend)
    { this.store = store; this.backend = backend; }

    public MutationResult Execute(TRequest request, FullValidationReason requested = FullValidationReason.None)
    {
        lock (this)
        {
            if (running) return new(false, "TRANSACTION_BUSY", "A mutation transaction is already active.", "integrity",
                false, false, false, false, false, false, null, ChangeSet.Empty, null, null);
            running = true;
        }
        CadState? state = null; TPrepared? prepared = null; TRollback rollback = default!;
        var started = false; var rebuilt = false; var recovery = false;
        var changes = ChangeSet.Empty; DirtySet? dirty = null; ValidationScope? scope = null;
        var stage = "load current state";
        try
        {
            state = store.Load(); StateValidation.Validate(state);
            stage = "resolve inputs"; prepared = backend.ResolveInputs(state, request);
            changes = prepared.Changes; dirty = DirtySet.Expand(state, changes);
            scope = ValidationScope.Select(state, dirty, requested | prepared.ValidationReasons);
            stage = "preflight"; backend.Preflight(state, prepared);
            stage = "capture rollback"; rollback = backend.CaptureRollback(state, prepared);
            stage = "execute"; started = true;
            var actual = backend.Execute(prepared);
            if (!Same(changes, actual)) throw new StateException("TRANSACTION_INTEGRITY_FAILED", "Executed ChangeSet differs from the preflight mutation boundary.");
            stage = "rebuild"; rebuilt = backend.Rebuild();
            if (!rebuilt) throw new StateException("FEATURE_REBUILD_FAILED", "Mutation rebuild failed.");
            stage = "required postconditions";
            try { backend.ValidatePostconditions(prepared); }
            catch when (backend.RecoveryAllowed)
            {
                recovery = true; stage = "bounded recovery";
                if (!backend.Recover(prepared) || !backend.Rebuild()) throw new StateException("RECOVERY_FAILED", "One allowed recovery attempt failed.");
                backend.ValidatePostconditions(prepared);
                scope = ValidationScope.Select(state, dirty, scope.Reasons | FullValidationReason.Recovery);
            }
            stage = "final validation";
            var validated = backend.ValidateFinal(state, prepared, scope);
            StateValidation.Validate(validated);
            if (!validated.Document.Matches(state.Document) || validated.Revision != checked(state.Revision + 1))
                throw new StateException("TRANSACTION_INTEGRITY_FAILED", "Mutation changed identity or did not advance exactly one revision.");
            // Creation introduces identities absent from the baseline. The final
            // report uses the validated state so its full scope includes them.
            dirty = DirtySet.Expand(validated, changes);
            scope = ValidationScope.Select(validated, dirty, scope.Reasons);
            stage = "stage state"; backend.StageState(prepared, validated);
            stage = "atomic state commit"; store.Commit(validated);
            return new(true, null, "Mutation validated and atomically committed.", stage, true, rebuilt, false, false, true,
                recovery, validated.Revision, changes, dirty, scope);
        }
        catch (Exception error)
        {
            var failure = Failure(error); var restored = false; string? rollbackCode = null; string? rollbackMessage = null;
            if (started)
            {
                try
                {
                    backend.Rollback(rollback);
                    if (!backend.Rebuild()) throw new StateException("FEATURE_REBUILD_FAILED", "Rollback rebuild failed.");
                    backend.ValidateRestored(state!, rollback, ValidationScope.Select(state!, dirty!, FullValidationReason.Rollback));
                    restored = true;
                }
                catch (Exception rollbackError)
                {
                    (rollbackCode, rollbackMessage) = Failure(rollbackError);
                    backend.Invalidate();
                }
            }
            return new(false, failure.Code, failure.Message, stage, started, rebuilt, started, restored, false,
                recovery, state?.Revision, changes, dirty, scope, rollbackCode, rollbackMessage);
        }
        finally { lock (this) running = false; }
    }
    private static bool Same(ChangeSet a, ChangeSet b) => a.ChangedFeatures.ToHashSet().SetEquals(b.ChangedFeatures) &&
        a.ChangedParameters.ToHashSet().SetEquals(b.ChangedParameters) && a.PossiblyInvalidatedEntities.ToHashSet().SetEquals(b.PossiblyInvalidatedEntities) &&
        a.CreatedFeatures.ToHashSet().SetEquals(b.CreatedFeatures) && a.CreatedEntities.ToHashSet().SetEquals(b.CreatedEntities);
    private static (string Code, string Message) Failure(Exception error) => error is ICadFailure failure ? (failure.Code, error.Message) : ("MUTATION_FAILED", error.Message);
}
