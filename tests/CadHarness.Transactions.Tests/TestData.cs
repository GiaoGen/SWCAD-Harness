using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;

namespace CadHarness.Transactions.Tests;

internal static class TestData
{
    internal static CadProgram Fixture(string root)
    {
        var parsed = new CadProgramJson().Parse(File.ReadAllText(Path.Combine(root, "tests", "CadHarness.Transactions.Tests", "Fixtures", "transaction.json")));
        if (!parsed.IsValid) throw new Exception(parsed.Issues[0].Message);
        return new DesignRelationEngine().Solve(parsed.Program!).Program;
    }
    internal static OperationNode Edit(double spacing) => new("edit", OperationKind.EditParameter, null,
        new[] { new OperationInput("target", new[] { new SemanticReference("linear_holes", SemanticType.FeatureRef) }) },
        new Dictionary<string, OperationParameter>
        { ["parameter"] = new ParameterNameParameter(EditableParameter.PatternSpacing), ["value"] = new EditValueParameter(new LengthParameter(spacing)) });

    internal static CadState Sample(CadProgram program)
    {
        var reference = new NativePersistentReference("AQID");
        var frame = new LocalFrameGeometry(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var entities = program.Operations.SelectMany(o => OperationRegistry.Default.Get(o.Kind).Outputs.Select(output =>
            new SemanticEntityNode(o.SemanticId + output.Suffix, output.Type, o.SemanticId!, reference, ReferenceHealth.Healthy)
            { Geometry = output.Type == SemanticType.LocalFrame ? new(new(0, 0, 0), Frame: frame) :
                output.Type == SemanticType.ReferenceAxis ? new(new(0, 0, 0), new(1, 0, 0)) : null })).ToArray();
        return new()
        {
            SchemaVersion = "0.2", Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""), Revision = 0,
            Features = program.Operations.Select(o => new FeatureNode(o.SemanticId!, o.Kind, reference, ReferenceHealth.Healthy)).ToArray(),
            Entities = entities, Parameters = new[] { new ParameterNode("linear_holes.pattern_spacing", ParameterKind.Length, 40) },
            Bindings = new[] { new ParameterBinding("linear_holes.pattern_spacing", "linear_holes", EditableParameter.PatternSpacing) },
            Relations = StateRelationData.Encode(program.Relations), Dependencies = StateRelationData.Encode(new DesignRelationEngine().Solve(program).Dependencies.Edges)
        };
    }
    internal static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
}
