using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CadHarness.Ir.V03;

// One field definition drives the strict reader and published JSON Schema.
// All fields are required on the wire, including explicit nulls in tagged records.
public static class ContractJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 32,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false) }
    };
    public static T Read<T>(string json, Action<T> validate, int maximumBytes = ContractLimits.ProgramBytes)
    {
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > maximumBytes) Invalid("JSON byte limit exceeded.");
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            Check(document.RootElement, typeof(T), false, "$", 0);
            var value = JsonSerializer.Deserialize<T>(json, Options)!;
            validate(value);
            return value;
        }
        catch (JsonException error) { throw new ContractException(V03FailureCodes.ContractInvalid, error.Message); }
    }
    public static string Write<T>(T value, Action<T> validate)
    {
        validate(value);
        var json = JsonSerializer.Serialize(value, Options);
        Read(json, validate, typeof(T).Namespace == "CadHarness.State.V03" ? ContractLimits.StateBytes : ContractLimits.ProgramBytes);
        return json;
    }
    public static string Schema<T>()
    {
        var schema = Shape(typeof(T), false);
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        schema["$id"] = "urn:cad-harness:0.3:" + typeof(T).Name;
        schema["$comment"] = "Strict field schema; ContractValidation supplies tagged-union, identity, numeric, and safety invariants. Contract-only; not an executable provider schema.";
        return schema.ToJsonString(Options);
    }
    private static bool AllowsNull(PropertyInfo property) =>
        Nullable.GetUnderlyingType(property.PropertyType) is not null ||
        new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;
    private static string Name(PropertyInfo property) => JsonNamingPolicy.CamelCase.ConvertName(property.Name);
    private static string EnumName(object value) => JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString()!);
    private static Type? Item(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;
    private static JsonObject Shape(Type type, bool nullable)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) { type = underlying; nullable = true; }
        if (nullable) return new() { ["anyOf"] = new JsonArray(Shape(type, false), new JsonObject { ["type"] = "null" }) };
        if (type == typeof(string)) return new() { ["type"] = "string", ["maxLength"] = 10924 };
        if (type == typeof(Guid)) return new() { ["type"] = "string", ["format"] = "uuid" };
        if (type == typeof(bool)) return new() { ["type"] = "boolean" };
        if (type == typeof(double) || type == typeof(int) || type == typeof(long)) return new() { ["type"] = type == typeof(double) ? "number" : "integer" };
        if (type.IsEnum) return new() { ["type"] = "string", ["enum"] = new JsonArray(Enum.GetValues(type).Cast<object>().Select(v => (JsonNode?)JsonValue.Create(EnumName(v))).ToArray()) };
        if (Item(type) is { } item) return new() { ["type"] = "array", ["maxItems"] = ContractLimits.Parameters, ["items"] = Shape(item, false) };
        var properties = new JsonObject(); var required = new JsonArray();
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var property = Shape(p.PropertyType, AllowsNull(p));
            if (p.Name is "SchemaVersion" or "ProgramVersion")
            {
                if (type.Name == "ArtifactIdentity") property["enum"] = new JsonArray("0.2", "0.3");
                else property["const"] = "0.3";
            }
            if (Item(p.PropertyType) is not null)
            {
                var maximum = p.Name switch
                {
                    "Operations" => ContractLimits.Operations, "Requirements" => ContractLimits.Requirements,
                    "Questions" => ContractLimits.Questions, "Edits" => ContractLimits.BatchMaximum,
                    "Features" => ContractLimits.Features, "Geometry" => ContractLimits.Geometry,
                    "Remappings" => ContractLimits.Features,
                    "Entities" or "Constraints" => 64, "Loops" => 8, "Evidence" => 16,
                    "Points" => type == typeof(ClosedSketch) ? 128 : 64, "Targets" => 16, "References" => 4,
                    _ => ContractLimits.Parameters
                };
                property["maxItems"] = maximum;
                property["minItems"] = p.Name == "Edits" ? 2 : p.Name is "Operations" or "Requirements" or "Loops" or "Entities" or "Evidence" or "Targets" or "References" ? 1 : 0;
            }
            if (p.Name is "SemanticId" or "Id" or "RecordId" or "RequirementRecordId" or "SemanticKey" or "Target" or "ParameterSemanticId" or "PreviousSemanticId" or "CurrentSemanticId")
            { property["maxLength"] = 128; property["pattern"] = "^[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*)*$"; }
            if (p.Name == "Sha256") { property["minLength"] = 64; property["maxLength"] = 64; property["pattern"] = "^[a-f0-9]{64}$"; }
            if (p.Name is "Revision" or "ExpectedRevision" or "CurrentRevision" or "TreeOrdinal") property["minimum"] = 0;
            if (p.Name == "SizeBytes") property["minimum"] = 1;
            if (type == typeof(PositiveLength) && p.Name == "Millimeters") { property["exclusiveMinimum"] = 0; property["maximum"] = ContractLimits.LengthBoundMm; }
            if ((type == typeof(SignedCoordinate) || type == typeof(SignedOffset)) && p.Name == "Millimeters")
            { property["minimum"] = -ContractLimits.SpatialBoundMm; property["maximum"] = ContractLimits.SpatialBoundMm; }
            if (p.Name == "Confidence") { property["minimum"] = 0; property["maximum"] = 1; }
            properties[Name(p)] = property; required.Add(Name(p));
        }
        return new() { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = required };
    }
    private static void Check(JsonElement e, Type type, bool nullable, string path, int depth)
    {
        if (depth > 32) Invalid("JSON nesting exceeds limit.");
        if (Nullable.GetUnderlyingType(type) is { } underlying) { type = underlying; nullable = true; }
        if (e.ValueKind == JsonValueKind.Null) { if (!nullable) Invalid(path + " is required and nonnull."); return; }
        if (type == typeof(string)) { if (e.ValueKind != JsonValueKind.String || e.GetString()!.Length > 10924) Invalid(path + " must be a bounded string."); return; }
        if (type == typeof(Guid)) { if (e.ValueKind != JsonValueKind.String || !Guid.TryParse(e.GetString(), out _)) Invalid(path + " must be a UUID."); return; }
        if (type == typeof(bool)) { if (e.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Invalid(path + " must be boolean."); return; }
        if (type == typeof(double) || type == typeof(int) || type == typeof(long))
        { if (e.ValueKind != JsonValueKind.Number || !e.TryGetDouble(out var n) || !double.IsFinite(n) || (type != typeof(double) && (!e.TryGetInt64(out var integer) || (type == typeof(int) && (integer < int.MinValue || integer > int.MaxValue))))) Invalid(path + " must be a finite typed number."); return; }
        if (type.IsEnum)
        { if (e.ValueKind != JsonValueKind.String || !Enum.GetValues(type).Cast<object>().Any(v => EnumName(v) == e.GetString())) Invalid(path + " has an unknown enum value."); return; }
        if (Item(type) is { } item)
        { if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() > ContractLimits.Parameters) Invalid(path + " must be a bounded array."); foreach (var child in e.EnumerateArray()) Check(child, item, false, path + "[]", depth + 1); return; }
        if (e.ValueKind != JsonValueKind.Object) Invalid(path + " must be an object.");
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(Name, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in e.EnumerateObject())
        {
            if (!seen.Add(p.Name)) Invalid(path + " has duplicate field " + p.Name);
            if (!properties.TryGetValue(p.Name, out var definition)) Invalid(path + " has unknown field " + p.Name);
            Check(p.Value, definition!.PropertyType, AllowsNull(definition), path + "." + p.Name, depth + 1);
        }
        if (seen.Count != properties.Count) Invalid(path + " is missing required fields.");
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid(string message) => throw new ContractException(V03FailureCodes.ContractInvalid, message);
}
