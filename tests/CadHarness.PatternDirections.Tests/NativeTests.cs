using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

namespace CadHarness.PatternDirections.Tests;
internal sealed record DirectionRead(string Slot, bool ReferenceMatches, bool ReverseMatches, bool ActualReverse, bool ExpectedReverse,
    NativePersistentReference ExpectedReference, NativePersistentReference ActualReference, double[] NativeVector);
internal sealed record CylinderRead(double X, double Y, double Diameter, double[] Levels);
internal sealed record GeometryRead(ExtrudeMeasurement Extents, double Volume, CylinderRead[] Cylinders);
internal static class NativeTests
{
    private static void Near(double a, double b, double tolerance = 1e-6) => Program.Check(double.IsFinite(a) && Math.Abs(a - b) < tolerance, $"Actual {a} differs from expected {b}.");
    internal static int Run(string root, string mode, string? template)
    {
        var linear = mode == "--linear"; var output = Path.Combine(root, "artifacts/milestone9e", linear ? "linear" : "rectangular"); Directory.CreateDirectory(output);
        Program.VerifyEvidence(root); Program.Check(!File.Exists(Path.Combine(output, "result.json")), "Case already attempted.");
        var definition = CaseData.All().Single(c => c.Name == (linear ? "G1" : "G5")); var program = PureTests.Stable(definition.Program!);
        var initialProgram = new DesignRelationEngine().Solve(program).Program;
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM9ETest_";
        using var budget = new NativeTestBudget(Path.Combine(root, "artifacts/milestone9e/native-budget.json"), "M9E", 3);
        SolidWorksConnection? connection = null; TestPartScope? part = null; CompositionExecutionResult? creation = null;
        ResourceSnapshot? guard = null; string? failure = null; string? revision = null; var stages = new List<object>();
        try
        {
            var runtime = SolidWorksPlanningRuntime.ForConstruction(); Program.Check(runtime.Preflight(program).IsValid, "Stable plan rejected.");
            File.WriteAllText(Path.Combine(output, "program.json"), new CadProgramJson().Serialize(program));
            File.WriteAllText(Path.Combine(output, "construction-capabilities.json"), runtime.Capabilities.ToPromptJson());
            connection = SolidWorksConnection.Connect(); revision = connection.Application.RevisionNumber(); guard = NativeResourceGuard.Inspect(connection.Application, budget);
            Program.Check(NativeResourceGuard.Evaluate(guard.Responding, guard.GdiCount, guard.OpenTestOwnedParts) is null, "Resource guard rejected Part.");
            part = new(connection.Application, budget); var context = part.Create(connection, connection.ResolvePartTemplate(template));
            var path = Path.Combine(output, "state.json"); var store = new AtomicStateStore(path); store.Commit(context.CaptureConstructionState());
            creation = new RelationBackend().Create(context, program, store);
            Program.Check(creation.Succeeded && creation.StateCommitted && creation.Operations.Count == 4 && creation.Operations.All(o => o.Succeeded),
                creation.FailureCode + ": " + creation.Message + "; rollback=" + creation.Transaction?.RollbackFailureMessage);
            var initial = store.Load(); VerifyState(context, initial); var beforeRefs = initial.Entities.Where(e => e.SemanticId.Contains(".direction_", StringComparison.Ordinal)).ToDictionary(e => e.SemanticId, e => e.NativeReference);
            var geometry = Observe(context); Program.Write(Path.Combine(output, "initial-observation.json"), geometry); VerifyGeometry(geometry, initialProgram, definition.Expected!);
            var directions = ReadDirections(context, initial, initialProgram); Program.Check(directions.All(d => d.ReferenceMatches && d.ReverseMatches), "Initial native direction mismatch.");
            stages.Add(new { Name = "finalized construction", Directions = directions, Geometry = geometry });
            File.Copy(path, Path.Combine(output, "initial-state.json"));
            var catalog = SolidWorksPlanningRuntime.ForEdit(context, initial).Capabilities;
            File.WriteAllText(Path.Combine(output, "edit-capabilities.json"), catalog.ToPromptJson());
            var spacingParameter = linear ? EditableParameter.PatternSpacing : EditableParameter.PatternSpacingX;
            var countParameter = linear ? EditableParameter.PatternCount : EditableParameter.PatternCountX;
            var values = new[] { (spacingParameter, linear ? 24.0 : 30.0), (countParameter, 3.0), (countParameter, 2.0), (spacingParameter, linear ? 40.0 : 50.0) };
            var current = initialProgram;
            foreach (var (parameter, value) in values)
            {
                var state = store.Load(); var projection = SolidWorksPlanningRuntime.ForEdit(context, state);
                Program.Check(projection.Capabilities.ParameterEdits.Any(c => c.Target == "layout" && c.Parameter == parameter), "Edit projection omitted supported datum-backed pattern edit.");
                var edit = CaseData.Edit("layout", parameter, value); Program.Check(projection.Preflight(new("0.2", new[] { edit }, Array.Empty<DesignRelation>())).IsValid, "Projected edit cannot preflight.");
                var proposed = new DesignRelationEngine().Solve(ParameterMutationRegistry.Default.ApplyProgram(current, edit)).Program;
                var adapter = new TransactionalParameterBackend(context); var result = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, adapter).Execute(edit);
                Program.Check(result.Succeeded && result.StateCommitted, result.FailureCode + ": " + result.Message + "; rollback=" + result.RollbackFailureMessage);
                current = proposed; state = store.Load(); VerifyState(context, state);
                Program.Check(beforeRefs.All(pair => state.Entities.Single(e => e.SemanticId == pair.Key).NativeReference == pair.Value), "Stable datum identity changed across pattern edit.");
                geometry = Observe(context); Program.Write(Path.Combine(output, "observation-" + stages.Count + ".json"), geometry); VerifyGeometry(geometry, current, definition.Expected!);
                directions = ReadDirections(context, state, current); Program.Check(directions.All(d => d.ReferenceMatches && d.ReverseMatches), "Edited directions differ.");
                stages.Add(new { Name = parameter + "->" + value, Transaction = result, Directions = directions, Geometry = geometry, Reads = adapter.ValidationReads.ToArray() });
            }
            // Real failed append verifies generic rollback also preserves the new datum outputs.
            var bytes = File.ReadAllBytes(path); var original = Observe(context); var snapshot = JsonSerializer.Serialize(context.CaptureBindingState());
            var addition = new CadProgram("0.2", new[] { CaseData.Hole("extra", 3, 0, -18) }, new[] { new DesignRelation(RelationKind.HostedOn, "extra", "stock.top_face") });
            CompositionExecutionResult rejected;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) rejected = new RelationBackend().Create(context, addition, store);
            Program.Check(rejected.FailureCode == "STATE_COMMIT_FAILED" && rejected.RollbackSucceeded && !rejected.StateCommitted && bytes.SequenceEqual(File.ReadAllBytes(path)) &&
                snapshot == JsonSerializer.Serialize(context.CaptureBindingState()), "Datum-backed construction rollback failed: " + rejected.Message + "; " + rejected.Transaction?.RollbackFailureMessage);
            geometry = Observe(context); Near(geometry.Volume, original.Volume, 0.001); VerifyGeometry(geometry, current, definition.Expected!);
            VerifyState(context, store.Load()); directions = ReadDirections(context, store.Load(), current);
            Program.Check(directions.All(d => d.ReferenceMatches && d.ReverseMatches) && beforeRefs.All(pair => store.Load().Entities.Single(e => e.SemanticId == pair.Key).NativeReference == pair.Value), "Rollback changed datum axes.");
            stages.Add(new { Name = "native append atomic failure rollback", Transaction = rejected, Directions = directions, Geometry = geometry });
        }
        catch (Exception e) { failure = e.Message; }
        finally
        {
            part?.Cleanup(); connection?.Dispose(); Program.VerifyEvidence(root);
            Program.Write(Path.Combine(output, "result.json"), new { Status = failure is null && part?.CleanupError is null ? "COMPLETE" : "BLOCKED", Failure = failure,
                SolidWorksRevision = revision, ResourceGuard = guard, Creation = creation, Stages = stages,
                PartsCreated = part?.Created == true ? 1 : 0, PartsClosed = part?.Closed == true ? 1 : 0, OriginalActiveRestored = part?.OriginalActiveRestored,
                CleanupError = part?.CleanupError, Budget = budget.Snapshot, M9DEvidenceUnchanged = true });
            Console.WriteLine(mode + " " + (failure ?? "COMPLETE") + "; Parts " + (part?.Created == true ? 1 : 0) + "/" + (part?.Closed == true ? 1 : 0));
        }
        return failure is null && part?.CleanupError is null ? 0 : 1;
    }
    private static void VerifyState(SolidWorksExecutionContext context, CadState state)
    {
        StateValidation.Validate(state);
        foreach (var entity in state.Entities)
        {
            var binding = new SemanticEntityBinder().Bind(state, new InputContract("verify", null, new[] { entity.Type }), new(entity.SemanticId, entity.Type, entity.OwnerFeatureSemanticId));
            if (entity.ReferenceHealth != ReferenceHealth.Healthy) { Program.Check(!binding.Succeeded && entity.Type == SemanticType.LinearEdge, "Consumed edge health is incorrect."); continue; }
            Program.Check(binding.Succeeded && PersistentReferenceAdapter.Resolve<object>(context, entity.NativeReference).Health == ReferenceHealth.Healthy, "Persisted binding failed: " + entity.SemanticId);
            if (entity.SemanticId.Contains(".direction_", StringComparison.Ordinal))
            {
                var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, entity.NativeReference).NativeObject;
                Program.Check(entity.Type == SemanticType.ReferenceAxis && feature?.GetSpecificFeature2() is IRefAxis && entity.Geometry?.Direction is not null, "Direction is not a real typed native datum.");
            }
        }
    }
    private static DirectionRead[] ReadDirections(SolidWorksExecutionContext context, CadState state, CadProgram program)
    {
        var op = program.Operations.Single(o => o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern);
        var feature = (IFeature)context.NativeFeature(op.SemanticId!); var data = (ILinearPatternFeatureData)feature.GetDefinition();
        var linear = op.Kind == OperationKind.CreateLinearPattern; var x = linear ? op.Parameter<CountParameter>("count").Value : op.Parameter<CountParameter>("countX").Value;
        var y = linear ? 1 : op.Parameter<CountParameter>("countY").Value;
        Program.Check(data.D1TotalInstances == x && data.D2TotalInstances == y, "Native counts differ.");
        Near(data.D1Spacing * 1000, op.Parameter<LengthParameter>(linear ? "spacingMm" : "spacingXMm").Millimeters);
        if (!linear) Near(data.D2Spacing * 1000, op.Parameter<LengthParameter>("spacingYMm").Millimeters);
        var results = new List<DirectionRead>(); Program.Check(data.AccessSelections(context.Document, null), "Cannot access native pattern selection data.");
        try
        {
            for (var axis = 0; axis < (linear ? 1 : 2); axis++)
            {
                var native = axis == 0 ? data.D1Axis : data.D2Axis;
                var expected = state.Entities.Single(e => e.SemanticId == "stock.direction_" + (axis == 0 ? "x" : "y"));
                var expectedFeature = PersistentReferenceAdapter.Resolve<IFeature>(context, expected.NativeReference).NativeObject!;
                var axisFeature = Canonical(context, native); var actualReference = PersistentReferenceAdapter.Capture(context, axisFeature);
                var p = (double[])((IRefAxis)expectedFeature.GetSpecificFeature2()).GetRefAxisParams();
                var v = new[] { p[3] - p[0], p[4] - p[1], p[5] - p[2] }; var length = Math.Sqrt(v.Sum(a => a * a)); v = v.Select(a => a / length).ToArray();
                for (var i = 0; i < 3; i++) Near(Math.Abs(v[i]), i == axis ? 1 : 0);
                var reverse = axis == 0 ? data.D1ReverseDirection : data.D2ReverseDirection; var expectedReverse = v[axis] < 0;
                results.Add(new(axis == 0 ? "D1" : "D2", actualReference == expected.NativeReference, reverse == expectedReverse, reverse, expectedReverse, expected.NativeReference, actualReference, v));
            }
        }
        finally { data.ReleaseSelectionAccess(); context.Document.ClearSelection2(true); }
        return results.ToArray();
    }
    private static IFeature Canonical(SolidWorksExecutionContext context, object native)
    {
        if (native is IFeature feature) return feature;
        var list = new List<IFeature>(); var next = (IFeature?)context.Document.FirstFeature();
        for (var i = 0; next is not null && i < 256; i++, next = (IFeature?)next.GetNextFeature())
            if (next.GetTypeName2() == "RefAxis" && Same(next.GetSpecificFeature2(), native)) list.Add(next);
        Program.Check(list.Count == 1, "Native datum owner is not unique."); return list[0];
    }
    private static bool Same(object a, object b)
    {
        var x = Marshal.GetIUnknownForObject(a); var y = Marshal.GetIUnknownForObject(b);
        try { return x == y; } finally { Marshal.Release(x); Marshal.Release(y); }
    }
    private static GeometryRead Observe(SolidWorksExecutionContext context)
    {
        var body = ((Array)((IPartDoc)context.Document).GetBodies2(0, false)).Cast<object>().Cast<IBody2>().Single();
        var cylinders = ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>().Where(f => ((ISurface)f.GetSurface()).IsCylinder()).Select(f =>
        {
            var c = (double[])((ISurface)f.GetSurface()).CylinderParams;
            var levels = ((Array)f.GetEdges()).Cast<object>().Cast<IEdge>().Select(e => (ICurve)e.GetCurve()).Where(curve => curve.IsCircle())
                .Select(curve => ((double[])curve.CircleParams)[2] * 1000).OrderBy(z => z).ToArray();
            return new CylinderRead(c[0] * 1000, c[1] * 1000, c[6] * 2000, levels);
        }).ToArray();
        return new(ExtrudeMeasurementReader.Read(context), ((double[])body.GetMassProperties(1))[3] * 1e9, cylinders);
    }
    private static void VerifyGeometry(GeometryRead geometry, CadProgram program, ExpectedModel nominal)
    {
        var layout = new RelationContextAccessor(program).Positions;
        var diameter = program.Operations.Single(o => o.SemanticId == "seed").Parameter<LengthParameter>("diameterMm").Millimeters;
        Near(geometry.Extents.WidthMm, nominal.Width); Near(geometry.Extents.HeightMm, nominal.Height); Near(geometry.Extents.DepthMm, nominal.Thickness);
        Near(geometry.Volume, (nominal.Width * nominal.Height - layout.Length * Math.PI * diameter * diameter / 4 - (4 - Math.PI) * nominal.Fillet * nominal.Fillet - 2 * nominal.Chamfer * nominal.Chamfer) * nominal.Thickness, 0.001);
        Program.Check(geometry.Cylinders.Length == layout.Length + (nominal.Fillet > 0 ? 4 : 0), "Native cylinder count differs.");
        foreach (var position in layout)
        {
            var hole = geometry.Cylinders.Single(c => Math.Abs(c.X - position.XMm) < 1e-6 && Math.Abs(c.Y - position.YMm) < 1e-6 && Math.Abs(c.Diameter - diameter) < 1e-6);
            Program.Check(hole.Levels.Length == 2, "Through hole boundaries missing."); Near(hole.Levels[0], 0); Near(hole.Levels[1], nominal.Thickness);
        }
    }
    // Independent arithmetic, avoiding production PatternGeometry as the oracle.
    private sealed class RelationContextAccessor
    {
        internal Point2D[] Positions { get; }
        internal RelationContextAccessor(CadProgram program)
        {
            var p = program.Operations.Single(o => o.SemanticId == "layout"); var linear = p.Kind == OperationKind.CreateLinearPattern;
            var nx = p.Parameter<CountParameter>(linear ? "count" : "countX").Value; var ny = linear ? 1 : p.Parameter<CountParameter>("countY").Value;
            var sx = p.Parameter<LengthParameter>(linear ? "spacingMm" : "spacingXMm").Millimeters; var sy = linear ? 0 : p.Parameter<LengthParameter>("spacingYMm").Millimeters;
            Positions = Enumerable.Range(0, nx).SelectMany(x => Enumerable.Range(0, ny).Select(y => new Point2D((x - (nx - 1) / 2.0) * sx, (y - (ny - 1) / 2.0) * sy))).ToArray();
        }
    }
}
