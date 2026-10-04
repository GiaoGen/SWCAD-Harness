using System;
using System.Collections.Generic;
using System.Text.Json;
using CadHarness.Ir;

namespace CadHarness.State;

public enum ReferenceHealth { Healthy, Stale, Suppressed, Deleted }
public sealed record NativePersistentReference(string Base64);
public sealed record DocumentIdentity(Guid DocumentId, Guid ConfigurationId, string ConfigurationName, string SavedPath)
{
    public bool Matches(DocumentIdentity other) => DocumentId == other.DocumentId &&
        ConfigurationId == other.ConfigurationId && ConfigurationName == other.ConfigurationName &&
        string.Equals(SavedPath, other.SavedPath, StringComparison.OrdinalIgnoreCase);
}
public sealed record FeatureNode(string SemanticId, OperationKind Kind, NativePersistentReference NativeReference, ReferenceHealth ReferenceHealth);
public sealed record SemanticEntityNode(string SemanticId, SemanticType Type, string OwnerFeatureSemanticId,
    NativePersistentReference NativeReference, ReferenceHealth ReferenceHealth)
{
    public SemanticGeometry? Geometry { get; init; }
}
public sealed record ParameterNode(string SemanticId, ParameterKind Kind, double Value);
// The finite semantic parameter key is mapped to a native accessor by the
// backend. The owning feature's persistent reference binds its native source.
public sealed record ParameterBinding(string ParameterSemanticId, string OwnerFeatureSemanticId, EditableParameter Parameter);

public sealed record CadState
{
    public required string SchemaVersion { get; init; }
    public required DocumentIdentity Document { get; init; }
    public required IReadOnlyList<FeatureNode> Features { get; init; }
    public required IReadOnlyList<SemanticEntityNode> Entities { get; init; }
    public required IReadOnlyList<ParameterNode> Parameters { get; init; }
    public required IReadOnlyList<ParameterBinding> Bindings { get; init; }
    // Wire-compatible arrays, strictly decoded to DesignRelation/DependencyEdge
    // by StateRelationData; arbitrary JSON is never executed.
    public required IReadOnlyList<JsonElement> Relations { get; init; }
    public required IReadOnlyList<JsonElement> Dependencies { get; init; }
    public required long Revision { get; init; }
}

public interface ICadFailure { string Code { get; } }

public sealed class StateException : Exception, ICadFailure
{
    public string Code { get; }
    public StateException(string code, string message, Exception? inner = null) : base(message, inner) => Code = code;
}
