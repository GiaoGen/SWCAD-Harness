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

namespace CadHarness.GeneralizationRevalidation.Tests;
internal sealed record DirectionRead(string Slot, int Count, double SpacingMm, bool ReferenceMatches, bool ReverseMatches,
    bool ActualReverse, bool ExpectedReverse, NativePersistentReference ExpectedReference, NativePersistentReference ActualReference, double[] NativeVector, double[] AxisEndpointsMeters);
internal sealed record CylinderRead(double X, double Y, double Diameter, double[] Levels);
internal sealed record GeometryRead(ExtrudeMeasurement Extents, double Volume, CylinderRead[] Cylinders);
internal sealed class CaseReport
{
    public string Case { get; set; } = "";
    public string Status { get; set; } = "BLOCKED";
    public string Planned { get; set; } = "NOT_RUN";
    public string Executed { get; set; } = "NOT_RUN";
    public string StrictReadback { get; set; } = "NOT_RUN";
    public string FinalGeometry { get; set; } = "NOT_RUN";
    public string PersistentState { get; set; } = "NOT_RUN";
    public string CapabilityProjection { get; set; } = "NOT_RUN";
    public string Editable { get; set; } = "NOT_RUN";
    public string RollbackSafe { get; set; } = "NOT_RUN";
    public int ProductionCodeChanges { get; set; }
    public bool ProductionAndHistoricalEvidenceUnchanged { get; set; }
    public CompositionExecutionResult? Construction { get; set; }
    public List<object> Checks { get; } = new();
    public ResourceSnapshot? ResourceGuard { get; set; }
    public string? SolidWorksRevision { get; set; }
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public string? CleanupError { get; set; }
}
internal static class NativeTests
{
    private static void Near(double a, double b, double tolerance = 1e-6) => Program.Check(double.IsFinite(a) && Math.Abs(a - b) <= tolerance, $"Actual {a} differs from expected {b}.");
    internal static int Run(string root, string name, string? template)
    {
        Program.VerifyFreeze(root);
        var output = Path.Combine(Program.Output(root), name); var resultPath = Path.Combine(output, "result.json");
        Program.Check(!File.Exists(resultPath) && !File.Exists(Path.Combine(output, "state.json")), "Case already attempted; no native retry.");
        var definition = CaseData.All().Single(c => c.Name == name); var program = Program.Stable(definition.Program!);
        Program.Check(new CadProgramJson().Serialize(program) == File.ReadAllText(Path.Combine(output, "program.json")), "Prepared plan changed.");
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM9FTest_";
        using var budget = new NativeTestBudget(Path.Combine(Program.Output(root), "native-budget.json"), "M9F", 3);
        var report = new CaseReport { Case = name };
        SolidWorksConnection? connection = null; TestPartScope? part = null;
        try
        {
            var runtime = SolidWorksPlanningRuntime.ForConstruction(); var plan = Program.Plan(definition.Intent, program, runtime);
            Program.Check(plan.Succeeded && plan.Program is not null && runtime.Preflight(plan.Program).IsValid, "Production Planner rejected: " + plan.Message);
            report.Planned = "PASS"; Program.Write(Path.Combine(output, "native-plan.json"), plan);
            connection = SolidWorksConnection.Connect(); report.SolidWorksRevision = connection.Application.RevisionNumber();
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget);
            Program.Check(NativeResourceGuard.Evaluate(report.ResourceGuard.Responding, report.ResourceGuard.GdiCount, report.ResourceGuard.OpenTestOwnedParts) is null, "Resource guard rejected Part.");
            part = new(connection.Application, budget); var context = part.Create(connection, connection.ResolvePartTemplate(template));
            var path = Path.Combine(output, "state.json"); var store = new AtomicStateStore(path); store.Commit(context.CaptureConstructionState());
            report.Construction = new RelationBackend().Create(context, plan.Program!, store);
            var creation = report.Construction;
            Program.Check(creation.Succeeded && creation.MutationStarted && creation.StateCommitted && creation.Operations.Count == 4 && creation.Operations.All(o => o.Succeeded),
                creation.FailureCode + ": " + creation.Message + "; rollback=" + creation.Transaction?.RollbackFailureMessage);
            report.Executed = "PASS";
            var solved = new DesignRelationEngine().Solve(program).Program; var state = store.Load();
            Program.Check(state.Revision == 1, "Construction did not commit exactly revision 1."); VerifyState(context, state, solved); report.PersistentState = "PASS";
            var geometry = Observe(context); VerifyGeometry(context, geometry, definition.Expected!); report.FinalGeometry = "PASS";
            var directions = ReadDirections(context, state, solved); Program.Check(directions.All(d => d.ReferenceMatches && d.ReverseMatches), "Final direction mismatch."); report.StrictReadback = "PASS";
            Program.Write(Path.Combine(output, "final-observation.json"), geometry);
            Program.Write(Path.Combine(output, "strict-readback.json"), directions); File.Copy(path, Path.Combine(output, "committed-state.json"));
            report.Checks.Add(new { Name = "four-operation nominal construction", Directions = directions, Geometry = geometry, Revision = state.Revision });
            AuditProjection(context, store, solved, output, report);
            // Failure injection is in the test's file-sharing boundary. Production
            // construction and rollback run without mocks or case-specific logic.
            var bytes = File.ReadAllBytes(path); var before = Observe(context); var signature = ModelSignature(context);
            var session = JsonSerializer.Serialize(context.CaptureConstructionState());
            var references = state.Entities.Where(e => e.Type == SemanticType.ReferenceAxis).ToDictionary(e => e.SemanticId, e => e.NativeReference);
            var addition = new CadProgram("0.2", new[] { CaseData.Hole("probe", 3, 0, -18) }, new[] { new DesignRelation(RelationKind.HostedOn, "probe", "stock.top_face") });
            CompositionExecutionResult rejected;
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) rejected = new RelationBackend().Create(context, addition, store);
            var after = Observe(context); var afterSignature = ModelSignature(context); var finalBytes = File.ReadAllBytes(path);
            var modelUnchanged = signature == afterSignature && SameGeometry(before, after);
            var fileUnchanged = bytes.SequenceEqual(finalBytes); var sessionUnchanged = session == JsonSerializer.Serialize(context.CaptureConstructionState());
            var evidence = new { Name = "appended native hole then real atomic commit failure rollback", Result = rejected, Before = before, After = after,
                BeforeModelSignature = signature, AfterModelSignature = afterSignature, StateBeforeSha256 = Program.Hash(bytes), StateAfterSha256 = Program.Hash(finalBytes),
                ModelUnchanged = modelUnchanged, FileUnchanged = fileUnchanged, SessionUnchanged = sessionUnchanged };
            Program.Write(Path.Combine(output, "rollback.json"), evidence); report.Checks.Add(evidence);
            Program.Check(!rejected.Succeeded && rejected.FailureCode == "STATE_COMMIT_FAILED" && rejected.MutationStarted && rejected.RollbackAttempted && rejected.RollbackSucceeded && !rejected.StateCommitted &&
                rejected.Operations.Count == 1 && rejected.Operations[0].Succeeded && modelUnchanged && fileUnchanged && sessionUnchanged,
                "Atomic construction rollback failed: " + rejected.Message + "; " + rejected.Transaction?.RollbackFailureMessage);
            state = store.Load(); VerifyState(context, state, solved); VerifyGeometry(context, after, definition.Expected!);
            directions = ReadDirections(context, state, solved);
            Program.Check(directions.All(d => d.ReferenceMatches && d.ReverseMatches) && references.All(r => state.Entities.Single(e => e.SemanticId == r.Key).NativeReference == r.Value), "Rollback changed native direction identities.");
            Program.Check(state.Revision == 1 && Directory.GetFiles(output, ".state.json.*.tmp").Length == 0, "Rollback changed committed revision or leaked temp state.");
            Program.Write(Path.Combine(output, "post-rollback-readback.json"), directions); report.RollbackSafe = "PASS"; report.Status = "PASS";
        }
        catch (Exception e)
        {
            report.Status = "BLOCKED"; report.FailureCode = e is ICadFailure f ? f.Code : "REVALIDATION_FAILED"; report.Message = e.Message;
        }
        finally
        {
            part?.Cleanup(); report.PartsCreated = part?.Created == true ? 1 : 0; report.PartsClosed = part?.Closed == true ? 1 : 0;
            report.OriginalActiveRestored = part?.OriginalActiveRestored == true; report.CleanupError = part?.CleanupError;
            if (report.CleanupError is not null) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            try { connection?.Dispose(); } catch (Exception e) { report.Status = "BLOCKED"; report.CleanupError = e.Message; }
            try { Program.VerifyFreeze(root); report.ProductionAndHistoricalEvidenceUnchanged = true; } catch (Exception e) { report.Status = "BLOCKED"; report.Message = e.Message; }
            Program.Write(resultPath, report);
            Console.WriteLine(JsonSerializer.Serialize(new { report.Case, report.Status, report.Planned, report.Executed, report.StrictReadback, report.FinalGeometry,
                report.PersistentState, report.CapabilityProjection, report.RollbackSafe, report.FailureCode, report.Message, report.PartsCreated, report.PartsClosed, report.CleanupError }, Program.Json));
        }
        return report.Status == "PASS" ? 0 : 1;
    }
    private static void AuditProjection(SolidWorksExecutionContext context, AtomicStateStore store, CadProgram program, string output, CaseReport report)
    {
        var state = store.Load(); var runtime = SolidWorksPlanningRuntime.ForEdit(context, state);
        File.WriteAllText(Path.Combine(output, "edit-capabilities.json"), runtime.Capabilities.ToPromptJson()); var checks = new List<object>();
        foreach (var capability in runtime.Capabilities.ParameterEdits)
        {
            var owner = program.Operations.Single(o => o.SemanticId == capability.Target);
            Program.Check(ParameterMutationRegistry.Default.TryGet(owner, capability.Parameter, out var handler), "Catalog offers missing native handler.");
            var value = handler.Read(context, owner, capability.Parameter); Near(value, ParameterMutationRegistry.Default.Expected(owner, capability.Parameter));
            var edit = CaseData.Edit(capability.Target, capability.Parameter, value);
            var editProgram = new CadProgram("0.2", new[] { edit }, Array.Empty<DesignRelation>());
            var plan = Program.Plan("Audit " + capability.Target + "/" + capability.Parameter, editProgram, runtime); Program.Check(plan.Succeeded, "Projected edit rejected by Planner.");
            var adapter = new TransactionalParameterBackend(context); var prepared = adapter.ResolveInputs(state, edit);
            adapter.Preflight(state, prepared); adapter.CaptureRollback(state, prepared);
            checks.Add(new { capability.Target, capability.Parameter, NativeValue = value, Handler = handler.GetType().Name, PlannerAccepted = true,
                Registered = true, NativeReadable = true, PreflightExecutable = true, RollbackCapturable = true });
        }
        var layout = program.Operations.Single(o => o.SemanticId == "layout");
        var expected = layout.Kind == OperationKind.CreateLinearPattern ? new[] { EditableParameter.PatternCount, EditableParameter.PatternSpacing } :
            new[] { EditableParameter.PatternCountX, EditableParameter.PatternCountY, EditableParameter.PatternSpacingX, EditableParameter.PatternSpacingY };
        Program.Check(runtime.Capabilities.ParameterEdits.Where(c => c.Target == "layout").Select(c => c.Parameter).ToHashSet().SetEquals(expected), "Active datum-backed pattern edit projection incomplete.");
        var hidden = state.Bindings.Where(b => !runtime.Capabilities.ParameterEdits.Any(p => p.Target == b.OwnerFeatureSemanticId && p.Parameter == b.Parameter)).ToArray();
        foreach (var binding in hidden)
        {
            var value = state.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value;
            Program.Check(!runtime.Capabilities.Validate(new("0.2", new[] { CaseData.Edit(binding.OwnerFeatureSemanticId, binding.Parameter, value) }, Array.Empty<DesignRelation>())).IsValid, "Hidden bound edit leaked into projection.");
        }
        Program.Write(Path.Combine(output, "projection-audit.json"), new { Visible = checks, Hidden = hidden,
            Scope = "Native read, production Planner, resolve, preflight and rollback capture only; nominal geometry is not edited" });
        report.CapabilityProjection = "PASS"; report.Editable = "PASS_READ_PREFLIGHT_CAPTURE_ONLY";
    }
    private static void VerifyState(SolidWorksExecutionContext context, CadState state, CadProgram program)
    {
        StateValidation.Validate(state); Program.Check(state.Features.Count == 4 && state.Revision == 1, "Managed feature/revision differs.");
        Program.Check(StateRelationData.Relations(state).ToHashSet().SetEquals(program.Relations), "Persisted relations differ.");
        Program.Check(StateRelationData.Dependencies(state).ToHashSet().SetEquals(new DesignRelationEngine().Solve(program).Dependencies.Edges), "Persisted dependencies differ.");
        foreach (var feature in state.Features) Program.Check(PersistentReferenceAdapter.Resolve<IFeature>(context, feature.NativeReference).Health == ReferenceHealth.Healthy, "Feature persistent reference failed.");
        foreach (var binding in state.Bindings) Near(state.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value,
            ParameterMutationRegistry.Default.Expected(program.Operations.Single(o => o.SemanticId == binding.OwnerFeatureSemanticId), binding.Parameter));
        var consumed = program.Operations.Single(o => o.SemanticId == "corners").Input("edges")!.References.Select(r => r.SemanticId).ToHashSet();
        foreach (var entity in state.Entities)
        {
            var binding = new SemanticEntityBinder().Bind(state, new InputContract("verify", null, new[] { entity.Type }), new(entity.SemanticId, entity.Type, entity.OwnerFeatureSemanticId));
            if (entity.ReferenceHealth != ReferenceHealth.Healthy)
            { Program.Check(!binding.Succeeded && entity.Type == SemanticType.LinearEdge && consumed.Contains(entity.SemanticId), "Unexpected unavailable entity."); continue; }
            Program.Check(binding.Succeeded && PersistentReferenceAdapter.Resolve<object>(context, entity.NativeReference).Health == ReferenceHealth.Healthy, "Persisted binding failed: " + entity.SemanticId);
            if (entity.Type == SemanticType.ReferenceAxis)
            {
                var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, entity.NativeReference).NativeObject;
                Program.Check(feature?.GetSpecificFeature2() is IRefAxis && entity.Geometry?.Direction is not null, "Direction is not a real native datum.");
            }
        }
    }
    private static DirectionRead[] ReadDirections(SolidWorksExecutionContext context, CadState state, CadProgram program)
    {
        var op = program.Operations.Single(o => o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern);
        var data = (ILinearPatternFeatureData)((IFeature)context.NativeFeature(op.SemanticId!)).GetDefinition();
        var linear = op.Kind == OperationKind.CreateLinearPattern;
        Program.Check(data.D1TotalInstances == op.Parameter<CountParameter>(linear ? "count" : "countX").Value && data.D2TotalInstances == (linear ? 1 : op.Parameter<CountParameter>("countY").Value), "Native counts differ.");
        Near(data.D1Spacing * 1000, op.Parameter<LengthParameter>(linear ? "spacingMm" : "spacingXMm").Millimeters);
        if (!linear) Near(data.D2Spacing * 1000, op.Parameter<LengthParameter>("spacingYMm").Millimeters);
        Program.Check(!data.D2PatternSeedOnly && !data.VarySketch, "Native pattern mode differs.");
        var results = new List<DirectionRead>(); Program.Check(data.AccessSelections(context.Document, null), "Cannot access native selections.");
        try
        {
            Program.Check(data.PatternFeatureArray is Array seeds && seeds.Length == 1 && PersistentReferenceAdapter.Capture(context, seeds.GetValue(0)!) == state.Features.Single(f => f.SemanticId == "seed").NativeReference, "Native pattern seed reference differs.");
            Program.Check(data.SkippedItemArray is not Array skipped || skipped.Length == 0, "Pattern skips native instances.");
            for (var axis = 0; axis < (linear ? 1 : 2); axis++)
            {
                var expected = state.Entities.Single(e => e.SemanticId == "stock.direction_" + (axis == 0 ? "x" : "y"));
                var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, expected.NativeReference).NativeObject!;
                var actual = PersistentReferenceAdapter.Capture(context, Canonical(context, axis == 0 ? data.D1Axis : data.D2Axis));
                var p = (double[])((IRefAxis)feature.GetSpecificFeature2()).GetRefAxisParams();
                var v = new[] { p[3] - p[0], p[4] - p[1], p[5] - p[2] }; var length = Math.Sqrt(v.Sum(a => a * a)); Program.Check(length > 1e-12, "Degenerate native datum."); v = v.Select(a => a / length).ToArray();
                for (var i = 0; i < 3; i++) { Near(Math.Abs(v[i]), i == axis ? 1 : 0); if (i != axis) { Near(p[i] * 1000, 0); Near(p[i + 3] * 1000, 0); } }
                var reverse = axis == 0 ? data.D1ReverseDirection : data.D2ReverseDirection; var expectedReverse = v[axis] < 0;
                results.Add(new(axis == 0 ? "D1" : "D2", axis == 0 ? data.D1TotalInstances : data.D2TotalInstances,
                    (axis == 0 ? data.D1Spacing : data.D2Spacing) * 1000, actual == expected.NativeReference, reverse == expectedReverse, reverse, expectedReverse, expected.NativeReference, actual, v, p));
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
        Program.Check(context.Document.Extension.NeedsRebuild2 == 0, "Document still requires rebuild.");
        var body = ((Array)((IPartDoc)context.Document).GetBodies2(0, false)).Cast<object>().Cast<IBody2>().Single();
        var cylinders = ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>().Where(f => ((ISurface)f.GetSurface()).IsCylinder()).Select(f =>
        {
            var c = (double[])((ISurface)f.GetSurface()).CylinderParams; Near(Math.Abs(c[5]), 1);
            var levels = ((Array)f.GetEdges()).Cast<object>().Cast<IEdge>().Select(e => (ICurve)e.GetCurve()).Where(curve => curve.IsCircle())
                .Select(curve => ((double[])curve.CircleParams)[2] * 1000).OrderBy(z => z).ToArray();
            return new CylinderRead(c[0] * 1000, c[1] * 1000, c[6] * 2000, levels);
        }).OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Diameter).ToArray();
        return new(ExtrudeMeasurementReader.Read(context), ((double[])body.GetMassProperties(1))[3] * 1e9, cylinders);
    }
    internal static bool SameGeometry(GeometryRead a, GeometryRead b) =>
        a.Extents.SolidBodyCount == b.Extents.SolidBodyCount && Math.Abs(a.Extents.WidthMm - b.Extents.WidthMm) <= 1e-6 &&
        Math.Abs(a.Extents.HeightMm - b.Extents.HeightMm) <= 1e-6 && Math.Abs(a.Extents.DepthMm - b.Extents.DepthMm) <= 1e-6 &&
        Math.Abs(a.Volume - b.Volume) <= 0.001 && a.Cylinders.Length == b.Cylinders.Length && a.Cylinders.Zip(b.Cylinders).All(pair =>
            Math.Abs(pair.First.X - pair.Second.X) <= 1e-6 && Math.Abs(pair.First.Y - pair.Second.Y) <= 1e-6 && Math.Abs(pair.First.Diameter - pair.Second.Diameter) <= 1e-6 &&
            pair.First.Levels.Length == pair.Second.Levels.Length && pair.First.Levels.Zip(pair.Second.Levels).All(z => Math.Abs(z.First - z.Second) <= 1e-6));
    internal static void VerifyRecordedGeometry(GeometryRead geometry, ExpectedModel expected)
    {
        Near(geometry.Extents.WidthMm, expected.Width); Near(geometry.Extents.HeightMm, expected.Height); Near(geometry.Extents.DepthMm, expected.Thickness); Near(geometry.Volume, expected.Volume, 0.001);
        Program.Check(geometry.Cylinders.Length == expected.Holes.Count + (expected.Fillet > 0 ? 4 : 0), "Native cylinder count differs.");
        foreach (var h in expected.Holes)
        {
            var hole = geometry.Cylinders.Single(c => Math.Abs(c.X - h.X) < 1e-6 && Math.Abs(c.Y - h.Y) < 1e-6 && Math.Abs(c.Diameter - h.Diameter) < 1e-6);
            Program.Check(hole.Levels.Length == 2, "Through-hole boundaries missing."); Near(hole.Levels[0], h.Bottom); Near(hole.Levels[1], h.Top);
        }
    }
    private static void VerifyGeometry(SolidWorksExecutionContext context, GeometryRead geometry, ExpectedModel expected)
    {
        VerifyRecordedGeometry(geometry, expected);
        var treatment = (IFeature)context.NativeFeature("corners"); Program.Check(treatment.GetErrorCode2(out var warning) == 0 && !warning, "Treatment has native errors/warnings.");
        if (expected.Fillet > 0)
        {
            Near(((ISimpleFilletFeatureData2)treatment.GetDefinition()).DefaultRadius * 1000, expected.Fillet);
            foreach (var x in new[] { -1, 1 }) foreach (var y in new[] { -1, 1 })
                Program.Check(geometry.Cylinders.Count(c => Math.Abs(c.X - x * (expected.Width / 2 - expected.Fillet)) < 1e-6 && Math.Abs(c.Y - y * (expected.Height / 2 - expected.Fillet)) < 1e-6 && Math.Abs(c.Diameter - 2 * expected.Fillet) < 1e-6) == 1, "Final outer fillet cylinder differs.");
        }
        else
        {
            var data = (IChamferFeatureData2)treatment.GetDefinition(); Near(data.GetEdgeChamferDistance(0) * 1000, expected.Chamfer); Near(data.GetEdgeChamferDistance(1) * 1000, expected.Chamfer);
            var body = ((Array)((IPartDoc)context.Document).GetBodies2(0, false)).Cast<object>().Cast<IBody2>().Single();
            var planes = ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>().Where(f => ((ISurface)f.GetSurface()).IsPlane()).ToArray();
            Program.Check(planes.Count(f => { var n = (double[])f.Normal; return Math.Abs(Math.Abs(n[0]) - Math.Sqrt(0.5)) < 1e-6 && Math.Abs(Math.Abs(n[1]) - Math.Sqrt(0.5)) < 1e-6 && Math.Abs(n[2]) < 1e-6; }) == 4, "Four final diagonal chamfer faces missing.");
        }
    }
    private static string ModelSignature(SolidWorksExecutionContext context)
    {
        var features = new List<object>(); var next = (IFeature?)context.Document.FirstFeature();
        for (var i = 0; next is not null && i < 256; i++, next = (IFeature?)next.GetNextFeature()) features.Add(new { Type = next.GetTypeName2(), next.Name });
        var bodies = ((Array)((IPartDoc)context.Document).GetBodies2(0, false)).Cast<object>().Cast<IBody2>().ToArray();
        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var scope in new[] { "", context.Document.ConfigurationManager.ActiveConfiguration.Name })
        {
            var manager = (ICustomPropertyManager)context.Document.Extension.CustomPropertyManager[scope];
            if (manager.GetNames() is Array names) foreach (var name in names.Cast<string>())
            { manager.Get6(name, false, out var value, out _, out _, out _); properties[scope + ":" + name] = manager.GetType2(name) + ":" + value; }
        }
        return JsonSerializer.Serialize(new { FeatureCount = context.Document.GetFeatureCount(), Features = features, Bodies = bodies.Length,
            Faces = bodies.Sum(b => b.GetFaceCount()), Edges = bodies.Sum(b => b.GetEdgeCount()), Properties = properties, Rebuild = context.Document.Extension.NeedsRebuild2 });
    }
}
