using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CadHarness.Ir;
using CadHarness.Ir.V03;

namespace CadHarness.State.V03;

public enum NativeSubtype { StraightBlindBossExtrude, SingleCircleThroughAllCut, SingleDirectionLinearPattern, TwoDirectionRectangularPattern, HoleWizard, Unrecognized }
public enum ObservationHealth { Healthy, Stale, Suppressed, Deleted, Unknown }
public enum EvidenceCompleteness { Known, Unknown }
public enum EditSupport { Editable, ReadOnly, Unsupported }
public enum EvidenceSource { NativeDefinition, PersistentReference, NativeTopology, NativeDependency, NativeSketch, FileHash }
public enum NativeAccessor { ExtrudeDepthDirection1, SingleCircleRadius, LinearPatternDirection1Count, LinearPatternDirection1Spacing }
public enum GeometryKind { Body, PlanarFace, CylindricalFace, Axis }
public enum NativeDependencyKind { ParentChild, SketchToFeature, Host, Axis, PatternSeed, ParameterOwner }
public enum ReopenStatus { Closed, Opening, Inspectable, Editable, Quarantined }
public enum PublishStage { WorkingCopy, Checkpointed, NativeSaved, StateFlushed, PackageVerified, PointerPublished, RecoveryRequired }
public sealed record ObservationEvidence(EvidenceSource Source, string Detail);
public sealed record ObservedParameter(string SemanticId, ParameterKey Key, ScalarUnit Unit, double Value,
    NativeAccessor Accessor, IReadOnlyList<ObservationEvidence> Evidence);
public sealed record GeometryObservation(string SemanticId, GeometryKind Kind, ObservationHealth ReferenceHealth,
    NativePersistentReference? NativeReference, LocalFrame? Frame, Vector3? MinimumMm, Vector3? MaximumMm,
    double? RadiusMm, double? VolumeMm3, IReadOnlyList<ObservationEvidence> Evidence);
public sealed record ObservedDependency(string Prerequisite, string Dependent, NativeDependencyKind Kind,
    IReadOnlyList<ObservationEvidence> Evidence);
public sealed record IdentityRemapping(string PreviousSemanticId, string CurrentSemanticId,
    string Reason, IReadOnlyList<ObservationEvidence> Evidence);
public sealed record ObservedFeature(string SemanticId, string DisplayName, int TreeOrdinal, ModelOrigin Origin,
    string NativeType, NativeSubtype Subtype, ObservationHealth Health, NativePersistentReference? NativeReference,
    EvidenceCompleteness DependencyCompleteness, EditSupport EditSupport, string SupportReason,
    IReadOnlyList<ObservationEvidence> Evidence, IReadOnlyList<ObservedParameter> Parameters,
    IReadOnlyList<GeometryObservation> Geometry);
public sealed record ObservedModel(string SchemaVersion, ModelOrigin Origin, PartSelection Selection,
    bool InventoryComplete, string? LimitOutcome, IReadOnlyList<ObservedFeature> Features,
    IReadOnlyList<ObservedDependency> Dependencies, IReadOnlyList<IdentityRemapping> Remappings);
public sealed record ArtifactIdentity(string Path, string Sha256, string SchemaVersion);
public sealed record RevisionManifest(string SchemaVersion, ModelOrigin Origin, Guid DocumentId, Guid ConfigurationId,
    string ConfigurationName, long Revision, FileFingerprint NativePart, ArtifactIdentity State,
    ArtifactIdentity? Program, string? PreviousManifestSha256, PublishStage Stage);
public sealed record MigrationAudit(string SchemaVersion, string OldStateVersion, string NewCompanionVersion,
    string OriginalStatePath, string OriginalStateSha256, string? OriginalProgramSha256,
    string RollbackPath, string Result, bool NativeReopenVerified, bool Editable);
public sealed record ManagedStateCompanion(string SchemaVersion, ModelOrigin Origin, ArtifactIdentity LegacyState,
    ArtifactIdentity LegacyProgram, MigrationAudit Migration, ReopenStatus Status);
public sealed record MutationReport(string SchemaVersion, PartSelection Selection, IReadOnlyList<string> Targets,
    long CurrentRevision, bool NativeMutationStarted, bool? RebuildSucceeded, bool RollbackAttempted,
    bool? RollbackSucceeded, bool StatePublished, bool NativePublished, bool SessionEditable,
    string? FailureCode);

public static class ObservedStateValidation
{
    private static void Evidence(IReadOnlyList<ObservationEvidence> evidence)
    {
        ContractValidation.Require(evidence.Count is >= 1 and <= 16 && evidence.All(e => Enum.IsDefined(e.Source) &&
            ContractValidation.Text(e.Detail)), "Native facts require bounded accessor/reference evidence.");
    }
    private static void Reference(NativePersistentReference? reference)
    {
        if (reference is null) return;
        try { StateValidation.DecodeReference(reference); }
        catch (StateException error) { throw new ContractException(V03FailureCodes.ContractInvalid, error.Message); }
    }
    public static bool Matches(NativeSubtype subtype, ParameterKey parameter, NativeAccessor accessor) => (subtype, parameter, accessor) switch
    {
        (NativeSubtype.StraightBlindBossExtrude, ParameterKey.ExtrusionDepth, NativeAccessor.ExtrudeDepthDirection1) => true,
        (NativeSubtype.SingleCircleThroughAllCut, ParameterKey.HoleDiameter, NativeAccessor.SingleCircleRadius) => true,
        (NativeSubtype.SingleDirectionLinearPattern, ParameterKey.PatternCount, NativeAccessor.LinearPatternDirection1Count) => true,
        (NativeSubtype.SingleDirectionLinearPattern, ParameterKey.PatternSpacing, NativeAccessor.LinearPatternDirection1Spacing) => true,
        _ => false
    };
    public static void Validate(ObservedModel model)
    {
        ContractValidation.Version(model.SchemaVersion); ContractValidation.Selection(model.Selection);
        ContractValidation.Require(model.Origin == ModelOrigin.External, "External observation cannot impersonate managed construction.", V03FailureCodes.ModeMismatch);
        ContractValidation.Require(model.Features.Count <= ContractLimits.Features && model.Dependencies.Count <= ContractLimits.Dependencies &&
            model.Features.Sum(f => f.Parameters.Count) <= ContractLimits.Parameters && model.Features.Sum(f => f.Geometry.Count) <= ContractLimits.Geometry,
            "Observation exceeds finite bounds.", V03FailureCodes.ObservationLimitExceeded);
        ContractValidation.Require(model.InventoryComplete ? model.LimitOutcome is null : model.LimitOutcome == V03FailureCodes.ObservationLimitExceeded,
            "Partial inventory must explicitly report the limit outcome.");
        ContractValidation.Unique(model.Features.Select(f => f.SemanticId), "observed feature");
        var features = model.Features.ToDictionary(f => f.SemanticId, StringComparer.Ordinal);
        var allIds = model.Features.Select(f => f.SemanticId).Concat(model.Features.SelectMany(f => f.Parameters.Select(p => p.SemanticId)))
            .Concat(model.Features.SelectMany(f => f.Geometry.Select(g => g.SemanticId))).ToArray();
        ContractValidation.Unique(allIds, "observed identity");
        foreach (var f in model.Features)
        {
            ContractValidation.Require(ContractValidation.Id(f.SemanticId) && ContractValidation.Text(f.DisplayName, 256) && f.TreeOrdinal >= 0 &&
                Enum.IsDefined(f.Origin) && ContractValidation.Text(f.NativeType, 256) && Enum.IsDefined(f.Subtype) && Enum.IsDefined(f.Health) &&
                Enum.IsDefined(f.DependencyCompleteness) && Enum.IsDefined(f.EditSupport) && ContractValidation.Text(f.SupportReason), "Invalid observed feature metadata.");
            Reference(f.NativeReference); Evidence(f.Evidence);
            if (f.EditSupport == EditSupport.Editable)
                ContractValidation.Require(model.InventoryComplete && f.Health == ObservationHealth.Healthy && f.NativeReference is not null &&
                    f.DependencyCompleteness == EvidenceCompleteness.Known && f.Parameters.Count > 0 &&
                    f.Parameters.All(p => Matches(f.Subtype, p.Key, p.Accessor)), "Editable descriptor requires complete evidence and a candidate subtype/accessor pair.");
            ContractValidation.Require(f.Subtype != NativeSubtype.Unrecognized || f.EditSupport != EditSupport.Editable, "Unknown features remain visible and noneditable.");
            ContractValidation.Unique(f.Parameters.Select(p => p.Key.ToString()), "observed parameter key");
            foreach (var p in f.Parameters)
            {
                ContractValidation.Require(ContractValidation.Id(p.SemanticId) && Enum.IsDefined(p.Accessor), "Invalid observed scalar identity/accessor.");
                ContractValidation.Scalar(p.Key, p.Unit, p.Value); Evidence(p.Evidence);
            }
            foreach (var g in f.Geometry)
            {
                ContractValidation.Require(ContractValidation.Id(g.SemanticId) && Enum.IsDefined(g.Kind) && Enum.IsDefined(g.ReferenceHealth), "Invalid geometry observation.");
                Reference(g.NativeReference); Evidence(g.Evidence);
                if (g.Frame is not null) ContractValidation.Frame(g.Frame);
                ContractValidation.Require((g.MinimumMm is null) == (g.MaximumMm is null), "Geometry bounds must be paired.");
                if (g.MinimumMm is { } min && g.MaximumMm is { } max)
                    ContractValidation.Require(new[] { min.X, min.Y, min.Z, max.X, max.Y, max.Z }.All(ContractValidation.Signed) &&
                        min.X <= max.X && min.Y <= max.Y && min.Z <= max.Z, "Invalid geometry bounds.");
                ContractValidation.Require(g.RadiusMm is null || ContractValidation.Length(g.RadiusMm.Value), "Radius must be positive.");
                ContractValidation.Require(g.VolumeMm3 is null || double.IsFinite(g.VolumeMm3.Value) && g.VolumeMm3.Value > 0, "Volume must be finite and positive.");
            }
        }
        ContractValidation.Unique(model.Dependencies.Select(e => e.Prerequisite + ":" + e.Dependent + ":" + e.Kind), "native dependency");
        foreach (var edge in model.Dependencies)
        {
            ContractValidation.Require(features.ContainsKey(edge.Prerequisite) && features.ContainsKey(edge.Dependent) &&
                edge.Prerequisite != edge.Dependent && Enum.IsDefined(edge.Kind), "Native dependency endpoints/kind are invalid."); Evidence(edge.Evidence);
        }
        try { new DependencyGraph(model.Dependencies.Select(e => new DependencyEdge(e.Prerequisite, e.Dependent, DependencyKind.NativeInput))).NativeOrder(features.Keys); }
        catch (StateException error) { throw new ContractException(V03FailureCodes.ContractInvalid, error.Message); }
        ContractValidation.Require(model.Remappings.Count <= ContractLimits.Features, "Identity remapping exceeds bound.");
        ContractValidation.Unique(model.Remappings.Select(r => r.PreviousSemanticId), "previous remapped ID");
        ContractValidation.Unique(model.Remappings.Select(r => r.CurrentSemanticId), "current remapped ID");
        foreach (var mapping in model.Remappings)
        {
            ContractValidation.Require(ContractValidation.Id(mapping.PreviousSemanticId) && features.ContainsKey(mapping.CurrentSemanticId) &&
                mapping.PreviousSemanticId != mapping.CurrentSemanticId && ContractValidation.Text(mapping.Reason), "Invalid explicit identity remapping."); Evidence(mapping.Evidence);
        }
        // Unknown edges are not synthesized as declared design relations or an empty proven dependency set.
    }
    public static void Manifest(RevisionManifest manifest)
    {
        ContractValidation.Version(manifest.SchemaVersion); ContractValidation.Fingerprint(manifest.NativePart);
        ContractValidation.Require(Enum.IsDefined(manifest.Origin) && manifest.DocumentId != Guid.Empty && manifest.ConfigurationId != Guid.Empty &&
            ContractValidation.Text(manifest.ConfigurationName, 256) && manifest.Revision >= 0 && Enum.IsDefined(manifest.Stage), "Invalid manifest identity/revision.");
        Artifact(manifest.State); if (manifest.Program is not null) Artifact(manifest.Program);
        ContractValidation.Require(manifest.Origin == ModelOrigin.Harness ? manifest.Program is not null : manifest.Program is null,
            "Managed packages require a program; external packages cannot fabricate one.");
        ContractValidation.Require(manifest.PreviousManifestSha256 is null || ContractValidation.Hash(manifest.PreviousManifestSha256), "Invalid previous revision hash.");
    }
    public static void Artifact(ArtifactIdentity artifact) => ContractValidation.Require(System.IO.Path.IsPathFullyQualified(artifact.Path) &&
        ContractValidation.Hash(artifact.Sha256) && artifact.SchemaVersion is "0.2" or "0.3", "Invalid versioned artifact identity.");
    public static void Companion(ManagedStateCompanion companion)
    {
        ContractValidation.Version(companion.SchemaVersion); Artifact(companion.LegacyState); Artifact(companion.LegacyProgram);
        var audit = companion.Migration;
        ContractValidation.Require(companion.Origin == ModelOrigin.Harness && companion.LegacyState.SchemaVersion == "0.2" && companion.LegacyProgram.SchemaVersion == "0.2" &&
            companion.Status == ReopenStatus.Inspectable && audit.SchemaVersion == "0.3" && audit.OldStateVersion == "0.2" && audit.NewCompanionVersion == "0.3" &&
            audit.OriginalStateSha256 == companion.LegacyState.Sha256 && audit.OriginalProgramSha256 == companion.LegacyProgram.Sha256 &&
            audit.OriginalStatePath == companion.LegacyState.Path && System.IO.Path.IsPathFullyQualified(audit.RollbackPath) &&
            audit.Result == V03FailureCodes.NativeValidationRequired && !audit.NativeReopenVerified && !audit.Editable,
            "M11 companion is an inspectable migration proposal, never a proven native migration.");
    }
    public static void Report(MutationReport report)
    {
        ContractValidation.Version(report.SchemaVersion); ContractValidation.Selection(report.Selection);
        ContractValidation.Require(report.CurrentRevision >= 0 && report.Targets.Count is >= 1 and <= 16 && report.Targets.All(ContractValidation.Id), "Invalid mutation report targets/revision.");
        ContractValidation.Unique(report.Targets, "report target");
        ContractValidation.Require(report.RollbackAttempted == (report.RollbackSucceeded is not null) &&
            (report.NativeMutationStarted || report.RebuildSucceeded is null && !report.RollbackAttempted && !report.NativePublished && !report.StatePublished), "Contradictory mutation/recovery facts.");
        ContractValidation.Require(report.FailureCode is not null || report.NativeMutationStarted && report.RebuildSucceeded == true && report.NativePublished && report.StatePublished &&
            report.SessionEditable && !report.RollbackAttempted && report.CurrentRevision == report.Selection.ExpectedRevision + 1, "Success requires native/state publication and one revision.");
        ContractValidation.Require(report.RollbackSucceeded != false || !report.SessionEditable, "Failed rollback must invalidate the session.");
        if (report.FailureCode is not null && report.NativeMutationStarted && report.SessionEditable)
            ContractValidation.Require(report.RollbackAttempted && report.RollbackSucceeded == true && !report.StatePublished && !report.NativePublished &&
                report.CurrentRevision == report.Selection.ExpectedRevision, "Editable failure requires proven restoration of the batch-start native/state revision.");
    }
}

public static class ObservedIdentity
{
    public static string FromReference(Guid documentId, Guid configurationId, NativePersistentReference reference)
    {
        ContractValidation.Require(documentId != Guid.Empty && configurationId != Guid.Empty, "Observed identity requires verified document/configuration lineage.");
        StateValidation.DecodeReference(reference);
        var input = documentId.ToString("D") + ":" + configurationId.ToString("D") + ":" + reference.Base64;
        return "observed_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
