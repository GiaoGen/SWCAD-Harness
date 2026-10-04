using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir;

namespace CadHarness.State;

public sealed partial class SemanticEntityBinder
{
    public const int MaximumJudgeCandidates = 8;
    public const int MaximumJudgeIntentBytes = 2048;
    public const int MaximumJudgeRationaleBytes = 1024;
    private readonly IBoundedJudge? judge;
    private readonly TimeSpan judgeTimeout;

    public SemanticEntityBinder(IBoundedJudge? judge = null, TimeSpan? judgeTimeout = null)
    {
        this.judge = judge;
        this.judgeTimeout = judgeTimeout ?? TimeSpan.FromSeconds(10);
        if (this.judgeTimeout <= TimeSpan.Zero || this.judgeTimeout > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(judgeTimeout), "Judge timeout must be positive and at most 30 seconds.");
    }

    // Explicit opt-in to semantic judgement. Bind remains synchronous and fully
    // deterministic, including when a judge has been supplied to this instance.
    // This method has no COM access; native callers must resume on their owning
    // STA and recheck document/revision and native reference before execution.
    public async Task<JudgedBindingResult> BindAsync(CadState state, InputContract slot,
        BindingQuery query, string intent, CancellationToken cancellationToken = default)
    {
        StateValidation.Validate(state);
        var snapshot = Snapshot(state);
        var input = slot with { AcceptedTypes = Array.AsReadOnly(slot.AcceptedTypes.ToArray()) };
        var deterministic = Bind(snapshot, input, query);
        if (deterministic.FailureCode != "BINDING_AMBIGUOUS")
            return new(deterministic, BoundedJudgeStatus.NotNeeded, 0);
        JudgedBindingResult Unresolved(BoundedJudgeStatus status, int calls, string message, string? rationale = null) =>
            new(deterministic with { Message = message }, status, calls, rationale);
        if (cancellationToken.IsCancellationRequested)
            return Unresolved(BoundedJudgeStatus.Cancelled, 0, "Candidate judgement cancelled; ambiguity remains unresolved.");
        if (judge is null)
            return Unresolved(BoundedJudgeStatus.NotConfigured, 0, "No bounded judge configured; ambiguity remains unresolved.");
        // Never truncate to the judge budget. The deterministic binder's 64-ID
        // diagnostic cap is larger than this limit, so <=8 is a complete set.
        if (deterministic.Candidates.Count > MaximumJudgeCandidates)
            return Unresolved(BoundedJudgeStatus.CandidateLimitExceeded, 0, "Legal candidate set exceeds the judge bound; narrow deterministic constraints.");
        if (string.IsNullOrWhiteSpace(intent) || Encoding.UTF8.GetByteCount(intent) > MaximumJudgeIntentBytes ||
            string.IsNullOrWhiteSpace(input.Name) || Encoding.UTF8.GetByteCount(input.Name) > 128 ||
            (input.Role is { } role && !Enum.IsDefined(role)))
            return Unresolved(BoundedJudgeStatus.InvalidRequest, 0, "Bounded semantic intent or input metadata is invalid.");

        var legalIds = deterministic.Candidates.ToHashSet(StringComparer.Ordinal);
        var features = snapshot.Features.ToDictionary(f => f.SemanticId, StringComparer.Ordinal);
        var candidates = snapshot.Entities.Where(e => legalIds.Contains(e.SemanticId))
            .OrderBy(e => e.SemanticId, StringComparer.Ordinal)
            .Select(e => new BoundedJudgeCandidate(e.SemanticId, e.Type, e.OwnerFeatureSemanticId,
                features[e.OwnerFeatureSemanticId].Kind, e.Geometry)).ToArray();
        var request = new BoundedJudgeRequest(Guid.NewGuid(), intent, input.Name, input.Role, candidates);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(judgeTimeout);
        BoundedJudgeDecision decision;
        Task<BoundedJudgeDecision>? pending = null;
        try
        {
            pending = judge.SelectCandidateAsync(request, deadline.Token);
            decision = await pending.WaitAsync(deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            ObserveLateFailure(pending);
            return Unresolved(cancellationToken.IsCancellationRequested ? BoundedJudgeStatus.Cancelled :
                deadline.IsCancellationRequested ? BoundedJudgeStatus.TimedOut : BoundedJudgeStatus.Failed,
                1, "Candidate judgement cancelled or timed out; ambiguity remains unresolved.");
        }
        catch (Exception)
        {
            // Adapter exceptions may contain credentials/provider payloads.
            return Unresolved(BoundedJudgeStatus.Failed, 1, "Bounded judge failed; ambiguity remains unresolved.");
        }
        if (decision is null || decision.RequestId != request.RequestId || !Enum.IsDefined(decision.Choice) ||
            decision.Rationale is null || Encoding.UTF8.GetByteCount(decision.Rationale) > MaximumJudgeRationaleBytes ||
            (decision.Choice == BoundedJudgeChoice.Abstain && decision.SelectedSemanticId is not null) ||
            (decision.Choice == BoundedJudgeChoice.Select &&
                (!legalIds.Contains(decision.SelectedSemanticId ?? "") || string.IsNullOrWhiteSpace(decision.Rationale))))
            return Unresolved(BoundedJudgeStatus.InvalidResponse, 1, "Judge response does not justify one member of the legal set.");
        if (decision.Choice == BoundedJudgeChoice.Abstain)
            return Unresolved(BoundedJudgeStatus.Abstained, 1, "Judge abstained; ambiguity remains unresolved.", decision.Rationale);
        try
        {
            // Snapshot protects the request from mutable IReadOnlyList backing
            // arrays; reject changes to the caller's supplied state or contract
            // while awaiting the judge. Immutable record replacements outside
            // this call still require the caller's usual revision check.
            StateValidation.Validate(state);
            if (!SameState(snapshot, state) || !input.AcceptedTypes.SequenceEqual(slot.AcceptedTypes) ||
                !Bind(state, input, query).Candidates.SequenceEqual(deterministic.Candidates))
                return Unresolved(BoundedJudgeStatus.StateChanged, 1, "State or binding contract changed during judgement; bind again.");
        }
        catch (Exception)
        {
            return Unresolved(BoundedJudgeStatus.StateChanged, 1, "State became invalid during judgement; bind again.");
        }
        var selected = snapshot.Entities.Single(e => e.SemanticId == decision.SelectedSemanticId);
        return new(new(true, null, "One legal candidate selected by the bounded judge.", selected,
            Array.AsReadOnly(deterministic.Candidates.ToArray())), BoundedJudgeStatus.Selected, 1, decision.Rationale);
    }

    private static CadState Snapshot(CadState state) => state with
    {
        Features = Array.AsReadOnly(state.Features.ToArray()), Entities = Array.AsReadOnly(state.Entities.ToArray()),
        Parameters = Array.AsReadOnly(state.Parameters.ToArray()), Bindings = Array.AsReadOnly(state.Bindings.ToArray()),
        Relations = Array.AsReadOnly(state.Relations.Select(r => r.Clone()).ToArray()),
        Dependencies = Array.AsReadOnly(state.Dependencies.Select(d => d.Clone()).ToArray())
    };
    private static bool SameState(CadState before, CadState after) =>
        before.SchemaVersion == after.SchemaVersion && before.Document == after.Document && before.Revision == after.Revision &&
        before.Features.SequenceEqual(after.Features) && before.Entities.SequenceEqual(after.Entities) &&
        before.Parameters.SequenceEqual(after.Parameters) && before.Bindings.SequenceEqual(after.Bindings) &&
        before.Relations.Select(r => r.GetRawText()).SequenceEqual(after.Relations.Select(r => r.GetRawText())) &&
        before.Dependencies.Select(d => d.GetRawText()).SequenceEqual(after.Dependencies.Select(d => d.GetRawText()));
    private static void ObserveLateFailure(Task<BoundedJudgeDecision>? pending)
    {
        if (pending is not null)
            _ = pending.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
