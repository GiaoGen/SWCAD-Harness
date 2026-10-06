using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CadHarness.Planning;

public sealed record ProviderSchemaIssue(string Path, string Message);

// Audits schema construction, not model responses or executable CAD programs.
// The strict envelope, IR parser, runtime catalog and preflight remain separate.
public static class ProviderSchemaCompatibility
{
    public static IReadOnlyList<ProviderSchemaIssue> Audit(string json)
    {
        var issues = new List<ProviderSchemaIssue>();
        void Invalid(string path, string message) => issues.Add(new(path, message));
        void Visit(JsonElement node, string path)
        {
            if (node.ValueKind != JsonValueKind.Object) { Invalid(path, "Schema must be an object."); return; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in node.EnumerateObject()) if (!names.Add(field.Name)) Invalid(path, "Duplicate schema keyword.");
            var alternatives = node.TryGetProperty("anyOf", out var anyOf);
            if (alternatives)
            {
                if (anyOf.ValueKind != JsonValueKind.Array || anyOf.GetArrayLength() == 0) Invalid(path + ".anyOf", "anyOf must contain at least one schema.");
                else { var i = 0; foreach (var item in anyOf.EnumerateArray()) Visit(item, path + $".anyOf[{i++}]"); }
            }
            if (!node.TryGetProperty("type", out var type))
            {
                if (!alternatives) Invalid(path, "Schema requires a supported type or nonempty anyOf.");
                return;
            }
            if (type.ValueKind != JsonValueKind.String) { Invalid(path + ".type", "Use a scalar type name or anyOf variants."); return; }
            switch (type.GetString())
            {
                case "object":
                    if (!node.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object || !properties.EnumerateObject().Any())
                    { Invalid(path + ".properties", "Object properties must be nonempty."); break; }
                    var propertyNames = properties.EnumerateObject().Select(p => p.Name).ToArray();
                    if (propertyNames.Distinct(StringComparer.Ordinal).Count() != propertyNames.Length) Invalid(path + ".properties", "Duplicate object property.");
                    if (!node.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False)
                        Invalid(path + ".additionalProperties", "Strict objects require additionalProperties=false.");
                    if (!node.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array ||
                        required.EnumerateArray().Any(p => p.ValueKind != JsonValueKind.String)) Invalid(path + ".required", "Strict objects require all property names.");
                    else
                    {
                        var requiredNames = required.EnumerateArray().Select(p => p.GetString()!).ToArray();
                        if (requiredNames.Distinct(StringComparer.Ordinal).Count() != requiredNames.Length || !requiredNames.ToHashSet(StringComparer.Ordinal).SetEquals(propertyNames))
                            Invalid(path + ".required", "required must name each property exactly once.");
                    }
                    foreach (var property in properties.EnumerateObject()) Visit(property.Value, path + ".properties." + property.Name);
                    break;
                case "array":
                    if (!node.TryGetProperty("items", out var items)) Invalid(path + ".items", "Arrays require a compilable items schema even when maxItems=0.");
                    else Visit(items, path + ".items");
                    int? Bound(string key)
                    {
                        if (!node.TryGetProperty(key, out var value)) return null;
                        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var bound) || bound < 0)
                        { Invalid(path + "." + key, "Array bounds must be nonnegative integers."); return null; }
                        return bound;
                    }
                    var minimum = Bound("minItems"); var maximum = Bound("maxItems");
                    if (minimum.HasValue && maximum.HasValue && minimum > maximum) Invalid(path, "Array bounds conflict.");
                    break;
                case "string": case "number": case "integer": case "boolean": case "null": break;
                default: Invalid(path + ".type", "Unsupported schema type."); break;
            }
            if (node.TryGetProperty("enum", out var enumeration) && (enumeration.ValueKind != JsonValueKind.Array || enumeration.GetArrayLength() == 0))
                Invalid(path + ".enum", "enum must contain at least one value.");
        }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            Visit(document.RootElement, "$");
        }
        catch (JsonException) { Invalid("$", "Schema JSON is malformed or exceeds depth limits."); }
        return issues.AsReadOnly();
    }
    public static void RequireCompatible(string json)
    {
        var issues = Audit(json);
        if (issues.Count != 0) throw new PlannerException("PLANNER_PROVIDER_SCHEMA_INVALID", issues[0].Path + ": " + issues[0].Message);
    }
}
