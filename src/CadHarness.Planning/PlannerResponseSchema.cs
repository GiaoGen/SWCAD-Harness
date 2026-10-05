using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;

namespace CadHarness.Planning;

// Provider-compatible strict schema built ONLY from the runtime projection.
// Optional IR fields use closed variants: no null field is sent to the IR parser.
public static class PlannerResponseSchema
{
    public const string IdentifierInstructions = "Identifiers must be lowercase snake_case, start with a letter and match the schema's identifier patterns. " +
        "Do not use camelCase, native-looking numbered names such as extrude1, or native API identifiers. " +
        "Use semantic names for operations/features. Dotted references append an exact documented output suffix to an existing semantic ID. " +
        "Relation subjects and references must satisfy the projected relationContracts; a direction axis is not a local frame or pattern seed. ";
    public static string Create(RuntimeCapabilityCatalog catalog, bool stepwise = false)
    {
        var alternatives = new List<object>();
        foreach (var operation in catalog.Registry.Contracts)
        {
            if (operation.Kind == OperationKind.EditParameter)
            {
                foreach (var edit in catalog.ParameterEdits)
                    alternatives.Add(Closed(new()
                    {
                        ["id"] = Identifier(), ["kind"] = Constant(operation.WireName),
                        ["target"] = Closed(new() { ["semanticId"] = Constant(edit.Target), ["type"] = Constant("feature_ref") }),
                        ["parameter"] = Constant(WireNames.Of(edit.Parameter)), ["value"] = Scalar(edit.ValueContract)
                    }));
                continue;
            }
            var required = new Dictionary<string, object> { ["id"] = Identifier(), ["kind"] = Constant(operation.WireName), ["semanticId"] = Identifier() };
            var optional = new Dictionary<string, object>();
            foreach (var input in operation.Inputs)
            {
                object reference = Closed(new() { ["semanticId"] = Identifier(true), ["type"] = Strings(input.AcceptedTypes.Select(t => WireNames.Of(t))) });
                if (input.IsSet) reference = new { type = "array", minItems = 1, maxItems = ProgramValidator.MaximumInputSetSize, items = reference };
                (input.Required ? required : optional)[input.Name] = reference;
            }
            foreach (var parameter in operation.Parameters)
            {
                object parameterSchema = parameter.Kind switch
                {
                    ParameterKind.Profile => new { anyOf = catalog.Profiles.Select(p => p == ProfileKind.CenteredRectangle ?
                        Closed(new() { ["kind"] = Constant("centered_rectangle"), ["widthMm"] = Positive(), ["heightMm"] = Positive() }) :
                        Closed(new() { ["kind"] = Constant("circle"), ["diameterMm"] = Positive() })).ToArray() },
                    ParameterKind.Point2D => Closed(new() { ["xMm"] = new { type = "number" }, ["yMm"] = new { type = "number" } }),
                    _ => Scalar(parameter)
                };
                (parameter.Required ? required : optional)[parameter.Name] = parameterSchema;
            }
            var fields = optional.ToArray();
            for (var mask = 0; mask < (1 << fields.Length); mask++)
            {
                var shape = new Dictionary<string, object>(required);
                for (var i = 0; i < fields.Length; i++) if ((mask & (1 << i)) != 0) shape[fields[i].Key] = fields[i].Value;
                alternatives.Add(Closed(shape));
            }
        }
        object relationItems = catalog.Relations.Count == 0 ? new { type = "object", properties = new { }, additionalProperties = false, required = Array.Empty<string>() } :
            new { anyOf = catalog.Relations.Select(r => Closed(new()
            { ["kind"] = Constant(WireNames.Of(r)), ["subject"] = Identifier(), ["reference"] = Identifier(true) })).ToArray() };
        object program = alternatives.Count == 0 ? new { type = "null" } : Closed(new()
        {
            ["programVersion"] = Constant("0.2"),
            ["operations"] = new { type = "array", minItems = 1, maxItems = stepwise || catalog.Mode == PlanningMode.EditModel ? 1 : ProgramValidator.MaximumOperations, items = new { anyOf = alternatives } },
            ["relations"] = new { type = "array", maxItems = catalog.Relations.Count == 0 ? 0 : ProgramValidator.MaximumRelations, items = relationItems }
        });
        var schema = Closed(new()
        {
            ["outcome"] = Strings(stepwise ? new[] { "planned", "unsupported", "complete" } : new[] { "planned", "unsupported" }),
            ["program"] = alternatives.Count == 0 ? program : new { anyOf = new[] { program, new { type = "null" } } },
            ["reason"] = new { type = "string" }
        });
        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
    }
    private static object Identifier(bool reference = false) => new { type = "string", minLength = 1, maxLength = 128, pattern = Identifiers.SchemaPattern(reference) };
    private static object Constant(string value) => new { type = "string", @enum = new[] { value } };
    private static object Strings(IEnumerable<string> values) => new { type = "string", @enum = values.ToArray() };
    private static object Positive() => new { type = "number", exclusiveMinimum = 0 };
    private static object Scalar(ParameterContract contract)
    {
        var shape = new Dictionary<string, object> { ["type"] = contract.Kind == ParameterKind.Count ? "integer" : "number" };
        if (contract.ExclusiveMinimum.HasValue) shape["exclusiveMinimum"] = contract.ExclusiveMinimum.Value;
        if (contract.Minimum.HasValue) shape["minimum"] = contract.Minimum.Value;
        if (contract.Maximum.HasValue) shape["maximum"] = contract.Maximum.Value;
        return shape;
    }
    private static Dictionary<string, object> Closed(Dictionary<string, object> fields) => new()
    { ["type"] = "object", ["properties"] = fields, ["required"] = fields.Keys.ToArray(), ["additionalProperties"] = false };
}
