using System;
using System.Linq;
using System.Runtime.InteropServices;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks;

public sealed partial class SolidWorksExecutionContext
{
    // A cold context is populated only from persisted native references. No
    // feature name lookup or geometry search is permitted during restoration.
    public void RestoreManagedState(CadState state, CadProgram program)
    {
        CheckThread();
        if (operations.Count != 0 || MutationInProgress)
            throw new StateException("TRANSACTION_BUSY", "Managed restoration needs a new execution context.");
        try
        {
            ManagedRevisionStore.ValidateAssociation(state, program);
            VerifyManagedIdentity(state.Document);
            var preflight = new RelationBackend().Preflight(program);
            if (!preflight.IsValid) throw new StateException(preflight.FailureCode!, preflight.Message);
            var roots = program.Operations.Where(o => o.Kind == OperationKind.CreateExtrude).ToArray();
            if (roots.Length != 1 || roots[0].Parameter<ProfileParameter>("profile").Value is not CenteredRectangleProfile ||
                program.Operations.Any(o => o.Kind is not (OperationKind.CreateExtrude or OperationKind.CreateThroughHole or
                    OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern)))
                throw new StateException(FailureCodes.OperationUnsupported, "M12 qualifies rectangular managed extrusion, through-hole and linear/rectangular pattern histories only.");
            foreach (var operation in program.Operations)
            {
                var node = state.Features.Single(f => f.SemanticId == operation.SemanticId);
                var feature = ResolveManagedReference<IFeature>(node.NativeReference) ??
                    throw new StateException("STALE_REFERENCE", "Managed feature is stale: " + node.SemanticId);
                var definition = feature.GetDefinition();
                var qualified = definition is ILinearPatternFeatureData && operation.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern;
                var evidence = feature.GetTypeName2();
                if (definition is IExtrudeFeatureData2 extrusion)
                {
                    if (!extrusion.AccessSelections(Document, null))
                        throw new StateException(V03FailureCodes.UnsupportedNativeSubtype, "Cannot inspect persisted extrusion selections.");
                    try
                    {
                        var boss = extrusion.IsBossFeature(); var initialBase = extrusion.IsBaseExtrude();
                        var thin = extrusion.IsThinFeature(); var end = extrusion.GetEndCondition(true);
                        evidence += $"; boss={boss}; base={initialBase}; thin={thin}; endCondition={end}";
                        qualified = ManagedHistoryQualification.Extrude(operation.Kind, boss, initialBase, thin, end);
                    }
                    finally { extrusion.ReleaseSelectionAccess(); }
                }
                if (!qualified) throw new StateException(V03FailureCodes.UnsupportedNativeSubtype, "Persistent feature has a different native subtype: " + node.SemanticId + " (" + evidence + ")");
                operations.Add(node.SemanticId, operation);
            }
            var expectedOutputs = program.Operations.SelectMany(o => ProfileOutputs.For(o).Select(p =>
                (Id: o.SemanticId + p.Suffix, p.Type, Owner: o.SemanticId!))).ToArray();
            if (state.Entities.Count != expectedOutputs.Length || expectedOutputs.Any(p =>
                !state.Entities.Any(e => e.SemanticId == p.Id && e.Type == p.Type && e.OwnerFeatureSemanticId == p.Owner)))
                throw new StateException("STATE_DRIFT_DETECTED", "Managed semantic output inventory differs from its program.");
            foreach (var entity in state.Entities)
            {
                object? resolved = entity.Type switch
                {
                    SemanticType.FeatureRef or SemanticType.LocalFrame or SemanticType.ReferenceAxis or SemanticType.SketchProfile =>
                        ResolveManagedReference<IFeature>(entity.NativeReference),
                    SemanticType.BodyRef => ResolveManagedReference<IBody2>(entity.NativeReference),
                    SemanticType.PlanarFace or SemanticType.CylindricalFace => ResolveManagedReference<IFace2>(entity.NativeReference),
                    SemanticType.LinearEdge => ResolveManagedReference<IEdge>(entity.NativeReference),
                    _ => null
                };
                if (resolved is null) throw new StateException("STALE_REFERENCE", "Managed output is stale or has a different interface: " + entity.SemanticId);
                if (entity.Type is SemanticType.FeatureRef or SemanticType.LocalFrame && entity.NativeReference !=
                    state.Features.Single(f => f.SemanticId == entity.OwnerFeatureSemanticId).NativeReference)
                    throw new StateException("STALE_REFERENCE", "Output's native feature owner differs.");
                if (entity.Type == SemanticType.SketchProfile)
                {
                    if (resolved is not IFeature profile || profile.GetSpecificFeature2() is not ISketch ||
                        !NativeTopology.Objects<IFeature>(DirectFromState(entity.OwnerFeatureSemanticId).GetParents()).Any(p =>
                            PersistentReferenceAdapter.Capture(this, p) == entity.NativeReference))
                        throw new StateException("STALE_REFERENCE", "Hole sketch is not a parent of its persisted owner.");
                    holeProfiles.Add(entity.OwnerFeatureSemanticId, entity.NativeReference);
                }
                if (entity.Type is SemanticType.PlanarFace or SemanticType.CylindricalFace && resolved is IFace2 face &&
                    (face.GetFeature() is not IFeature faceOwner || PersistentReferenceAdapter.Capture(this, faceOwner) !=
                        state.Features.Single(f => f.SemanticId == entity.OwnerFeatureSemanticId).NativeReference))
                    throw new StateException("STALE_REFERENCE", "Face reference belongs to another native feature.");
                SemanticGeometry? logical = null;
                if (entity.Type == SemanticType.LocalFrame)
                {
                    var origin = new Point3(0, 0, 0);
                    logical = new(origin, Frame: new(origin, new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)));
                    CompareManagedGeometry(entity.Geometry, logical);
                }
                outputs.Add(entity.SemanticId, new(entity.Type, entity.OwnerFeatureSemanticId, entity.NativeReference, logical));
            }
            IFeature DirectFromState(string id) => ResolveManagedReference<IFeature>(state.Features.Single(f => f.SemanticId == id).NativeReference)!;
            ConstructionRoot = roots[0].SemanticId;
            CreatedFeature = DirectFeature(ConstructionRoot!);
            RelationProgram = program; RelationDependencies = new DesignRelationEngine().Solve(program).Dependencies;
            RelationRevision = state.Revision; RelationContextUsable = true;
            VerifyManagedReadback(state, program);
        }
        catch { RelationContextUsable = false; throw; }
    }

    private T? ResolveManagedReference<T>(NativePersistentReference reference) where T : class
    {
        try { return PersistentReferenceAdapter.Resolve<T>(this, reference).NativeObject; }
        catch (COMException error) { throw new StateException("STALE_REFERENCE", "Native persistent-reference resolution failed.", error); }
    }

    public void VerifyManagedIdentity(DocumentIdentity expected)
    {
        CheckThread();
        if (Document.ConfigurationManager.ActiveConfiguration.Name != expected.ConfigurationName)
            throw new StateException(V03FailureCodes.ConfigurationMismatch, "Active native configuration name differs.");
        var actual = DocumentIdentityAdapter.Read(this);
        if (actual.ConfigurationId != expected.ConfigurationId)
            throw new StateException(V03FailureCodes.ConfigurationMismatch, "Native configuration GUID differs.");
        if (actual.DocumentId != expected.DocumentId || !actual.Matches(expected))
            throw new StateException("DOCUMENT_IDENTITY_MISMATCH", "Native document GUID/path differs; Save As is not an implicit identity transition.");
    }

    public void VerifyManagedReadback(CadState state, CadProgram program)
    {
        CheckThread(); VerifyManagedIdentity(state.Document);
        if (!RelationContextUsable || MutationInProgress || RelationRevision != state.Revision ||
            Document.GetActiveSketch2() is not null || Document.Extension.NeedsRebuild2 != 0)
            throw new StateException("FEATURE_REBUILD_FAILED", "Managed context is not stable for readback.");
        RelationNativeReadback.Verify(this, program);
        NativeHoleInstanceVerifier.Verify(this, program, program.Operations.Select(o => o.SemanticId!));
        var actual = CaptureState(null, program, null, state.Revision);
        if (actual.Features.Any(f => f.ReferenceHealth != ReferenceHealth.Healthy) || actual.Entities.Any(e => e.ReferenceHealth != ReferenceHealth.Healthy))
            throw new StateException("STALE_REFERENCE", "A reopened managed native reference is not healthy.");
        if (state.Parameters.Count != actual.Parameters.Count || state.Bindings.Count != actual.Bindings.Count ||
            !state.Bindings.ToHashSet().SetEquals(actual.Bindings))
            throw new StateException("STATE_DRIFT_DETECTED", "Native parameter binding inventory differs.");
        foreach (var binding in state.Bindings)
        {
            var owner = program.Operations.Single(o => o.SemanticId == binding.OwnerFeatureSemanticId);
            var expected = ParameterMutationRegistry.Default.Expected(owner, binding.Parameter);
            RelationNativeReadback.Near(state.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value, expected, "Committed scalar differs from program.");
            RelationNativeReadback.Near(actual.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value, expected, "Saved native scalar differs from program.");
        }
        foreach (var entity in state.Entities)
            CompareManagedGeometry(actual.Entities.Single(e => e.SemanticId == entity.SemanticId).Geometry, entity.Geometry);
        var root = program.Operations.Single(o => o.SemanticId == ConstructionRoot);
        var profile = (CenteredRectangleProfile)root.Parameter<ProfileParameter>("profile").Value;
        var measurement = ExtrudeMeasurementReader.Read(this);
        if (measurement.SolidBodyCount != 1) throw new StateException("GEOMETRY_INVALID", "Managed native Part is not a single solid.");
        RelationNativeReadback.Near(measurement.WidthMm, profile.WidthMm, "Managed native width differs.");
        RelationNativeReadback.Near(measurement.HeightMm, profile.HeightMm, "Managed native height differs.");
        RelationNativeReadback.Near(measurement.DepthMm, root.Parameter<LengthParameter>("depthMm").Millimeters, "Managed native axial extent differs.");
    }

    private static void CompareManagedGeometry(SemanticGeometry? actual, SemanticGeometry? expected)
    {
        if (actual == expected) return;
        if (actual is null || expected is null) throw new StateException("STATE_DRIFT_DETECTED", "Persisted/native semantic geometry differs.");
        void Near(double a, double b) => RelationNativeReadback.Near(a, b, "Persisted/native geometry differs.");
        if (actual.OriginMm is { } a && expected.OriginMm is { } b) { Near(a.X, b.X); Near(a.Y, b.Y); Near(a.Z, b.Z); }
        else if (actual.OriginMm != expected.OriginMm) throw new StateException("STATE_DRIFT_DETECTED", "Geometry origin differs.");
        if (actual.Direction is { } x && expected.Direction is { } y) { Near(x.X, y.X); Near(x.Y, y.Y); Near(x.Z, y.Z); }
        else if (actual.Direction != expected.Direction) throw new StateException("STATE_DRIFT_DETECTED", "Geometry direction differs.");
        if (actual.RadiusMm is { } ar && expected.RadiusMm is { } br) Near(ar, br);
        else if (actual.RadiusMm != expected.RadiusMm) throw new StateException("STATE_DRIFT_DETECTED", "Geometry radius differs.");
        if (actual.Frame != expected.Frame) throw new StateException("STATE_DRIFT_DETECTED", "Geometry frame differs.");
    }
}

// SOLIDWORKS distinguishes its initial base extrusion from a subsequent boss.
// This managed-only rule grants no external intake/edit capability.
public static class ManagedHistoryQualification
{
    public static bool Extrude(OperationKind kind, bool isBoss, bool isBase, bool thin, int endCondition) => !thin && kind switch
    {
        OperationKind.CreateExtrude => (isBoss || isBase) && endCondition == (int)swEndConditions_e.swEndCondBlind,
        OperationKind.CreateThroughHole => !isBoss && !isBase && endCondition == (int)swEndConditions_e.swEndCondThroughAll,
        _ => false
    };
}
