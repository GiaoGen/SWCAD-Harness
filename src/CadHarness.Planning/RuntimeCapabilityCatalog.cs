using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadHarness.Ir;

namespace CadHarness.Planning;

public enum PlanningMode { CreateModel, EditModel }
public sealed record ParameterEditCapability(string Target, OperationKind OwnerKind, EditableParameter Parameter,
    ParameterContract ValueContract);

// Supplied by a runtime projection, never inferred from the entire IR registry.
// The registry here is a private, finite executable subset for this request mode.
public sealed class RuntimeCapabilityCatalog
{
    public string RuntimeId { get; }
    public PlanningMode Mode { get; }
    public OperationRegistry Registry { get; }
    public IReadOnlyList<ProfileKind> Profiles { get; }
    public IReadOnlyList<RelationKind> Relations { get; }
    public IReadOnlyList<ParameterEditCapability> ParameterEdits { get; }
    public IReadOnlyList<string> Constraints { get; }

    public RuntimeCapabilityCatalog(string runtimeId, PlanningMode mode, IEnumerable<OperationContract> operations,
        IEnumerable<ProfileKind> profiles, IEnumerable<RelationKind> relations,
        IEnumerable<ParameterEditCapability> parameterEdits, IEnumerable<string> constraints)
    {
        RuntimeId = runtimeId; Mode = mode; Registry = new(operations);
        Profiles = Array.AsReadOnly(profiles.Distinct().ToArray());
        Relations = Array.AsReadOnly(relations.Distinct().ToArray());
        ParameterEdits = Array.AsReadOnly(parameterEdits.ToArray()); Constraints = Array.AsReadOnly(constraints.ToArray());
        if (string.IsNullOrWhiteSpace(runtimeId) || !Enum.IsDefined(mode) || Profiles.Any(p => !Enum.IsDefined(p)) ||
            Relations.Any(r => !Enum.IsDefined(r)) || (mode == PlanningMode.CreateModel && ParameterEdits.Count != 0) ||
            (mode == PlanningMode.EditModel && Registry.Contracts.Any(o => o.Kind != OperationKind.EditParameter)))
            throw new ArgumentException("Invalid runtime capability projection.");
    }

    public ProgramValidationResult Validate(CadProgram program)
    {
        var basic = new ProgramValidator(Registry).Validate(program);
        if (!basic.IsValid) return basic;
        var issues = new List<ValidationIssue>();
        void Unsupported(string path, string message) => issues.Add(new(FailureCodes.OperationUnsupported, path, message));
        for (var i = 0; i < program.Operations.Count; i++)
        {
            var operation = program.Operations[i];
            foreach (var profile in operation.Parameters.Values.OfType<ProfileParameter>())
                if (!Profiles.Contains(profile.Value.Kind)) Unsupported($"$.operations[{i}].profile", "Profile is not executable by this runtime.");
            if (operation.Kind == OperationKind.EditParameter)
            {
                var target = operation.Input("target")!.References[0].SemanticId;
                var parameter = operation.Parameter<ParameterNameParameter>("parameter").Value;
                var allowed = ParameterEdits.SingleOrDefault(p => p.Target == target && p.Parameter == parameter);
                if (allowed is null) { Unsupported($"$.operations[{i}]", "Target/parameter edit is not executable in this session."); continue; }
                var value = operation.Parameter<EditValueParameter>("value").Value;
                var scalar = value switch { LengthParameter length => length.Millimeters, CountParameter count => count.Value, _ => double.NaN };
                var c = allowed.ValueContract;
                if (!double.IsFinite(scalar) || (c.Minimum.HasValue && scalar < c.Minimum.Value) ||
                    (c.Maximum.HasValue && scalar > c.Maximum.Value) || (c.ExclusiveMinimum.HasValue && scalar <= c.ExclusiveMinimum.Value))
                    Unsupported($"$.operations[{i}].value", "Edit would leave the current runtime's active parameter range.");
            }
        }
        for (var i = 0; i < program.Relations.Count; i++)
            if (!Relations.Contains(program.Relations[i].Kind)) Unsupported($"$.relations[{i}].kind", "Relation has no executable handler in this mode.");
        if (Mode == PlanningMode.EditModel && (program.Operations.Count != 1 || program.Relations.Count != 0))
            Unsupported("$", "Current edit runtime executes one parameter transaction with existing relations per plan.");
        return new(issues.AsReadOnly());
    }

    public string ToPromptJson() => JsonSerializer.Serialize(new
    {
        runtime = RuntimeId, mode = Mode, maximumOperations = Mode == PlanningMode.EditModel ? 1 : ProgramValidator.MaximumOperations,
        operations = Registry.Contracts.Select(o => new
        { kind = o.WireName, inputs = o.Inputs, parameters = o.Parameters, outputs = o.Outputs, preconditions = o.Preconditions }),
        profiles = Profiles.Select(p => p == ProfileKind.CenteredRectangle ? "centered_rectangle" : "circle"),
        relations = Relations.Select(r => WireNames.Of(r)),
        parameterEdits = ParameterEdits.Select(e => new { target = e.Target, ownerKind = WireNames.Of(e.OwnerKind), parameter = WireNames.Of(e.Parameter), value = e.ValueContract }),
        constraints = Constraints
    }, JsonOptions);
    private static readonly JsonSerializerOptions JsonOptions = new()
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

// Implementations expose pure preflight only. The planner has no execute/tool
// callback and cannot perform a sequence of native observations/mutations.
public interface IPlanningRuntime
{
    RuntimeCapabilityCatalog Capabilities { get; }
    string ModelContextJson { get; }
    ProgramValidationResult Preflight(CadProgram program);
}
