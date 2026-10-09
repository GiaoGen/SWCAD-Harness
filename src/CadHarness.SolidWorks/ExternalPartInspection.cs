using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Vector3 = CadHarness.State.Vector3;

namespace CadHarness.SolidWorks;

public sealed record ExternalPartInspectionResult(ExternalObservationResult Observation,
    IReadOnlyList<string> Configurations, IReadOnlyList<GeometryObservation> Bodies,
    bool ReadOnlyNative, bool DirtyOnOpen, bool DirtyAfterInspection, bool OriginalPreserved,
    bool CopyPreserved, bool OriginalActiveRestored, bool NoOwnedDocumentRemains, int OpenWarnings);

// No context that can execute construction/editing is returned. The copy is
// byte-identical to the selected source and opened read-only, never adopted.
public static class ExternalPartInspection
{
    public static ExternalPartInspectionResult Inspect(SolidWorksConnection connection, string sourcePath, string copyPath,
        string? configuration = null, ExternalObservationLimits? limits = null, IManagedDocumentLifecycle? lifecycle = null)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new InvalidOperationException("Inspection requires STA.");
        limits ??= new(); limits.Validate();
        sourcePath = Path.GetFullPath(sourcePath); copyPath = Path.GetFullPath(copyPath);
        if (!string.Equals(Path.GetExtension(sourcePath), ".SLDPRT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sourcePath, copyPath, StringComparison.OrdinalIgnoreCase))
            throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "Select a native Part and a separate fresh inspection copy.");
        var sw = connection.Application;
        if (sw.GetOpenDocumentByName(sourcePath) is not null || sw.GetOpenDocumentByName(copyPath) is not null)
            throw new StateException("DOCUMENT_ALREADY_OPEN", "Do not adopt or inspect an existing engineer document; close it explicitly first.");
        var original = ManagedRevisionStore.Fingerprint(sourcePath);
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        File.Copy(sourcePath, copyPath, false);
        var copy = ManagedRevisionStore.Fingerprint(copyPath);
        if (copy.Sha256 != original.Sha256 || copy.SizeBytes != original.SizeBytes)
            throw new StateException(V03FailureCodes.SourceFileDrift, "Source changed while making its read-only inspection copy.");
        IModelDoc2? owned = null;
        var originalTitle = sw.IActiveDoc2 is IModelDoc2 active ? active.GetTitle() : null;
        ExternalPartInspectionResult? result = null;
        try
        {
            Guard(sw); lifecycle?.BeforeOpen(copyPath);
            var errors = 0; var warnings = 0;
            owned = sw.OpenDoc6(copyPath, (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly,
                configuration ?? "", ref errors, ref warnings) as IModelDoc2;
            if (owned is not null) lifecycle?.Opened(copyPath, owned.GetTitle());
            if (owned is null || errors != 0) throw new StateException("NATIVE_INTAKE_FAILED", $"Read-only OpenDoc6 errors={errors}, warnings={warnings}.");
            if (!owned.IsOpenedReadOnly() || !string.Equals(owned.GetPathName(), copyPath, StringComparison.OrdinalIgnoreCase))
                throw new StateException("NATIVE_INTAKE_NOT_READ_ONLY", "Selected copy was not opened read-only at the expected path.");
            var configs = ((Array)owned.GetConfigurationNames()).Cast<string>().ToArray();
            if (configs.Length > ContractLimits.Features) throw new StateException(V03FailureCodes.ObservationLimitExceeded, "Configuration list exceeds bound.");
            var actualConfiguration = owned.ConfigurationManager.ActiveConfiguration.Name;
            if (configuration is not null && actualConfiguration != configuration)
                throw new StateException(V03FailureCodes.ConfigurationMismatch, "Native configuration differs from the explicitly selected one.");
            var dirty = owned.GetSaveFlag();
            var selection = ExternalObservation.Selection(original, copy, actualConfiguration);
            var source = new NativeSource(owned);
            var observation = ExternalObservation.Capture(selection, source, limits, owned.GetFeatureCount());
            var bodies = source.Bodies(limits.Geometry - observation.Model.Features.Sum(f => f.Geometry.Count), out var bodyOverflow);
            // Body limit is independently reported as a partial noneditable overlay.
            if (bodyOverflow) observation = observation with { Model = observation.Model with {
                InventoryComplete = false, LimitOutcome = V03FailureCodes.ObservationLimitExceeded,
                Features = observation.Model.Features.Select(f => f with { DependencyCompleteness = EvidenceCompleteness.Unknown,
                    SupportReason = "Body geometry limit exceeded; partial noneditable observation." }).ToArray() } };
            ObservedStateValidation.Validate(observation.Model);
            if (owned.ConfigurationManager.ActiveConfiguration.Name != actualConfiguration)
                throw new StateException(V03FailureCodes.ConfigurationMismatch, "Configuration changed during inspection.");
            if (owned.GetSaveFlag() != dirty) throw new StateException("READ_ONLY_OBSERVATION_CHANGED_DOCUMENT", "Observation changed the native dirty flag.");
            Guard(sw);
            result = new(observation, configs, bodies, true, dirty, owned.GetSaveFlag(), true, true, false, false, warnings);
        }
        finally
        {
            try
            {
                if (owned is not null)
                {
                    var title = owned.GetTitle(); sw.CloseDoc(title);
                    if (sw.GetOpenDocumentByName(copyPath) is not null) throw new StateException("OWNED_DOCUMENT_CLEANUP_FAILED", "Read-only copy remains open.");
                    lifecycle?.Closed(copyPath, title); owned = null;
                }
            }
            finally
            {
                var error = 0;
                if (originalTitle is not null) sw.ActivateDoc3(originalTitle, false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error);
                var restored = error == 0 && (originalTitle is null ? sw.IActiveDoc2 is null : sw.IActiveDoc2 is IModelDoc2 doc && doc.GetTitle() == originalTitle);
                if (!restored) throw new StateException("OWNED_DOCUMENT_CLEANUP_FAILED", "Original active engineer document was not restored.");
                if (ManagedRevisionStore.Hash(sourcePath) != original.Sha256 || ManagedRevisionStore.Hash(copyPath) != copy.Sha256)
                    throw new StateException(V03FailureCodes.SourceFileDrift, "Source or inspection copy changed; never save either during intake.");
                if (result is not null) result = result with { OriginalActiveRestored = true, NoOwnedDocumentRemains = owned is null };
            }
        }
        return result!;
    }
    private static void Guard(ISldWorks application)
    {
        using var process = Process.GetProcessById(application.GetProcessID()); process.Refresh();
        Marshal.SetLastPInvokeError(0); var gdi = GetGuiResources(process.Handle, 0);
        if (!process.Responding || gdi >= 7000 || gdi == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new StateException("TEST_RESOURCE_LIMIT", "Native process is unresponsive or GDI exceeds its guard.");
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetGuiResources(IntPtr process, uint flag);

    internal sealed class NativeSource : IExternalObservationSource
    {
        private readonly IModelDoc2 document;
        private readonly Dictionary<string, IFeature> features = new(StringComparer.Ordinal);
        private readonly bool emptyLinksKnown;
        public NativeSource(IModelDoc2 document, bool emptyLinksKnown = false)
        { this.document = document; this.emptyLinksKnown = emptyLinksKnown; }
        private static string Key(object native)
        {
            var pointer = Marshal.GetIUnknownForObject(native);
            try { return pointer.ToInt64().ToString("x"); } finally { Marshal.Release(pointer); }
        }
        public IEnumerable<ExternalInventoryNode> Inventory()
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<(IFeature Feature, bool Sub)>();
            if (document.FirstFeature() is IFeature first) stack.Push((first, false));
            var visits = 0;
            while (stack.Count > 0)
            {
                if (++visits > ContractLimits.Dependencies) throw new StateException(V03FailureCodes.ObservationLimitExceeded, "Cyclic or excessive native feature traversal.");
                var (feature, sub) = stack.Pop(); var key = Key(feature);
                var next = sub ? feature.GetNextSubFeature() : feature.GetNextFeature();
                if (next is IFeature sibling) stack.Push((sibling, sub));
                if (!visited.Add(key)) continue;
                if (feature.GetFirstSubFeature() is IFeature child) stack.Push((child, true));
                features.Add(key, feature);
                var reference = Reference(feature, out var referenceHealth);
                var health = feature.IsSuppressed() ? ObservationHealth.Suppressed : feature.GetErrorCode() != 0 ? ObservationHealth.Unknown : referenceHealth;
                var nativeType = feature.GetTypeName2();
                yield return new(key, feature.Name, string.IsNullOrWhiteSpace(nativeType) ? "unknown_native_type" : nativeType,
                    health, reference, new[] { new ObservationEvidence(EvidenceSource.PersistentReference,
                        reference is null ? "GetPersistReference3 unavailable or unresolvable; no binding reference." : "GetPersistReference3 resolved back to the exact IFeature COM identity."),
                        new ObservationEvidence(EvidenceSource.NativeDefinition, "GetTypeName2/IsSuppressed/GetErrorCode; no display-name classification.") });
            }
        }
        private NativePersistentReference? Reference(object value, out ObservationHealth health)
        {
            health = ObservationHealth.Unknown;
            try
            {
                if (document.Extension.GetPersistReference3(value) is not byte[] bytes || bytes.Length == 0) return null;
                var error = 0; var resolved = document.Extension.GetObjectByPersistReference3(bytes, out error);
                if (error != 0 || resolved is null || Key(resolved) != Key(value)) { health = ObservationHealth.Stale; return null; }
                health = ObservationHealth.Healthy; return new(Convert.ToBase64String(bytes));
            }
            catch (COMException) { return null; }
        }
        public ExternalFeatureFacts Read(ExternalInventoryNode node, int geometryAllowance)
        {
            var feature = features[node.CaptureKey];
            string[]? parents = null; string[]? children = null;
            var overflow = false;
            try { parents = Links(feature.GetParents()); children = Links(feature.GetChildren()); }
            catch (COMException) { }
            catch (StateException) { overflow = true; }
            var parameters = new List<ObservedParameter>(); var geometry = new List<GeometryObservation>();
            var subtype = NativeSubtype.Unrecognized;
            var reason = "Native subtype has no M13 scalar reader; visible read-only.";
            if (node.Health == ObservationHealth.Suppressed)
                return new(subtype, parameters, geometry, parents, children, "Suppressed in selected configuration; scalar/geometry qualification withheld.", overflow);
            try
            {
                var definition = feature.GetDefinition();
                if (definition is IExtrudeFeatureData2 probe)
                    reason = $"Extrude subtype not qualified: thin={probe.IsThinFeature()}, bothDirections={probe.BothDirections}, draftD1={probe.GetDraftWhileExtruding(true)}, boss={probe.IsBossFeature()}, base={probe.IsBaseExtrude()}, endConditionD1={probe.GetEndCondition(true)}.";
                if (definition is IExtrudeFeatureData2 extrude && !extrude.IsThinFeature() && !extrude.BothDirections && !extrude.GetDraftWhileExtruding(true))
                {
                    if ((extrude.IsBossFeature() || extrude.IsBaseExtrude()) && extrude.GetEndCondition(true) == (int)swEndConditions_e.swEndCondBlind)
                    {
                        subtype = NativeSubtype.StraightBlindBossExtrude;
                        parameters.Add(Scalar(ParameterKey.ExtrusionDepth, extrude.GetDepth(true) * 1000, NativeAccessor.ExtrudeDepthDirection1, "IExtrudeFeatureData2.GetDepth(true), meters*1000; blind non-thin boss/base definition. Read-only candidate, not edit qualification."));
                    }
                    else if (!extrude.IsBossFeature() && !extrude.IsBaseExtrude() && extrude.GetEndCondition(true) == (int)swEndConditions_e.swEndCondThroughAll)
                    {
                        var parentFeatures = (parents ?? Array.Empty<string>()).Where(features.ContainsKey).Select(p => features[p]).ToArray();
                        var sketches = parentFeatures.Where(p => ExternalProfileOwnership.IsConsumingProfile(p.GetTypeName2()))
                            .Select(p => p.GetSpecificFeature2()).OfType<ISketch>().ToArray();
                        reason = "Through-all cut reader: native parents=" + string.Join(",", parentFeatures.Select(p => p.GetTypeName2())) +
                            $"; consumed ProfileFeature sketches={sketches.Length}; OriginProfileFeature is a constraint/reference parent, not a profile.";
                        if (sketches.Length == 1 && sketches[0].GetSketchSegments() is Array segments)
                        {
                            if (segments.Length > ContractLimits.SketchEntities) overflow = true;
                            else
                            {
                                var active = segments.Cast<object>().OfType<ISketchSegment>().Where(s => !s.ConstructionGeometry).ToArray();
                                reason += $" Total segments={segments.Length}; active={active.Length}; active circular arcs={active.OfType<ISketchArc>().Count(a => a.IsCircle() != 0)}.";
                                if (active.Length == 1 && active[0] is ISketchArc circle && circle.IsCircle() != 0)
                                { subtype = NativeSubtype.SingleCircleThroughAllCut; parameters.Add(Scalar(ParameterKey.HoleDiameter, circle.GetRadius() * 2000, NativeAccessor.SingleCircleRadius, "Single owning ISketchArc.IsCircle/GetRadius, meters*2000; cut definition through-all. Hole Wizard is not equivalent.")); }
                            }
                        }
                    }
                }
                else if (definition is ILinearPatternFeatureData pattern)
                {
                    subtype = pattern.IsDirection2Specified() ? NativeSubtype.TwoDirectionRectangularPattern : NativeSubtype.SingleDirectionLinearPattern;
                    parameters.Add(Scalar(ParameterKey.PatternCount, pattern.D1TotalInstances, NativeAccessor.LinearPatternDirection1Count, "ILinearPatternFeatureData.D1TotalInstances; count including seed, active direction 1 only."));
                    parameters.Add(Scalar(ParameterKey.PatternSpacing, pattern.D1Spacing * 1000, NativeAccessor.LinearPatternDirection1Spacing, "ILinearPatternFeatureData.D1Spacing, meters*1000; native direction 1 only."));
                }
                else if (node.NativeType is "HoleWzd" or "WizardHole") subtype = NativeSubtype.HoleWizard;
                if (subtype != NativeSubtype.Unrecognized) reason = "Native definition read; external mutation remains unqualified until M14.";
                if (feature.GetFaces() is Array faces)
                {
                    if (faces.Length > geometryAllowance) overflow = true;
                    else foreach (var face in faces.Cast<object>().OfType<IFace2>())
                    {
                        var surface = face.GetSurface() as ISurface;
                        if (surface is null || !surface.IsPlane() && !surface.IsCylinder()) continue;
                        var reference = Reference(face, out var health); var box = (double[])face.GetBox();
                        geometry.Add(new("placeholder", surface.IsCylinder() ? GeometryKind.CylindricalFace : GeometryKind.PlanarFace,
                            health, reference, null, Min(box), Max(box), surface.IsCylinder() ? ((double[])surface.CylinderParams)[6] * 1000 : null,
                            null, new[] { new ObservationEvidence(EvidenceSource.NativeTopology, "IFeature.GetFaces/IFace2.GetBox/ISurface native plane or cylinder; bounds are approximate.") }));
                    }
                }
            }
            catch (Exception error) when (error is COMException or InvalidCastException or ContractException)
            { subtype = NativeSubtype.Unrecognized; parameters.Clear(); geometry.Clear(); reason = "Native reader unavailable: " + error.GetType().Name + "; no value inferred."; }
            return new(subtype, parameters, geometry, parents, children, reason, overflow);
        }
        private string[]? Links(object? links)
        {
            if (links is null) return emptyLinksKnown ? Array.Empty<string>() : null;
            if (links is not Array array) throw new COMException("Native dependency array unavailable.");
            if (array.Length > ContractLimits.Dependencies) throw new StateException(V03FailureCodes.ObservationLimitExceeded, "Dependency array exceeds bound.");
            if (array.Cast<object>().Any(value => value is not IFeature)) return null;
            return array.Cast<object>().Select(Key).ToArray();
        }
        private static ObservedParameter Scalar(ParameterKey key, double value, NativeAccessor accessor, string detail)
        {
            var unit = key == ParameterKey.PatternCount ? ScalarUnit.Count : ScalarUnit.Millimeter;
            ContractValidation.Scalar(key, unit, value);
            return new("placeholder", key, unit, value, accessor, new[] { new ObservationEvidence(EvidenceSource.NativeDefinition, detail) });
        }
        private static Vector3 Min(double[] box) => new(box[0] * 1000, box[1] * 1000, box[2] * 1000);
        private static Vector3 Max(double[] box) => new(box[3] * 1000, box[4] * 1000, box[5] * 1000);
        public IReadOnlyList<GeometryObservation> Bodies(int limit, out bool overflow)
        {
            var bodies = ((IPartDoc)document).GetBodies2((int)swBodyType_e.swAllBodies, false) as Array;
            overflow = bodies is not null && bodies.Length > limit;
            if (bodies is null || overflow) return Array.Empty<GeometryObservation>();
            var result = new List<GeometryObservation>();
            foreach (var body in bodies.Cast<object>().OfType<IBody2>())
            {
                var reference = Reference(body, out var health); var box = (double[])body.GetBodyBox();
                var mass = body.GetMassProperties(1) as double[];
                var volume = mass is { Length: > 3 } && mass[3] > 0 ? mass[3] * 1e9 : (double?)null;
                result.Add(new("body_" + result.Count, GeometryKind.Body, health, reference, null, Min(box), Max(box), null, volume,
                    new[] { new ObservationEvidence(EvidenceSource.NativeTopology, "IPartDoc.GetBodies2/IBody2.GetBodyBox (approximate)/GetMassProperties(1)[3] m^3*1e9; no rebuild.") }));
            }
            return result;
        }
    }
}
