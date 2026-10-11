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

public interface IRequestMutationBackend<TRequest, TPrepared, TRollback> : IRequestMutationBackend<CadState, TRequest, TPrepared, TRollback>
    where TPrepared : MutationPreparation { }

public interface IRequestMutationBackend<TState, TRequest, TPrepared, TRollback> where TPrepared : MutationPreparation
{
    TPrepared ResolveInputs(TState state, TRequest request);
    void Preflight(TState state, TPrepared prepared);
    TRollback CaptureRollback(TState state, TPrepared prepared);
    ChangeSet Execute(TPrepared prepared);
    bool Rebuild();
    void ValidatePostconditions(TPrepared prepared);
    bool RecoveryAllowed { get; }
    bool Recover(TPrepared prepared);
    TState ValidateFinal(TState state, TPrepared prepared, ValidationScope scope);
    // Stage session metadata before the atomic commit; rollback must restore it
    // if the disk commit fails. Nothing fallible runs after a successful commit.
    void StageState(TPrepared prepared, TState validated);
    void Rollback(TRollback rollback);
    void ValidateRestored(TState state, TRollback rollback, ValidationScope scope);
    void Invalidate();
}

public interface IMutationStateStore<TState>
{
    TState Load();
    void Commit(TState state);
}
public interface IMutationStatePolicy<TState>
{
    void Validate(TState state);
    long Revision(TState state);
    bool SameIdentity(TState before, TState after);
    DirtySet Expand(TState state, ChangeSet changes);
    ValidationScope Scope(TState state, DirtySet dirty, FullValidationReason reasons);
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
    private readonly RequestMutationTransaction<CadState, TRequest, TPrepared, TRollback> transaction;
    private sealed class Store(ICadStateStore inner) : IMutationStateStore<CadState>
    { public CadState Load() => inner.Load(); public void Commit(CadState state) => inner.Commit(state); }
    private sealed class Policy : IMutationStatePolicy<CadState>
    {
        public void Validate(CadState state) => StateValidation.Validate(state);
        public long Revision(CadState state) => state.Revision;
        public bool SameIdentity(CadState before, CadState after) => before.Document.Matches(after.Document);
        public DirtySet Expand(CadState state, ChangeSet changes) => DirtySet.Expand(state, changes);
        public ValidationScope Scope(CadState state, DirtySet dirty, FullValidationReason reasons) => ValidationScope.Select(state, dirty, reasons);
    }
    public RequestMutationTransaction(ICadStateStore store, IRequestMutationBackend<TRequest, TPrepared, TRollback> backend)
        => transaction = new(new Store(store), backend, new Policy());
    public MutationResult Execute(TRequest request, FullValidationReason requested = FullValidationReason.None) => transaction.Execute(request, requested);
}

// Versioned state contracts share the same mutation sequence without encoding
// new feature kinds as fictitious v0.2 operations.
public sealed class RequestMutationTransaction<TState, TRequest, TPrepared, TRollback>
    where TState : class where TPrepared : MutationPreparation
{
    private readonly IMutationStateStore<TState> store;
    private readonly IRequestMutationBackend<TState, TRequest, TPrepared, TRollback> backend;
    private readonly IMutationStatePolicy<TState> policy;
    private bool running;
    public RequestMutationTransaction(IMutationStateStore<TState> store, IRequestMutationBackend<TState, TRequest, TPrepared, TRollback> backend, IMutationStatePolicy<TState> policy)
    { this.store = store; this.backend = backend; this.policy = policy; }

    public MutationResult Execute(TRequest request, FullValidationReason requested = FullValidationReason.None)
    {
        lock (this)
        {
            if (running) return new(false, "TRANSACTION_BUSY", "A mutation transaction is already active.", "integrity",
                false, false, false, false, false, false, null, ChangeSet.Empty, null, null);
            running = true;
        }
        TState? state = null; TPrepared? prepared = null; TRollback rollback = default!;
        var started = false; var rebuilt = false; var recovery = false;
        var changes = ChangeSet.Empty; DirtySet? dirty = null; ValidationScope? scope = null;
        var stage = "load current state";
        try
        {
            state = store.Load();
            using (ExecutionTelemetry.Measure(ExecutionPhase.Validation)) policy.Validate(state);
            stage = "resolve inputs";
            using (ExecutionTelemetry.Measure(ExecutionPhase.Validation)) prepared = backend.ResolveInputs(state, request);
            changes = prepared.Changes; dirty = policy.Expand(state, changes);
            scope = policy.Scope(state, dirty, requested | prepared.ValidationReasons);
            stage = "preflight";
            using (ExecutionTelemetry.Measure(ExecutionPhase.Validation)) backend.Preflight(state, prepared);
            stage = "capture rollback"; rollback = backend.CaptureRollback(state, prepared);
            stage = "execute"; started = true;
            var actual = backend.Execute(prepared);
            if (!Same(changes, actual)) throw new StateException("TRANSACTION_INTEGRITY_FAILED", "Executed ChangeSet differs from the preflight mutation boundary.");
            stage = "rebuild"; rebuilt = backend.Rebuild();
            if (!rebuilt) throw new StateException("FEATURE_REBUILD_FAILED", "Mutation rebuild failed.");
            stage = "required postconditions";
            try { using (ExecutionTelemetry.Measure(ExecutionPhase.Validation)) backend.ValidatePostconditions(prepared); }
            catch when (backend.RecoveryAllowed)
            {
                recovery = true; stage = "bounded recovery";
                if (!ExecutionTelemetry.Recover(() => backend.Recover(prepared)) || !backend.Rebuild()) throw new StateException("RECOVERY_FAILED", "One allowed recovery attempt failed.");
                using (ExecutionTelemetry.Measure(ExecutionPhase.Validation)) backend.ValidatePostconditions(prepared);
                scope = policy.Scope(state, dirty, scope.Reasons | FullValidationReason.Recovery);
            }
            stage = "final validation";
            TState validated;
            using (ExecutionTelemetry.Measure(ExecutionPhase.Validation))
            { validated = backend.ValidateFinal(state, prepared, scope); policy.Validate(validated); }
            if (!policy.SameIdentity(state, validated) || policy.Revision(validated) != checked(policy.Revision(state) + 1))
                throw new StateException("TRANSACTION_INTEGRITY_FAILED", "Mutation changed identity or did not advance exactly one revision.");
            // Creation introduces identities absent from the baseline. The final
            // report uses the validated state so its full scope includes them.
            dirty = policy.Expand(validated, changes);
            scope = policy.Scope(validated, dirty, scope.Reasons);
            stage = "stage state"; backend.StageState(prepared, validated);
            stage = "atomic state commit"; store.Commit(validated);
            return new(true, null, "Mutation validated and atomically committed.", stage, true, rebuilt, false, false, true,
                recovery, policy.Revision(validated), changes, dirty, scope);
        }
        catch (Exception error)
        {
            var failure = Failure(error); var restored = false; string? rollbackCode = null; string? rollbackMessage = null;
            if (started)
            {
                try
                {
                    ExecutionTelemetry.Rollback(() => backend.Rollback(rollback));
                    if (!backend.Rebuild()) throw new StateException("FEATURE_REBUILD_FAILED", "Rollback rebuild failed.");
                    using (ExecutionTelemetry.Measure(ExecutionPhase.Validation))
                        backend.ValidateRestored(state!, rollback, policy.Scope(state!, dirty!, FullValidationReason.Rollback));
                    restored = true;
                }
                catch (Exception rollbackError)
                {
                    (rollbackCode, rollbackMessage) = Failure(rollbackError);
                    backend.Invalidate();
                }
            }
            return new(false, failure.Code, failure.Message, stage, started, rebuilt, started, restored, false,
                recovery, state is null ? null : policy.Revision(state), changes, dirty, scope, rollbackCode, rollbackMessage);
        }
        finally { lock (this) running = false; }
    }
    private static bool Same(ChangeSet a, ChangeSet b) => a.ChangedFeatures.ToHashSet().SetEquals(b.ChangedFeatures) &&
        a.ChangedParameters.ToHashSet().SetEquals(b.ChangedParameters) && a.PossiblyInvalidatedEntities.ToHashSet().SetEquals(b.PossiblyInvalidatedEntities) &&
        a.CreatedFeatures.ToHashSet().SetEquals(b.CreatedFeatures) && a.CreatedEntities.ToHashSet().SetEquals(b.CreatedEntities);
    private static (string Code, string Message) Failure(Exception error) => error is ICadFailure failure ? (failure.Code, error.Message) :
        error is CadHarness.Ir.V03.ContractException contract ? (contract.Code, error.Message) : ("MUTATION_FAILED", error.Message);
}
