using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.State;

namespace CadHarness.ConstructionTransactions.Tests;

internal static class TestData
{
    internal static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static CadProgram Base() => new("0.2", new[]
    {
        new OperationNode("base", OperationKind.CreateExtrude, "plate", Array.Empty<OperationInput>(), new Dictionary<string, OperationParameter>
            { ["profile"] = new ProfileParameter(new CenteredRectangleProfile(80, 50)), ["depthMm"] = new LengthParameter(8) }),
        Hole("original_hole", 6, new(0, 0))
    }, new[] { new DesignRelation(RelationKind.HostedOn, "original_hole", "plate.top_face") });
    internal static OperationNode Hole(string id, double diameter, Point2D placement) => new(id, OperationKind.CreateThroughHole, id,
        new[] { new OperationInput("host", new[] { new SemanticReference("plate.top_face", SemanticType.PlanarFace) }) },
        new Dictionary<string, OperationParameter> { ["diameterMm"] = new LengthParameter(diameter), ["placement"] = new PlacementParameter(placement) });
    internal static OperationNode Fillet() => new("fillet", OperationKind.ApplyFillet, "impossible_fillet",
        new[] { new OperationInput("edges", Enumerable.Range(1, 4).Select(i => new SemanticReference("plate.outer_edge_" + i, SemanticType.LinearEdge)).ToArray()) },
        new Dictionary<string, OperationParameter> { ["radiusMm"] = new LengthParameter(1000) });
    internal static CadProgram ImpossibleFresh() => Base() with { Operations = Base().Operations.Append(Fillet()).ToArray() };
    internal static CadProgram Extension(bool fail) => new("0.2", fail ? new[] { Hole("added_hole", 4, new(20, 10)), Fillet() } : new[] { Hole("added_hole", 4, new(20, 10)) },
        new[] { new DesignRelation(RelationKind.HostedOn, "added_hole", "plate.top_face") });
    internal static CadProgram InvalidPlacement() => Base() with { Operations = new[] { Base().Operations[0], Hole("original_hole", 6, new(40, 25)) } };
    internal static CadProgram InvalidPattern() => Base() with { Operations = Base().Operations.Append(new OperationNode("pattern", OperationKind.CreateLinearPattern, "invalid_pattern",
        new[] { new OperationInput("seed", new[] { new SemanticReference("original_hole", SemanticType.FeatureRef) }) },
        new Dictionary<string, OperationParameter> { ["count"] = new CountParameter(1025), ["spacingMm"] = new LengthParameter(10) })).ToArray() };
    internal static CadState Empty() => new()
    {
        SchemaVersion = "0.2", Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""), Revision = 0,
        Features = Array.Empty<FeatureNode>(), Entities = Array.Empty<SemanticEntityNode>(), Parameters = Array.Empty<ParameterNode>(), Bindings = Array.Empty<ParameterBinding>(),
        Relations = Array.Empty<JsonElement>(), Dependencies = Array.Empty<JsonElement>()
    };
}
