using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir;

namespace CadHarness.State;

// Semantic ranking only. Native measurement, persistent-reference resolution,
// rebuild/body checks and CAD execution remain deterministic backend duties.
public interface IBoundedJudge
{
    Task<BoundedJudgeDecision> SelectCandidateAsync(BoundedJudgeRequest request, CancellationToken cancellationToken);
}

public sealed record BoundedJudgeCandidate(string SemanticId, SemanticType Type,
    string OwnerFeatureSemanticId, OperationKind OwnerKind, SemanticGeometry? Geometry);

// Constructed only by the binder from its complete, deterministically legal set.
// No CADState, native reference, path, operation registry or execution callback.
public sealed class BoundedJudgeRequest
{
    public Guid RequestId { get; }
    public string Intent { get; }
    public string InputName { get; }
    public SemanticRole? Role { get; }
    public IReadOnlyList<BoundedJudgeCandidate> Candidates { get; }
    internal BoundedJudgeRequest(Guid requestId, string intent, string inputName, SemanticRole? role,
        BoundedJudgeCandidate[] candidates)
    {
        RequestId = requestId; Intent = intent; InputName = inputName; Role = role;
        Candidates = Array.AsReadOnly((BoundedJudgeCandidate[])candidates.Clone());
    }
}

public enum BoundedJudgeChoice { Select, Abstain }
public sealed record BoundedJudgeDecision(Guid RequestId, BoundedJudgeChoice Choice,
    string? SelectedSemanticId, string Rationale);

public enum BoundedJudgeStatus
{
    NotNeeded, NotConfigured, CandidateLimitExceeded, InvalidRequest,
    Selected, Abstained, InvalidResponse, Failed, Cancelled, TimedOut, StateChanged
}

public sealed record JudgedBindingResult(BindingResult Binding, BoundedJudgeStatus JudgeStatus,
    int JudgeCalls, string? Rationale = null);
