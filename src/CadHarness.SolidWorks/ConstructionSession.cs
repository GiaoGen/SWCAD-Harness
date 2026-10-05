using System;
using System.Collections.Generic;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed partial class SolidWorksExecutionContext
{
    internal Guid ConstructionDocumentId { get; } = Guid.NewGuid();
    internal Guid ConstructionConfigurationId { get; } = Guid.NewGuid();

    public CadState CaptureConstructionState()
    {
        CheckThread();
        if (!RelationContextUsable || MutationInProgress) throw new StateException("TRANSACTION_BUSY", "No stable usable construction session.");
        if (RelationProgram is not null) return CaptureBindingState();
        if (operations.Count != 0) throw new StateException(FailureCodes.OperationUnsupported, "Construction requires a pristine or managed relation session.");
        var state = new CadState
        {
            SchemaVersion = "0.2", Document = DocumentIdentityAdapter.ReadForConstruction(this), Revision = RelationRevision,
            Features = Array.Empty<FeatureNode>(), Entities = Array.Empty<SemanticEntityNode>(), Parameters = Array.Empty<ParameterNode>(),
            Bindings = Array.Empty<ParameterBinding>(), Relations = Array.Empty<JsonElement>(), Dependencies = Array.Empty<JsonElement>()
        };
        StateValidation.Validate(state); return state;
    }

    private sealed record ConstructionSessionSnapshot(Dictionary<string, NativeOutput> Outputs, Dictionary<string, OperationNode> Operations,
        Dictionary<string, NativePersistentReference> Profiles, IFeature? CreatedFeature, string? Root, CadProgram? Program,
        long Revision, bool Usable, bool Mutation, DependencyGraph? Dependencies);

    internal object SnapshotConstructionSession() => new ConstructionSessionSnapshot(new(outputs, StringComparer.Ordinal), new(operations, StringComparer.Ordinal),
        new(holeProfiles, StringComparer.Ordinal), CreatedFeature, ConstructionRoot, RelationProgram, RelationRevision, RelationContextUsable, MutationInProgress, RelationDependencies);
    internal void RestoreConstructionSession(object payload)
    {
        var saved = (ConstructionSessionSnapshot)payload;
        outputs.Clear(); foreach (var item in saved.Outputs) outputs.Add(item.Key, item.Value);
        operations.Clear(); foreach (var item in saved.Operations) operations.Add(item.Key, item.Value);
        holeProfiles.Clear(); foreach (var item in saved.Profiles) holeProfiles.Add(item.Key, item.Value);
        CreatedFeature = saved.CreatedFeature; ConstructionRoot = saved.Root; RelationProgram = saved.Program;
        RelationRevision = saved.Revision; RelationContextUsable = saved.Usable; MutationInProgress = saved.Mutation; RelationDependencies = saved.Dependencies;
    }
}
