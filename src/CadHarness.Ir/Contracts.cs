using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CadHarness.Ir;

public sealed record InputContract(
    string Name, SemanticRole? Role, IReadOnlyList<SemanticType> AcceptedTypes,
    bool Required = true, bool IsSet = false)
{
    public bool Accepts(SemanticType type) => AcceptedTypes.Contains(type);
}

public sealed record ParameterContract(
    string Name, ParameterKind Kind, bool Required = true,
    double? ExclusiveMinimum = null, double? Minimum = null, double? Maximum = null);

public sealed record SemanticOutput(string Suffix, SemanticType Type);

public sealed record OperationContract(
    OperationKind Kind, string WireName, bool CreatesSemanticEntity,
    IReadOnlyList<InputContract> Inputs,
    IReadOnlyList<ParameterContract> Parameters,
    IReadOnlyList<string> Preconditions,
    IReadOnlyList<string> Effects,
    IReadOnlyList<string> Postconditions,
    IReadOnlyList<SemanticOutput> Outputs);

public sealed class OperationRegistry
{
    private readonly IReadOnlyDictionary<OperationKind, OperationContract> byKind;
    private readonly IReadOnlyDictionary<string, OperationContract> byName;
    public static OperationRegistry Default { get; } = new(CreateDefaultContracts());
    public IReadOnlyList<OperationContract> Contracts { get; }

    // A public registry and role/type contracts allow future finite operations
    // such as concentric relations without adding native API types to the IR.
    public OperationRegistry(IEnumerable<OperationContract> contracts)
    {
        var items = contracts.ToArray();
        if (items.Select(x => x.Kind).Distinct().Count() != items.Length ||
            items.Select(x => x.WireName).Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("Operation kinds and wire names must be unique.", nameof(contracts));
        foreach (var item in items)
        {
            if (item.Inputs.Select(x => x.Name).Concat(item.Parameters.Select(x => x.Name))
                    .Distinct(StringComparer.Ordinal).Count() != item.Inputs.Count + item.Parameters.Count)
                throw new ArgumentException("Operation field names must be unique.", nameof(contracts));
            if (item.Preconditions.Count == 0 || item.Effects.Count == 0 || item.Postconditions.Count == 0)
                throw new ArgumentException("Every operation requires a complete contract.", nameof(contracts));
        }
        Contracts = Array.AsReadOnly(items);
        byKind = new ReadOnlyDictionary<OperationKind, OperationContract>(items.ToDictionary(x => x.Kind));
        byName = new ReadOnlyDictionary<string, OperationContract>(items.ToDictionary(x => x.WireName, StringComparer.Ordinal));
    }

    public bool TryGet(OperationKind kind, out OperationContract contract) => byKind.TryGetValue(kind, out contract!);
    public bool TryGet(string wireName, out OperationContract contract) => byName.TryGetValue(wireName, out contract!);
    public OperationContract Get(OperationKind kind) => byKind.TryGetValue(kind, out var contract)
        ? contract : throw new KeyNotFoundException($"Unsupported operation: {kind}.");

    private static IEnumerable<OperationContract> CreateDefaultContracts()
    {
        static InputContract Input(string name, SemanticRole role, bool required = true, bool set = false) =>
            new(name, role, SemanticTypes.ForRole(role), required, set);
        static ParameterContract Length(string name, bool required = true) =>
            new(name, ParameterKind.Length, required, ExclusiveMinimum: 0);
        static ParameterContract Count(string name, int minimum) => new(name, ParameterKind.Count, Minimum: minimum);
        static IReadOnlyList<T> List<T>(params T[] items) => Array.AsReadOnly(items);
        static IReadOnlyList<SemanticOutput> FeatureOutputs(params SemanticOutput[] extra) =>
            Array.AsReadOnly(new[] { new SemanticOutput("", SemanticType.FeatureRef) }.Concat(extra).ToArray());

        yield return new(OperationKind.CreateExtrude, "create_extrude", true,
            List<InputContract>(), List(new ParameterContract("profile", ParameterKind.Profile), Length("depthMm")),
            List("profile is closed and dimensionally valid", "depth is positive"),
            List("creates extrude feature", "creates or modifies solid body", "creates host surface and local frame"),
            List("native feature exists", "rebuild succeeds", "profile dimensions and depth match"),
            FeatureOutputs(new(".body", SemanticType.BodyRef), new(".top_face", SemanticType.PlanarFace),
                new(".bottom_face", SemanticType.PlanarFace), new(".local_frame", SemanticType.LocalFrame),
                new(".axis_x", SemanticType.ReferenceAxis), new(".axis_y", SemanticType.ReferenceAxis),
                new(".direction_x", SemanticType.ReferenceAxis), new(".direction_y", SemanticType.ReferenceAxis),
                new(".outer_edge_1", SemanticType.LinearEdge), new(".outer_edge_2", SemanticType.LinearEdge),
                new(".outer_edge_3", SemanticType.LinearEdge), new(".outer_edge_4", SemanticType.LinearEdge),
                new(".rotational_reference", SemanticType.CylindricalFace)));
        yield return new(OperationKind.CreateThroughHole, "create_through_hole", true,
            List(Input("host", SemanticRole.HostSurface)),
            List(Length("diameterMm"), new ParameterContract("placement", ParameterKind.Point2D, false)),
            List("host is a planar host surface", "diameter is positive", "placement is supported"),
            List("creates through-hole feature", "modifies body", "creates cylindrical wall"),
            List("native feature exists", "rebuild succeeds", "measured diameter matches", "end condition is through all"),
            FeatureOutputs(new SemanticOutput(".wall_face", SemanticType.CylindricalFace), new(".profile", SemanticType.SketchProfile)));
        yield return new(OperationKind.CreateBlindHole, "create_blind_hole", true,
            List(Input("host", SemanticRole.HostSurface)),
            List(Length("diameterMm"), Length("depthMm"), new ParameterContract("placement", ParameterKind.Point2D, false)),
            List("host is a planar host surface", "diameter and depth are positive", "placement is supported"),
            List("creates blind-hole feature", "modifies body", "creates cylindrical wall"),
            List("native feature exists", "rebuild succeeds", "measured diameter and depth match", "end condition is blind"),
            FeatureOutputs(new SemanticOutput(".wall_face", SemanticType.CylindricalFace), new(".profile", SemanticType.SketchProfile)));
        yield return new(OperationKind.CreateLinearPattern, "create_linear_pattern", true,
            List(Input("seed", SemanticRole.PatternSeed), Input("direction", SemanticRole.PatternDirection, false)),
            List(Count("count", 2), Length("spacingMm")),
            List("seed is a feature", "direction is linear", "count and spacing are valid"),
            List("creates linear pattern", "modifies body"),
            List("native feature exists", "rebuild succeeds", "instance count and spacing match"), FeatureOutputs());
        yield return new(OperationKind.CreateRectangularPattern, "create_rectangular_pattern", true,
            List(Input("seed", SemanticRole.PatternSeed), Input("directionX", SemanticRole.PatternDirection, false),
                Input("directionY", SemanticRole.PatternDirection, false)),
            List(Count("countX", 1), Count("countY", 1), Length("spacingXMm", false), Length("spacingYMm", false)),
            List("seed is a feature", "directions are independent", "at least two total instances", "spacing is supplied or relation-resolved before execution"),
            List("creates rectangular pattern", "modifies body"),
            List("native feature exists", "rebuild succeeds", "instance counts and spacings match"), FeatureOutputs());
        yield return new(OperationKind.CreateCircularPattern, "create_circular_pattern", true,
            List(Input("seed", SemanticRole.PatternSeed), Input("axis", SemanticRole.RotationalReference)),
            List(Count("count", 2), new ParameterContract("angleDeg", ParameterKind.Angle, false, ExclusiveMinimum: 0, Maximum: 360)),
            List("seed is a feature", "axis is a rotational reference", "count and angular span are valid"),
            List("creates circular pattern", "modifies body"),
            List("native feature exists", "rebuild succeeds", "instance count and angular span match"), FeatureOutputs());
        yield return new(OperationKind.ApplyFillet, "apply_fillet", true,
            List(Input("edges", SemanticRole.FilletEdgeSet, set: true)), List(Length("radiusMm")),
            List("edge set is nonempty", "radius is positive", "native geometry permits fillet"),
            List("creates fillet feature", "modifies body and edge topology"),
            List("native feature exists", "rebuild succeeds", "radius matches"), FeatureOutputs());
        yield return new(OperationKind.ApplyChamfer, "apply_chamfer", true,
            List(Input("edges", SemanticRole.ChamferEdgeSet, set: true)), List(Length("distanceMm")),
            List("edge set is nonempty", "distance is positive", "native geometry permits chamfer"),
            List("creates chamfer feature", "modifies body and edge topology"),
            List("native feature exists", "rebuild succeeds", "distance matches"), FeatureOutputs());
        yield return new(OperationKind.EditParameter, "edit_parameter", false,
            List(new InputContract("target", null, List(SemanticType.FeatureRef))),
            List(new ParameterContract("parameter", ParameterKind.ParameterName), new ParameterContract("value", ParameterKind.EditValue)),
            List("target feature owns the named parameter", "parameter kind and value match its operation contract"),
            List("changes bound parameter", "marks dependent semantics as affected"),
            List("native parameter matches", "rebuild succeeds", "required design relations hold"), List<SemanticOutput>());
    }
}
