using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed partial class SolidWorksExecutionContext
{
    public GenericRevisionPackage CaptureGenericRevisionPackage(GenericProvenance? provenance = null)
    {
        var state = CaptureGenericConstructionState();
        GenericPlanningProvenance.Verify(provenance,state);
        var package = new GenericRevisionPackage("0.3", "generic_construction", state.Program.Operations.Any(o => o.EdgeTreatment is not null) ? 3 : state.Sketches.Any(s => s.Specification.RadialProfile is not null) ? 2 : 1, state,
            genericSketches.Values.Select(s => new GenericSketchImage(s.SemanticId, s.Primitives.Select(p => p.Reference).ToArray(),
                s.Dimensions.Select(d => new GenericDimensionImage(d.EntityId, d.Kind, d.CircleReference)).ToArray(), s.NativeModelToSketchTransform, s.Revision) { Radial = s.Radial }).ToArray(),
            genericFeatures.Values.Select(f => new GenericFeatureImage(f.Operation.SemanticId, f.ProfileId, f.NativeReverse, f.AxisLine, f.AxisSketch, f.AxisDatum) { Edge = f.Edge }).ToArray(), provenance);
        GenericRevisionValidation.Validate(package); return package;
    }
    public void RestoreGenericRevisionPackage(GenericRevisionPackage package)
    {
        CheckThread();
        if (genericState is not null || genericSketches.Count != 0 || operations.Count != 0 || outputs.Count != 0 || MutationInProgress)
            throw new StateException("TRANSACTION_BUSY", "Generic restore requires an empty fresh controller context.");
        try
        {
            GenericRevisionValidation.Validate(package); var state = package.State;
            GenericPlanningProvenance.Verify(package.Provenance,state);
            VerifyManagedIdentity(state.Document);
            if (Document.GetActiveSketch2() is not null || Document.Extension.NeedsRebuild2 != 0)
                throw new StateException("FEATURE_REBUILD_FAILED", "Cold native document is not stable.");
            foreach (var o in state.Program.Operations)
            {
                var n = state.Features.Single(f => f.SemanticId == o.SemanticId);
                var feature = PersistentReferenceAdapter.Resolve<IFeature>(this, n.Reference).NativeObject;
                if (feature is null || feature.GetTypeName2() != n.NativeType)
                    throw new StateException("STALE_REFERENCE", "Cold feature subtype/identity differs.");
                Register(o.SemanticId, SemanticType.FeatureRef, o.SemanticId, feature);
                if (o.Kind == ConstructionKind.CreateSketch)
                {
                    var s = state.Sketches.Single(s => s.SemanticId == o.SemanticId);
                    var image = package.SketchBindings.Single(s => s.SemanticId == o.SemanticId);
                    var sketch = feature.GetSpecificFeature2() as ISketch ?? throw new StateException("STALE_REFERENCE", "Profile no longer owns a native sketch.");
                    var referenceType = 0;
                    var host = sketch.GetReferenceEntity(ref referenceType);
                    if (host is null || PersistentReferenceAdapter.Capture(this, host) != s.Plane)
                        throw new StateException("BINDING_AMBIGUOUS", "Native sketch host differs from its exact persisted plane/face owner.");
                    var geometry = PlanarSketchPreflight.Prepare(s.Specification).Primitives;
                    var binding = new PlanarSketchBinding(ConstructionDocumentId, state.Document.ConfigurationName, s.SemanticId, s.Placement, s.Specification, s.Plane, s.Profile,
                        geometry.Select((p, i) => new NativeSketchPrimitive(p, image.Primitives[i])).ToArray(),
                        image.Dimensions.Select(d => new NativeSketchDimension(d.EntityId, d.Kind, d.CircleReference)).ToArray(), image.ModelToSketchTransform, image.Revision) { Radial = image.Radial };
                    genericSketches.Add(s.SemanticId, binding); _ = ReadPlanarSketch(binding);
                    Register(o.SemanticId + ".profile", SemanticType.SketchProfile, o.SemanticId, feature);
                    var origin = SketchFrames.World(s.Placement, new(0, 0));
                    Register(o.SemanticId + ".axis_x", SemanticType.ReferenceAxis, o.SemanticId, feature,
                        new(new Point3(origin.X, origin.Y, origin.Z), new CadHarness.State.Vector3(s.Placement.Frame.XAxis.X, s.Placement.Frame.XAxis.Y, s.Placement.Frame.XAxis.Z)));
                }
                else if (o.Kind == ConstructionKind.CreateDatumPlane)
                {
                    genericDatums.Add(o.SemanticId, n.Reference);
                    var origin = SketchFrames.World(o.Placement!, new(0, 0));
                    var effective = o.Placement! with { Frame = o.Placement!.Frame with { OriginMm = origin }, Offset = new(0) };
                    PlanarSketchNative.VerifyPlane(feature, effective, null);
                    Register(o.SemanticId + ".plane", SemanticType.ReferencePlane, o.SemanticId, feature,
                        new(new Point3(origin.X, origin.Y, origin.Z), new CadHarness.State.Vector3(effective.Frame.ZAxis.X, effective.Frame.ZAxis.Y, effective.Frame.ZAxis.Z)));
                }
                else
                {
                    var b = package.FeatureBindings.Single(f => f.SemanticId == o.SemanticId);
                    if (b.AxisLine is not null)
                    {
                        var line = PersistentReferenceAdapter.Resolve<ISketchSegment>(this, b.AxisLine).NativeObject
                            ?? throw new StateException("STALE_REFERENCE", "Axis line is stale.");
                        var axisOwner = PersistentReferenceAdapter.Capture(this, CenteredRectangleProfileBackend.FeatureForSketch(Document, line.GetSketch()));
                        var axis = PersistentReferenceAdapter.Resolve<IFeature>(this, b.AxisDatum!).NativeObject;
                        if (axisOwner != b.AxisSketch || axis is null || !NativeTopology.Objects<IFeature>(axis.GetParents()).Any(p => PersistentReferenceAdapter.Capture(this, p) == b.AxisSketch))
                            throw new StateException("BINDING_AMBIGUOUS", "Axis line/sketch/datum ownership differs.");
                    }
                    genericFeatures.Add(o.SemanticId, new(o, n.Reference, b.ProfileId, b.NativeReverse, b.AxisLine, b.AxisSketch, b.AxisDatum) { Edge = b.Edge });
                    var body = PersistentReferenceAdapter.Resolve<IBody2>(this, state.Solid!.Reference).NativeObject
                        ?? throw new StateException("STALE_REFERENCE", "Cold single body identity differs.");
                    Register(o.SemanticId + ".body", SemanticType.BodyRef, o.SemanticId, body);
                }
            }
            genericState = state; VerifyGeneric(state.Program);
            if (!GenericConstructionStatePolicy.MatchesNativeSnapshot(state, ReadGenericState(state.Program, state.Revision)))
                throw new StateException("STATE_DRIFT_DETECTED", "Full cold Generic native/state association differs.");
        }
        catch { RelationContextUsable = false; throw; }
    }
}
