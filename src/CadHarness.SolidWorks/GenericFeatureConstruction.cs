using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using V = CadHarness.Ir.V03.Vector3;

[assembly: InternalsVisibleTo("CadHarness.FeatureConstruction.Tests")]

namespace CadHarness.SolidWorks;

public sealed record FeatureConstructionTrace(string Stage, string SemanticId, string Api, double? Before = null, double? After = null, bool? Success = null);
public sealed record GenericScalarCommand(string Target, ParameterKey Parameter, double ExpectedOldValue, double Value, long ExpectedRevision, string? ParameterSemanticId = null);
internal sealed record GenericCommand(ConstructionProgram? Program, GenericScalarCommand? Edit, bool Candidate = false);
internal sealed record GenericFeatureBinding(ConstructionOperation Operation, NativePersistentReference Feature, string ProfileId,
    bool NativeReverse, NativePersistentReference? AxisLine, NativePersistentReference? AxisSketch, NativePersistentReference? AxisDatum)
{
    public GenericEdgeImage? Edge { get; init; }
}
internal sealed record GenericPrepared(GenericCommand Command, ConstructionProgram Program, IReadOnlyList<ConstructionOperation> Addition,
    FeatureSolidExpectation Expectation, ChangeSet Changes) : MutationPreparation(Changes, FullValidationReason.NewModelFinalization);
internal sealed record GenericRollback(NativeConstructionCheckpoint Checkpoint, GenericConstructionState Before);

public sealed partial class SolidWorksExecutionContext
{
    private readonly Dictionary<string, PlanarSketchBinding> genericSketches = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GenericFeatureBinding> genericFeatures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NativePersistentReference> genericDatums = new(StringComparer.Ordinal);
    private GenericConstructionState? genericState;
    internal Action? GenericDurableCheckpoint { get; set; }
    internal Action<GenericConstructionState>? GenericDurableCommit { get; set; }
    public Action<FeatureConstructionTrace>? FeatureEvidenceSink { get; set; }
    internal void FeatureTrace(FeatureConstructionTrace trace) => FeatureEvidenceSink?.Invoke(trace);
    private sealed record GenericSnapshot(Dictionary<string, PlanarSketchBinding> Sketches, Dictionary<string, GenericFeatureBinding> Features,
        Dictionary<string, NativePersistentReference> Datums, GenericConstructionState? State);
    internal object SnapshotGenericSession() => new GenericSnapshot(new(genericSketches), new(genericFeatures), new(genericDatums), genericState);
    internal void RestoreGenericSession(object saved)
    {
        var s = (GenericSnapshot)saved;
        genericSketches.Clear(); foreach (var p in s.Sketches) genericSketches.Add(p.Key, p.Value);
        genericFeatures.Clear(); foreach (var p in s.Features) genericFeatures.Add(p.Key, p.Value);
        genericDatums.Clear(); foreach (var p in s.Datums) genericDatums.Add(p.Key, p.Value);
        genericState = s.State;
    }
    public GenericConstructionState CaptureGenericConstructionState()
    {
        CheckThread();
        if (!RelationContextUsable || MutationInProgress) throw new StateException("TRANSACTION_BUSY", "Generic session is not stable.");
        if (genericState is null)
        {
            if (operations.Count != 0 || outputs.Count != 0 || NativeGeometry.SolidBodies(Document).Count != 0)
                throw new StateException("UNTRACKED_NATIVE_HISTORY", "Generic construction must start on a pristine managed context.");
            VerifyManagedHistory();
            genericState = new(DocumentIdentityAdapter.ReadForConstruction(this), 0, new("0.3", ModelOrigin.Harness, RequestMode.CreateModel, "generic_requirements", Array.Empty<ConstructionOperation>()),
                Array.Empty<GenericNativeFeature>(), Array.Empty<GenericNativeSketch>(), Array.Empty<GenericNativeParameter>(), Array.Empty<DependencyEdge>(), null);
        }
        VerifyGeneric(genericState.Program);
        return ReadGenericState(genericState.Program, genericState.Revision);
    }
    public MutationResult ConstructGeneric(ConstructionProgram program) => GenericMutation(new(program, null));
    public MutationResult EditGenericScalar(GenericScalarCommand edit) => GenericMutation(new(null, edit));
    internal MutationResult ConstructGenericCandidate(ConstructionProgram program) => GenericMutation(new(program, null, true));
    internal MutationResult EditGenericScalarCandidate(GenericScalarCommand edit) => GenericMutation(new(null, edit, true));
    private MutationResult GenericMutation(GenericCommand command)
    {
        CheckThread();
        if (MutationInProgress || !RelationContextUsable) return new(false, "TRANSACTION_BUSY", "Context busy or quarantined.", "integrity", false, false, false, false, false, false, null, ChangeSet.Empty, null, null);
        _ = CaptureGenericConstructionState();
        return new RequestMutationTransaction<GenericConstructionState, GenericCommand, GenericPrepared, GenericRollback>(new GenericStore(this),
            new GenericBackend(this), new GenericConstructionStatePolicy()).Execute(command);
    }
    private sealed class GenericStore(SolidWorksExecutionContext context) : IMutationStateStore<GenericConstructionState>
    {
        public GenericConstructionState Load() => context.genericState!;
        public void Commit(GenericConstructionState state)
        {
            if (context.GenericDurableCommit is { } publish) publish(state);
            context.genericState = state;
        }
    }
    internal FeatureSolidExpectation GenericExpectation(ConstructionProgram program)
    {
        var sketches = program.Operations.Where(o => o.Kind == ConstructionKind.CreateSketch).ToDictionary(o => o.SemanticId);
        var datums = program.Operations.Where(o => o.Kind == ConstructionKind.CreateDatumPlane).ToDictionary(o => o.SemanticId + ".plane");
        SpatialPlacement Placement(ConstructionOperation sketch)
        {
            var p = sketch.Placement!;
            if (datums.TryGetValue(p.Plane.SemanticId, out var datum))
            {
                var expectedOrigin = SketchFrames.World(datum.Placement!, new(0, 0));
                ContractValidation.Require(Math.Abs(SketchFrames.Dot(SketchFrames.Subtract(p.Frame.OriginMm, expectedOrigin), datum.Placement!.Frame.ZAxis)) < ContractLimits.LinearToleranceMm,
                    "Sketch frame differs from its declared datum.");
            }
            return p;
        }
        var profiles = program.Operations.Where(o => o.Profile is not null).Select(o =>
        {
            var s = sketches[o.Profile!.SemanticId[..^8]];
            return new PreparedFeatureProfile(o, Placement(s), PlanarSketchPreflight.Prepare(s.Sketch!));
        }).ToArray();
        var material = program.Operations.Any(o => o.EdgeTreatment is not null) ? OrthogonalMaterialProof.Evaluate(profiles) : FeatureConstructionPreflight.Evaluate(profiles);
        return ConvexFilletProof.Apply(program, material);
    }
    internal GenericConstructionState ReadGenericState(ConstructionProgram program, long revision)
    {
        var nodes = new List<GenericNativeFeature>();
        foreach (var o in program.Operations)
        {
            var reference = o.Kind == ConstructionKind.CreateSketch ? genericSketches[o.SemanticId].Profile :
                o.Kind == ConstructionKind.CreateDatumPlane ? genericDatums[o.SemanticId] : genericFeatures[o.SemanticId].Feature;
            var feature = PersistentReferenceAdapter.Resolve<IFeature>(this, reference).NativeObject ?? throw new StateException("STALE_REFERENCE", "Generic native feature unresolved.");
            var auxiliary = genericFeatures.TryGetValue(o.SemanticId, out var binding) ? new[] { binding.AxisLine, binding.AxisSketch, binding.AxisDatum }.OfType<NativePersistentReference>().ToArray() : Array.Empty<NativePersistentReference>();
            foreach (var r in auxiliary) if (PersistentReferenceAdapter.Resolve<object>(this, r).Health != ReferenceHealth.Healthy) throw new StateException("STALE_REFERENCE", "Auxiliary axis reference unhealthy.");
            nodes.Add(new(o.SemanticId, o.Kind, feature.GetTypeName2(), reference, auxiliary));
        }
        var parameters = new List<GenericNativeParameter>();
        foreach (var f in genericFeatures.Values)
        {
            if (f.Operation.Kind == ConstructionKind.ApplyFillet) continue;
            var key = f.Operation.Kind == ConstructionKind.CreateAdditiveBoss ? ParameterKey.AdditiveBossDepth : f.Operation.Kind == ConstructionKind.CreateExtrudedCut ? ParameterKey.CutDepth : ParameterKey.ExtrusionDepth;
            if (f.Operation.Depth is not null) parameters.Add(new(f.Operation.SemanticId + ".depth", f.Operation.SemanticId, key, NativeDepth(f), GenericFeatureCapabilities.IsQualified(f.Operation.Kind, key)));
            if (f.Operation.Kind == ConstructionKind.CreateRevolvedBoss && genericSketches[f.ProfileId].Specification.Entities is { Count: 1 } entities && entities[0].Kind == SketchEntityKind.Circle)
                parameters.Add(new(f.Operation.SemanticId + ".radius", f.Operation.SemanticId, ParameterKey.RevolveRadius,
                    ReadPlanarSketch(genericSketches[f.ProfileId]).DimensionsMm[entities[0].Id] / (genericSketches[f.ProfileId].Dimensions[0].Kind == ConstraintKind.Diameter ? 2 : 1), GenericFeatureCapabilities.IsQualified(f.Operation.Kind, ParameterKey.RevolveRadius)));
            if (f.Operation.Kind == ConstructionKind.CreateRevolvedBoss && genericSketches[f.ProfileId].Specification.RadialProfile is { } radial)
            {
                var values = ReadPlanarSketch(genericSketches[f.ProfileId]).DimensionsMm;
                foreach (var driver in radial.Drivers)
                    parameters.Add(new(f.Operation.SemanticId + ".radius." + driver.Id, f.Operation.SemanticId, ParameterKey.RevolveRadius,
                        values[driver.Id], !radial.Relations.Any(r => r.TargetDriver == driver.Id)));
            }
        }
        var owners = nodes.ToDictionary(n => n.Reference, n => n.SemanticId);
        var dependencies = nodes.SelectMany(n => NativeTopology.Objects<IFeature>(PersistentReferenceAdapter.Resolve<IFeature>(this, n.Reference).NativeObject!.GetParents())
            .Select(p => PersistentReferenceAdapter.Capture(this, p)).Where(owners.ContainsKey)
            .Select(p => new DependencyEdge(owners[p], n.SemanticId, DependencyKind.NativeInput))).Distinct().ToArray();
        var bodies = NativeGeometry.SolidBodies(Document);
        GenericSolidObservation? body = null;
        if (bodies.Count != 0)
        {
            if (bodies.Count != 1) throw new StateException(V03FailureCodes.UnsupportedBodyTopology, "Expected one intended solid body.");
            var mass = NativeGeometry.Doubles(bodies[0].GetMassProperties(1));
            var expectation = GenericExpectation(program);
            var points = Supports(bodies[0], expectation.Frame);
            body = new(PersistentReferenceAdapter.Capture(this, bodies[0]), mass[3] * 1e9, mass, points);
        }
        return new(DocumentIdentityAdapter.ReadForConstruction(this), revision, program, nodes,
            genericSketches.Values.Select(s => new GenericNativeSketch(s.SemanticId, s.Profile, s.Plane, s.Placement, s.Specification, s.Primitives.Select(p => p.Reference).ToArray())).ToArray(), parameters, dependencies, body);
    }
    internal void VerifyGeneric(ConstructionProgram program)
    {
        VerifyManagedHistory();
        foreach (var s in genericSketches.Values) _ = ReadPlanarSketch(s);
        foreach (var reference in genericDatums.Values)
        {
            var f = PersistentReferenceAdapter.Resolve<IFeature>(this, reference).NativeObject;
            if (f is null || f.GetErrorCode2(out var warning) != 0 || warning || f.IsSuppressed()) throw new StateException("STALE_REFERENCE", "Datum unhealthy.");
        }
        foreach (var f in genericFeatures.Values)
            if (f.Operation.Kind == ConstructionKind.ApplyFillet) VerifyGenericFillet(f, program); else VerifyDefinition(f);
        if (genericFeatures.Count == 0) return;
        var e = GenericExpectation(program); var bodies = NativeGeometry.SolidBodies(Document);
        if (bodies.Count != 1) throw new StateException(V03FailureCodes.UnsupportedBodyTopology, "Feature must retain exactly one solid body.");
        var volume = NativeGeometry.Doubles(bodies[0].GetMassProperties(1))[3] * 1e9;
        if (Math.Abs(volume - e.VolumeMm3) > Math.Max(1e-3, e.VolumeMm3 * 1e-8)) throw new StateException("GEOMETRY_INVALID", $"Material volume differs: actual={volume:R}, expected={e.VolumeMm3:R}.");
        var supports = Supports(bodies[0], e.Frame);
        var expected = new[] { e.MaximumLocalMm.X, e.MaximumLocalMm.Y, e.MaximumLocalMm.Z, e.MinimumLocalMm.X, e.MinimumLocalMm.Y, e.MinimumLocalMm.Z };
        var axes = new[] { e.Frame.XAxis, e.Frame.YAxis, e.Frame.ZAxis };
        for (var i = 0; i < 6; i++)
        {
            var value = SketchFrames.Dot(SketchFrames.Subtract(supports[i], e.Frame.OriginMm), axes[i % 3]);
            if (Math.Abs(value - expected[i]) > ContractLimits.LinearToleranceMm) throw new StateException("GEOMETRY_INVALID", $"Solid support {i} differs: actual={value:R}, expected={expected[i]:R}.");
        }
        foreach (var face in NativeTopology.Faces(bodies[0]))
        {
            var s = (ISurface)face.GetSurface();
            if (!s.IsPlane() && !s.IsCylinder() && !s.IsCone() && !s.IsTorus() && !s.IsSphere()) throw new StateException(V03FailureCodes.UnsupportedBodyTopology, "Unqualified boundary surface type.");
        }
    }
    private static V[] Supports(IBody2 body, LocalFrame frame)
    {
        var axes = new[] { frame.XAxis, frame.YAxis, frame.ZAxis };
        return Enumerable.Range(0, 6).Select(i =>
        {
            var axis = SketchFrames.Scale(axes[i % 3], i < 3 ? 1 : -1);
            if (!body.GetExtremePoint(axis.X, axis.Y, axis.Z, out var x, out var y, out var z)) throw new StateException("GEOMETRY_INVALID", "Body support point unavailable.");
            return new V(x * 1000, y * 1000, z * 1000);
        }).ToArray();
    }
    internal double NativeDepth(GenericFeatureBinding binding) => ((IExtrudeFeatureData2)ResolveGenericFeature(binding).GetDefinition()).GetDepth(true) * 1000;
    private IFeature ResolveGenericFeature(GenericFeatureBinding binding) => PersistentReferenceAdapter.Resolve<IFeature>(this, binding.Feature).NativeObject ?? throw new StateException("STALE_REFERENCE", "Feature reference unresolved.");
    private void VerifyDefinition(GenericFeatureBinding binding)
    {
        var f = ResolveGenericFeature(binding); var o = binding.Operation;
        if (f.GetErrorCode2(out var warning) != 0 || warning || f.IsSuppressed()) throw new StateException("FEATURE_REBUILD_FAILED", "Native feature unhealthy.");
        var profiles = NativeTopology.Objects<IFeature>(f.GetParents()).Where(p => p.GetTypeName2() == "ProfileFeature")
            .Select(p => PersistentReferenceAdapter.Capture(this, p)).Where(r => r != binding.AxisSketch).ToArray();
        if (profiles.Length != 1 || profiles[0] != genericSketches[binding.ProfileId].Profile)
            throw new StateException("BINDING_AMBIGUOUS", "Feature does not consume its exact unique bound profile.");
        if (o.Kind == ConstructionKind.CreateRevolvedBoss)
        {
            var data = (IRevolveFeatureData2)f.GetDefinition();
            if (data.IsThinFeature() || data.ReverseDirection != binding.NativeReverse || Math.Abs(data.GetRevolutionAngle(true) * 180 / Math.PI - o.AngleDegrees!.Value) > ContractLimits.AngularToleranceDeg)
                throw new StateException("STATE_DRIFT_DETECTED", "Revolve mode/angle/direction differs.");
            if (!data.AccessSelections(Document, null!)) throw new StateException("STALE_REFERENCE", "Revolve selections unavailable.");
            try
            {
                var axisFeature = NativePatternDirection.CanonicalFeature(this, data.Axis);
                if (PersistentReferenceAdapter.Capture(this, axisFeature) != binding.AxisDatum) throw new StateException("BINDING_AMBIGUOUS", "Revolve axis differs from its exact bound datum.");
                var line = PersistentReferenceAdapter.Resolve<ISketchLine>(this, binding.AxisLine!).NativeObject ?? throw new StateException("STALE_REFERENCE", "Revolve line unresolved.");
                var s = ((ISketchSegment)line).GetSketch(); var inv = NativeGeometry.Doubles(s.ModelToSketchTransform.IInverse().ArrayData);
                V Point(ISketchPoint p) => SketchFrames.Scale(PlanarSketchNative.Transform(new(p.X, p.Y, p.Z), inv), 1000);
                var a = Point((ISketchPoint)line.GetStartPoint2()); var b = Point((ISketchPoint)line.GetEndPoint2()); var frame = genericSketches[binding.ProfileId].Placement;
                var origin = SketchFrames.World(frame, new(0, 0)); var v = SketchFrames.Subtract(b, a); var len = Math.Sqrt(SketchFrames.Dot(v, v));
                if (len <= 0 || Math.Abs(Math.Abs(SketchFrames.Dot(SketchFrames.Scale(v, 1 / len), frame.Frame.XAxis)) - 1) > 1e-8 ||
                    Math.Abs(SketchFrames.Dot(SketchFrames.Subtract(a, origin), frame.Frame.YAxis)) > ContractLimits.LinearToleranceMm ||
                    Math.Abs(SketchFrames.Dot(SketchFrames.Subtract(a, origin), frame.Frame.ZAxis)) > ContractLimits.LinearToleranceMm) throw new StateException("SPATIAL_FRAME_MISMATCH", "Actual revolve axis geometry differs.");
                var axisParameters = NativeGeometry.Doubles(((IRefAxis)axisFeature.GetSpecificFeature2()).GetRefAxisParams());
                var axisDirection = NativePatternDirection.Vector(axisFeature);
                var axisPoint = new V(axisParameters[0] * 1000, axisParameters[1] * 1000, axisParameters[2] * 1000);
                if (Math.Abs(Math.Abs(SketchFrames.Dot(new(axisDirection[0], axisDirection[1], axisDirection[2]), frame.Frame.XAxis)) - 1) > 1e-8 ||
                    Math.Abs(SketchFrames.Dot(SketchFrames.Subtract(axisPoint, origin), frame.Frame.YAxis)) > ContractLimits.LinearToleranceMm ||
                    Math.Abs(SketchFrames.Dot(SketchFrames.Subtract(axisPoint, origin), frame.Frame.ZAxis)) > ContractLimits.LinearToleranceMm)
                    throw new StateException("SPATIAL_FRAME_MISMATCH", "Revolve datum geometry differs from the declared local axis.");
            }
            finally { data.ReleaseSelectionAccess(); }
        }
        else
        {
            var data = (IExtrudeFeatureData2)f.GetDefinition();
            var condition = o.EndCondition == EndCondition.ThroughAll ? (int)swEndConditions_e.swEndCondThroughAll : (int)swEndConditions_e.swEndCondBlind;
            if (data.IsThinFeature() || data.BothDirections || data.GetDraftWhileExtruding(true) || data.FromType != 0 || data.LinkToThickness ||
                (o.Kind == ConstructionKind.CreateExtrudedCut && data.FlipSideToCut) ||
                (data.IsBossFeature() || data.IsBaseExtrude()) != (o.Kind != ConstructionKind.CreateExtrudedCut) || data.ReverseDirection != binding.NativeReverse || data.GetEndCondition(true) != condition ||
                (o.Depth is not null && Math.Abs(data.GetDepth(true) * 1000 - o.Depth.Millimeters) > ContractLimits.LinearToleranceMm) ||
                (o.Kind == ConstructionKind.CreateAdditiveBoss && !data.Merge)) throw new StateException("STATE_DRIFT_DETECTED",
                    $"Extrusion {o.SemanticId} differs: thin={data.IsThinFeature()}, both={data.BothDirections}, draft={data.GetDraftWhileExtruding(true)}, from={data.FromType}, boss={data.IsBossFeature()}, base={data.IsBaseExtrude()}, reverse={data.ReverseDirection}/{binding.NativeReverse}, end={data.GetEndCondition(true)}/{condition}, depth={data.GetDepth(true) * 1000:R}/{o.Depth?.Millimeters:R}, merge={data.Merge}.");
        }
        _ = ReadPlanarSketch(genericSketches[binding.ProfileId]);
    }
    internal void ExecuteGenericOperation(ConstructionOperation o, ConstructionProgram program)
    {
        FeatureTrace(new("before", o.SemanticId, "native-operation"));
        if (o.Kind == ConstructionKind.CreateSketch) genericSketches.Add(o.SemanticId, CreatePlanarSketchInTransaction(o));
        else if (o.Kind == ConstructionKind.CreateDatumPlane) genericDatums.Add(o.SemanticId, CreateOffsetDatumInTransaction(o));
        else if (o.Kind == ConstructionKind.ApplyFillet) CreateGenericFillet(o, program);
        else
        {
            var sketch = genericSketches[o.Profile!.SemanticId[..^8]];
            _ = ReadPlanarSketch(sketch);
            var bodies = NativeGeometry.SolidBodies(Document);
            if (o.HostBody is not null)
            {
                if (bodies.Count != 1 || !genericFeatures.ContainsKey(o.HostBody.SemanticId[..^5]) || Resolve<IBody2>(o.HostBody) is not IBody2)
                    throw new StateException("BINDING_UNRESOLVED", "Host is not the tracked single managed body.");
            }
            else if (bodies.Count != 0) throw new StateException(V03FailureCodes.UnsupportedBodyTopology, "Initial feature cannot merge untracked material.");
            var profile = PersistentReferenceAdapter.Resolve<IFeature>(this, sketch.Profile).NativeObject!;
            var transform = NativeGeometry.Doubles(((ISketch)profile.GetSpecificFeature2()).ModelToSketchTransform.IInverse().ArrayData);
            var nativeNormal = new V(transform[6], transform[7], transform[8]);
            var direction = SketchFrames.Scale(sketch.Placement.Frame.ZAxis, o.Direction == Direction.Reverse ? -1 : 1);
            var nativeReverse = SketchFrames.Dot(nativeNormal, direction) < 0;
            NativePersistentReference? axis = null; NativePersistentReference? axisSketch = null; NativePersistentReference? axisDatum = null;
            var manager = (IFeatureManager)Document.FeatureManager; IFeature? feature;
            if (o.Kind == ConstructionKind.CreateRevolvedBoss)
            {
                (axis, axisSketch, axisDatum) = CreateGenericAxis(sketch);
                nativeReverse = o.Direction == Direction.Reverse;
                Document.ClearSelection2(true);
                profile = PersistentReferenceAdapter.Resolve<IFeature>(this, sketch.Profile).NativeObject!;
                if (!profile.Select2(false, 0) || !PersistentReferenceAdapter.Resolve<IFeature>(this, axisDatum).NativeObject!.Select2(true, 4))
                    throw new StateException("BINDING_UNRESOLVED", "Exact profile/axis datum selection failed.");
                var selection = (ISelectionMgr)Document.SelectionManager;
                FeatureTrace(new("revolve-selection", o.SemanticId, "profile mark0 / RefAxis mark4", selection.GetSelectedObjectCount2(0), selection.GetSelectedObjectCount2(4), Document.GetActiveSketch2() is null));
                feature = manager.FeatureRevolve2(true, true, false, false, nativeReverse, false, 0, 0, o.AngleDegrees!.Value * Math.PI / 180, 0, false, false, 0, 0, 0, 0, 0, false, true, true);
                FeatureTrace(new("revolve-return", o.SemanticId, "IFeatureManager.FeatureRevolve2", Success: feature is not null));
            }
            else
            {
                Document.ClearSelection2(true); profile.Select2(false, 0);
                var depth = (o.Depth?.Millimeters ?? 0) / 1000;
                if (o.Kind == ConstructionKind.CreateExtrudedCut)
                {
                    nativeReverse = !nativeReverse;
                    var end = o.EndCondition == EndCondition.ThroughAll ? (int)swEndConditions_e.swEndCondThroughAll : (int)swEndConditions_e.swEndCondBlind;
                    feature = manager.FeatureCut4(true, false, nativeReverse, end, 0, depth, 0, false, false, false, false, 0, 0, false, false, false, false, false, true, true, false, false, false, 0, 0, false, false);
                }
                else feature = manager.FeatureExtrusion3(true, false, nativeReverse, 0, 0, depth, 0, false, false, false, false, 0, 0, false, false, false, false, o.Kind == ConstructionKind.CreateAdditiveBoss, true, true, 0, 0, false);
            }
            if (feature is null) throw new StateException("GEOMETRY_INVALID", "Native feature creation returned null.");
            RebuildSketch();
            var binding = new GenericFeatureBinding(o, PersistentReferenceAdapter.Capture(this, feature), sketch.SemanticId, nativeReverse, axis, axisSketch, axisDatum);
            genericFeatures.Add(o.SemanticId, binding); Register(o.SemanticId, SemanticType.FeatureRef, o.SemanticId, feature);
            var solids = NativeGeometry.SolidBodies(Document);
            if (solids.Count != 1) throw new StateException(V03FailureCodes.UnsupportedBodyTopology, "Native result is not a single merged/retained body.");
            Register(o.SemanticId + ".body", SemanticType.BodyRef, o.SemanticId, solids[0]);
            VerifyDefinition(binding);
        }
        FeatureTrace(new("after", o.SemanticId, "native-operation", Success: true));
    }
    private (NativePersistentReference, NativePersistentReference, NativePersistentReference) CreateGenericAxis(PlanarSketchBinding profile)
    {
        var sm = (ISketchManager)Document.SketchManager;
        NativeTopology.Select(Document, PersistentReferenceAdapter.Resolve<object>(this, profile.Plane).NativeObject!, false, 0);
        sm.InsertSketch(false); var s = (ISketch)sm.ActiveSketch;
        ISketchSegment line;
        try
        {
            var m = NativeGeometry.Doubles(s.ModelToSketchTransform.ArrayData);
            V Point(double x) => PlanarSketchNative.Transform(SketchFrames.Scale(SketchFrames.World(profile.Placement, new(x, 0)), 0.001), m);
            var a = Point(-10); var b = Point(10);
            line = sm.CreateCenterLine(a.X, a.Y, a.Z, b.X, b.Y, b.Z) ?? throw new StateException("GEOMETRY_INVALID", "Axis line creation failed.");
            line.Select4(false, null!); Document.SketchAddConstraints("sgFIXED");
        }
        finally { sm.InsertSketch(false); Document.ClearSelection2(true); }
        RebuildSketch();
        var lineReference = PersistentReferenceAdapter.Capture(this, line);
        var sketchReference = PersistentReferenceAdapter.Capture(this, CenteredRectangleProfileBackend.FeatureForSketch(Document, s));
        var priorAxes = AxisFeatures().Select(f => PersistentReferenceAdapter.Capture(this, f)).ToHashSet();
        if (!PersistentReferenceAdapter.Resolve<ISketchSegment>(this, lineReference).NativeObject!.Select4(false, null!) || !Document.InsertAxis2(true))
            throw new StateException("GEOMETRY_INVALID", "Explicit local-axis datum creation failed.");
        Document.ClearSelection2(true); RebuildSketch();
        var created = AxisFeatures().Where(f => !priorAxes.Contains(PersistentReferenceAdapter.Capture(this, f))).ToArray();
        if (created.Length != 1) throw new StateException("BINDING_AMBIGUOUS", "New local-axis datum is not unique.");
        return (lineReference, sketchReference, PersistentReferenceAdapter.Capture(this, created[0]));
    }
    private IFeature[] AxisFeatures()
    {
        var axes = new List<IFeature>();
        var count = 0;
        for (var feature = (IFeature?)Document.FirstFeature(); feature is not null; feature = (IFeature?)feature.GetNextFeature())
        {
            if (++count > 256) throw new StateException("OBSERVATION_LIMIT_EXCEEDED", "Axis inventory limit.");
            if (feature.GetTypeName2() == "RefAxis") axes.Add(feature);
        }
        return axes.ToArray();
    }
    private void VerifyManagedHistory()
    {
        var equations = Document.GetEquationMgr() as IEquationMgr;
        if (Document.GetConfigurationCount() != 1 || Document.Extension.HasDesignTable() || equations is null || equations.GetCount() != 0 ||
            equations.LinkToFile || Document.ListExternalFileReferencesCount(false) != 0)
            throw new StateException("UNSUPPORTED_PARAMETER_DRIVER", "Generic managed construction requires one configuration and independent local drivers.");
        var owned = genericSketches.Values.Select(s => s.Profile).Concat(genericDatums.Values)
            .Concat(genericFeatures.Values.SelectMany(f => new[] { f.Feature, f.AxisSketch, f.AxisDatum }.OfType<NativePersistentReference>())).ToHashSet();
        var neutral = new HashSet<string>(StringComparer.Ordinal) { "HistoryFolder", "CommentsFolder", "FavoriteFolder", "SelectionSetFolder", "SensorFolder", "DocsFolder",
            "DetailCabinet", "NotesAreaFtrFolder", "SurfaceBodyFolder", "SolidBodyFolder", "MaterialFolder", "OriginProfileFeature",
            "EnvFolder", "AmbientLight", "DirectionLight", "InkMarkupFolder", "EqnFolder", "AnnotationViewFeat", "FtrFolder", "ConfigTableFolder", "NativeConfigurationTableFeature" };
        var count = 0;
        for (var f = (IFeature?)Document.FirstFeature(); f is not null; f = (IFeature?)f.GetNextFeature())
        {
            if (++count > ContractLimits.Features) throw new StateException("OBSERVATION_LIMIT_EXCEEDED", "Managed history limit.");
            var type = f.GetTypeName2();
            if (neutral.Contains(type))
            {
                if (f.GetFirstDisplayDimension() is not null || NativeTopology.Objects<IFace2>(f.GetFaces()).Any())
                    throw new StateException("UNTRACKED_NATIVE_HISTORY", "Nonphysical inventory has material or dimensions.");
                continue;
            }
            if (owned.Contains(PersistentReferenceAdapter.Capture(this, f))) continue;
            if (type == "RefPlane" && f.GetSpecificFeature2() is IRefPlane plane)
            {
                var m = NativeGeometry.Doubles(plane.Transform.ArrayData);
                if (m.Length >= 13 && Enumerable.Range(9, 3).All(i => Math.Abs(m[i]) < 1e-9)) continue;
            }
            throw new StateException("UNTRACKED_NATIVE_HISTORY", "Untracked native feature: " + type);
        }
    }
    internal void ApplyGenericEdit(GenericScalarCommand edit, ConstructionProgram preparedProgram)
    {
        var f = genericFeatures[edit.Target];
        FeatureTrace(new("before-setter", edit.Target, "qualified-scalar", edit.ExpectedOldValue, edit.Value));
        if (edit.Parameter == ParameterKey.RevolveRadius)
        {
            var s = genericSketches[f.ProfileId];
            if (s.Specification.RadialProfile is not null)
                genericSketches[f.ProfileId] = RadialSketchNative.Edit(this, s, preparedProgram.Operations.Single(o => o.SemanticId == f.ProfileId).Sketch!);
            else genericSketches[f.ProfileId] = EditSketchRadiusInTransaction(s, s.Specification.Entities.Single().Id, new(edit.ExpectedOldValue), new(edit.Value));
        }
        else SetGenericDepth(f, edit.Value);
        FeatureTrace(new("setter-return", edit.Target, "qualified-scalar", edit.ExpectedOldValue, edit.Value, true));
    }
    internal void SetGenericDepth(GenericFeatureBinding f, double value)
    {
        var feature = ResolveGenericFeature(f); var data = (IExtrudeFeatureData2)feature.GetDefinition();
        if (!data.AccessSelections(Document, null!)) throw new StateException("STALE_REFERENCE", "Extrude selections unavailable.");
        try
        {
            var old = data.GetDepth(true) * 1000;
            data.SetDepth(true, value / 1000);
            var result = feature.ModifyDefinition(data, Document, null!);
            FeatureTrace(new("native-setter-return", f.Operation.SemanticId, "IExtrudeFeatureData2/ModifyDefinition", old, NativeDepth(f), result));
            if (!result) throw new StateException("PARAMETER_NOT_APPLIED", "Native extrude depth Setter failed.");
        }
        finally { data.ReleaseSelectionAccess(); }
    }
    private sealed class GenericBackend(SolidWorksExecutionContext c) : IRequestMutationBackend<GenericConstructionState, GenericCommand, GenericPrepared, GenericRollback>
    {
        public bool RecoveryAllowed => false;
        public bool Recover(GenericPrepared p) => false;
        public GenericPrepared ResolveInputs(GenericConstructionState state, GenericCommand command)
        {
            ConstructionProgram program; ConstructionOperation[] addition;
            if (command.Program is { } requested)
            {
                ContractValidation.Program(requested); program = requested;
                ContractValidation.Require(requested.Operations.Count > state.Program.Operations.Count && requested.Operations.Take(state.Program.Operations.Count).Select(o => JsonSerializer.Serialize(o)).SequenceEqual(state.Program.Operations.Select(o => JsonSerializer.Serialize(o))), "Continuation must preserve the complete managed prefix.");
                addition = requested.Operations.Skip(state.Program.Operations.Count).ToArray();
                ContractValidation.Require(command.Candidate || addition.All(o => GenericFeatureCapabilities.IsOperationQualified(o.Kind)), "Construction family not yet native-qualified.", V03FailureCodes.CapabilityUnavailable);
                foreach (var o in requested.Operations.Where(o => o.Sketch is not null)) PlanarSketchPreflight.Prepare(o.Sketch!);
                ContractValidation.Require(command.Candidate || GenericFeatureCapabilities.RadialProfileExecutable || !addition.Any(o => o.Sketch?.RadialProfile is not null),
                    "Radial polyline drivers have not completed native qualification.", V03FailureCodes.CapabilityUnavailable);
            }
            else
            {
                var edit = command.Edit!;
                ContractValidation.Require(edit.ExpectedRevision == state.Revision && edit.Value != edit.ExpectedOldValue, "Edit revision/old value must match and change.");
                var selected = GenericParameterSelection.Resolve(state.Parameters, edit.Target, edit.Parameter, edit.ParameterSemanticId);
                var parameter = selected is not null && (selected.Qualified || command.Candidate) ? selected : null;
                ContractValidation.Require(parameter is not null && Math.Abs(parameter.Value - edit.ExpectedOldValue) <= ContractLimits.LinearToleranceMm, "Scalar not qualified or expected old value differs.");
                ContractValidation.Scalar(edit.Parameter, ScalarUnit.Millimeter, edit.Value);
                var f = c.genericFeatures[edit.Target];
                var profile = c.genericSketches[f.ProfileId].Specification;
                ContractValidation.Require(profile.RadialProfile is null || command.Candidate || GenericFeatureCapabilities.RadialProfileExecutable,
                    "Radial polyline edit remains candidate-only.", V03FailureCodes.CapabilityUnavailable);
                var revised = edit.Parameter == ParameterKey.RevolveRadius ? profile.RadialProfile is null ? RevisedCircle(profile, edit.Value) :
                    RadialProfilePolicy.Edit(profile, parameter!.SemanticId[(edit.Target.Length + ".radius.".Length)..], edit.ExpectedOldValue, edit.Value) : null;
                if (revised?.RadialProfile is not null) _ = RadialProfilePolicy.NativeOrder(profile, revised);
                program = state.Program with { Operations = state.Program.Operations.Select(o =>
                    edit.Parameter == ParameterKey.RevolveRadius && o.SemanticId == f.ProfileId ? o with { Sketch = revised } :
                    o.SemanticId == edit.Target && edit.Parameter != ParameterKey.RevolveRadius ? o with { Depth = new(edit.Value) } : o).ToArray() };
                addition = Array.Empty<ConstructionOperation>();
            }
            ContractValidation.Program(program);
            var expectation = c.GenericExpectation(program);
            ContractValidation.Require(command.Candidate || GenericFeatureCapabilities.CrossAxisExecutable || expectation.Orthogonal is null,
                "Orthogonal mixed-axis construction remains candidate-only.", V03FailureCodes.CapabilityUnavailable);
            var ids = addition.Length > 0 ? addition.Select(o => o.SemanticId).ToArray() : command.Edit!.Parameter == ParameterKey.RevolveRadius ?
                new[] { c.genericFeatures[command.Edit.Target].ProfileId, command.Edit.Target } : new[] { command.Edit.Target };
            var changedParameters = command.Edit is null ? Array.Empty<string>() : state.Parameters.Where(p => p.Owner == command.Edit.Target && p.Key == command.Edit.Parameter &&
                (command.Edit.Parameter != ParameterKey.RevolveRadius || c.genericSketches[c.genericFeatures[p.Owner].ProfileId].Specification.RadialProfile is null ||
                    program.Operations.Single(o => o.SemanticId == c.genericFeatures[p.Owner].ProfileId).Sketch!.RadialProfile!.Drivers.Any(d => p.SemanticId == p.Owner + ".radius." + d.Id &&
                        d.Radius != c.genericSketches[c.genericFeatures[p.Owner].ProfileId].Specification.RadialProfile!.Drivers.Single(old => old.Id == d.Id).Radius))).Select(p => p.SemanticId).ToArray();
            var changes = new ChangeSet(ids, changedParameters, Array.Empty<string>())
            { CreatedFeatures = addition.Select(o => o.SemanticId).ToArray(), CreatedEntities = addition.Select(o => o.SemanticId).ToArray() };
            return new(command, program, addition, expectation, changes);
        }
        private static ClosedSketch RevisedCircle(ClosedSketch sketch, double radius) => sketch with
        {
            Entities = sketch.Entities.Select(e => e with { Radius = new(radius) }).ToArray(),
            Constraints = sketch.Constraints.Select(d => d.Kind is ConstraintKind.Radius or ConstraintKind.Diameter ? d with { Dimension = new(radius * (d.Kind == ConstraintKind.Diameter ? 2 : 1)) } : d).ToArray()
        };
        public void Preflight(GenericConstructionState state, GenericPrepared p)
        {
            if (c.Document.GetActiveSketch2() is not null || c.Document.Extension.NeedsRebuild2 != 0 || c.RelationProgram is not null) throw new StateException("TRANSACTION_BUSY", "Generic construction requires its own rebuilt managed session.");
            c.VerifyGeneric(state.Program);
            var live = c.ReadGenericState(state.Program, state.Revision);
            if (!GenericConstructionStatePolicy.MatchesNativeSnapshot(state, live)) throw new StateException("STATE_DRIFT_DETECTED", "Native state differs from authoritative generic state.");
            foreach (var o in p.Addition.Where(o => o.EdgeTreatment is not null && c.genericFeatures.ContainsKey(o.EdgeTreatment.HostFeature.SemanticId)))
                c.ProbeConvexEdge(o, p.Program);
        }
        public GenericRollback CaptureRollback(GenericConstructionState state, GenericPrepared p)
        {
            var checkpoint = NativeConstructionCheckpoint.Capture(c);
            c.GenericDurableCheckpoint?.Invoke();
            return new(checkpoint, state);
        }
        public ChangeSet Execute(GenericPrepared p)
        {
            c.MutationInProgress = true;
            DocumentIdentityAdapter.EnsurePersistentIds(c);
            if (p.Command.Edit is { } edit) c.ApplyGenericEdit(edit, p.Program);
            else foreach (var operation in p.Addition) c.ExecuteGenericOperation(operation, p.Program);
            foreach (var o in p.Program.Operations)
                if (c.genericFeatures.TryGetValue(o.SemanticId, out var f)) c.genericFeatures[o.SemanticId] = f with { Operation = o };
            return p.Changes;
        }
        public bool Rebuild() => ExecutionTelemetry.Rebuild(() => c.Document.ForceRebuild3(false)) && c.Document.Extension.NeedsRebuild2 == 0;
        public void ValidatePostconditions(GenericPrepared p)
        {
            c.VerifyGeneric(p.Program);
            c.FeatureTrace(new("postconditions", "program", "full-native-material-proof", After: p.Expectation.VolumeMm3, Success: true));
        }
        public GenericConstructionState ValidateFinal(GenericConstructionState state, GenericPrepared p, ValidationScope scope) => c.ReadGenericState(p.Program, checked(state.Revision + 1));
        public void StageState(GenericPrepared p, GenericConstructionState state) { c.genericState = state; c.MutationInProgress = false; }
        public void Rollback(GenericRollback saved)
        {
            foreach (var sketch in saved.Before.Sketches.Where(s => s.Specification.RadialProfile is not null))
            {
                var owner = c.genericFeatures.Values.Single(f => f.ProfileId == sketch.SemanticId).Operation.SemanticId;
                var values = saved.Before.Parameters.Where(p => p.Owner == owner && p.Key == ParameterKey.RevolveRadius)
                    .ToDictionary(p => p.SemanticId[(owner.Length + ".radius.".Length)..], p => p.Value);
                RadialSketchNative.Restore(c, c.genericSketches[sketch.SemanticId], sketch.Specification, values);
            }
            foreach (var old in saved.Before.Parameters)
            {
                if (old.Key == ParameterKey.RevolveRadius)
                {
                    var f = c.genericFeatures[old.Owner]; var s = c.genericSketches[f.ProfileId];
                    if (s.Radial is not null) continue;
                    var d = PlanarSketchNative.Dimension(c, s.Dimensions.Single());
                    var factor = s.Dimensions[0].Kind == ConstraintKind.Diameter ? 2 : 1;
                    if (d.SetSystemValue3(old.Value * factor / 1000, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null!) != 0) throw new StateException("ROLLBACK_FAILED", "Radial restoration Setter failed.");
                }
                else if (Math.Abs(c.NativeDepth(c.genericFeatures[old.Owner]) - old.Value) > PlanarSketchPreflight.ToleranceMm) c.SetGenericDepth(c.genericFeatures[old.Owner], old.Value);
            }
            saved.Checkpoint.Restore(c);
        }
        public void ValidateRestored(GenericConstructionState state, GenericRollback saved, ValidationScope scope)
        {
            saved.Checkpoint.Verify(c); c.VerifyGeneric(state.Program);
            if (!GenericConstructionStatePolicy.MatchesNativeSnapshot(state, c.ReadGenericState(state.Program, state.Revision))) throw new StateException("ROLLBACK_FAILED", "Restored native/state association differs.");
        }
        public void Invalidate() { c.RelationContextUsable = false; c.MutationInProgress = false; }
    }
}

public static class GenericFeatureCapabilities
{
    // Qualified box-union/disjoint principal-bore and terminal convex-edge
    // domains only. Per-request material, ownership and restore proofs remain.
    public static bool CrossAxisExecutable => true;
    public static bool ConvexFilletExecutable => true;
    // Qualified orthogonal half-sections only. Native driver ownership, safe
    // intermediate order and complete material proof remain per-request gates.
    public static bool RadialProfileExecutable => true;
    // Each pair has native create/edit/readback and rollback evidence. Actual
    // profile ownership, independent driver and material proof still gate it.
    public static bool IsQualified(ConstructionKind kind, ParameterKey parameter) => (kind, parameter) is
        (ConstructionKind.CreateRevolvedBoss, ParameterKey.RevolveRadius) or
        (ConstructionKind.CreateAdditiveBoss, ParameterKey.AdditiveBossDepth) or
        (ConstructionKind.CreateExtrudedCut, ParameterKey.CutDepth);
    public static bool IsOperationQualified(ConstructionKind kind) => kind == ConstructionKind.ApplyFillet ? ConvexFilletExecutable : V03ContractCapabilities.Construction.Any(c => c.Kind == kind && c.Executable);
}
