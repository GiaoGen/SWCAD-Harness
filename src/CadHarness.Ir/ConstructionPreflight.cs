using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CadHarness.Ir.V03;

public static class ConstructionPreflight
{
    public static FeatureSolidExpectation Evaluate(ConstructionProgram program, IReadOnlyList<ConstructionOperation> prefix)
    {
        ContractValidation.Program(program);
        ContractValidation.Require(program.Operations.Count > prefix.Count &&
            program.Operations.Take(prefix.Count).Select(o => JsonSerializer.Serialize(o)).SequenceEqual(prefix.Select(o => JsonSerializer.Serialize(o))),
            "Continuation must preserve the complete committed prefix.");
        return EvaluateGeometry(program);
    }

    public static FeatureSolidExpectation EvaluateGeometry(ConstructionProgram program)
    {
        ContractValidation.Program(program);
        var sketches = program.Operations.Where(o => o.Kind == ConstructionKind.CreateSketch).ToDictionary(o => o.SemanticId);
        var datums = new Dictionary<string, SpatialPlacement>(StringComparer.Ordinal);
        foreach (var o in program.Operations)
        {
            if (o.Sketch is not null) _ = PlanarSketchPreflight.Prepare(o.Sketch);
            if (o.Placement is not { } p) continue;
            ContractValidation.Require(p.InPlaneDirection is null, "Generic planner uses explicit frames, not unqualified native direction accessors.", V03FailureCodes.CapabilityUnavailable);
            ContractValidation.Require(o.Kind != ConstructionKind.CreateSketch || Math.Abs(p.Offset.Millimeters) <= 1e-9,
                "Use an explicit tracked datum for an offset sketch; implicit offset-plane ownership is not generic-qualified.", V03FailureCodes.CapabilityUnavailable);
            Vector3 origin; Vector3 normal;
            if (datums.TryGetValue(p.Plane.SemanticId, out var datum))
            { origin = SketchFrames.World(datum, new(0, 0)); normal = datum.Frame.ZAxis; }
            else
            {
                origin = new(0, 0, 0);
                normal = p.Plane.SemanticId switch
                {
                    "world.xy" => new(0, 0, 1), "world.xz" => new(0, 1, 0), "world.yz" => new(1, 0, 0),
                    _ => throw new ContractException(V03FailureCodes.CapabilityUnavailable, "Unqualified spatial host in construction plan.")
                };
            }
            SketchFrames.VerifyPlane(p, origin, normal, null);
            if (o.Kind == ConstructionKind.CreateDatumPlane)
            {
                ContractValidation.Require(Math.Abs(p.Offset.Millimeters) > 1e-9, "Datum requires a nonzero signed offset.");
                datums.Add(o.SemanticId + ".plane", p);
            }
        }
        return FeatureConstructionPreflight.Evaluate(program.Operations.Where(o => o.Profile is not null).Select(o =>
        {
            var s = sketches[o.Profile!.SemanticId[..^8]];
            return new PreparedFeatureProfile(o, s.Placement!, PlanarSketchPreflight.Prepare(s.Sketch!));
        }).ToArray());
    }
}
