using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadHarness.Ir;

namespace CadHarness.State;

public enum DependencyKind { NativeInput, RelationConstraint }
public sealed record DependencyEdge(string Prerequisite, string Dependent, DependencyKind Kind);

public static class StateRelationData
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    public static IReadOnlyList<JsonElement> Encode<T>(IEnumerable<T> values) => values.Select(v => JsonSerializer.SerializeToElement(v, Options)).ToArray();
    public static IReadOnlyList<DesignRelation> Relations(CadState state) => Decode<DesignRelation>(state.Relations);
    public static IReadOnlyList<DependencyEdge> Dependencies(CadState state) => Decode<DependencyEdge>(state.Dependencies);
    private static IReadOnlyList<T> Decode<T>(IReadOnlyList<JsonElement> values)
    {
        try
        {
            var result = new List<T>();
            foreach (var value in values)
            {
                if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Expected a typed record.");
                var fields = typeof(T) == typeof(DesignRelation) ? new[] { "kind", "subject", "reference" } : new[] { "prerequisite", "dependent", "kind" };
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in value.EnumerateObject())
                    if (!seen.Add(field.Name) || !fields.Contains(field.Name, StringComparer.Ordinal)) throw new JsonException("Unknown or duplicate relation/dependency field.");
                if (seen.Count != fields.Length) throw new JsonException("All relation/dependency fields must be present.");
                result.Add(value.Deserialize<T>(Options) ?? throw new JsonException("Missing typed record."));
            }
            return result.AsReadOnly();
        }
        catch (JsonException error) { throw new StateException("STATE_SCHEMA_INVALID", "Invalid typed relation/dependency record.", error); }
    }
    private static JsonSerializerOptions CreateOptions()
    {
        var result = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        result.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false));
        return result;
    }
}
