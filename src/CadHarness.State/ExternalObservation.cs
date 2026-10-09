using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State.V03;

namespace CadHarness.State;

public sealed record ExternalObservationLimits(int Features = ContractLimits.Features,
    int Parameters = ContractLimits.Parameters, int Geometry = ContractLimits.Geometry,
    int Dependencies = ContractLimits.Dependencies)
{
    public void Validate()
    {
        if (Features is < 1 or > ContractLimits.Features || Parameters is < 1 or > ContractLimits.Parameters ||
            Geometry is < 1 or > ContractLimits.Geometry || Dependencies is < 1 or > ContractLimits.Dependencies)
            throw new ContractException(V03FailureCodes.ContractInvalid, "Observation limits must stay within the frozen contract.");
    }
}
public sealed record ExternalInventoryNode(string CaptureKey, string DisplayName, string NativeType,
    ObservationHealth Health, NativePersistentReference? Reference, IReadOnlyList<ObservationEvidence> Evidence);
public sealed record ExternalFeatureFacts(NativeSubtype Subtype, IReadOnlyList<ObservedParameter> Parameters,
    IReadOnlyList<GeometryObservation> Geometry, IReadOnlyList<string>? Parents, IReadOnlyList<string>? Children,
    string Reason, bool LimitExceeded = false);
public interface IExternalObservationSource
{
    // Enumeration is lazy: inventory overflow must not trigger parameter readers.
    IEnumerable<ExternalInventoryNode> Inventory();
    ExternalFeatureFacts Read(ExternalInventoryNode node, int geometryAllowance);
}
public sealed record ExternalInventorySummary(int InventoryCount, int NativeInventoryCountLowerBound,
    bool NativeInventoryCountExact, int? NativeApiFeatureCount, string CountMethod);
public sealed record ExternalObservationResult(ObservedModel Model, ExternalInventorySummary Inventory);

public static class ExternalObservation
{
    public static PartSelection Selection(FileFingerprint file, FileFingerprint inspectionCopy, string configuration)
    {
        ContractValidation.Fingerprint(file);
        if (!ContractValidation.Text(configuration, 256)) throw new ContractException(V03FailureCodes.ContractInvalid, "Configuration must be explicit after native open.");
        // Path lineage is local to the explicitly selected original, not a claim
        // of a GUID stored in the engineer's file. Save As is a different lineage.
        var doc = StableGuid("external-path:" + System.IO.Path.GetFullPath(file.Path).ToUpperInvariant());
        var selection = new PartSelection(doc, StableGuid(doc.ToString("D") + ":configuration:" + configuration), configuration, 0, file, inspectionCopy);
        ContractValidation.Selection(selection);
        if (file.Sha256 != inspectionCopy.Sha256 || file.SizeBytes != inspectionCopy.SizeBytes)
            throw new StateException(V03FailureCodes.SourceFileDrift, "Read-only inspection copy differs from the selected source.");
        return selection;
    }
    private static Guid StableGuid(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    public static ExternalObservationResult Capture(PartSelection selection, IExternalObservationSource source,
        ExternalObservationLimits? limits = null, int? nativeApiFeatureCount = null)
    {
        ContractValidation.Selection(selection); limits ??= new(); limits.Validate();
        var nodes = new List<ExternalInventoryNode>(); var keys = new HashSet<string>(StringComparer.Ordinal);
        var limited = false; var lowerBound = 0;
        using (var iterator = source.Inventory().GetEnumerator())
        {
            while (iterator.MoveNext())
            {
                var node = iterator.Current; lowerBound++;
                if (nodes.Count == limits.Features) { limited = true; break; }
                if (string.IsNullOrWhiteSpace(node.CaptureKey) || !keys.Add(node.CaptureKey))
                    throw new StateException("AMBIGUOUS_NATIVE_INVENTORY", "Inventory contains duplicate or missing native capture identity.");
                nodes.Add(node);
            }
        }
        var inventoryLimited = limited;
        var identities = nodes.Select((node, index) => node.Reference is { } reference
            ? ObservedIdentity.FromReference(selection.DocumentId, selection.ConfigurationId, reference)
            : "unbound_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selection.Source.Sha256 + ":" + index + ":" + node.NativeType))).ToLowerInvariant()).ToArray();
        if (identities.Distinct(StringComparer.Ordinal).Count() != identities.Length)
            throw new StateException("AMBIGUOUS_NATIVE_INVENTORY", "Two inventory nodes share the same persistent identity.");
        var byKey = nodes.Select((node, index) => (node.CaptureKey, Id: identities[index])).ToDictionary(x => x.CaptureKey, x => x.Id, StringComparer.Ordinal);
        var features = new List<ObservedFeature>(); var facts = new List<ExternalFeatureFacts>();
        var parameters = 0; var geometry = 0;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index]; var id = identities[index];
            var detail = limited ? new ExternalFeatureFacts(NativeSubtype.Unrecognized, Array.Empty<ObservedParameter>(),
                Array.Empty<GeometryObservation>(), null, null, "Inventory overflow; qualification not attempted.") : source.Read(node, limits.Geometry - geometry);
            if (detail.LimitExceeded || parameters + detail.Parameters.Count > limits.Parameters || geometry + detail.Geometry.Count > limits.Geometry)
            { limited = true; detail = detail with { Parameters = Array.Empty<ObservedParameter>(), Geometry = Array.Empty<GeometryObservation>() }; }
            parameters += detail.Parameters.Count; geometry += detail.Geometry.Count; facts.Add(detail);
            var reason = node.Reference is null ? "No verified reference; unbound diagnostic identity is not a binding target. " + detail.Reason : detail.Reason;
            features.Add(new(id, node.DisplayName, index, ModelOrigin.External, node.NativeType, detail.Subtype,
                node.Health, node.Reference, EvidenceCompleteness.Unknown, EditSupport.ReadOnly,
                "M13 read-only inspection; mutation unqualified. " + reason, node.Evidence,
                detail.Parameters.Select(p => p with { SemanticId = id + "." + p.Key.ToString().ToLowerInvariant() }).ToArray(),
                detail.Geometry.Select((g, i) => g with { SemanticId = id + ".geometry_" + i }).ToArray()));
        }
        var edges = new Dictionary<string, ObservedDependency>(StringComparer.Ordinal);
        for (var index = 0; index < nodes.Count; index++)
        {
            var detail = facts[index]; var complete = !limited && detail.Parents is not null && detail.Children is not null;
            foreach (var parent in detail.Parents ?? Array.Empty<string>()) Add(parent, nodes[index].CaptureKey);
            foreach (var child in detail.Children ?? Array.Empty<string>()) Add(nodes[index].CaptureKey, child);
            features[index] = features[index] with { DependencyCompleteness = complete ? EvidenceCompleteness.Known : EvidenceCompleteness.Unknown };
            void Add(string parent, string child)
            {
                if (!byKey.TryGetValue(parent, out var p) || !byKey.TryGetValue(child, out var c) || p == c) { complete = false; return; }
                var key = p + ":" + c;
                if (edges.ContainsKey(key)) return;
                if (edges.Count == limits.Dependencies) { limited = true; complete = false; return; }
                edges.Add(key, new(p, c, NativeDependencyKind.ParentChild, new[] { new ObservationEvidence(EvidenceSource.NativeDependency, "IFeature.GetParents/GetChildren; no design relation inferred.") }));
            }
        }
        if (limited)
            features = features.Select(f => f with { DependencyCompleteness = EvidenceCompleteness.Unknown,
                SupportReason = "OBSERVATION_LIMIT_EXCEEDED; partial noneditable report. " + f.SupportReason }).ToList();
        var model = new ObservedModel("0.3", ModelOrigin.External, selection, !limited,
            limited ? V03FailureCodes.ObservationLimitExceeded : null, features, edges.Values.ToArray(), Array.Empty<IdentityRemapping>());
        ObservedStateValidation.Validate(model);
        return new(model, new(nodes.Count, lowerBound, !inventoryLimited, nativeApiFeatureCount,
            "Unique native IFeature nodes: FirstFeature/GetNextFeature plus nested subfeatures; API GetFeatureCount reported separately (different counting convention)."));
    }
}
