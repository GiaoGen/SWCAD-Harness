using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CadHarness.Ir;

namespace CadHarness.State;

public static class StateValidation
{
    public static void Validate(CadState state)
    {
        if (state is null || state.SchemaVersion != "0.2" || state.Revision < 0) Invalid("Only v0.2 state with a nonnegative revision is supported; no v0.1 migration exists.");
        var document = state!.Document;
        if (document is null || document.DocumentId == Guid.Empty || document.ConfigurationId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.ConfigurationName) || document.ConfigurationName.Length > 256 ||
            document.SavedPath is null || (document.SavedPath.Length != 0 && (!Path.IsPathFullyQualified(document.SavedPath) ||
            !string.Equals(Path.GetExtension(document.SavedPath), ".sldprt", StringComparison.OrdinalIgnoreCase))))
            Invalid("Part document and configuration identity are required; empty path denotes an unsaved live Part.");
        if (state.Features is null || state.Entities is null || state.Parameters is null || state.Bindings is null ||
            state.Relations is null || state.Dependencies is null) Invalid("All state arrays are required.");
        // An empty managed Part is a legitimate construction transaction baseline.
        if (state.Features!.Count > 256 || state.Entities!.Count > 1024 || state.Parameters!.Count > 4096 || state.Bindings!.Count > 4096)
            Invalid("State exceeds finite schema bounds.");
        if (state.Relations!.Count > 48 || state.Dependencies!.Count > 4096) Invalid("Relation/dependency count exceeds finite state bounds.");
        var features = new Dictionary<string, FeatureNode>(StringComparer.Ordinal);
        foreach (var feature in state.Features)
        {
            if (feature is null || !Id(feature.SemanticId) || !Enum.IsDefined(feature.Kind) || !Enum.IsDefined(feature.ReferenceHealth))
                Invalid("Feature identity, operation kind or reference health is invalid.");
            if (!features.TryAdd(feature!.SemanticId, feature)) Invalid("Duplicate feature semantic ID.");
            ValidateReference(feature.NativeReference);
        }
        var entities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in state.Entities!)
        {
            if (entity is null || !Id(entity.SemanticId) || !Enum.IsDefined(entity.Type) || !Enum.IsDefined(entity.ReferenceHealth) ||
                !Id(entity.OwnerFeatureSemanticId) || !features.ContainsKey(entity.OwnerFeatureSemanticId)) Invalid("Entity type or feature ownership is invalid.");
            if (!entities.Add(entity!.SemanticId)) Invalid("Duplicate entity semantic ID.");
            ValidateReference(entity.NativeReference);
            if (entity.Type == SemanticType.LocalFrame && entity.Geometry?.Frame is null) Invalid("A local frame requires typed frame geometry.");
            if (entity.Type == SemanticType.ReferenceAxis && entity.Geometry?.Direction is null) Invalid("A semantic reference axis requires typed axis geometry.");
            if (entity.Geometry is { } geometry && (!GeometryMath.Valid(geometry) ||
                (entity.Type == SemanticType.LocalFrame && geometry.Frame is null))) Invalid("Entity geometry/frame is invalid.");
        }
        var parameters = new Dictionary<string, ParameterNode>(StringComparer.Ordinal);
        foreach (var parameter in state.Parameters!)
        {
            if (parameter is null || !Id(parameter.SemanticId) || !double.IsFinite(parameter.Value) || parameter.Value <= 0 ||
                parameter.Kind is not (ParameterKind.Length or ParameterKind.Count or ParameterKind.Angle) ||
                (parameter.Kind == ParameterKind.Count && (parameter.Value != Math.Truncate(parameter.Value) || parameter.Value > int.MaxValue)))
                Invalid("Parameter must have a finite, positive typed scalar value (mm, degrees or integer count).");
            if (!parameters.TryAdd(parameter!.SemanticId, parameter)) Invalid("Duplicate parameter semantic ID.");
        }
        var bound = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in state.Bindings!)
        {
            if (binding is null || !Id(binding.ParameterSemanticId) || !Id(binding.OwnerFeatureSemanticId) || !Enum.IsDefined(binding.Parameter) ||
                !parameters.ContainsKey(binding.ParameterSemanticId) || !features.ContainsKey(binding.OwnerFeatureSemanticId)) Invalid("Parameter binding or owner is invalid.");
            if (!bound.Add(binding!.ParameterSemanticId)) Invalid("Duplicate parameter binding.");
            var parameter = parameters[binding.ParameterSemanticId];
            var contract = EditableParameters.Contract(binding.Parameter);
            if (parameter.Kind != contract.Kind ||
                (contract.ExclusiveMinimum.HasValue && parameter.Value <= contract.ExclusiveMinimum.Value) ||
                (contract.Minimum.HasValue && parameter.Value < contract.Minimum.Value) ||
                (contract.Maximum.HasValue && parameter.Value > contract.Maximum.Value))
                Invalid("Bound parameter type or numeric value differs from its semantic parameter contract.");
        }
        if (bound.Count != parameters.Count) Invalid("Every parameter must have exactly one binding.");
        var byEntity = state.Entities.ToDictionary(e => e.SemanticId, StringComparer.Ordinal);
        foreach (var relation in StateRelationData.Relations(state))
        {
            if (!Enum.IsDefined(relation.Kind) || !Id(relation.Subject) || !features.ContainsKey(relation.Subject)) Invalid("Relation subject must be a managed feature.");
            if (relation.Kind == RelationKind.ThroughAll)
            { if (relation.Reference is not null) Invalid("ThroughAll is unary."); continue; }
            if (!Id(relation.Reference)) Invalid("Relation reference must be a managed semantic entity.");
            if (!byEntity.TryGetValue(relation.Reference!, out var reference)) Invalid("Relation reference must be a managed semantic entity.");
            var expected = relation.Kind switch
            {
                RelationKind.HostedOn => SemanticType.PlanarFace,
                RelationKind.PatternSeed or RelationKind.EqualSpacing => SemanticType.FeatureRef,
                RelationKind.CenteredAbout => SemanticType.LocalFrame,
                RelationKind.SymmetricAboutAxis => SemanticType.ReferenceAxis,
                _ => reference!.Type
            };
            if (reference!.Type != expected) Invalid("Relation reference has the wrong semantic type.");
            var subjectKind = features[relation.Subject].Kind;
            if (relation.Kind == RelationKind.HostedOn && subjectKind is not (OperationKind.CreateThroughHole or OperationKind.CreateBlindHole)) Invalid("HostedOn subject must be a hole.");
            if (relation.Kind is RelationKind.PatternSeed or RelationKind.EqualSpacing &&
                subjectKind is not (OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern or OperationKind.CreateCircularPattern)) Invalid("Pattern relation subject has the wrong feature kind.");
            if (relation.Kind is RelationKind.CenteredAbout or RelationKind.SymmetricAboutAxis &&
                subjectKind is not (OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern)) Invalid("Pattern relation subject has the wrong feature kind.");
        }
        if (StateRelationData.Relations(state).Distinct().Count() != state.Relations.Count) Invalid("Duplicate design relation.");
        var edges = StateRelationData.Dependencies(state);
        foreach (var edge in edges)
            if (!Id(edge.Prerequisite) || !Id(edge.Dependent) || !Enum.IsDefined(edge.Kind) || edge.Prerequisite == edge.Dependent ||
                !features.ContainsKey(edge.Prerequisite) || !features.ContainsKey(edge.Dependent)) Invalid("Dependency endpoints must be distinct managed features.");
        if (edges.Distinct().Count() != edges.Count) Invalid("Duplicate dependency edge.");
        try { new DependencyGraph(edges).NativeOrder(features.Keys); }
        catch (StateException error) { throw new StateException("STATE_SCHEMA_INVALID", "Native dependency graph is cyclic.", error); }
    }

    public static byte[] DecodeReference(NativePersistentReference reference)
    {
        ValidateReference(reference);
        return Convert.FromBase64String(reference.Base64);
    }
    private static void ValidateReference(NativePersistentReference reference)
    {
        if (reference is null || reference.Base64 is null || reference.Base64.Length is < 4 or > 10924) Invalid("Persistent reference is missing or oversized.");
        try
        {
            var bytes = Convert.FromBase64String(reference!.Base64);
            if (bytes.Length is < 1 or > 8192 || Convert.ToBase64String(bytes) != reference.Base64) Invalid("Persistent reference is not canonical base64.");
        }
        catch (FormatException error) { throw new StateException("STATE_SCHEMA_INVALID", "Persistent reference is not base64.", error); }
    }
    private static bool Id(string? value) => value is not null && value.Length <= 128 &&
        Regex.IsMatch(value, @"^[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*)*$", RegexOptions.CultureInvariant);
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid(string message) => throw new StateException("STATE_SCHEMA_INVALID", message);
}
