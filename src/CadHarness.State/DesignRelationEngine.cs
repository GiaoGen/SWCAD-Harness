using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;

namespace CadHarness.State;

public sealed record PatternLayout(string Seed, string HostOwner, int Count1, int Count2,
    double Spacing1Mm, double Spacing2Mm, Vector3 Direction1, Vector3 Direction2)
{
    public Point2D SpanMm => new((Count1 - 1) * Spacing1Mm * Direction1.X + (Count2 - 1) * Spacing2Mm * Direction2.X,
        (Count1 - 1) * Spacing1Mm * Direction1.Y + (Count2 - 1) * Spacing2Mm * Direction2.Y);
}
public sealed record RelationPlan(CadProgram Program, DependencyGraph Dependencies);

public interface IDesignRelationHandler
{
    RelationKind Kind { get; }
    void Apply(RelationContext context, DesignRelation relation);
}

// Pure, finite constraint context. Coupled coordinates are assigned only by
// relation handlers; disagreements are detected instead of last-writer wins.
public sealed class RelationContext
{
    private readonly Dictionary<string, OperationNode> operations;
    private readonly Dictionary<string, (SemanticType Type, string Owner)> symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Seed, int Axis), double> constrained = new();
    private readonly List<DependencyEdge> dependencies = new();
    internal RelationContext(CadProgram program)
    {
        operations = program.Operations.Where(o => o.SemanticId is not null).ToDictionary(o => o.SemanticId!, StringComparer.Ordinal);
        foreach (var operation in operations.Values)
            foreach (var output in ProfileOutputs.For(operation))
                symbols.Add(operation.SemanticId! + output.Suffix, (output.Type, operation.SemanticId!));
        foreach (var operation in operations.Values)
            foreach (var input in operation.Inputs.SelectMany(i => i.References))
            {
                if (!symbols.TryGetValue(input.SemanticId, out var output) || output.Type != input.Type)
                    Fail("BINDING_UNRESOLVED", "Operation input is not a typed managed output: " + input.SemanticId);
                AddDependency(output.Owner, operation.SemanticId!, DependencyKind.NativeInput);
            }
    }
    public OperationNode Operation(string id) => operations.TryGetValue(id, out var operation) ? operation :
        throw new StateException("BINDING_UNRESOLVED", "Unknown relation feature: " + id);
    public string ReferenceOwner(string id, SemanticType type)
    {
        if (!symbols.TryGetValue(id, out var symbol) || symbol.Type != type)
            Fail("BINDING_UNRESOLVED", "Relation reference has no matching semantic type: " + id);
        return symbol.Owner;
    }
    public PatternLayout Layout(string subject)
    {
        var pattern = Operation(subject);
        if (pattern.Kind is not (OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern))
            Fail("RELATION_VIOLATED", "This relation requires a linear or rectangular pattern.");
        var seed = pattern.Input("seed")!.References[0].SemanticId;
        var hole = Operation(seed);
        if (hole.Kind is not (OperationKind.CreateThroughHole or OperationKind.CreateBlindHole))
            Fail("OPERATION_UNSUPPORTED", "M5 layout constraints support hole feature seeds.");
        var owner = ReferenceOwner(hole.Input("host")!.References[0].SemanticId, SemanticType.PlanarFace);
        Vector3 Direction(string slot, int axis)
        {
            var reference = pattern.Input(slot)?.References[0] ?? new(owner + (axis == 0 ? ".direction_x" : ".direction_y"), SemanticType.LinearEdge);
            if (ReferenceOwner(reference.SemanticId, SemanticType.LinearEdge) != owner)
                Fail("RELATION_VIOLATED", "Pattern direction must share the host local frame.");
            if (reference.SemanticId == owner + ".direction_x") return new(1, 0, 0);
            if (reference.SemanticId == owner + ".direction_y") return new(0, 1, 0);
            Fail("OPERATION_UNSUPPORTED", "M5 relation directions must be local X/Y direction outputs.");
            return null!;
        }
        var linear = pattern.Kind == OperationKind.CreateLinearPattern;
        var n1 = pattern.Parameter<CountParameter>(linear ? "count" : "countX").Value;
        var n2 = linear ? 1 : pattern.Parameter<CountParameter>("countY").Value;
        double Spacing(string name, int count)
        {
            if (pattern.Parameters.TryGetValue(name, out var value)) return ((LengthParameter)value).Millimeters;
            if (count > 1) Fail("RELATION_VIOLATED", "A repeated direction needs an explicit spacing; equal_spacing does not invent a length.");
            return 0;
        }
        if ((long)n1 * n2 > 1024) Fail(FailureCodes.PreconditionFailed, "Pattern exceeds 1024 instances.");
        var d1 = Direction(linear ? "direction" : "directionX", 0);
        var d2 = linear ? new Vector3(0, 1, 0) : Direction("directionY", 1);
        if (!linear && Math.Abs(GeometryMath.Dot(d1, d2)) > 1e-8) Fail("RELATION_VIOLATED", "Rectangular directions must be perpendicular.");
        return new(seed, owner, n1, n2, Spacing(linear ? "spacingMm" : "spacingXMm", n1), linear ? 0 : Spacing("spacingYMm", n2), d1, d2);
    }
    public Point2D Placement(string seed) => Operation(seed).Parameters.TryGetValue("placement", out var value) ? ((PlacementParameter)value).Value : new(0, 0);
    public void ConstrainCoordinate(string seed, int axis, double coordinate)
    {
        if (!double.IsFinite(coordinate)) Fail("RELATION_VIOLATED", "Relation solution is not finite.");
        var key = (seed, axis);
        if (constrained.TryGetValue(key, out var current) && Math.Abs(current - coordinate) > GeometryMath.ToleranceMm)
            Fail("RELATION_VIOLATED", "Conflicting relation assignments for " + seed + ".");
        constrained[key] = coordinate;
    }
    public void AddDependency(string prerequisite, string dependent, DependencyKind kind)
    { if (prerequisite != dependent) dependencies.Add(new(prerequisite, dependent, kind)); }
    public Vector3 SymmetryAxis(string reference, string hostOwner)
    {
        if (ReferenceOwner(reference, SemanticType.ReferenceAxis) != hostOwner) Fail("RELATION_VIOLATED", "Symmetry axis must share the hole host frame.");
        if (reference == hostOwner + ".axis_x") return new(1, 0, 0);
        if (reference == hostOwner + ".axis_y") return new(0, 1, 0);
        Fail("OPERATION_UNSUPPORTED", "Only local X/Y symmetry axes are supported in M5."); return null!;
    }
    internal RelationPlan Finish(CadProgram original)
    {
        foreach (var seed in constrained.Keys.Select(k => k.Seed).Distinct(StringComparer.Ordinal))
        {
            var operation = Operation(seed); var p = Placement(seed);
            var parameters = new Dictionary<string, OperationParameter>(operation.Parameters)
            {
                ["placement"] = new PlacementParameter(new(constrained.GetValueOrDefault((seed, 0), p.XMm), constrained.GetValueOrDefault((seed, 1), p.YMm)))
            };
            operations[seed] = operation with { Parameters = parameters };
        }
        foreach (var operation in operations.Values.Where(o => o.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole).ToArray())
            if (!operation.Parameters.ContainsKey("placement"))
            {
                var parameters = new Dictionary<string, OperationParameter>(operation.Parameters) { ["placement"] = new PlacementParameter(new(0, 0)) };
                operations[operation.SemanticId!] = operation with { Parameters = parameters };
            }
        var program = original with { Operations = original.Operations.Select(o => operations[o.SemanticId!]).ToArray() };
        var graph = new DependencyGraph(dependencies);
        graph.NativeOrder(operations.Keys);
        ValidateHostBounds(program);
        return new(program, graph);
    }
    private void ValidateHostBounds(CadProgram program)
    {
        var occupied = new Dictionary<string, List<(Point2D Position, double Radius)>>();
        foreach (var hole in program.Operations.Where(o => o.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole))
        {
            var owner = ReferenceOwner(hole.Input("host")!.References[0].SemanticId, SemanticType.PlanarFace);
            var root = Operation(owner);
            if (root.Kind != OperationKind.CreateExtrude ||
                hole.Input("host")!.References[0].SemanticId != owner + ".top_face")
                Fail("OPERATION_UNSUPPORTED", "Hole constraints require the initial extrusion top face.");
            var profile = root.Parameter<ProfileParameter>("profile").Value;
            var p = Placement(hole.SemanticId!); var radius = hole.Parameter<LengthParameter>("diameterMm").Millimeters / 2;
            if (!occupied.TryGetValue(owner, out var instances)) occupied[owner] = instances = new();
            void Inside(Point2D position)
            {
                if (!double.IsFinite(position.XMm) || !double.IsFinite(position.YMm) || !ProfileGeometry.ContainsHole(profile, position, radius))
                    Fail(FailureCodes.PreconditionFailed, "Relation-resolved hole layout exceeds the host profile.");
                if (instances.Count >= 4096) Fail(FailureCodes.PreconditionFailed, "Host exceeds 4096 hole instances.");
                foreach (var other in instances)
                    if (Math.Sqrt(Math.Pow(other.Position.XMm - position.XMm, 2) + Math.Pow(other.Position.YMm - position.YMm, 2)) <= radius + other.Radius)
                        Fail(FailureCodes.PreconditionFailed, "Managed hole instances must not intersect or touch.");
                instances.Add((position, radius));
            }
            Inside(p);
            foreach (var pattern in program.Operations.Where(o => o.Input("seed")?.References[0].SemanticId == hole.SemanticId))
            {
                foreach (var position in PatternGeometry.Positions(program, pattern).Skip(1)) Inside(position);
            }
        }
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal static void Fail(string code, string message) => throw new StateException(code, message);
}

public sealed class CenteredAboutHandler : IDesignRelationHandler
{
    public RelationKind Kind => RelationKind.CenteredAbout;
    public void Apply(RelationContext context, DesignRelation relation)
    {
        var layout = context.Layout(relation.Subject);
        var owner = context.ReferenceOwner(relation.Reference!, SemanticType.LocalFrame);
        if (owner != layout.HostOwner) RelationContext.Fail("RELATION_VIOLATED", "Center frame must be the host frame.");
        // Generic count/spacing span, applied in the relation handler only.
        context.ConstrainCoordinate(layout.Seed, 0, -layout.SpanMm.XMm / 2);
        context.ConstrainCoordinate(layout.Seed, 1, -layout.SpanMm.YMm / 2);
        context.AddDependency(owner, relation.Subject, DependencyKind.RelationConstraint);
        context.AddDependency(relation.Subject, layout.Seed, DependencyKind.RelationConstraint);
    }
}
public sealed class SymmetricAboutAxisHandler : IDesignRelationHandler
{
    public RelationKind Kind => RelationKind.SymmetricAboutAxis;
    public void Apply(RelationContext context, DesignRelation relation)
    {
        var layout = context.Layout(relation.Subject);
        var axis = context.SymmetryAxis(relation.Reference!, layout.HostOwner);
        var constrainedAxis = axis.X == 1 ? 1 : 0;
        context.ConstrainCoordinate(layout.Seed, constrainedAxis, -(constrainedAxis == 0 ? layout.SpanMm.XMm : layout.SpanMm.YMm) / 2);
        context.AddDependency(layout.HostOwner, relation.Subject, DependencyKind.RelationConstraint);
        context.AddDependency(relation.Subject, layout.Seed, DependencyKind.RelationConstraint);
    }
}
public sealed class HostedOnHandler : IDesignRelationHandler
{
    public RelationKind Kind => RelationKind.HostedOn;
    public void Apply(RelationContext context, DesignRelation relation)
    {
        var hole = context.Operation(relation.Subject);
        if (hole.Kind is not (OperationKind.CreateThroughHole or OperationKind.CreateBlindHole) || hole.Input("host")!.References[0].SemanticId != relation.Reference)
            RelationContext.Fail("RELATION_VIOLATED", "hosted_on must agree with the hole's typed host input.");
        context.AddDependency(context.ReferenceOwner(relation.Reference!, SemanticType.PlanarFace), relation.Subject, DependencyKind.RelationConstraint);
    }
}
public sealed class PatternSeedHandler : IDesignRelationHandler
{
    public RelationKind Kind => RelationKind.PatternSeed;
    public void Apply(RelationContext context, DesignRelation relation)
    {
        var pattern = context.Operation(relation.Subject);
        var seed = pattern.Kind == OperationKind.CreateCircularPattern ? PatternGeometry.Seed(pattern) : context.Layout(relation.Subject).Seed;
        if (seed != relation.Reference) RelationContext.Fail("RELATION_VIOLATED", "pattern_seed must agree with the pattern's typed seed input.");
        context.ReferenceOwner(relation.Reference!, SemanticType.FeatureRef);
        context.AddDependency(seed, relation.Subject, DependencyKind.RelationConstraint);
    }
}
public sealed class EqualSpacingHandler : IDesignRelationHandler
{
    public RelationKind Kind => RelationKind.EqualSpacing;
    public void Apply(RelationContext context, DesignRelation relation)
    {
        var pattern = context.Operation(relation.Subject);
        var seed = pattern.Kind == OperationKind.CreateCircularPattern ? PatternGeometry.Seed(pattern) : context.Layout(relation.Subject).Seed;
        if (relation.Reference != seed) RelationContext.Fail("RELATION_VIOLATED", "equal_spacing references the pattern's seed and asserts uniform native steps in each repeated direction.");
        context.ReferenceOwner(relation.Reference!, SemanticType.FeatureRef);
        context.AddDependency(seed, relation.Subject, DependencyKind.RelationConstraint);
    }
}

public sealed class DesignRelationEngine
{
    public IReadOnlyList<RelationKind> SupportedKinds => handlers.Keys.ToArray();
    public static PatternLayout ReadLayout(CadProgram program, string subject) => new RelationContext(program).Layout(subject);
    private readonly IReadOnlyDictionary<RelationKind, IDesignRelationHandler> handlers = new IDesignRelationHandler[]
    { new CenteredAboutHandler(), new SymmetricAboutAxisHandler(), new HostedOnHandler(), new PatternSeedHandler(), new EqualSpacingHandler() }.ToDictionary(h => h.Kind);
    public RelationPlan Solve(CadProgram program)
    {
        var check = new ProgramValidator().Validate(program);
        if (!check.IsValid) throw new StateException(check.Issues[0].Code, check.Issues[0].Message);
        if (program.Operations.Any(o => o.SemanticId is null)) throw new StateException(FailureCodes.OperationUnsupported, "Use the M5 edit entry point for parameter edits.");
        if (program.Relations.Distinct().Count() != program.Relations.Count) throw new StateException("RELATION_VIOLATED", "Duplicate design relation.");
        var context = new RelationContext(program);
        foreach (var relation in program.Relations)
        {
            if (!handlers.TryGetValue(relation.Kind, out var handler)) throw new StateException(FailureCodes.OperationUnsupported, "No M5 relation handler for " + relation.Kind);
            handler.Apply(context, relation);
        }
        return context.Finish(program);
    }
}
