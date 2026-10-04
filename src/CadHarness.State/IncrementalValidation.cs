using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;

namespace CadHarness.State;

public sealed record ChangeSet(IReadOnlyList<string> ChangedFeatures, IReadOnlyList<string> ChangedParameters,
    IReadOnlyList<string> PossiblyInvalidatedEntities)
{
    public static ChangeSet Empty { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}

public sealed record DirtySet(IReadOnlyList<string> Features, IReadOnlyList<string> Parameters,
    IReadOnlyList<string> Entities, IReadOnlyList<DesignRelation> Relations)
{
    public static DirtySet Expand(CadState state, ChangeSet changes)
    {
        var featureIds = state.Features.Select(f => f.SemanticId).ToHashSet(StringComparer.Ordinal);
        var entityIds = state.Entities.Select(e => e.SemanticId).ToHashSet(StringComparer.Ordinal);
        var parameterIds = state.Parameters.Select(p => p.SemanticId).ToHashSet(StringComparer.Ordinal);
        if (changes.ChangedFeatures.Any(f => !featureIds.Contains(f)) ||
            changes.ChangedParameters.Any(p => !parameterIds.Contains(p)) ||
            changes.PossiblyInvalidatedEntities.Any(e => !entityIds.Contains(e)))
            throw new StateException("CHANGESET_INVALID", "ChangeSet must name existing managed identities.");
        var relations = StateRelationData.Relations(state);
        var roots = changes.ChangedFeatures.Concat(state.Bindings.Where(b => changes.ChangedParameters.Contains(b.ParameterSemanticId)).Select(b => b.OwnerFeatureSemanticId))
            .Concat(relations.Where(r => r.Reference is not null && changes.PossiblyInvalidatedEntities.Contains(r.Reference)).Select(r => r.Subject));
        var features = new DependencyGraph(StateRelationData.Dependencies(state)).AffectedBy(roots);
        var owned = state.Entities.Where(e => features.Contains(e.OwnerFeatureSemanticId)).Select(e => e.SemanticId);
        var invalidated = owned.Concat(changes.PossiblyInvalidatedEntities).ToHashSet(StringComparer.Ordinal);
        var dirtyRelations = relations.Where(r => features.Contains(r.Subject) || (r.Reference is not null && invalidated.Contains(r.Reference))).ToArray();
        // Referenced hosts/frames/directions are read dependencies; reading them
        // does not make another layout on the same host dirty.
        var entities = invalidated.Concat(dirtyRelations.Where(r => r.Reference is not null).Select(r => r.Reference!));
        return new(features, Sorted(changes.ChangedParameters), Sorted(entities), dirtyRelations);
    }
    internal static string[] Sorted(IEnumerable<string> ids) => ids.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
}

[Flags]
public enum FullValidationReason
{
    None = 0, NewModelFinalization = 1, HighRiskTopology = 2, Recovery = 4,
    ReferenceReresolution = 8, StateDriftSuspicion = 16, ExplicitFullValidate = 32,
    BenchmarkFaultInjection = 64, Rollback = 128
}

public sealed record ValidationScope(bool FullModel, FullValidationReason Reasons,
    IReadOnlyList<string> Parameters, IReadOnlyList<string> Entities, IReadOnlyList<DesignRelation> Relations)
{
    public static ValidationScope Select(CadState state, DirtySet dirty, FullValidationReason reasons)
    {
        const FullValidationReason allowed = FullValidationReason.NewModelFinalization | FullValidationReason.HighRiskTopology |
            FullValidationReason.Recovery | FullValidationReason.ReferenceReresolution | FullValidationReason.StateDriftSuspicion |
            FullValidationReason.ExplicitFullValidate | FullValidationReason.BenchmarkFaultInjection | FullValidationReason.Rollback;
        if ((reasons & ~allowed) != 0) throw new StateException("VALIDATION_POLICY_INVALID", "Unknown escalation reason.");
        return reasons == FullValidationReason.None ? new(false, reasons, dirty.Parameters, dirty.Entities, dirty.Relations) :
            new(true, reasons, DirtySet.Sorted(state.Parameters.Select(p => p.SemanticId)),
                DirtySet.Sorted(state.Entities.Select(e => e.SemanticId)), StateRelationData.Relations(state));
    }
}

// Read sets describe actual native reads, not duration or benchmark estimates.
public sealed record ValidationReadSet(string Stage, ValidationScope Scope, IReadOnlyList<string> FeatureStatusReads,
    IReadOnlyList<string> ParameterReads, IReadOnlyList<string> EntityReads, IReadOnlyList<DesignRelation> RelationReads);
