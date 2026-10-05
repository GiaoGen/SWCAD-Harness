using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;
internal static partial class BenchmarkValidator
{
    // One final correctness oracle, called identically by both orchestration
    // modes and after every requested parameter edit. IDs come from the plan.
    internal static void Validate(SolidWorksExecutionContext context, AtomicStateStore store,
        BenchmarkTask task, ExpectedModel expected, string output, string stage)
    {
        using var measured = ExecutionTelemetry.Measure(ExecutionPhase.Validation);
        var program = SolidWorksStepwiseRuntime.ForSession(context, store.Load()).ManagedProgram ?? throw new InvalidOperationException("No constructed program.");
        Program.Check(program.Operations.Select(o => o.Kind).OrderBy(k => k).SequenceEqual(task.Composition.OrderBy(k => k)), "Operation composition differs from requested task.");
        var state = store.Load(); VerifyState(context, state, program, context.CaptureConstructionState().Revision);
        var geometry = Observe(context); VerifyRecordedGeometry(geometry, expected);
        Program.Check(geometry.Extents.SolidBodyCount == 1, "Expected one solid body.");
        var directions = ReadDirections(context, state, program);
        Program.Check(directions.All(d => d.ReferenceMatches && d.ReverseMatches), "Strict native pattern direction mismatch.");
        var pattern = program.Operations.Single(o => o.Kind is OperationKind.CreateLinearPattern or OperationKind.CreateRectangularPattern);
        var root = program.Operations.Single(o => o.Kind == OperationKind.CreateExtrude).SemanticId!;
        Program.Check(program.Relations.Contains(new(RelationKind.CenteredAbout, pattern.SemanticId!, root + ".local_frame")) &&
            program.Relations.Contains(new(RelationKind.PatternSeed, pattern.SemanticId!, pattern.Input("seed")!.References[0].SemanticId)) &&
            program.Relations.Any(r => r.Kind == RelationKind.EqualSpacing && r.Subject == pattern.SemanticId), "Requested design relations missing.");
        foreach (var hole in program.Operations.Where(o => o.Kind is OperationKind.CreateThroughHole or OperationKind.CreateBlindHole))
            Program.Check(program.Relations.Contains(new(RelationKind.HostedOn, hole.SemanticId!, root + ".top_face")), "Hosted-on relation missing.");
        // Probe projected handlers without mutation; actual editability is
        // proven by the subsequent user-requested parameter transactions.
        var runtime = SolidWorksPlanningRuntime.ForEdit(context, state);
        var handlers = runtime.Capabilities.ParameterEdits.Select(c =>
        {
            var op = program.Operations.Single(o => o.SemanticId == c.Target);
            Program.Check(ParameterMutationRegistry.Default.TryGet(op, c.Parameter, out var handler), "Projection advertises unregistered edit.");
            Near(handler.Read(context, op, c.Parameter), ParameterMutationRegistry.Default.Expected(op, c.Parameter));
            return new { c.Target, c.Parameter, Handler = handler.GetType().Name };
        }).ToArray();
        Program.Write(Path.Combine(output, stage + "-validation.json"), new { Expected = expected, Geometry = geometry, Directions = directions,
            Projection = JsonSerializer.Deserialize<JsonElement>(runtime.Capabilities.ToPromptJson()), Handlers = handlers, State = state });
        File.WriteAllText(Path.Combine(output, stage + "-program.json"), new CadProgramJson().Serialize(program));
    }
}
