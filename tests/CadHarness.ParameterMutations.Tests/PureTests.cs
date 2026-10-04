using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.ParameterMutations.Tests;

// A test registration uses an existing finite IR scalar. No production class
// needs editing to add/remove it; native methods intentionally never run here.
internal sealed class TestBlindDepthHandler : IParameterMutationHandler
{
    public IReadOnlyList<ParameterMutationDescriptor> Descriptors { get; } = Array.AsReadOnly(new[]
        { new ParameterMutationDescriptor(OperationKind.CreateBlindHole, EditableParameter.BlindHoleDepth, "depthMm") });
    public bool CanExecute(OperationNode owner, EditableParameter parameter) => true;
    public void ValidateTransition(OperationNode before, OperationNode after, EditableParameter parameter) { }
    public FullValidationReason ValidationReasons(OperationNode owner, EditableParameter parameter) => FullValidationReason.HighRiskTopology;
    public double Read(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) => throw new NotSupportedException();
    public object Capture(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) => throw new NotSupportedException();
    public void Apply(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter) => throw new NotSupportedException();
    public void Restore(SolidWorksExecutionContext context, object rollback) => throw new NotSupportedException();
    public void ValidateNative(SolidWorksExecutionContext context, CadProgram expected, string target, EditableParameter parameter) => throw new NotSupportedException();
}

internal static class PureTests
{
    internal static int Run(string root)
    {
        var program = TestData.Fixture(root); var state = TestData.Sample(program); var registry = ParameterMutationRegistry.Default;
        var runtime = SolidWorksPlanningRuntime.ForEditSnapshot(program, state);
        var tests = new List<(string Name, Action Run)>(); void Add(string name, Action run) => tests.Add((name, run));
        Add("G2 fixture solves to legal centered 2x2 layout", () =>
        {
            TestData.Check(new RelationBackend().Preflight(program).IsValid &&
                program.Operations[1].Parameter<PlacementParameter>("placement").Value == new Point2D(-30, -15), "G2 layout differs.");
        });
        Add("default registry grants exactly implemented scalar descriptors", () =>
        {
            TestData.Check(registry.Descriptors.Count == 8 && registry.Descriptors.Any(d => d.Parameter == EditableParameter.ExtrusionDepth) &&
                registry.Descriptors.Any(d => d.Parameter == EditableParameter.HoleDiameter) &&
                !registry.Descriptors.Any(d => d.OwnerKind == OperationKind.CreateCircularPattern || d.Parameter == EditableParameter.BlindHoleDepth), "Unimplemented accessor projected.");
        });
        Add("duplicate native descriptor registration fails closed", () =>
        {
            try { _ = new ParameterMutationRegistry(new IParameterMutationHandler[] { new ExtrusionDepthMutationHandler(), new ExtrusionDepthMutationHandler() });
                throw new Exception("Duplicate descriptor accepted."); } catch (ArgumentException) { }
        });
        Add("empty registry removes all executable edits", () =>
        {
            var empty = new ParameterMutationRegistry(Array.Empty<IParameterMutationHandler>());
            TestData.Check(SolidWorksPlanningRuntime.ForEditSnapshot(program, state, empty).Capabilities.ParameterEdits.Count == 0, "Capability inferred from IR vocabulary.");
        });
        Add("caller registry adds handler without editing transaction coordinator", () =>
        {
            var custom = new ParameterMutationRegistry(new[] { new TestBlindDepthHandler() });
            var hole = program.Operations[1] with { Kind = OperationKind.CreateBlindHole,
                Parameters = new Dictionary<string, OperationParameter>(program.Operations[1].Parameters) { ["depthMm"] = new LengthParameter(2) } };
            var model = program with { Operations = new[] { program.Operations[0], hole, program.Operations[2] } };
            TestData.Check(custom.Get(hole, EditableParameter.BlindHoleDepth) is TestBlindDepthHandler &&
                custom.ApplyProgram(model, TestData.Edit("mounting_seed", EditableParameter.BlindHoleDepth, 3)).Operations[1].Parameter<LengthParameter>("depthMm").Millimeters == 3,
                "Registration cannot extend mutation dispatch.");
        });
        foreach (var (target, parameter, value) in new[] { ("plate", EditableParameter.ExtrusionDepth, 10.0), ("mounting_seed", EditableParameter.HoleDiameter, 8.0) })
        {
            var t = target; var p = parameter; var v = value;
            Add("registered program mutation " + parameter + " preserves identity and relations", () =>
            {
                var before = new CadProgramJson().Serialize(program); var next = registry.ApplyProgram(program, TestData.Edit(t, p, v));
                TestData.Check(registry.Expected(next.Operations.Single(o => o.SemanticId == t), p) == v && next.Relations.SequenceEqual(program.Relations) &&
                    new CadProgramJson().Serialize(program) == before, "Mutation modified source or semantic relation identities.");
            });
            Add("Planner catalog and deterministic preflight expose " + parameter, () =>
            {
                TestData.Check(runtime.Capabilities.ParameterEdits.Any(e => e.Target == t && e.Parameter == p) &&
                    runtime.Preflight(TestData.EditProgram(t, p, v)).IsValid, "Registered valid edit was hidden/rejected.");
            });
            Add("strict single-plan fixture accepts " + parameter, () =>
            {
                var source = new FixturePlanSource(new Dictionary<string, string> { ["edit"] = TestData.Envelope(TestData.EditProgram(t, p, v)) });
                var result = new CadPlanner(runtime, source).PlanAsync("edit").GetAwaiter().GetResult();
                TestData.Check(result.Succeeded && result.ModelCalls == 0 && result.Program!.Operations.Single().Kind == OperationKind.EditParameter, "Strict planning failed.");
            });
            Add("DirtySet for " + parameter + " expands dependencies but reads one parameter", () =>
            {
                var changes = new ChangeSet(new[] { t }, new[] { t + "." + WireNames.Of(p) }, Array.Empty<string>());
                var dirty = DirtySet.Expand(state, changes); var scope = ValidationScope.Select(state, dirty, FullValidationReason.None);
                TestData.Check(dirty.Features.Contains("mounting_seed") && dirty.Features.Contains("mounting_holes") &&
                    !scope.FullModel && scope.Parameters.SequenceEqual(changes.ChangedParameters) && scope.Entities.Contains("plate.top_face"), "Dependency/read boundary differs.");
            });
            Add("missing binding hides " + parameter, () =>
            {
                var id = t + "." + WireNames.Of(p); var missing = state with { Parameters = state.Parameters.Where(x => x.SemanticId != id).ToArray(),
                    Bindings = state.Bindings.Where(x => x.ParameterSemanticId != id).ToArray() };
                TestData.Check(!SolidWorksPlanningRuntime.ForEditSnapshot(program, missing).Capabilities.ParameterEdits.Any(e => e.Target == t && e.Parameter == p), "Missing binding exposed.");
            });
            Add("parameter state drift hides " + parameter, () =>
            {
                var id = t + "." + WireNames.Of(p); var drift = state with { Parameters = state.Parameters.Select(x => x.SemanticId == id ? x with { Value = x.Value + 1 } : x).ToArray() };
                TestData.Check(!SolidWorksPlanningRuntime.ForEditSnapshot(program, drift).Capabilities.ParameterEdits.Any(e => e.Target == t && e.Parameter == p), "Drift exposed.");
            });
            Add("unhealthy dependent entity hides " + parameter, () =>
            {
                var bad = state with { Entities = state.Entities.Select(e => e.SemanticId == "mounting_seed.wall_face" ? e with { ReferenceHealth = ReferenceHealth.Stale } : e).ToArray() };
                TestData.Check(!SolidWorksPlanningRuntime.ForEditSnapshot(program, bad).Capabilities.ParameterEdits.Any(e => e.Target == t && e.Parameter == p), "Unhealthy dependency exposed.");
            });
        }
        Add("impossible enlarged hole is rejected by relation host bounds", () =>
            TestData.Check(!runtime.Preflight(TestData.EditProgram("mounting_seed", EditableParameter.HoleDiameter, 40)).IsValid, "Impossible diameter passed."));
        Add("wrong owner parameter pair remains unsupported", () =>
            TestData.Check(!runtime.Preflight(TestData.EditProgram("mounting_holes", EditableParameter.HoleDiameter, 8)).IsValid, "Wrong owner accepted."));
        Add("unregistered profile fillet chamfer and blind depth edits remain hidden", () =>
            TestData.Check(!runtime.Capabilities.ParameterEdits.Any(e => e.Parameter is EditableParameter.ProfileWidth or EditableParameter.ProfileHeight or
                EditableParameter.ProfileDiameter or EditableParameter.FilletRadius or EditableParameter.ChamferDistance or EditableParameter.BlindHoleDepth), "Unimplemented parameter leaked."));
        Add("removing diameter handler removes its schema and capability", () =>
        {
            var restricted = new ParameterMutationRegistry(new IParameterMutationHandler[] { new PatternScalarMutationHandler(), new ExtrusionDepthMutationHandler() });
            var projection = SolidWorksPlanningRuntime.ForEditSnapshot(program, state, restricted);
            TestData.Check(!projection.Capabilities.ParameterEdits.Any(e => e.Parameter == EditableParameter.HoleDiameter) &&
                !PlannerResponseSchema.Create(projection.Capabilities).Contains("hole_diameter"), "Removed handler remains available.");
        });
        Add("restricted mutation registry still validates uneditable captured scalar values", () =>
        {
            var onlyDepth = new ParameterMutationRegistry(new[] { new ExtrusionDepthMutationHandler() });
            TestData.Check(onlyDepth.Expected(program.Operations[1], EditableParameter.HoleDiameter) == 6 &&
                onlyDepth.Expected(program.Operations[2], EditableParameter.PatternSpacingX) == 60 &&
                !onlyDepth.TryGet(program.Operations[1], EditableParameter.HoleDiameter, out _), "Read-only validation depended on an executable edit handler.");
        });
        Add("construction still excludes circle and circular pattern", () =>
        {
            var c = SolidWorksPlanningRuntime.ForConstruction().Capabilities;
            TestData.Check(!c.Profiles.Contains(ProfileKind.Circle) && !c.Registry.TryGet(OperationKind.CreateCircularPattern, out _), "M9B capability appeared.");
        });
        Add("existing pattern accessor restrictions and risk are retained", () =>
        {
            var owner = program.Operations[2]; var handler = registry.Get(owner, EditableParameter.PatternCountY);
            TestData.Check(handler.ValidationReasons(owner, EditableParameter.PatternCountY) == FullValidationReason.HighRiskTopology &&
                registry.Get(owner, EditableParameter.PatternSpacingX).ValidationReasons(owner, EditableParameter.PatternSpacingX) == FullValidationReason.None,
                "Pattern risk policy changed.");
            TestData.Check(!runtime.Preflight(TestData.EditProgram("mounting_holes", EditableParameter.PatternCountY, 1)).IsValid, "Inactive direction transition accepted.");
        });
        var results = new List<object>(); var passed = 0;
        foreach (var test in tests)
        {
            try { test.Run(); passed++; results.Add(new { test.Name, Passed = true }); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { results.Add(new { test.Name, Passed = false, Error = error.Message }); Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        var output = Path.Combine(root, "artifacts", "milestone9a"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "pure-result.json"), JsonSerializer.Serialize(new { Status = passed == tests.Count ? "COMPLETE" : "BLOCKED",
            Passed = passed, Total = tests.Count, NativePartsCreated = 0, Tests = results }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "edit-capabilities.json"), runtime.Capabilities.ToPromptJson());
        File.WriteAllText(Path.Combine(output, "edit-response.schema.json"), PlannerResponseSchema.Create(runtime.Capabilities));
        return passed == tests.Count ? 0 : 1;
    }
}
