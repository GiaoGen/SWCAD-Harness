using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;

namespace CadHarness.CurrentRuntimeCompatibility.Tests;
internal sealed class CaseReport
{
    public string Case { get; set; } = "";
    public string Status { get; set; } = "BLOCKED";
    public string Planned { get; set; } = "NOT_RUN";
    public string Executed { get; set; } = "NOT_RUN";
    public string Editable { get; set; } = "NOT_RUN";
    public string StrictReadback { get; set; } = "NOT_RUN";
    public string FinalGeometry { get; set; } = "NOT_RUN";
    public string PersistentState { get; set; } = "NOT_RUN";
    public string CapabilityProjection { get; set; } = "NOT_RUN";
    public string RollbackSafe { get; set; } = "NOT_RUN";
    public int ProductionCodeChanges { get; set; }
    public bool ProductionAndHistoricalEvidenceUnchanged { get; set; }
    public CompositionExecutionResult? Construction { get; set; }
    public List<object> Stages { get; } = new();
    public List<object> Rollbacks { get; } = new();
    public ResourceSnapshot? ResourceGuard { get; set; }
    public string? SolidWorksRevision { get; set; }
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public string? CleanupError { get; set; }
}
internal static partial class NativeTests
{
    internal static int Run(string root, string name, string? template)
    {
        Program.VerifyFreeze(root);
        var output = Path.Combine(Program.Output(root), name); var resultPath = Path.Combine(output, "result.json");
        Program.Check(!File.Exists(resultPath) && !File.Exists(Path.Combine(output, "state.json")), "Case already attempted; no native retry.");
        var definition = CaseData.All().Single(c => c.Name == name); var program = Program.Stable(definition.Program!);
        Program.Check(new CadProgramJson().Serialize(program) == File.ReadAllText(Path.Combine(output, "program.json")), "Prepared typed plan changed.");
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM9GTest_";
        using var budget = new NativeTestBudget(Path.Combine(Program.Output(root), "native-budget.json"), "M9G", 2);
        var report = new CaseReport { Case = name }; SolidWorksConnection? connection = null; TestPartScope? part = null;
        try
        {
            var plan = Program.Plan(definition.Intent, program, SolidWorksPlanningRuntime.ForConstruction());
            Program.Check(plan.Succeeded, "Production Planner rejected: " + plan.Message); report.Planned = "PASS";
            Program.Write(Path.Combine(output, "native-plan.json"), plan);
            connection = SolidWorksConnection.Connect(); report.SolidWorksRevision = connection.Application.RevisionNumber();
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget);
            Program.Check(NativeResourceGuard.Evaluate(report.ResourceGuard.Responding, report.ResourceGuard.GdiCount, report.ResourceGuard.OpenTestOwnedParts) is null, "Resource guard rejected Part.");
            part = new(connection.Application, budget); var context = part.Create(connection, connection.ResolvePartTemplate(template));
            var path = Path.Combine(output, "state.json"); var store = new AtomicStateStore(path); store.Commit(context.CaptureConstructionState());
            report.Construction = new RelationBackend().Create(context, plan.Program!, store);
            var creation = report.Construction;
            Program.Check(creation.Succeeded && creation.MutationStarted && creation.StateCommitted && creation.Operations.Count == program.Operations.Count && creation.Operations.All(o => o.Succeeded),
                creation.FailureCode + ": " + creation.Message + "; rollback=" + creation.Transaction?.RollbackFailureMessage);
            report.Executed = "PASS";
            var current = new DesignRelationEngine().Solve(program).Program; var expected = definition.Expected!; long revision = 1;
            var axes = store.Load().Entities.Where(e => e.Type == SemanticType.ReferenceAxis).ToDictionary(e => e.SemanticId, e => e.NativeReference);
            CheckStage(context, store, current, expected, revision, axes, output, "creation", report);
            if (name == "G2")
            {
                ApplyEdit(context, store, ref current, ref expected, ref revision, axes, CaseData.Edit("stock", EditableParameter.ExtrusionDepth, 10),
                    ResizeThickness(expected, 10), output, "thickness-10", report);
                ApplyEdit(context, store, ref current, ref expected, ref revision, axes, CaseData.Edit("seed", EditableParameter.HoleDiameter, 8),
                    ResizeThroughHoles(expected, 8), output, "diameter-8", report);
            }
            else
            {
                ApplyEdit(context, store, ref current, ref expected, ref revision, axes, CaseData.Edit("seed", EditableParameter.HoleDiameter, 9),
                    ResizeThroughHoles(expected, 9), output, "diameter-9", report);
            }
            report.Editable = "PASS";
            var before = Observe(context); var signature = ModelSignature(context); var bytes = File.ReadAllBytes(path); var session = JsonSerializer.Serialize(context.CaptureConstructionState());
            var diameter = current.Operations.Single(o => o.SemanticId == "seed").Parameter<LengthParameter>("diameterMm").Millimeters;
            var failedEdit = CaseData.Edit("seed", EditableParameter.HoleDiameter, diameter + 1);
            var failurePlan = Program.Plan("Rollback probe: enlarge through holes", new("0.2", new[] { failedEdit }, Array.Empty<DesignRelation>()), SolidWorksPlanningRuntime.ForEdit(context, store.Load()));
            Program.Check(failurePlan.Succeeded, "Rollback probe edit rejected before mutation.");
            var editAdapter = new TransactionalParameterBackend(context); MutationResult rejectedEdit;
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                rejectedEdit = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, editAdapter).Execute(failurePlan.Program!.Operations.Single());
            Program.Check(!rejectedEdit.Succeeded && rejectedEdit.FailureCode == "STATE_COMMIT_FAILED" && rejectedEdit.Stage == "atomic state commit" &&
                rejectedEdit.MutationStarted && rejectedEdit.RollbackAttempted && rejectedEdit.RollbackSucceeded && !rejectedEdit.StateCommitted, "Edit atomic rollback flags differ: " + rejectedEdit.Message + "; " + rejectedEdit.RollbackFailureMessage);
            CheckRollbackUnchanged(context, path, before, signature, bytes, session, rejectedEdit, output, "edit", report, editAdapter.ValidationReads);
            CheckStage(context, store, current, expected, revision, axes, output, "after-edit-rollback", report);
            before = Observe(context); signature = ModelSignature(context); bytes = File.ReadAllBytes(path); session = JsonSerializer.Serialize(context.CaptureConstructionState());
            var addition = new CadProgram("0.2", new[] { CaseData.Hole("probe", 3, 0, -18) }, new[] { new DesignRelation(RelationKind.HostedOn, "probe", "stock.top_face") });
            CompositionExecutionResult rejectedAppend;
            using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) rejectedAppend = new RelationBackend().Create(context, addition, store);
            Program.Check(!rejectedAppend.Succeeded && rejectedAppend.FailureCode == "STATE_COMMIT_FAILED" && rejectedAppend.MutationStarted && rejectedAppend.RollbackAttempted && rejectedAppend.RollbackSucceeded && !rejectedAppend.StateCommitted &&
                rejectedAppend.Operations.Count == 1 && rejectedAppend.Operations[0].Succeeded, "Construction atomic rollback flags differ: " + rejectedAppend.Message + "; " + rejectedAppend.Transaction?.RollbackFailureMessage);
            CheckRollbackUnchanged(context, path, before, signature, bytes, session, rejectedAppend, output, "construction", report, null);
            CheckStage(context, store, current, expected, revision, axes, output, "after-construction-rollback", report);
            Program.Check(Directory.GetFiles(output, ".state.json.*.tmp").Length == 0, "State commit temporary file leaked.");
            report.RollbackSafe = "PASS"; report.StrictReadback = "PASS"; report.FinalGeometry = "PASS"; report.PersistentState = "PASS"; report.CapabilityProjection = "PASS"; report.Status = "PASS";
        }
        catch (Exception e) { report.Status = "BLOCKED"; report.FailureCode = e is ICadFailure f ? f.Code : "COMPATIBILITY_FAILED"; report.Message = e.Message; }
        finally
        {
            part?.Cleanup(); report.PartsCreated = part?.Created == true ? 1 : 0; report.PartsClosed = part?.Closed == true ? 1 : 0;
            report.OriginalActiveRestored = part?.OriginalActiveRestored == true; report.CleanupError = part?.CleanupError;
            if (report.CleanupError is not null) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            try { connection?.Dispose(); } catch (Exception e) { report.Status = "BLOCKED"; report.CleanupError = e.Message; }
            try { Program.VerifyFreeze(root); report.ProductionAndHistoricalEvidenceUnchanged = true; } catch (Exception e) { report.Status = "BLOCKED"; report.Message = e.Message; }
            Program.Write(resultPath, report);
            Console.WriteLine(JsonSerializer.Serialize(new { report.Case, report.Status, report.Planned, report.Executed, report.Editable, report.StrictReadback, report.FinalGeometry,
                report.PersistentState, report.CapabilityProjection, report.RollbackSafe, report.FailureCode, report.Message, report.PartsCreated, report.PartsClosed, report.CleanupError }, Program.Json));
        }
        return report.Status == "PASS" ? 0 : 1;
    }
    private static ExpectedModel ResizeThickness(ExpectedModel m, double thickness) => m with { Thickness = thickness, Volume = m.Volume / m.Thickness * thickness,
        Holes = m.Holes.Select(h => h with { Top = thickness, Bottom = h.Bottom == 0 ? 0 : thickness - (h.Top - h.Bottom) }).ToArray() };
    private static ExpectedModel ResizeThroughHoles(ExpectedModel m, double diameter) => m with
    {
        Volume = m.Volume - m.Holes.Where(h => h.Bottom == 0).Sum(h => Math.PI * (diameter * diameter - h.Diameter * h.Diameter) / 4 * m.Thickness),
        Holes = m.Holes.Select(h => h.Bottom == 0 ? h with { Diameter = diameter } : h).ToArray()
    };
    private static void ApplyEdit(SolidWorksExecutionContext context, AtomicStateStore store, ref CadProgram current, ref ExpectedModel expected, ref long revision,
        Dictionary<string, NativePersistentReference> axes, OperationNode edit, ExpectedModel proposedGeometry, string output, string stage, CaseReport report)
    {
        var runtime = SolidWorksPlanningRuntime.ForEdit(context, store.Load());
        var plan = Program.Plan(stage, new("0.2", new[] { edit }, Array.Empty<DesignRelation>()), runtime);
        Program.Check(plan.Succeeded, "Edit Planner rejected: " + plan.FailureCode + ": " + plan.Message);
        var next = new DesignRelationEngine().Solve(ParameterMutationRegistry.Default.ApplyProgram(current, plan.Program!.Operations.Single())).Program;
        var adapter = new TransactionalParameterBackend(context);
        var result = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store, adapter).Execute(plan.Program.Operations.Single());
        Program.Write(Path.Combine(output, stage + "-transaction.json"), new { Plan = plan, Result = result, Reads = adapter.ValidationReads });
        Program.Check(result.Succeeded && result.StateCommitted && result.Revision == revision + 1 && result.Changes.ChangedParameters.Count == 1 && result.Dirty is not null && result.Validation is not null,
            "Edit failed: " + result.FailureCode + ": " + result.Message + "; rollback=" + result.RollbackFailureMessage);
        current = next; expected = proposedGeometry; revision++;
        CheckStage(context, store, current, expected, revision, axes, output, stage, report);
    }
    private static void CheckStage(SolidWorksExecutionContext context, AtomicStateStore store, CadProgram program, ExpectedModel expected, long revision,
        Dictionary<string, NativePersistentReference> axes, string output, string stage, CaseReport report)
    {
        var state = store.Load(); VerifyState(context, state, program, revision);
        Program.Check(axes.All(p => state.Entities.Single(e => e.SemanticId == p.Key).NativeReference == p.Value), "Extrusion datum identity changed across edits/rollback.");
        var geometry = Observe(context); VerifyRecordedGeometry(geometry, expected);
        var directions = ReadDirections(context, state, program); Program.Check(directions.All(d => d.ReferenceMatches && d.ReverseMatches), "Strict native direction mismatch.");
        var catalog = AuditProjection(context, state, program);
        Program.Write(Path.Combine(output, stage + "-observation.json"), new { Expected = expected, Geometry = geometry, Directions = directions, Revision = revision, Projection = catalog });
        File.Copy(storePath(output), Path.Combine(output, stage + "-state.json"));
        File.WriteAllText(Path.Combine(output, stage + "-current-program.json"), new CadProgramJson().Serialize(program));
        report.Stages.Add(new { Stage = stage, Geometry = geometry, Directions = directions, Revision = revision, Projection = catalog,
            StatePersistedAndRebound = true, DatumIdentitiesUnchanged = true });
    }
    private static string storePath(string output) => Path.Combine(output, "state.json");
    private static object AuditProjection(SolidWorksExecutionContext context, CadState state, CadProgram program)
    {
        var runtime = SolidWorksPlanningRuntime.ForEdit(context, state); var audit = new List<object>();
        foreach (var capability in runtime.Capabilities.ParameterEdits)
        {
            var owner = program.Operations.Single(o => o.SemanticId == capability.Target);
            Program.Check(ParameterMutationRegistry.Default.TryGet(owner, capability.Parameter, out var handler), "Catalog offers unregistered handler.");
            var value = handler.Read(context, owner, capability.Parameter); Near(value, ParameterMutationRegistry.Default.Expected(owner, capability.Parameter));
            var edit = CaseData.Edit(capability.Target, capability.Parameter, value);
            Program.Check(runtime.Preflight(new("0.2", new[] { edit }, Array.Empty<DesignRelation>())).IsValid, "Catalog edit cannot preflight.");
            var adapter = new TransactionalParameterBackend(context); var prepared = adapter.ResolveInputs(state, edit); adapter.Preflight(state, prepared); adapter.CaptureRollback(state, prepared);
            audit.Add(new { capability.Target, capability.Parameter, Handler = handler.GetType().Name, NativeValue = value, Registered = true, NativeReadable = true, PreflightExecutable = true, RollbackCapturable = true });
        }
        var pattern = program.Operations.Single(o => o.SemanticId == "layout");
        var required = pattern.Kind == OperationKind.CreateLinearPattern ? new[] { EditableParameter.PatternCount, EditableParameter.PatternSpacing } :
            new[] { EditableParameter.PatternCountX, EditableParameter.PatternCountY, EditableParameter.PatternSpacingX, EditableParameter.PatternSpacingY };
        Program.Check(runtime.Capabilities.ParameterEdits.Where(c => c.Target == "layout").Select(c => c.Parameter).ToHashSet().SetEquals(required), "Pattern edit capability projection incomplete.");
        var hidden = state.Bindings.Where(b => !runtime.Capabilities.ParameterEdits.Any(c => c.Target == b.OwnerFeatureSemanticId && c.Parameter == b.Parameter)).ToArray();
        foreach (var binding in hidden)
            Program.Check(!runtime.Capabilities.Validate(new("0.2", new[] { CaseData.Edit(binding.OwnerFeatureSemanticId, binding.Parameter, state.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value) }, Array.Empty<DesignRelation>())).IsValid,
                "Hidden edit leaked into runtime projection.");
        return new { Catalog = JsonSerializer.Deserialize<JsonElement>(runtime.Capabilities.ToPromptJson()), Audit = audit, Hidden = hidden };
    }
    private static void CheckRollbackUnchanged(SolidWorksExecutionContext context, string path, GeometryRead before, string signature, byte[] bytes, string session,
        object transaction, string output, string name, CaseReport report, object? reads)
    {
        var after = Observe(context); var afterSignature = ModelSignature(context); var file = File.ReadAllBytes(path);
        var model = signature == afterSignature && SameGeometry(before, after); var disk = bytes.SequenceEqual(file);
        var live = session == JsonSerializer.Serialize(context.CaptureConstructionState());
        var evidence = new { Name = name + " real atomic commit failure rollback", Transaction = transaction, Reads = reads, Before = before, After = after,
            BeforeModelSignature = signature, AfterModelSignature = afterSignature, ModelUnchanged = model, FileUnchanged = disk, SessionUnchanged = live,
            StateBeforeSha256 = Program.Hash(bytes), StateAfterSha256 = Program.Hash(file), VolumeToleranceMm3 = 0.001, LengthToleranceMm = 1e-6 };
        Program.Write(Path.Combine(output, name + "-rollback.json"), evidence); report.Rollbacks.Add(evidence);
        Program.Check(model && disk && live, "Rollback did not restore model/file/session.");
    }
}
