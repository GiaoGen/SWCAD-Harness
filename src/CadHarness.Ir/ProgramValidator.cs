using System;
using System.Collections.Generic;
using System.Linq;

namespace CadHarness.Ir;

public sealed class ProgramValidator
{
    public const int MaximumOperations = 12;
    public const int MaximumRelations = 48;
    public const int MaximumInputSetSize = 64;
    private readonly OperationRegistry registry;
    public ProgramValidator(OperationRegistry? registry = null) => this.registry = registry ?? OperationRegistry.Default;

    public ProgramValidationResult Validate(CadProgram program)
    {
        var issues = new List<ValidationIssue>();
        void Invalid(string path, string message) => issues.Add(new(FailureCodes.SchemaInvalid, path, message));
        if (program is null) { Invalid("$", "Program is required."); return new(issues.AsReadOnly()); }
        if (program.ProgramVersion != "0.2") Invalid("$.programVersion", "Only program version 0.2 is supported.");
        if (program.Operations is null || program.Relations is null)
        { Invalid("$", "Operations and relations must be present."); return new(issues.AsReadOnly()); }
        if (program.Operations.Count is < 1 or > MaximumOperations)
            Invalid("$.operations", $"Expected 1..{MaximumOperations} operations.");
        if (program.Relations.Count > MaximumRelations) Invalid("$.relations", "Relation count exceeds the finite limit.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, OperationNode>(StringComparer.Ordinal);
        var symbols = new Dictionary<string, SemanticType>(StringComparer.Ordinal);
        var producedRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in program.Operations)
            if (node?.SemanticId is not null) producedRoots.Add(node.SemanticId);

        for (var index = 0; index < program.Operations.Count; index++)
        {
            var node = program.Operations[index];
            var path = $"$.operations[{index}]";
            if (node is null) { Invalid(path, "Operation is required."); continue; }
            if (!Identifiers.IsSafe(node.Id)) Invalid(path + ".id", "Expected a safe semantic identifier, without native API/COM names.");
            else if (!ids.Add(node.Id)) Invalid(path + ".id", "Duplicate operation ID.");
            if (!registry.TryGet(node.Kind, out var contract))
            { issues.Add(new(FailureCodes.OperationUnsupported, path + ".kind", "Unknown operation kind.")); continue; }
            if (contract.CreatesSemanticEntity && !Identifiers.IsSafe(node.SemanticId))
                Invalid(path + ".semanticId", "Creating operations require a safe semantic ID.");
            if (!contract.CreatesSemanticEntity && node.SemanticId is not null)
                Invalid(path + ".semanticId", "An edit does not create a semantic entity.");
            if (node.Inputs is null || node.Parameters is null) { Invalid(path, "Inputs and parameters are required."); continue; }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in node.Inputs)
            {
                if (input is null || !names.Add(input.Name)) { Invalid(path, "Null or duplicate input."); continue; }
                var slot = contract.Inputs.FirstOrDefault(x => x.Name == input.Name);
                var inputPath = path + "." + input.Name;
                if (slot is null) { Invalid(inputPath, "Unknown input."); continue; }
                if (input.References is null || input.References.Count < 1 ||
                    input.References.Count > (slot.IsSet ? MaximumInputSetSize : 1))
                { Invalid(inputPath, "Invalid input cardinality."); continue; }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var reference in input.References)
                {
                    if (reference is null || !Identifiers.IsSafe(reference.SemanticId, true))
                    { Invalid(inputPath, "Expected a safe semantic reference, without native API/COM names."); continue; }
                    if (!seen.Add(reference.SemanticId)) Invalid(inputPath, "Duplicate reference in input set.");
                    if (!slot.Accepts(reference.Type)) Invalid(inputPath, "Semantic type cannot satisfy this input slot.");
                    if (symbols.TryGetValue(reference.SemanticId, out var knownType))
                    {
                        if (knownType != reference.Type) Invalid(inputPath, "Declared semantic type differs from the program's output type.");
                    }
                    else if (producedRoots.Contains(reference.SemanticId.Split('.')[0]))
                        Invalid(inputPath, "Unknown output or forward reference to a program-produced entity.");
                }
            }
            foreach (var input in contract.Inputs)
                if (input.Required && !names.Contains(input.Name)) Invalid(path + "." + input.Name, "Required input is missing.");
            foreach (var parameter in node.Parameters)
            {
                var spec = contract.Parameters.FirstOrDefault(x => x.Name == parameter.Key);
                if (spec is null) Invalid(path + "." + parameter.Key, "Unknown parameter.");
                else ValidateParameter(parameter.Value, spec, path + "." + parameter.Key, Invalid);
            }
            foreach (var parameter in contract.Parameters)
                if (parameter.Required && !node.Parameters.ContainsKey(parameter.Name))
                    Invalid(path + "." + parameter.Name, "Required parameter is missing.");

            if (node.Kind == OperationKind.CreateRectangularPattern &&
                node.Parameters.TryGetValue("countX", out var x) && x is CountParameter cx &&
                node.Parameters.TryGetValue("countY", out var y) && y is CountParameter cy && (long)cx.Value * cy.Value < 2)
                Invalid(path, "A rectangular pattern requires at least two total instances.");
            if (node.Kind == OperationKind.EditParameter &&
                node.Parameters.TryGetValue("parameter", out var name) && name is ParameterNameParameter selected && Enum.IsDefined(selected.Value) &&
                node.Parameters.TryGetValue("value", out var edit) && edit is EditValueParameter value)
            {
                ValidateParameter(value.Value, EditableParameters.Contract(selected.Value), path + ".value", Invalid);
                var target = node.Input("target")?.References?.FirstOrDefault();
                if (target is not null && owners.TryGetValue(target.SemanticId, out var owner) && !EditableParameters.IsOwnedBy(selected.Value, owner))
                    Invalid(path + ".parameter", "The target operation does not own this editable parameter.");
            }
            if (contract.CreatesSemanticEntity && Identifiers.IsSafe(node.SemanticId))
            {
                if (!owners.TryAdd(node.SemanticId!, node)) Invalid(path + ".semanticId", "Duplicate semantic ID.");
                else
                    foreach (var output in contract.Outputs) symbols.Add(node.SemanticId! + output.Suffix, output.Type);
            }
        }
        for (var index = 0; index < program.Relations.Count; index++)
        {
            var relation = program.Relations[index];
            var path = $"$.relations[{index}]";
            if (relation is null) { Invalid(path, "Relation is required."); continue; }
            if (!Enum.IsDefined(relation.Kind)) Invalid(path + ".kind", "Unknown relation kind.");
            if (!Identifiers.IsSafe(relation.Subject, true)) Invalid(path + ".subject", "Expected safe semantic reference.");
            if (relation.Kind == RelationKind.ThroughAll)
            {
                if (relation.Reference is not null) Invalid(path + ".reference", "Through-all is unary.");
            }
            else if (!Identifiers.IsSafe(relation.Reference, true)) Invalid(path + ".reference", "Relation requires a safe reference.");
            foreach (var reference in new[] { relation.Subject, relation.Reference })
                if (reference is not null && producedRoots.Contains(reference.Split('.')[0]) && !symbols.ContainsKey(reference))
                    Invalid(path, "Relation references an unknown program output.");
        }
        return new(issues.AsReadOnly());
    }

    private static void ValidateParameter(OperationParameter? parameter, ParameterContract spec, string path, Action<string, string> invalid)
    {
        if (parameter is null || parameter.Kind != spec.Kind)
        { invalid(path, "Wrong parameter type."); return; }
        switch (parameter)
        {
            case LengthParameter length: Numeric(length.Millimeters); break;
            case CountParameter count: Numeric(count.Value); break;
            case AngleParameter angle: Numeric(angle.Degrees); break;
            case PlacementParameter placement:
                if (placement.Value is null || !double.IsFinite(placement.Value.XMm) || !double.IsFinite(placement.Value.YMm))
                    invalid(path, "Placement coordinates must be finite numbers in millimeters.");
                break;
            case ProfileParameter profile:
                switch (profile.Value)
                {
                    case CenteredRectangleProfile rectangle:
                        if (!Positive(rectangle.WidthMm) || !Positive(rectangle.HeightMm)) invalid(path, "Rectangle dimensions must be finite and positive.");
                        break;
                    case CircleProfile circle:
                        if (!Positive(circle.DiameterMm)) invalid(path, "Circle diameter must be finite and positive.");
                        break;
                    default: invalid(path, "Unsupported profile type."); break;
                }
                break;
            case ParameterNameParameter name:
                if (!Enum.IsDefined(name.Value)) invalid(path, "Unsupported editable parameter.");
                break;
            case EditValueParameter edit:
                if (edit.Value is not (LengthParameter or CountParameter or AngleParameter)) invalid(path, "Edits require a typed scalar value.");
                break;
            default: invalid(path, "Unsupported parameter implementation."); break;
        }
        void Numeric(double value)
        {
            if (!double.IsFinite(value) || (spec.ExclusiveMinimum.HasValue && value <= spec.ExclusiveMinimum.Value) ||
                (spec.Minimum.HasValue && value < spec.Minimum.Value) || (spec.Maximum.HasValue && value > spec.Maximum.Value))
                invalid(path, "Numeric value violates the parameter contract.");
        }
        static bool Positive(double value) => double.IsFinite(value) && value > 0;
    }
}
