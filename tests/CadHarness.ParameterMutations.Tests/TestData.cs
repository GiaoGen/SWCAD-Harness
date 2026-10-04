using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.ParameterMutations.Tests;

internal static class TestData
{
    internal static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static CadProgram Fixture(string root)
    {
        var result = new CadProgramJson().Parse(File.ReadAllText(Path.Combine(root, "tests", "CadHarness.ParameterMutations.Tests", "Fixtures", "g2.json")));
        Check(result.IsValid, "G2 fixture is invalid."); return new DesignRelationEngine().Solve(result.Program!).Program;
    }
    internal static OperationNode Edit(string target, EditableParameter parameter, double value) => new("edit", OperationKind.EditParameter, null,
        new[] { new OperationInput("target", new[] { new SemanticReference(target, SemanticType.FeatureRef) }) },
        new Dictionary<string, OperationParameter> { ["parameter"] = new ParameterNameParameter(parameter),
            ["value"] = new EditValueParameter(EditableParameters.Contract(parameter).Kind == ParameterKind.Count ? new CountParameter((int)value) : new LengthParameter(value)) });
    internal static CadProgram EditProgram(string target, EditableParameter parameter, double value) =>
        new("0.2", new[] { Edit(target, parameter, value) }, Array.Empty<DesignRelation>());
    internal static string Envelope(CadProgram program) => JsonSerializer.Serialize(new
        { outcome = "planned", program = JsonSerializer.Deserialize<JsonElement>(new CadProgramJson().Serialize(program)), reason = "" });
    internal static CadState Sample(CadProgram program)
    {
        var reference = new NativePersistentReference("AQID");
        var frame = new LocalFrameGeometry(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var registry = ParameterMutationRegistry.Default;
        var bindings = program.Operations.SelectMany(o => registry.Descriptors.Where(d => d.OwnerKind == o.Kind && registry.TryGet(o, d.Parameter, out _))
            .Select(d => new ParameterBinding(o.SemanticId + "." + WireNames.Of(d.Parameter), o.SemanticId!, d.Parameter))).ToArray();
        return new()
        {
            SchemaVersion = "0.2", Revision = 0, Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""),
            Features = program.Operations.Select(o => new FeatureNode(o.SemanticId!, o.Kind, reference, ReferenceHealth.Healthy)).ToArray(),
            Entities = program.Operations.SelectMany(o => OperationRegistry.Default.Get(o.Kind).Outputs.Select(e =>
                new SemanticEntityNode(o.SemanticId + e.Suffix, e.Type, o.SemanticId!, reference, ReferenceHealth.Healthy)
                { Geometry = e.Type == SemanticType.LocalFrame ? new(new(0, 0, 0), Frame: frame) :
                    e.Type == SemanticType.ReferenceAxis ? new(new(0, 0, 0), new(1, 0, 0)) : null })).ToArray(),
            Parameters = bindings.Select(b => new ParameterNode(b.ParameterSemanticId, EditableParameters.Contract(b.Parameter).Kind,
                registry.Expected(program.Operations.Single(o => o.SemanticId == b.OwnerFeatureSemanticId), b.Parameter))).ToArray(),
            Bindings = bindings, Relations = StateRelationData.Encode(program.Relations),
            Dependencies = StateRelationData.Encode(new DesignRelationEngine().Solve(program).Dependencies.Edges)
        };
    }
}
