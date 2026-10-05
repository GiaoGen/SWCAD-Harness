using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;

namespace CadHarness.CircularGeometry.Tests;

internal static class TestData
{
    internal static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static void Near(double a, double b) => Check(double.IsFinite(a) && Math.Abs(a - b) <= 1e-6, $"Actual {a} differs from expected {b}.");
    internal static CadProgram Fixture(string root)
    {
        var parsed = new CadProgramJson().Parse(File.ReadAllText(Path.Combine(root, "tests/CadHarness.CircularGeometry.Tests/Fixtures/g4.json")));
        Check(parsed.IsValid, parsed.Issues.FirstOrDefault()?.Message ?? "Invalid G4 fixture.");
        return new DesignRelationEngine().Solve(parsed.Program!).Program;
    }
    internal static CadProgram Change(CadProgram program, int index, string field, OperationParameter value) => program with
    { Operations = program.Operations.Select((o, i) => i != index ? o : o with { Parameters = new Dictionary<string, OperationParameter>(o.Parameters) { [field] = value } }).ToArray() };
    internal static CadProgram Input(CadProgram program, int index, string slot, SemanticReference reference) => program with
    { Operations = program.Operations.Select((o, i) => i != index ? o : o with { Inputs = o.Inputs.Select(input => input.Name != slot ? input : new(slot, new[] { reference })).ToArray() }).ToArray() };
    internal static OperationNode Edit(double diameter) => new("edit", OperationKind.EditParameter, null,
        new[] { new OperationInput("target", new[] { new SemanticReference("bolt_seed", SemanticType.FeatureRef) }) },
        new Dictionary<string, OperationParameter> { ["parameter"] = new ParameterNameParameter(EditableParameter.HoleDiameter), ["value"] = new EditValueParameter(new LengthParameter(diameter)) });
    internal static CadState Sample(CadProgram program)
    {
        var reference = new NativePersistentReference("AQID");
        var frame = new LocalFrameGeometry(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        return new()
        {
            SchemaVersion = "0.2", Revision = 0, Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""),
            Features = program.Operations.Select(o => new FeatureNode(o.SemanticId!, o.Kind, reference, ReferenceHealth.Healthy)).ToArray(),
            Entities = program.Operations.SelectMany(o => ProfileOutputs.For(o).Select(e => new SemanticEntityNode(o.SemanticId + e.Suffix, e.Type, o.SemanticId!, reference, ReferenceHealth.Healthy)
            { Geometry = e.Type == SemanticType.LocalFrame ? new(new(0, 0, 0), Frame: frame) : e.Type == SemanticType.ReferenceAxis ? new(new(0, 0, 0), new(1, 0, 0)) : null })).ToArray(),
            Parameters = new[] { new ParameterNode("bolt_seed.hole_diameter", ParameterKind.Length, 8) },
            Bindings = new[] { new ParameterBinding("bolt_seed.hole_diameter", "bolt_seed", EditableParameter.HoleDiameter) },
            Relations = StateRelationData.Encode(program.Relations), Dependencies = StateRelationData.Encode(new DesignRelationEngine().Solve(program).Dependencies.Edges)
        };
    }
}
