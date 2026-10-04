using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CadHarness.Ir;

public sealed class CadProgramJson
{
    public const int MaximumJsonBytes = 131072;
    private readonly OperationRegistry registry;
    private readonly ProgramValidator validator;
    public CadProgramJson(OperationRegistry? registry = null)
    {
        this.registry = registry ?? OperationRegistry.Default;
        validator = new(this.registry);
    }

    public ProgramParseResult Parse(string json)
    {
        if (json is null || Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes)
            return new(null, new[] { new ValidationIssue(FailureCodes.SchemaInvalid, "$", "Missing or oversized JSON program.") });
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32 });
            CheckDuplicates(document.RootElement, "$");
            var root = Object(document.RootElement, "$", "programVersion", "operations", "relations");
            var version = String(Required(root, "programVersion", "$"), "$.programVersion");
            var operationsJson = Required(root, "operations", "$");
            Require(operationsJson.ValueKind == JsonValueKind.Array, "$.operations", "Expected operation array.");
            Require(operationsJson.GetArrayLength() is >= 1 and <= ProgramValidator.MaximumOperations,
                "$.operations", "Operation count must be 1..12.");
            var operations = new List<OperationNode>();
            foreach (var element in operationsJson.EnumerateArray()) operations.Add(ReadOperation(element, $"$.operations[{operations.Count}]"));
            var relations = new List<DesignRelation>();
            if (root.TryGetProperty("relations", out var relationsJson))
            {
                Require(relationsJson.ValueKind == JsonValueKind.Array, "$.relations", "Expected relation array.");
                Require(relationsJson.GetArrayLength() <= ProgramValidator.MaximumRelations, "$.relations", "Too many relations.");
                foreach (var element in relationsJson.EnumerateArray()) relations.Add(ReadRelation(element, $"$.relations[{relations.Count}]"));
            }
            var program = new CadProgram(version, operations.AsReadOnly(), relations.AsReadOnly());
            var result = validator.Validate(program);
            return result.IsValid ? new(program, result.Issues) : new(null, result.Issues);
        }
        catch (ReadFailure failure) { return new(null, new[] { failure.Issue }); }
        catch (JsonException) { return new(null, new[] { new ValidationIssue(FailureCodes.SchemaInvalid, "$", "Invalid strict JSON.") }); }
    }

    public string Serialize(CadProgram program)
    {
        var result = validator.Validate(program);
        if (!result.IsValid) throw new ArgumentException("Cannot serialize an invalid typed program: " + result.Issues[0].Message, nameof(program));
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("programVersion", program.ProgramVersion);
            writer.WriteStartArray("operations");
            foreach (var node in program.Operations)
            {
                var contract = registry.Get(node.Kind);
                writer.WriteStartObject();
                writer.WriteString("id", node.Id);
                writer.WriteString("kind", contract.WireName);
                if (node.SemanticId is not null) writer.WriteString("semanticId", node.SemanticId);
                foreach (var slot in contract.Inputs)
                {
                    var input = node.Input(slot.Name);
                    if (input is null) continue;
                    writer.WritePropertyName(slot.Name);
                    if (slot.IsSet) writer.WriteStartArray();
                    foreach (var reference in input.References)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("semanticId", reference.SemanticId);
                        writer.WriteString("type", WireNames.Of(reference.Type));
                        writer.WriteEndObject();
                    }
                    if (slot.IsSet) writer.WriteEndArray();
                }
                foreach (var spec in contract.Parameters)
                    if (node.Parameters.TryGetValue(spec.Name, out var parameter))
                    { writer.WritePropertyName(spec.Name); WriteParameter(writer, parameter); }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("relations");
            foreach (var relation in program.Relations)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", WireNames.Of(relation.Kind));
                writer.WriteString("subject", relation.Subject);
                if (relation.Reference is not null) writer.WriteString("reference", relation.Reference);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(stream.ToArray());
        if (Encoding.UTF8.GetByteCount(json) > MaximumJsonBytes) throw new ArgumentException("Serialized program exceeds JSON byte limit.", nameof(program));
        return json;
    }

    private OperationNode ReadOperation(JsonElement element, string path)
    {
        Require(element.ValueKind == JsonValueKind.Object, path, "Expected operation object.");
        var kind = String(Required(element, "kind", path), path + ".kind");
        if (!registry.TryGet(kind, out var contract))
            throw new ReadFailure(new(FailureCodes.OperationUnsupported, path + ".kind", "Unknown operation kind."));
        var fields = new[] { "id", "kind" }.Concat(contract.CreatesSemanticEntity ? new[] { "semanticId" } : Array.Empty<string>())
            .Concat(contract.Inputs.Select(x => x.Name)).Concat(contract.Parameters.Select(x => x.Name)).ToArray();
        Object(element, path, fields);
        var id = String(Required(element, "id", path), path + ".id");
        var semanticId = contract.CreatesSemanticEntity ? String(Required(element, "semanticId", path), path + ".semanticId") : null;
        var inputs = new List<OperationInput>();
        foreach (var slot in contract.Inputs)
        {
            if (!element.TryGetProperty(slot.Name, out var value))
            { Require(!slot.Required, path + "." + slot.Name, "Missing required input."); continue; }
            var inputPath = path + "." + slot.Name;
            var references = new List<SemanticReference>();
            if (slot.IsSet)
            {
                Require(value.ValueKind == JsonValueKind.Array, inputPath, "Expected reference array.");
                Require(value.GetArrayLength() is >= 1 and <= ProgramValidator.MaximumInputSetSize, inputPath, "Invalid reference set size.");
                foreach (var item in value.EnumerateArray()) references.Add(ReadReference(item, slot, inputPath));
            }
            else references.Add(ReadReference(value, slot, inputPath));
            inputs.Add(new(slot.Name, references.AsReadOnly()));
        }
        var parameters = new Dictionary<string, OperationParameter>(StringComparer.Ordinal);
        foreach (var spec in contract.Parameters)
        {
            if (!element.TryGetProperty(spec.Name, out var value))
            { Require(!spec.Required, path + "." + spec.Name, "Missing required parameter."); continue; }
            if (spec.Kind == ParameterKind.EditValue) continue;
            parameters.Add(spec.Name, ReadParameter(value, spec.Kind, path + "." + spec.Name));
        }
        if (contract.Kind == OperationKind.EditParameter)
        {
            var selected = (ParameterNameParameter)parameters["parameter"];
            parameters.Add("value", new EditValueParameter(ReadParameter(Required(element, "value", path),
                EditableParameters.Contract(selected.Value).Kind, path + ".value")));
        }
        return new(id, contract.Kind, semanticId, inputs.AsReadOnly(),
            new ReadOnlyDictionary<string, OperationParameter>(parameters));
    }

    private static SemanticReference ReadReference(JsonElement value, InputContract slot, string path)
    {
        // Shorthand inherits the slot's first accepted semantic type. Explicit
        // references are required for alternatives (e.g. axis vs cylindrical face).
        if (value.ValueKind == JsonValueKind.String) return new(String(value, path), slot.AcceptedTypes[0]);
        var reference = Object(value, path, "semanticId", "type");
        var id = String(Required(reference, "semanticId", path), path + ".semanticId");
        var typeName = String(Required(reference, "type", path), path + ".type");
        Require(WireNames.TryParse<SemanticType>(typeName, out var type), path + ".type", "Unknown semantic type.");
        return new(id, type);
    }

    private static OperationParameter ReadParameter(JsonElement value, ParameterKind kind, string path)
    {
        switch (kind)
        {
            case ParameterKind.Length: return new LengthParameter(Number(value, path));
            case ParameterKind.Count:
                Require(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _), path, "Expected 32-bit integer count.");
                return new CountParameter(value.GetInt32());
            case ParameterKind.Angle: return new AngleParameter(Number(value, path));
            case ParameterKind.Point2D:
                Object(value, path, "xMm", "yMm");
                return new PlacementParameter(new(Number(Required(value, "xMm", path), path + ".xMm"), Number(Required(value, "yMm", path), path + ".yMm")));
            case ParameterKind.Profile:
                Require(value.ValueKind == JsonValueKind.Object, path, "Expected typed sketch profile.");
                var profileName = String(Required(value, "kind", path), path + ".kind");
                if (profileName == "centered_rectangle")
                {
                    Object(value, path, "kind", "widthMm", "heightMm");
                    return new ProfileParameter(new CenteredRectangleProfile(Number(Required(value, "widthMm", path), path + ".widthMm"),
                        Number(Required(value, "heightMm", path), path + ".heightMm")));
                }
                if (profileName == "circle")
                {
                    Object(value, path, "kind", "diameterMm");
                    return new ProfileParameter(new CircleProfile(Number(Required(value, "diameterMm", path), path + ".diameterMm")));
                }
                throw new ReadFailure(new(FailureCodes.SchemaInvalid, path + ".kind", "Unknown profile kind."));
            case ParameterKind.ParameterName:
                var parameterName = String(value, path);
                Require(WireNames.TryParse<EditableParameter>(parameterName, out var parameter), path, "Unknown editable parameter.");
                return new ParameterNameParameter(parameter);
            default: throw new ReadFailure(new(FailureCodes.SchemaInvalid, path, "Unsupported parameter kind."));
        }
    }

    private static DesignRelation ReadRelation(JsonElement element, string path)
    {
        Object(element, path, "kind", "subject", "reference");
        var name = String(Required(element, "kind", path), path + ".kind");
        Require(WireNames.TryParse<RelationKind>(name, out var kind), path + ".kind", "Unknown relation kind.");
        var subject = String(Required(element, "subject", path), path + ".subject");
        string? reference = element.TryGetProperty("reference", out var value) ? String(value, path + ".reference") : null;
        return new(kind, subject, reference);
    }

    private static void WriteParameter(Utf8JsonWriter writer, OperationParameter parameter)
    {
        switch (parameter)
        {
            case LengthParameter length: writer.WriteNumberValue(length.Millimeters); break;
            case CountParameter count: writer.WriteNumberValue(count.Value); break;
            case AngleParameter angle: writer.WriteNumberValue(angle.Degrees); break;
            case ParameterNameParameter name: writer.WriteStringValue(WireNames.Of(name.Value)); break;
            case EditValueParameter edit: WriteParameter(writer, edit.Value); break;
            case PlacementParameter placement:
                writer.WriteStartObject(); writer.WriteNumber("xMm", placement.Value.XMm); writer.WriteNumber("yMm", placement.Value.YMm); writer.WriteEndObject(); break;
            case ProfileParameter { Value: CenteredRectangleProfile rectangle }:
                writer.WriteStartObject(); writer.WriteString("kind", "centered_rectangle");
                writer.WriteNumber("widthMm", rectangle.WidthMm); writer.WriteNumber("heightMm", rectangle.HeightMm); writer.WriteEndObject(); break;
            case ProfileParameter { Value: CircleProfile circle }:
                writer.WriteStartObject(); writer.WriteString("kind", "circle"); writer.WriteNumber("diameterMm", circle.DiameterMm); writer.WriteEndObject(); break;
            default: throw new ArgumentException("Unsupported parameter implementation.", nameof(parameter));
        }
    }

    private static JsonElement Object(JsonElement value, string path, params string[] allowed)
    {
        Require(value.ValueKind == JsonValueKind.Object, path, "Expected object.");
        foreach (var property in value.EnumerateObject())
            Require(allowed.Contains(property.Name, StringComparer.Ordinal), path + "." + property.Name, "Unknown field.");
        return value;
    }
    private static JsonElement Required(JsonElement value, string name, string path)
    {
        Require(value.TryGetProperty(name, out var property), path + "." + name, "Missing required field.");
        return property;
    }
    private static string String(JsonElement value, string path)
    { Require(value.ValueKind == JsonValueKind.String, path, "Expected string."); return value.GetString()!; }
    private static double Number(JsonElement value, string path)
    {
        Require(value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _), path, "Expected numeric value.");
        var number = value.GetDouble();
        Require(double.IsFinite(number), path, "Number must be finite.");
        return number;
    }
    private static void CheckDuplicates(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), path + "." + property.Name, "Duplicate JSON field.");
                CheckDuplicates(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) CheckDuplicates(item, path + $"[{index++}]");
        }
    }
    private static void Require(bool condition, string path, string message)
    { if (!condition) throw new ReadFailure(new(FailureCodes.SchemaInvalid, path, message)); }
    private sealed class ReadFailure : Exception
    {
        internal ValidationIssue Issue { get; }
        internal ReadFailure(ValidationIssue issue) : base(issue.Message) => Issue = issue;
    }
}
