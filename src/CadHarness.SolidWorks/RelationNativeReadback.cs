using System;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks;

internal static class RelationNativeReadback
{
    internal static void Verify(SolidWorksExecutionContext context, CadProgram expected, ValidationScope? scope = null)
    {
        var readFeatures = scope?.Relations.Select(r => r.Subject).Concat(scope.Entities.Select(context.Owner)).ToHashSet(StringComparer.Ordinal);
        var nativeOperations = expected.Operations.Select(operation =>
        {
            if (readFeatures is not null && !readFeatures.Contains(operation.SemanticId!)) return operation;
            var feature = context.DirectFeature(operation.SemanticId!);
            if (feature.GetErrorCode2(out var warning) != 0 || warning)
                throw new NativeOperationException("FEATURE_REBUILD_FAILED", "Native managed feature error or warning.");
            if (operation.Kind == OperationKind.CreateExtrude && operation.Parameter<ProfileParameter>("profile").Value is CircleProfile diskProfile)
            {
                var axis = context.DirectFace(operation.SemanticId + ".rotational_reference");
                CreateCircularPatternHandler.Reverse(axis);
                if (scope is null || scope.FullModel || scope.Parameters.Contains(operation.SemanticId + ".profile_diameter"))
                    Near(NativeGeometry.Doubles(((ISurface)axis.GetSurface()).CylinderParams)[6] * 2000, diskProfile.DiameterMm, "Native circle profile diameter differs.");
                if (scope is null || scope.FullModel || scope.Parameters.Contains(operation.SemanticId + ".extrusion_depth"))
                    Near(((IExtrudeFeatureData2)feature.GetDefinition()).GetDepth(true) * 1000, operation.Parameter<LengthParameter>("depthMm").Millimeters, "Native circle extrusion depth differs.");
            }
            if (operation.Kind == OperationKind.CreateCircularPattern)
            {
                CreateCircularPatternHandler.VerifyDefinition(context, operation, feature);
                NativeHoleInstanceVerifier.Verify(context, expected, new[] { PatternGeometry.Seed(operation) });
            }
            if (operation.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole)
            {
                var circle = NativeHoleProfile.Read(context, operation.SemanticId!);
                var parameters = operation.Parameters.ToDictionary(p => p.Key, p => p.Value);
                parameters["placement"] = new PlacementParameter(new(circle.CenterMm.X, circle.CenterMm.Y));
                if (scope is null || scope.FullModel || scope.Parameters.Contains(operation.SemanticId + ".hole_diameter"))
                    Near(circle.RadiusMeters * 2000, operation.Parameter<LengthParameter>("diameterMm").Millimeters, "Native hole diameter differs.");
                var sketch = (ISketch)context.HoleProfile(operation.SemanticId!).GetSpecificFeature2();
                var nativeHostType = 0;
                var nativeHost = sketch.GetReferenceEntity(ref nativeHostType);
                var hostReference = operation.Input("host")!.References[0];
                var expectedHost = context.DirectFace(hostReference.SemanticId);
                Check(nativeHost is IFace2 && PersistentReferenceAdapter.Capture(context, nativeHost) == PersistentReferenceAdapter.Capture(context, expectedHost), "Native hosted_on reference differs.");
                return operation with { Parameters = parameters };
            }
            if (operation.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern)
            {
                var data = (ILinearPatternFeatureData)feature.GetDefinition();
                var d = LinearPatternHandler.Dimensions(operation);
                bool Reads(EditableParameter parameter) => scope is null || scope.FullModel || scope.Parameters.Contains(operation.SemanticId + "." + WireNames.Of(parameter));
                if (operation.Kind == OperationKind.CreateLinearPattern)
                {
                    if (Reads(EditableParameter.PatternCount)) Check(data.D1TotalInstances == d.X, "Native pattern count differs.");
                    if (Reads(EditableParameter.PatternSpacing)) Near(data.D1Spacing * 1000, d.SpacingX, "Native spacing differs.");
                }
                else
                {
                    if (Reads(EditableParameter.PatternCountX)) Check((d.Swap ? 1 : data.D1TotalInstances) == operation.Parameter<CountParameter>("countX").Value, "Native X count differs.");
                    if (Reads(EditableParameter.PatternCountY)) Check((d.Swap ? data.D1TotalInstances : data.D2TotalInstances) == operation.Parameter<CountParameter>("countY").Value, "Native Y count differs.");
                    if (Reads(EditableParameter.PatternSpacingX) && !d.Swap) Near(data.D1Spacing * 1000, d.SpacingX, "Native first spacing differs.");
                    if (Reads(EditableParameter.PatternSpacingY)) Near((d.Swap ? data.D1Spacing : data.D2Spacing) * 1000, d.Swap ? d.SpacingX : d.SpacingY, "Native second spacing differs.");
                }
                Check(!data.D2PatternSeedOnly && !data.VarySketch, "Pattern is not a uniform rectangular/linear layout.");
                var seed = context.DirectFeature(operation.Input("seed")!.References[0].SemanticId);
                var seedReference = PersistentReferenceAdapter.Capture(context, seed);
                var x = context.DirectDirection(operation, d.Swap ? 1 : 0);
                var y = d.Y > 1 ? context.DirectDirection(operation, 1) : null;
                var xReference = PersistentReferenceAdapter.Capture(context, x);
                var yReference = y is not null ? PersistentReferenceAdapter.Capture(context, y) : null;
                var flipX = LinearPatternHandler.IsReversed(LinearPatternHandler.Axis(x));
                var flipY = y is not null && LinearPatternHandler.IsReversed(LinearPatternHandler.Axis(y));
                var accessed = data.AccessSelections(context.Document, null);
                if (!accessed) throw new NativeOperationException("RELATION_VIOLATED", "Cannot read native pattern references.");
                try
                {
                    var seeds = NativeTopology.Objects<object>(data.PatternFeatureArray);
                    Check(seeds.Count == 1 && PersistentReferenceAdapter.Capture(context, seeds[0]) == seedReference, "Native pattern_seed reference differs.");
                    var actualX = data.D1Axis is null ? null : PersistentReferenceAdapter.Capture(context, NativePatternDirection.CanonicalFeature(context, data.D1Axis));
                    Check(actualX == xReference && data.D1ReverseDirection == flipX,
                        $"Native first pattern direction differs. axisReferenceMatches={actualX == xReference}; reverseMatches={data.D1ReverseDirection == flipX}; actualReverse={data.D1ReverseDirection}; expectedReverse={flipX}; actualReference={actualX?.Base64}; expectedReference={xReference.Base64}");
                    if (d.Y > 1)
                    {
                        var actualY = data.D2Axis is null ? null : PersistentReferenceAdapter.Capture(context, NativePatternDirection.CanonicalFeature(context, data.D2Axis));
                        Check(actualY == yReference && data.D2ReverseDirection == flipY,
                            $"Native second pattern direction differs. axisReferenceMatches={actualY == yReference}; reverseMatches={data.D2ReverseDirection == flipY}; actualReverse={data.D2ReverseDirection}; expectedReverse={flipY}");
                    }
                    Check(NativeTopology.Objects<object>(data.SkippedItemArray).Count == 0, "Equal-spacing layout has skipped instances.");
                }
                finally { data.ReleaseSelectionAccess(); }
            }
            return operation;
        }).ToArray();
        // Solving independently read positions must not require another move.
        // Pattern counts/steps and native direction/seed references were read
        // above, so these checks establish the finite relation constraints.
        var observed = expected with { Operations = nativeOperations };
        var resolved = new DesignRelationEngine().Solve(observed).Program;
        foreach (var operation in observed.Operations.Where(o => (o.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole) &&
            (readFeatures is null || readFeatures.Contains(o.SemanticId!))))
        {
            var actual = operation.Parameter<PlacementParameter>("placement").Value;
            var solved = resolved.Operations.Single(o => o.SemanticId == operation.SemanticId).Parameter<PlacementParameter>("placement").Value;
            Near(actual.XMm, solved.XMm, "Native layout violates a centering/symmetry relation.");
            Near(actual.YMm, solved.YMm, "Native layout violates a centering/symmetry relation.");
        }
        if (context.Document.Extension.NeedsRebuild2 != 0) throw new NativeOperationException("FEATURE_REBUILD_FAILED", "Document still needs rebuild after relation readback.");
        context.Document.ClearSelection2(true);
    }
    internal static void Near(double actual, double expected, string message)
    { if (!double.IsFinite(actual) || Math.Abs(actual - expected) > GeometryMath.ToleranceMm) throw new NativeOperationException("RELATION_VIOLATED", message); }
    private static void Check(bool condition, string message)
    { if (!condition) throw new NativeOperationException("RELATION_VIOLATED", message); }
}

public sealed partial class SolidWorksExecutionContext
{
    internal IFace2 DirectFace(string id) => PersistentReferenceAdapter.Resolve<IFace2>(this, outputs[id].Reference).NativeObject ??
        throw new NativeOperationException("STALE_REFERENCE", "Host reference is stale.");
    internal IFeature DirectDirection(OperationNode operation, int axis)
    {
        var reference = LinearPatternHandler.Direction(this, operation, axis);
        if (!outputs.TryGetValue(reference.SemanticId, out var output) || output.Type != SemanticType.ReferenceAxis)
            throw new NativeOperationException("BINDING_UNRESOLVED", "No native direction output.");
        var datum = PersistentReferenceAdapter.Resolve<IFeature>(this, output.Reference).NativeObject ?? throw new NativeOperationException("STALE_REFERENCE", "Direction reference is stale.");
        NativePatternDirection.Read(datum, reference.SemanticId.EndsWith("_x", StringComparison.Ordinal) ? 0 : 1);
        return datum;
    }
}
