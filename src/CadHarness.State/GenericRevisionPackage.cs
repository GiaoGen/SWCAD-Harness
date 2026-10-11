using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;

namespace CadHarness.State;

public sealed record GenericDimensionImage(string EntityId, ConstraintKind Kind, NativePersistentReference CircleReference);
public sealed record GenericSketchImage(string SemanticId, IReadOnlyList<NativePersistentReference> Primitives,
    IReadOnlyList<GenericDimensionImage> Dimensions, IReadOnlyList<double> ModelToSketchTransform, long Revision);
public sealed record GenericFeatureImage(string SemanticId, string ProfileId, bool NativeReverse,
    NativePersistentReference? AxisLine, NativePersistentReference? AxisSketch, NativePersistentReference? AxisDatum);
public sealed record GenericProvenance(string Version, string Kind, string Payload);
public sealed record GenericRevisionPackage(string SchemaVersion, string PackageType, int FormatVersion,
    GenericConstructionState State, IReadOnlyList<GenericSketchImage> SketchBindings,
    IReadOnlyList<GenericFeatureImage> FeatureBindings, GenericProvenance? Provenance);

public static class GenericRevisionValidation
{
    public static void Validate(GenericRevisionPackage package)
    {
        ContractValidation.Require(package.SchemaVersion == "0.3" && package.PackageType == "generic_construction" && package.FormatVersion == 1,
            "Unsupported explicit Generic package type/version.");
        new GenericConstructionStatePolicy().Validate(package.State);
        var s = package.State;
        ContractValidation.Require(s.Program.Origin == ModelOrigin.Harness && s.Features.Count == s.Program.Operations.Count &&
            s.Program.Operations.All(o => s.Features.Count(f => f.SemanticId == o.SemanticId && f.Kind == o.Kind) == 1), "Program/feature association differs.");
        ContractValidation.Unique(package.SketchBindings.Select(b => b.SemanticId), "persisted sketch");
        ContractValidation.Unique(package.FeatureBindings.Select(b => b.SemanticId), "persisted feature");
        ContractValidation.Unique(s.Features.Select(f => f.Reference.Base64), "feature reference");
        var sketches = s.Program.Operations.Where(o => o.Kind == ConstructionKind.CreateSketch).ToArray();
        var features = s.Program.Operations.Where(o => o.Profile is not null).ToArray();
        ContractValidation.Require(s.Sketches.Count == sketches.Length && package.SketchBindings.Count == sketches.Length && package.FeatureBindings.Count == features.Length,
            "Persisted binding inventory differs from program.");
        foreach (var o in sketches)
        {
            var native = s.Sketches.Single(n => n.SemanticId == o.SemanticId);
            var binding = package.SketchBindings.Single(b => b.SemanticId == o.SemanticId);
            ContractValidation.Require(JsonSerializer.Serialize(o.Placement) == JsonSerializer.Serialize(native.Placement) &&
                JsonSerializer.Serialize(o.Sketch) == JsonSerializer.Serialize(native.Specification), "Persisted profile/placement differs from program.");
            var expected = PlanarSketchPreflight.Prepare(native.Specification);
            ContractValidation.Require(binding.Revision >= 0 && binding.Revision <= s.Revision && binding.ModelToSketchTransform.Count == 16 &&
                binding.ModelToSketchTransform.All(double.IsFinite) && binding.Primitives.SequenceEqual(native.Primitives) &&
                binding.Primitives.Count == expected.Primitives.Count, "Primitive identity or transform invalid.");
            ContractValidation.Unique(binding.Primitives.Select(r => r.Base64), "primitive reference");
            var drivers = native.Specification.Constraints.Where(c => c.Kind is ConstraintKind.Radius or ConstraintKind.Diameter).ToArray();
            ContractValidation.Require(binding.Dimensions.Count == drivers.Length, "Dimension inventory differs.");
            foreach (var d in drivers)
            {
                var primitive = expected.Primitives.Select((p, i) => (p, i)).Single(p => p.p.EntityId == d.References.Single());
                ContractValidation.Require(binding.Dimensions.Count(b => b.EntityId == d.References.Single() && b.Kind == d.Kind &&
                    b.CircleReference == binding.Primitives[primitive.i]) == 1, "Dimension identity/primitive owner differs.");
            }
        }
        foreach (var o in features)
        {
            var b = package.FeatureBindings.Single(f => f.SemanticId == o.SemanticId);
            ContractValidation.Require(o.Profile!.SemanticId == b.ProfileId + ".profile" && s.Sketches.Any(p => p.SemanticId == b.ProfileId), "Feature profile owner differs.");
            var axes = new[] { b.AxisLine, b.AxisSketch, b.AxisDatum }.OfType<NativePersistentReference>().ToArray();
            ContractValidation.Require(axes.Length == (o.Kind == ConstructionKind.CreateRevolvedBoss ? 3 : 0) &&
                axes.SequenceEqual(s.Features.Single(f => f.SemanticId == b.SemanticId).AuxiliaryReferences), "Feature axis identity differs.");
        }
        if (package.Provenance is { } provenance)
        {
            ContractValidation.Require(provenance.Version == "1" && ContractValidation.Id(provenance.Kind) && provenance.Payload.Length <= 65536,
                "Unsupported provenance version/size.");
            using var json = JsonDocument.Parse(provenance.Payload, new JsonDocumentOptions { MaxDepth = 32 });
            ContractValidation.Require(json.RootElement.ValueKind == JsonValueKind.Object, "Provenance must be structured data, not code.");
        }
    }
    // Identity/revision carrier for the shared journal protocol only. It is
    // never serialized as legacy CADState or paired with a fictitious CadProgram.
    public static CadState Envelope(GenericConstructionState s) => new()
    {
        SchemaVersion = "0.2", Document = s.Document, Revision = s.Revision,
        Features = Array.Empty<FeatureNode>(), Entities = Array.Empty<SemanticEntityNode>(), Parameters = Array.Empty<ParameterNode>(),
        Bindings = Array.Empty<ParameterBinding>(), Relations = Array.Empty<JsonElement>(), Dependencies = Array.Empty<JsonElement>()
    };
}
