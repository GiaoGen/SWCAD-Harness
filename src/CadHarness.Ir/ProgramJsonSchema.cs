using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CadHarness.Ir;

// Structural schema is generated from the same contracts as the strict parser.
// Cross-operation type/ownership checks are performed by ProgramValidator.
public static class ProgramJsonSchema
{
    public static string Create(OperationRegistry? registry = null)
    {
        registry ??= OperationRegistry.Default;
        var definitions = new Dictionary<string, object>
        {
            ["centered_rectangle_profile"] = Closed(new()
            {
                ["kind"] = Constant("centered_rectangle"), ["widthMm"] = Positive(), ["heightMm"] = Positive()
            }, "kind", "widthMm", "heightMm"),
            ["circle_profile"] = Closed(new()
            { ["kind"] = Constant("circle"), ["diameterMm"] = Positive() }, "kind", "diameterMm"),
            ["placement"] = Closed(new() { ["xMm"] = new { type = "number" }, ["yMm"] = new { type = "number" } }, "xMm", "yMm")
        };
        foreach (var contract in registry.Contracts)
        {
            var properties = new Dictionary<string, object>
            { ["id"] = Identifier(false), ["kind"] = Constant(contract.WireName) };
            var required = new List<string> { "id", "kind" };
            if (contract.CreatesSemanticEntity) { properties.Add("semanticId", Identifier(false)); required.Add("semanticId"); }
            foreach (var slot in contract.Inputs)
            {
                object reference = new { oneOf = new object[]
                {
                    Identifier(true),
                    Closed(new() { ["semanticId"] = Identifier(true),
                        ["type"] = new { type = "string", @enum = slot.AcceptedTypes.Select(x => WireNames.Of(x)).ToArray() } }, "semanticId", "type")
                } };
                properties.Add(slot.Name, slot.IsSet ? new { type = "array", minItems = 1,
                    maxItems = ProgramValidator.MaximumInputSetSize, uniqueItems = true, items = reference } : reference);
                if (slot.Required) required.Add(slot.Name);
            }
            foreach (var parameter in contract.Parameters)
            {
                properties.Add(parameter.Name, Parameter(parameter));
                if (parameter.Required) required.Add(parameter.Name);
            }
            var shape = Closed(properties, required.ToArray());
            if (contract.Kind == OperationKind.EditParameter)
                shape["allOf"] = Enum.GetValues<EditableParameter>().Select(parameter => new Dictionary<string, object>
                {
                    ["if"] = new { properties = new { parameter = Constant(WireNames.Of(parameter)) } },
                    ["then"] = new { properties = new { value = Parameter(EditableParameters.Contract(parameter)) } }
                }).ToArray();
            if (contract.Kind == OperationKind.CreateRectangularPattern)
                shape["not"] = new { properties = new { countX = new { @const = 1 }, countY = new { @const = 1 } } };
            definitions.Add(contract.WireName, shape);
        }
        definitions["relation"] = new { oneOf = Enum.GetValues<RelationKind>().Select(kind =>
        {
            var properties = new Dictionary<string, object> { ["kind"] = Constant(WireNames.Of(kind)), ["subject"] = Identifier(true) };
            if (kind != RelationKind.ThroughAll) properties.Add("reference", Identifier(true));
            return Closed(properties, properties.Keys.ToArray());
        }).ToArray() };
        var schema = Closed(new()
        {
            ["programVersion"] = Constant("0.2"),
            ["operations"] = new { type = "array", minItems = 1, maxItems = ProgramValidator.MaximumOperations,
                items = new { oneOf = registry.Contracts.Select(contract => Reference(contract.WireName)).ToArray() } },
            ["relations"] = new { type = "array", maxItems = ProgramValidator.MaximumRelations, items = Reference("relation") }
        }, "programVersion", "operations");
        schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
        schema["$id"] = "urn:cad-harness:cad-program:0.2";
        schema["title"] = "CAD Harness v0.2 typed operation program";
        schema["$defs"] = definitions;
        return JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });
    }

    private static object Parameter(ParameterContract parameter)
    {
        switch (parameter.Kind)
        {
            case ParameterKind.Profile:
                return new { oneOf = new[] { Reference("centered_rectangle_profile"), Reference("circle_profile") } };
            case ParameterKind.Point2D: return Reference("placement");
            case ParameterKind.ParameterName:
                return new { type = "string", @enum = Enum.GetValues<EditableParameter>().Select(x => WireNames.Of(x)).ToArray() };
            case ParameterKind.EditValue: return new { type = "number" };
            default:
                var number = new Dictionary<string, object> { ["type"] = parameter.Kind == ParameterKind.Count ? "integer" : "number" };
                if (parameter.ExclusiveMinimum.HasValue) number["exclusiveMinimum"] = parameter.ExclusiveMinimum.Value;
                if (parameter.Minimum.HasValue) number["minimum"] = parameter.Minimum.Value;
                if (parameter.Maximum.HasValue) number["maximum"] = parameter.Maximum.Value;
                return number;
        }
    }
    private static object Identifier(bool reference) => new { type = "string", maxLength = 128, pattern = Identifiers.SchemaPattern(reference) };
    private static object Constant(string value) => new { type = "string", @const = value };
    private static object Positive() => new { type = "number", exclusiveMinimum = 0 };
    private static object Reference(string name) => new Dictionary<string, object> { ["$ref"] = "#/$defs/" + name };
    private static Dictionary<string, object> Closed(Dictionary<string, object> properties, params string[] required) =>
        new() { ["type"] = "object", ["additionalProperties"] = false, ["required"] = required, ["properties"] = properties };
}
