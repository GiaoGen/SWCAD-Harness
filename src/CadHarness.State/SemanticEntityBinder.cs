using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;

namespace CadHarness.State;

public sealed record BindingQuery(string? SemanticId = null, SemanticType? Type = null, string? OwnerFeature = null,
    string? DependentFeature = null, Point3? OriginMm = null, Vector3? Direction = null, double? RadiusMm = null,
    string? PreferredOwner = null);
public sealed record BindingResult(bool Succeeded, string? FailureCode, string Message,
    SemanticEntityNode? Entity, IReadOnlyList<string> Candidates);

public sealed partial class SemanticEntityBinder
{
    public const int MaximumRankedCandidates = 64;
    public BindingResult Bind(CadState state, InputContract slot, BindingQuery query)
    {
        StateValidation.Validate(state);
        if (query is null || (query.Type is { } type && !slot.Accepts(type)) ||
            (query.OriginMm is not null && !GeometryMath.Finite(query.OriginMm)) ||
            (query.Direction is not null && !GeometryMath.Unit(query.Direction)) ||
            (query.RadiusMm is double radius && (!double.IsFinite(radius) || radius <= 0)))
            return new(false, "BINDING_UNRESOLVED", "Binding constraints cannot satisfy the input contract.", null, Array.Empty<string>());
        var graph = new DependencyGraph(StateRelationData.Dependencies(state));
        var healthyOwners = state.Features.Where(f => f.ReferenceHealth == ReferenceHealth.Healthy).Select(f => f.SemanticId).ToHashSet(StringComparer.Ordinal);
        var candidates = state.Entities.Where(e =>
            (query.SemanticId is null || e.SemanticId == query.SemanticId) && slot.Accepts(e.Type) &&
            (query.Type is null || e.Type == query.Type) && e.ReferenceHealth == ReferenceHealth.Healthy && healthyOwners.Contains(e.OwnerFeatureSemanticId) &&
            (query.OwnerFeature is null || e.OwnerFeatureSemanticId == query.OwnerFeature) &&
            (query.DependentFeature is null || graph.HasPath(e.OwnerFeatureSemanticId, query.DependentFeature)) &&
            (query.OriginMm is null || e.Geometry is { } g && GeometryMath.Near(g.OriginMm, query.OriginMm)) &&
            (query.Direction is null || e.Geometry?.Direction is { } direction && GeometryMath.Parallel(direction, query.Direction)) &&
            (query.RadiusMm is null || e.Geometry?.RadiusMm is { } actual && Math.Abs(actual - query.RadiusMm.Value) <= GeometryMath.ToleranceMm))
            .OrderBy(e => e.SemanticId, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return new(false, "BINDING_UNRESOLVED", "No healthy candidate satisfies type, geometry, ownership and dependency constraints.", null, Array.Empty<string>());
        var ids = candidates.Take(MaximumRankedCandidates).Select(e => e.SemanticId).ToArray();
        if (candidates.Length == 1) return new(true, null, "Unique deterministic candidate.", candidates[0], ids);
        // A declared preferred owner is objective ranking evidence. Do not rank
        // by enumeration, proximity without a bound, or an LLM interpretation.
        if (candidates.Length <= MaximumRankedCandidates && query.PreferredOwner is not null)
        {
            var preferred = candidates.Where(e => e.OwnerFeatureSemanticId == query.PreferredOwner).ToArray();
            if (preferred.Length == 1) return new(true, null, "Unique candidate after bounded ownership ranking.", preferred[0], ids);
        }
        return new(false, "BINDING_AMBIGUOUS", "Multiple legal candidates remain; no justified unique choice.", null, ids);
    }
}
