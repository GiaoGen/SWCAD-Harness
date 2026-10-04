using System;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed record RestoredManagedFeature(string SemanticId, OperationKind Kind, ReferenceHealth ReferenceHealth);

public static class ExtrudeStateAdapter
{
    public static CadState Capture(SolidWorksExecutionContext context, OperationNode operation)
    {
        context.CheckThread();
        if (operation.Kind != OperationKind.CreateExtrude || context.CreatedFeature is null ||
            context.ConstructedFeatureIds.Count > 1 || !new CreateExtrudeHandler().Preflight(operation).IsValid)
            throw new StateException("OPERATION_UNSUPPORTED", "M3 captures only the single managed rectangle extrusion.");
        var identity = DocumentIdentityAdapter.Read(context);
        var feature = context.CreatedFeature;
        var reference = PersistentReferenceAdapter.Capture(context, feature);
        var id = operation.SemanticId!;
        var parameterId = id + ".extrusion_depth";
        var state = new CadState
        {
            SchemaVersion = "0.2", Document = identity, Revision = 0,
            Features = new[] { new FeatureNode(id, operation.Kind, reference, ReferenceHealth.Healthy) },
            Entities = new[] { new SemanticEntityNode(id, SemanticType.FeatureRef, id, reference, ReferenceHealth.Healthy) },
            Parameters = new[] { new ParameterNode(parameterId, ParameterKind.Length, ReadDepth(feature)) },
            Bindings = new[] { new ParameterBinding(parameterId, id, EditableParameter.ExtrusionDepth) },
            Relations = Array.Empty<JsonElement>(), Dependencies = Array.Empty<JsonElement>()
        };
        StateValidation.Validate(state);
        return state;
    }

    public static RestoredManagedFeature Restore(SolidWorksExecutionContext context, CadState state)
    {
        StateValidation.Validate(state);
        if (state.Features.Count != 1 || state.Features[0].Kind != OperationKind.CreateExtrude ||
            state.Entities.Any(entity => entity.Type != SemanticType.FeatureRef) ||
            state.Bindings.Any(binding => binding.Parameter != EditableParameter.ExtrusionDepth))
            throw new StateException("OPERATION_UNSUPPORTED", "M3 restores only one extrusion and its feature/depth bindings.");
        DocumentIdentityAdapter.Verify(context, state.Document);
        var feature = ResolveFeature(context, state.Features[0]);
        foreach (var entity in state.Entities)
        {
            var resolved = PersistentReferenceAdapter.Resolve<IFeature>(context, entity.NativeReference);
            if (entity.ReferenceHealth != ReferenceHealth.Healthy || resolved.Health != ReferenceHealth.Healthy ||
                entity.NativeReference != state.Features.Single(owner => owner.SemanticId == entity.OwnerFeatureSemanticId).NativeReference)
                throw new StateException("STALE_REFERENCE", "Semantic entity ownership or native reference is not healthy.");
        }
        context.CreatedFeature = feature;
        return new(state.Features[0].SemanticId, state.Features[0].Kind, ReferenceHealth.Healthy);
    }

    public static double ReadParameterMm(SolidWorksExecutionContext context, CadState state, string parameterSemanticId)
    {
        StateValidation.Validate(state);
        DocumentIdentityAdapter.Verify(context, state.Document);
        var binding = state.Bindings.SingleOrDefault(binding => binding.ParameterSemanticId == parameterSemanticId)
            ?? throw new StateException("OPERATION_PRECONDITION_FAILED", "No binding exists for this semantic parameter.");
        if (binding.Parameter != EditableParameter.ExtrusionDepth)
            throw new StateException("OPERATION_UNSUPPORTED", "M3 native parameter reading supports extrusion depth only.");
        var owner = state.Features.Single(feature => feature.SemanticId == binding.OwnerFeatureSemanticId);
        if (owner.Kind != OperationKind.CreateExtrude)
            throw new StateException("OPERATION_UNSUPPORTED", "Extrusion depth binding requires an extrusion owner.");
        return ReadDepth(ResolveFeature(context, owner));
    }

    private static IFeature ResolveFeature(SolidWorksExecutionContext context, FeatureNode feature)
    {
        if (feature.ReferenceHealth != ReferenceHealth.Healthy)
            throw new StateException("STALE_REFERENCE", "Stored feature reference health is not healthy.");
        var result = PersistentReferenceAdapter.Resolve<IFeature>(context, feature.NativeReference);
        if (result.Health != ReferenceHealth.Healthy)
            throw new StateException("STALE_REFERENCE", $"Managed extrusion reference health={result.Health}, native status={result.NativeStatus}.");
        // SOLIDWORKS can report ICE for Instant3D extrusions. The native feature
        // definition interface establishes legality without a display/type-name
        // assumption or a geometric re-resolution fallback.
        if (result.NativeObject!.GetDefinition() is not IExtrudeFeatureData2)
            throw new StateException("STALE_REFERENCE", "Resolved native feature does not have an extrusion definition: " + result.NativeObject.GetTypeName2());
        return result.NativeObject!;
    }
    private static double ReadDepth(IFeature feature)
    {
        if (feature.GetDefinition() is not IExtrudeFeatureData2 data) throw new StateException("STALE_REFERENCE", "Bound feature has no native extrusion definition.");
        var value = data.GetDepth(true) * 1000;
        if (!double.IsFinite(value) || value <= 0) throw new StateException("PARAMETER_NOT_APPLIED", "Native extrusion depth is invalid.");
        return value;
    }
}
