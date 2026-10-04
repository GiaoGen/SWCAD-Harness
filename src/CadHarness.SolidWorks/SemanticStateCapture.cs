using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

public sealed partial class SolidWorksExecutionContext
{
    internal DependencyGraph? RelationDependencies { get; set; }
    public CadState CaptureBindingState() => CaptureState(null, RelationProgram!, null, RelationRevision);

    internal CadState CaptureState(CadState? baseline, CadProgram expected, ValidationScope? scope, long revision)
    {
        CheckThread();
        if (!RelationContextUsable || RelationProgram is null || operations.Count == 0)
            throw new StateException(FailureCodes.PreconditionFailed, "No usable M5 relation construction state exists.");
        var features = new List<FeatureNode>(); var entities = new List<SemanticEntityNode>();
        var parameters = new List<ParameterNode>(); var bindings = new List<ParameterBinding>();
        foreach (var operation in expected.Operations.Where(o => o.SemanticId is not null && operations.ContainsKey(o.SemanticId)))
        {
            var id = operation.SemanticId!;
            var reference = outputs[id].Reference;
            var resolved = PersistentReferenceAdapter.Resolve<IFeature>(this, reference);
            features.Add(new(id, operation.Kind, reference, resolved.Health));
            if (resolved.NativeObject is null) continue;
            void Parameter(EditableParameter key, Func<double> read)
            {
                var parameterId = id + "." + WireNames.Of(key);
                if (scope is not null && !scope.Parameters.Contains(parameterId)) return;
                var value = read();
                if (!double.IsFinite(value) || value <= 0) throw new StateException("PARAMETER_NOT_APPLIED", "Bound native parameter is invalid.");
                parameters.Add(new(parameterId, EditableParameters.Contract(key).Kind, value));
                bindings.Add(new(parameterId, id, key));
            }
            switch (operation.Kind)
            {
                case OperationKind.CreateExtrude:
                    Parameter(EditableParameter.ExtrusionDepth, () => ((IExtrudeFeatureData2)resolved.NativeObject.GetDefinition()).GetDepth(true) * 1000); break;
                case OperationKind.CreateThroughHole:
                case OperationKind.CreateBlindHole:
                    Parameter(EditableParameter.HoleDiameter, () => NativeHoleProfile.Read(this, id).RadiusMeters * 2000);
                    if (operation.Kind == OperationKind.CreateBlindHole) Parameter(EditableParameter.BlindHoleDepth, () => ((IExtrudeFeatureData2)resolved.NativeObject.GetDefinition()).GetDepth(true) * 1000);
                    break;
                case OperationKind.CreateLinearPattern:
                case OperationKind.CreateRectangularPattern:
                    var data = (ILinearPatternFeatureData)resolved.NativeObject.GetDefinition();
                    var d = LinearPatternHandler.Dimensions(operation);
                    if (operation.Kind == OperationKind.CreateLinearPattern)
                    { Parameter(EditableParameter.PatternCount, () => data.D1TotalInstances); Parameter(EditableParameter.PatternSpacing, () => data.D1Spacing * 1000); }
                    else
                    {
                        Parameter(EditableParameter.PatternCountX, () => d.Swap ? 1 : data.D1TotalInstances);
                        Parameter(EditableParameter.PatternCountY, () => d.Swap ? data.D1TotalInstances : data.D2TotalInstances);
                        if (!d.Swap && d.SpacingX > 0) Parameter(EditableParameter.PatternSpacingX, () => data.D1Spacing * 1000);
                        if ((d.Swap ? d.SpacingX : d.SpacingY) > 0) Parameter(EditableParameter.PatternSpacingY, () => (d.Swap ? data.D1Spacing : data.D2Spacing) * 1000);
                    }
                    break;
                case OperationKind.ApplyFillet: Parameter(EditableParameter.FilletRadius, () => ((ISimpleFilletFeatureData2)resolved.NativeObject.GetDefinition()).DefaultRadius * 1000); break;
                case OperationKind.ApplyChamfer: Parameter(EditableParameter.ChamferDistance, () => ((IChamferFeatureData2)resolved.NativeObject.GetDefinition()).GetEdgeChamferDistance(0) * 1000); break;
            }
        }
        foreach (var (id, output) in outputs.Where(pair => operations.ContainsKey(pair.Value.Owner)))
        {
            if (scope is not null && !scope.Entities.Contains(id)) continue;
            var resolved = PersistentReferenceAdapter.Resolve<object>(this, output.Reference);
            SemanticGeometry? geometry = output.LogicalGeometry;
            if (resolved.NativeObject is not null && geometry is null)
            {
                if (output.Type == SemanticType.PlanarFace && resolved.NativeObject is IFace2 face && face.GetSurface() is ISurface surface && surface.IsPlane())
                {
                    var p = NativeGeometry.Doubles(surface.PlaneParams);
                    geometry = new(new(p[3] * 1000, p[4] * 1000, p[5] * 1000), new(p[0], p[1], p[2]));
                }
                else if (output.Type == SemanticType.CylindricalFace && resolved.NativeObject is IFace2 wall && wall.GetSurface() is ISurface cylinder && cylinder.IsCylinder())
                {
                    var p = NativeGeometry.Doubles(cylinder.CylinderParams);
                    geometry = new(new(p[0] * 1000, p[1] * 1000, p[2] * 1000), new(p[3], p[4], p[5]), RadiusMm: p[6] * 1000);
                }
                else if (output.Type == SemanticType.LinearEdge && resolved.NativeObject is IEdge edge && edge.GetCurve() is ICurve curve && curve.IsLine())
                {
                    var p = NativeGeometry.Doubles(curve.LineParams); var direction = LinearPatternHandler.Axis(edge);
                    if (LinearPatternHandler.IsReversed(direction)) for (var i = 0; i < 3; i++) direction[i] = -direction[i];
                    geometry = new(new(p[0] * 1000, p[1] * 1000, p[2] * 1000), new(direction[0], direction[1], direction[2]));
                }
                else if (output.Type == SemanticType.SketchProfile && holeProfiles.ContainsKey(output.Owner))
                {
                    var circle = NativeHoleProfile.Read(this, output.Owner);
                    geometry = new(circle.CenterMm, new(0, 0, 1), RadiusMm: circle.RadiusMeters * 1000);
                }
            }
            entities.Add(new(id, output.Type, output.Owner, output.Reference, resolved.Health) { Geometry = geometry });
        }
        if (baseline is not null)
        {
            var changedEntities = entities.ToDictionary(e => e.SemanticId);
            var changedParameters = parameters.ToDictionary(p => p.SemanticId);
            entities = baseline.Entities.Select(e => changedEntities.GetValueOrDefault(e.SemanticId, e)).ToList();
            parameters = baseline.Parameters.Select(p => changedParameters.GetValueOrDefault(p.SemanticId, p)).ToList();
            bindings = baseline.Bindings.ToList();
        }
        var available = entities.Select(e => e.SemanticId).ToHashSet(StringComparer.Ordinal);
        var state = new CadState
        {
            SchemaVersion = "0.2", Document = DocumentIdentityAdapter.ReadForBinding(this), Revision = revision,
            Features = features, Entities = entities, Parameters = parameters, Bindings = bindings,
            Relations = StateRelationData.Encode(expected.Relations.Where(r => operations.ContainsKey(r.Subject) && (r.Reference is null || available.Contains(r.Reference)))),
            Dependencies = StateRelationData.Encode((RelationDependencies?.Edges ?? Array.Empty<DependencyEdge>()).Where(e => operations.ContainsKey(e.Prerequisite) && operations.ContainsKey(e.Dependent)))
        };
        StateValidation.Validate(state);
        return state;
    }
    internal void BindOperationInputs(OperationNode operation)
    {
        if (RelationProgram is null) return;
        var state = CaptureBindingState();
        var binder = new SemanticEntityBinder();
        foreach (var input in operation.Inputs)
        {
            var contract = OperationRegistry.Default.Get(operation.Kind).Inputs.Single(i => i.Name == input.Name);
            foreach (var reference in input.References)
            {
                var result = binder.Bind(state, contract, new(reference.SemanticId, reference.Type,
                    outputs.TryGetValue(reference.SemanticId, out var output) ? output.Owner : null));
                if (!result.Succeeded) throw new NativeOperationException(result.FailureCode!, result.Message);
            }
        }
    }
    internal void RefreshHoleWall(string owner)
    {
        // Capture the directly produced output again after an accepted sketch
        // edit. This does not guess a replacement for a failed stored reference.
        var native = NativeTopology.Unique(NativeTopology.FeatureFaces(DirectFeature(owner)).Where(f => f.GetSurface() is ISurface surface && surface.IsCylinder()), "edited hole wall");
        outputs[owner + ".wall_face"] = new(SemanticType.CylindricalFace, owner, PersistentReferenceAdapter.Capture(this, native));
    }
}

internal sealed record NativeCircleState(Point3 CenterMm, double RadiusMeters);
internal static class NativeHoleProfile
{
    internal static ISketchArc Circle(ISketch sketch) => NativeTopology.Unique(NativeTopology.Objects<object>(sketch.GetSketchSegments())
        .OfType<ISketchArc>().Where(a => a.IsCircle() != 0), "circle sketch segment");
    internal static Point3 Transform(Point3 p, IMathTransform transform, double scale)
    {
        var m = NativeGeometry.Doubles(transform.ArrayData);
        if (!NativeTopology.Near(m[12], 1)) throw new NativeOperationException(FailureCodes.PreconditionFailed, "Unsupported sketch transform scale.");
        return new((p.X * m[0] + p.Y * m[3] + p.Z * m[6] + m[9]) * scale,
            (p.X * m[1] + p.Y * m[4] + p.Z * m[7] + m[10]) * scale,
            (p.X * m[2] + p.Y * m[5] + p.Z * m[8] + m[11]) * scale);
    }
    internal static NativeCircleState Read(SolidWorksExecutionContext context, string owner)
    {
        var sketch = (ISketch)context.HoleProfile(owner).GetSpecificFeature2();
        var arc = Circle(sketch); var center = (ISketchPoint)arc.GetCenterPoint2();
        var p = Transform(new(center.X, center.Y, center.Z), (IMathTransform)sketch.ModelToSketchTransform.Inverse(), 1000);
        return new(p, arc.GetRadius());
    }
    internal static void SetPlacement(SolidWorksExecutionContext context, string owner, Point2D placement)
    {
        var doc = context.Document; var manager = (ISketchManager)doc.SketchManager;
        var profile = context.HoleProfile(owner);
        NativeTopology.Select(doc, profile, false, 0);
        try
        {
            doc.EditSketch();
            var sketch = (ISketch?)manager.ActiveSketch ?? throw new NativeOperationException("GEOMETRY_INVALID", "Cannot edit the bound circle sketch.");
            var circle = Circle(sketch); var center = (ISketchPoint)circle.GetCenterPoint2();
            var old = new Point3(center.X, center.Y, center.Z);
            var world = Transform(old, (IMathTransform)sketch.ModelToSketchTransform.Inverse(), 1);
            var local = Transform(new(placement.XMm / 1000, placement.YMm / 1000, world.Z), sketch.ModelToSketchTransform, 1);
            if (!NativeTopology.Near(local.Z, 0) || !center.SetCoords(local.X, local.Y, 0))
                throw new NativeOperationException("PARAMETER_NOT_APPLIED", "Native circle placement was not applied.");
        }
        finally
        {
            if (manager.ActiveSketch is not null) manager.InsertSketch(false);
            doc.ClearSelection2(true);
        }
    }
}
