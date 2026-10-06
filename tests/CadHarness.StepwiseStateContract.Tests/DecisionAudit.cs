using System;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal sealed record DecisionAudit(string Outcome, string? OperationKind, string? OperationId, string? SemanticId,
    bool SingleDecisionParsePassed, string[] DuplicateOperationIds, string[] DuplicateSemanticIds, string[] RepeatedCommittedFeatureIds,
    string? FailureClass, bool GenuineStepwisePlanningFailure);
internal static class DecisionAuditor
{
    internal static DecisionAudit Analyze(string? json, CadProgram? prior, CadState? state, RuntimeCapabilityCatalog catalog,
        bool accepted, string? failureStage, string? code)
    {
        CadProgram? program = null; string outcome = "no_model_response";
        if (json is not null)
        {
            try
            {
                using var envelope = JsonDocument.Parse(json);
                outcome = envelope.RootElement.GetProperty("outcome").GetString() ?? "invalid";
                if (outcome == "planned") program = new CadProgramJson(catalog.Registry).Parse(envelope.RootElement.GetProperty("program").GetRawText()).Program;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) { outcome = "invalid_envelope"; }
        }
        var operation = program?.Operations.FirstOrDefault();
        var duplicateOperations = program?.Operations.Select(o => o.Id).Intersect(prior?.Operations.Select(o => o.Id) ?? Array.Empty<string>(), StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        var occupied = state is null ? Array.Empty<string>() : state.Features.Select(f => f.SemanticId).Concat(state.Entities.Select(e => e.SemanticId)).Concat(state.Parameters.Select(p => p.SemanticId)).ToArray();
        var duplicateSemantics = program?.Operations.Where(o => o.SemanticId is not null).Select(o => o.SemanticId!).Intersect(occupied, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        bool SameFeature(OperationNode a, OperationNode b) => a.Kind == b.Kind && a.Parameters.Count == b.Parameters.Count && a.Parameters.All(p => b.Parameters.TryGetValue(p.Key, out var v) && Equals(p.Value, v)) &&
            a.Inputs.Count == b.Inputs.Count && a.Inputs.All(i => b.Input(i.Name) is { } input && input.References.SequenceEqual(i.References));
        var repeated = operation is null ? Array.Empty<string>() : prior?.Operations.Where(o => SameFeature(o, operation)).Select(o => o.SemanticId!).ToArray() ?? Array.Empty<string>();
        var providerFailure = failureStage == "provider_structured_output";
        var infrastructure = code is "PLANNER_TRANSPORT_FAILED" or "PLANNER_CANCELLED" || json is null && !providerFailure;
        var failureClass = accepted ? null : infrastructure ? "infrastructure_failure" : providerFailure ? "provider/schema_failure" : failureStage is "committed_observation_target_validation" or "completion" ? "genuine_stepwise_planning_failure" : "local_contract_rejection";
        var genuine = !accepted && json is not null && !providerFailure && !infrastructure;
        return new(outcome, operation is null ? null : WireNames.Of(operation.Kind), operation?.Id, operation?.SemanticId,
            program is not null, duplicateOperations, duplicateSemantics, repeated, failureClass, genuine);
    }
}
