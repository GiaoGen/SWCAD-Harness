using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using CadHarness.Ir;
using CadHarness.Ir.V03;

namespace CadHarness.State.V03;

public enum QueryCardinality { ExactlyOne, IntentionalBatch }
public enum QueryRelationKind { DirectParentOf, DirectChildOf, DownstreamOf, PatternSeedOf }
public enum QueryOutcome { Ready, Empty, Ambiguous, Unsupported }
public sealed record QueryDimensionFilter(double? Minimum, double? Maximum, IReadOnlyList<double> Values);
public sealed record QueryRelationFilter(QueryRelationKind Kind, string Target);
public sealed record ExternalTargetQuery(NativeSubtype? Subtype, string? NativeType, ParameterKey Parameter,
    ScalarUnit Unit, QueryDimensionFilter? Dimension, string? Host, QueryRelationFilter? Relation,
    EditSupport? Capability, QueryCardinality Cardinality);
public sealed record QueryParameterChange(ExternalTargetQuery Query, double Value);
public sealed record QueryEditSetRequest(string SchemaVersion, int QueryVersion, PartSelection Selection,
    IReadOnlyList<QueryParameterChange> Changes);
public sealed record QueryMatch(int Clause, string Target, string DisplayName, string? NativeReference,
    NativeSubtype Subtype, ParameterKey Parameter, ScalarUnit Unit, double? OldValue, double NewValue,
    EditSupport Support, string Reason);
public sealed record QueryPreviewData(string SchemaVersion, int QueryVersion, QueryEditSetRequest Expression,
    IReadOnlyList<ScalarEdit> OrderedEdits, IReadOnlyList<QueryMatch> Matches,
    IReadOnlyList<string> UnsupportedMatches, IReadOnlyList<string> AmbiguousMatches,
    IReadOnlyList<string> AffectedTargets, IReadOnlyList<ObservedDependency> AffectedDependencies,
    bool FullNativeValidationRequired, string ObservationSha256);
public sealed record QueryPlanningResult(QueryOutcome Outcome, string? FailureCode, string Message,
    IReadOnlyList<QueryMatch> Matches, ExternalQueryPreview? Preview);

public sealed class ExternalQueryPreview
{
    private readonly Guid session;
    private int approved;
    public string Json { get; }
    public string Sha256 { get; }
    internal ExternalQueryPreview(Guid session, QueryPreviewData data)
    { this.session = session; Json = ExternalQueryPlanning.Serialize(data); Sha256 = ExternalQueryPlanning.Hash(Json); }
    public QueryPreviewData Snapshot() => ContractJson.Read<QueryPreviewData>(Json, ExternalQueryPlanning.ValidatePreview);
    public QueryEditApproval Approve(string sha256, long revision, IReadOnlyList<string> exactOrderedTargets)
    {
        var data = Snapshot();
        ContractValidation.Require(sha256 == Sha256 && revision == data.Expression.Selection.ExpectedRevision &&
            exactOrderedTargets.SequenceEqual(data.OrderedEdits.Select(e => e.Target)), "Confirm the exact preview hash, revision and ordered targets.", "QUERY_CONFIRMATION_MISMATCH");
        ContractValidation.Require(Interlocked.CompareExchange(ref approved, 1, 0) == 0, "Preview already approved.", "QUERY_APPROVAL_USED");
        return new(session, Json, Sha256);
    }
}

// An approval is process/session-local, opaque and one-use, not a deserializable permission.
public sealed class QueryEditApproval
{
    private readonly Guid session;
    private readonly string json;
    private readonly string sha256;
    private int used;
    internal QueryEditApproval(Guid session, string json, string sha256)
    { this.session = session; this.json = json; this.sha256 = sha256; }
    public QueryPreviewData Consume(Guid owningSession)
    {
        ContractValidation.Require(session == owningSession, "Approval belongs to another session/Part.", "QUERY_SESSION_MISMATCH");
        ContractValidation.Require(Interlocked.CompareExchange(ref used, 1, 0) == 0, "Approval has already been dispatched.", "QUERY_APPROVAL_USED");
        ContractValidation.Require(ExternalQueryPlanning.Hash(json) == sha256, "Preview bytes changed.", "QUERY_PREVIEW_STALE");
        return ContractJson.Read<QueryPreviewData>(json, ExternalQueryPlanning.ValidatePreview);
    }
}

public static class ExternalQueryPlanning
{
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal static string Serialize(QueryPreviewData data) => ContractJson.Write(data, ValidatePreview);
    public static void Validate(QueryEditSetRequest request)
    {
        ContractValidation.Version(request.SchemaVersion); ContractValidation.Selection(request.Selection);
        ContractValidation.Require(request.QueryVersion == 1 && request.Changes.Count is >= 1 and <= ContractLimits.BatchMaximum, "Bounded query version/changes required.");
        foreach (var change in request.Changes)
        {
            var q = change.Query;
            ContractValidation.Require(Enum.IsDefined(q.Parameter) && Enum.IsDefined(q.Unit) && Enum.IsDefined(q.Cardinality) &&
                (q.Subtype is null || Enum.IsDefined(q.Subtype.Value)) && (q.Capability is null || Enum.IsDefined(q.Capability.Value)) &&
                (q.NativeType is null || ContractValidation.Text(q.NativeType, 256)) && (q.Host is null || ContractValidation.Id(q.Host)), "Invalid query selector.");
            ContractValidation.Scalar(q.Parameter, q.Unit, change.Value);
            if (q.Relation is { } r) ContractValidation.Require(Enum.IsDefined(r.Kind) && ContractValidation.Id(r.Target), "Invalid query relation.");
            if (q.Dimension is { } d)
            {
                ContractValidation.Require(d.Values.Count <= ContractLimits.BatchMaximum && d.Values.Distinct().Count() == d.Values.Count &&
                    (d.Values.Count > 0 ? d.Minimum is null && d.Maximum is null : d.Minimum is not null || d.Maximum is not null), "Use a finite value set OR a bounded interval.");
                foreach (var value in d.Values.Concat(d.Minimum is { } min ? new[] { min } : Array.Empty<double>()).Concat(d.Maximum is { } max ? new[] { max } : Array.Empty<double>()))
                    ContractValidation.Scalar(q.Parameter, q.Unit, value);
                ContractValidation.Require(d.Minimum is null || d.Maximum is null || d.Minimum <= d.Maximum, "Reversed query interval.");
            }
        }
    }
    public static void ValidatePreview(QueryPreviewData data)
    {
        Validate(data.Expression); ContractValidation.Version(data.SchemaVersion);
        ContractValidation.Require(data.QueryVersion == 1 && data.FullNativeValidationRequired &&
            data.UnsupportedMatches.Count == 0 && data.AmbiguousMatches.Count == 0 && data.ObservationSha256.Length == 64 &&
            data.Matches.Count == data.OrderedEdits.Count && data.AffectedTargets.Count <= ContractLimits.Features &&
            data.AffectedDependencies.Count <= ContractLimits.Dependencies, "Invalid executable query preview.");
        ContractValidation.Edits(Batch(data));
    }
    private static EditSetRequest Batch(QueryPreviewData data) => new("0.3", RequestMode.EditSet, ModelOrigin.External, data.Expression.Selection, data.OrderedEdits);
    public static QueryPlanningResult Prepare(ExternalEditState current, QueryEditSetRequest request, Guid session)
    {
        Validate(request); ExternalEditPlanning.Validate(current);
        ContractValidation.Require(request.Selection == current.Observation.Selection, "Query selection fingerprint/revision/configuration drifted.", "QUERY_PREVIEW_STALE");
        var model = current.Observation; var matches = new List<QueryMatch>();
        var graph = new DependencyGraph(model.Dependencies.Select(e => new DependencyEdge(e.Prerequisite, e.Dependent, DependencyKind.NativeInput)));
        QueryPlanningResult Refuse(QueryOutcome outcome, string code, string message) => new(outcome, code, message, matches.ToArray(), null);
        for (var i = 0; i < request.Changes.Count; i++)
        {
            var change = request.Changes[i]; var q = change.Query;
            if (q.Host is not null) ContractValidation.Require(q.Host == current.Geometry.Root, "Unknown/unqualified host selector.", "QUERY_REFERENCE_UNRESOLVED");
            if (q.Relation is { } relation) ContractValidation.Require(model.Features.Any(f => f.SemanticId == relation.Target), "Unresolved query relation target.", "QUERY_REFERENCE_UNRESOLVED");
            var found = model.Features.Where(f => (q.Subtype is null || f.Subtype == q.Subtype) &&
                (q.NativeType is null || f.NativeType == q.NativeType) && (q.Capability is null || f.EditSupport == q.Capability) &&
                (q.Host is null || f.SemanticId == current.Geometry.Root || current.Geometry.Holes.Any(h => h.Target == f.SemanticId) || current.Geometry.Patterns.Any(p => p.Target == f.SemanticId)) &&
                RelationMatches(current, graph, f.SemanticId, q.Relation) && DimensionMatches(f, q)).OrderBy(f => f.TreeOrdinal).ThenBy(f => f.SemanticId, StringComparer.Ordinal).ToArray();
            foreach (var f in found)
            {
                var p = f.Parameters.SingleOrDefault(p => p.Key == q.Parameter);
                matches.Add(new(i, f.SemanticId, f.DisplayName, f.NativeReference?.Base64, f.Subtype, q.Parameter, q.Unit, p?.Value, change.Value, f.EditSupport, f.SupportReason));
            }
            if (found.Length == 0) return Refuse(QueryOutcome.Empty, "QUERY_EMPTY", "No exact query matches; never edit all.");
            if (q.Cardinality == QueryCardinality.ExactlyOne && found.Length != 1)
                return Refuse(QueryOutcome.Ambiguous, V03FailureCodes.AmbiguousTargetSet, "Single-target selection has multiple native identities; refine it or intentionally request a batch.");
            if (q.Cardinality == QueryCardinality.IntentionalBatch && found.Length < ContractLimits.BatchMinimum)
                return Refuse(QueryOutcome.Empty, "QUERY_BATCH_TOO_SMALL", "Intentional batch requires at least two matches.");
            if (matches.Count > ContractLimits.BatchMaximum) return Refuse(QueryOutcome.Unsupported, "QUERY_LIMIT_EXCEEDED", "Query exceeds the finite EditSet bound; no truncation.");
        }
        foreach (var match in matches)
        {
            try { NativeQualificationCandidates.RequireExecutable(model, match.Target, match.Parameter); }
            catch (ContractException) { return Refuse(QueryOutcome.Unsupported, V03FailureCodes.ObservedOnlyTarget, "Query includes an unsupported/accessor-unqualified match; no silent omission."); }
        }
        var requestBatch = new EditSetRequest("0.3", RequestMode.EditSet, ModelOrigin.External, request.Selection,
            matches.Select(m => new ScalarEdit(m.Target, m.Parameter, m.Unit, m.OldValue!.Value, m.NewValue)).ToArray());
        NativeQualificationCandidates.RequireExecutable(model, requestBatch);
        var prepared = ExternalEditPlanning.Prepare(current, requestBatch);
        var affected = graph.AffectedBy(requestBatch.Edits.Select(e => e.Target));
        var data = new QueryPreviewData("0.3", 1, request, prepared.Ordered.Select(e => e.Edit).ToArray(), matches.ToArray(),
            Array.Empty<string>(), Array.Empty<string>(), affected, model.Dependencies.Where(e => affected.Contains(e.Prerequisite) || affected.Contains(e.Dependent)).ToArray(),
            true, Hash(ContractJson.Write(model, ObservedStateValidation.Validate)));
        return new(QueryOutcome.Ready, null, "Exact ordered batch preview; confirm hash, revision and targets before dispatch.", matches.ToArray(), new(session, data));
    }
    public static EditSetRequest Revalidate(ExternalEditState current, QueryPreviewData approved)
    {
        var fresh = Prepare(current, approved.Expression, Guid.Empty);
        ContractValidation.Require(fresh.Preview is not null && fresh.Preview.Json == Serialize(approved), "Query membership, values, native identity or validation scope changed.", "QUERY_PREVIEW_STALE");
        return Batch(approved);
    }
    private static bool DimensionMatches(ObservedFeature feature, ExternalTargetQuery query)
    {
        if (query.Dimension is not { } d) return true;
        var p = feature.Parameters.SingleOrDefault(p => p.Key == query.Parameter);
        if (p is null) return false;
        // Cover meter-to-millimeter floating representation only, not geometric tolerance or nearest-target search.
        return d.Values.Count > 0 ? d.Values.Any(v => NumericIdentity(p.Value, v)) :
            (d.Minimum is null || p.Value >= d.Minimum || NumericIdentity(p.Value, d.Minimum.Value)) &&
            (d.Maximum is null || p.Value <= d.Maximum || NumericIdentity(p.Value, d.Maximum.Value));
    }
    private static bool NumericIdentity(double actual, double requested) => actual == requested ||
        Math.Abs(actual - requested) <= 8 * Math.Max(Math.Abs(Math.BitIncrement(actual) - actual), Math.Abs(Math.BitIncrement(requested) - requested));
    private static bool RelationMatches(ExternalEditState current, DependencyGraph graph, string id, QueryRelationFilter? filter) => filter is null || filter.Kind switch
    {
        QueryRelationKind.DirectParentOf => current.Observation.Dependencies.Any(e => e.Prerequisite == id && e.Dependent == filter.Target),
        QueryRelationKind.DirectChildOf => current.Observation.Dependencies.Any(e => e.Prerequisite == filter.Target && e.Dependent == id),
        QueryRelationKind.DownstreamOf => id != filter.Target && graph.HasPath(filter.Target, id),
        QueryRelationKind.PatternSeedOf => current.Geometry.Patterns.Any(p => p.Target == filter.Target && p.Seed == id),
        _ => false
    };
}
