using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.SolidWorks;

[assembly: SupportedOSPlatform("windows")]

namespace CadHarness.SolidWorks.Tests;

internal sealed class LiveReport
{
    public string Status { get; set; } = "BLOCKED";
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public string? SolidWorksRevision { get; set; }
    public string? PartTemplate { get; set; }
    public bool StartedApplication { get; set; }
    public ResourceSnapshot? ResourceGuard { get; set; }
    public OperationExecutionResult? Execution { get; set; }
    public ExtrudeMeasurement? Measured { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public string? CleanupError { get; set; }
    public BudgetSnapshot? MilestoneBudget { get; set; }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 2 or > 3 || args[1] is not ("--pure" or "--live"))
        { Console.WriteLine("Usage: CadHarness.SolidWorks.Tests <workspace> --pure|--live [part.prtdot]"); return 2; }
        var fixture = File.ReadAllText(Path.Combine(args[0], "tests", "CadHarness.SolidWorks.Tests", "Fixtures", "extrude.json"));
        return args[1] == "--pure" ? Pure(fixture) : Live(args[0], fixture, args.Length == 3 ? args[2] : null);
    }

    private static int Pure(string fixture)
    {
        var cases = new List<(string Name, Action Run)>
        {
            ("M2 rectangle extrusion preflight accepts fixture", () => Assert(Ready(fixture).IsValid, "Fixture failed preflight.")),
            ("mm to native meters converts 80/50/10", () => Assert(Millimeters.ToMeters(80) == 0.08 && Millimeters.ToMeters(50) == 0.05 && Millimeters.ToMeters(10) == 0.01, "Unit conversion differs.")),
            ("circle backend rejects before native mutation", () =>
            {
                var program = Parse(fixture);
                var parameters = new Dictionary<string, OperationParameter>(program.Operations[0].Parameters)
                    { ["profile"] = new ProfileParameter(new CircleProfile(80)) };
                Assert(!MilestoneTwoProgram.Preflight(program with
                    { Operations = new[] { program.Operations[0] with { Parameters = parameters } } }).IsValid, "Circle backend was accepted.");
            }),
            ("later operation rejects before native mutation", () => Assert(!Ready("""{"programVersion":"0.2","operations":[{"id":"hole","kind":"create_through_hole","host":"existing.top_face","diameterMm":8,"semanticId":"hole_seed"}]}""").IsValid, "Hole backend was accepted.")),
            ("multiple operations reject before native mutation", () =>
            {
                var program = Parse(fixture);
                var second = program.Operations[0] with { Id = "other", SemanticId = "other_plate" };
                Assert(!MilestoneTwoProgram.Preflight(program with { Operations = new[] { program.Operations[0], second } }).IsValid, "Composition was accepted.");
            }),
            ("design relations reject before native mutation", () =>
            {
                var program = Parse(fixture);
                Assert(!MilestoneTwoProgram.Preflight(program with { Relations = new[] { new DesignRelation(RelationKind.ThroughAll, "base_plate", null) } }).IsValid, "Relation was accepted.");
            }),
            ("invalid dimension rejects before native mutation", () => Assert(!new CadProgramJson().Parse(fixture.Replace("\"depthMm\": 10", "\"depthMm\": -1")).IsValid, "Invalid dimension was accepted.")),
            ("resource guard honors GDI boundary and document limit", () =>
            {
                Assert(NativeResourceGuard.Evaluate(true, 6999, 0) is null, "Below-limit guard rejected.");
                Assert(NativeResourceGuard.Evaluate(true, 7000, 0) == "TEST_RESOURCE_LIMIT", "GDI boundary was ignored.");
                Assert(NativeResourceGuard.Evaluate(false, null, 0) == "TEST_RESOURCE_LIMIT", "Responsiveness was ignored.");
                Assert(NativeResourceGuard.Evaluate(true, null, 1) == "TEST_RESOURCE_LIMIT", "Open test-owned document was ignored.");
            })
        };
        var failures = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"{cases.Count - failures}/{cases.Count} M2 pure tests passed; native Parts created=0, closed=0.");
        return failures == 0 ? 0 : 1;
    }

    private static int Live(string root, string fixture, string? template)
    {
        var output = Path.Combine(root, "artifacts", "milestone2");
        Directory.CreateDirectory(output);
        var report = new LiveReport();
        SolidWorksConnection? connection = null;
        TestPartScope? part = null;
        NativeTestBudget? budget = null;
        try
        {
            var program = Parse(fixture);
            var preflight = MilestoneTwoProgram.Preflight(program);
            if (!preflight.IsValid) throw new TestFailure(preflight.FailureCode!, preflight.Message);
            budget = new(Path.Combine(output, "native-budget.json"));
            if (budget.Snapshot.CreationAttempts >= NativeTestBudget.MaximumParts)
                throw new TestFailure("ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED", "Milestone 2 budget exhausted; no application or Part was started.");
            Console.WriteLine("Connecting to SOLIDWORKS; no Part has been created yet.");
            connection = SolidWorksConnection.Connect();
            report.StartedApplication = connection.StartedApplication;
            report.SolidWorksRevision = connection.Application.RevisionNumber();
            report.PartTemplate = connection.ResolvePartTemplate(template);
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget);
            Console.WriteLine(JsonSerializer.Serialize(report.ResourceGuard));
            var guard = report.ResourceGuard;
            var resourceFailure = NativeResourceGuard.Evaluate(guard.Responding, guard.GdiCount, guard.OpenTestOwnedParts);
            if (resourceFailure is not null) throw new TestFailure(resourceFailure, "Resource guard rejected native Part creation.");
            part = new(connection.Application, budget);
            var context = part.Create(connection, report.PartTemplate);
            Console.WriteLine("Registered one test-owned Part; executing the IR extrusion.");
            report.Execution = new CreateExtrudeHandler().Execute(context, program.Operations[0]);
            if (!report.Execution.Succeeded) throw new TestFailure(report.Execution.FailureCode!, report.Execution.Message);
            report.Measured = ExtrudeMeasurementReader.Read(context);
            if (report.Measured.SolidBodyCount != 1 || !Near(report.Measured.WidthMm, 80) ||
                !Near(report.Measured.HeightMm, 50) || !Near(report.Measured.DepthMm, 10) || !report.Execution.RebuildSucceeded)
                throw new TestFailure("GEOMETRY_INVALID", "Required body count, width, height, depth or rebuild check failed.");
            report.Status = "COMPLETE";
            report.Message = "80 x 50 x 10 mm; one solid body; rebuild passed.";
        }
        catch (TestFailure error) { report.FailureCode = error.Code; report.Message = error.Message; }
        catch (Exception error) { report.FailureCode = "NATIVE_TEST_FAILED"; report.Message = error.GetType().Name + ": " + error.Message; }
        finally
        {
            if (part is not null)
            {
                part.Cleanup();
                report.PartsCreated = part.Created ? 1 : 0;
                report.PartsClosed = part.Closed ? 1 : 0;
                report.OriginalActiveRestored = part.OriginalActiveRestored;
                report.CleanupError = part.CleanupError;
                if (part.CleanupError is not null)
                { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            }
            if (connection is not null)
            {
                try { connection.Dispose(); }
                catch (Exception error)
                { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; report.CleanupError = (report.CleanupError is null ? "" : report.CleanupError + "; ") + "Connection disposal failed: " + error.Message; }
            }
            if (budget is not null) { report.MilestoneBudget = budget.Snapshot; budget.Dispose(); }
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(output, "native-result.json"), json + Environment.NewLine);
            Console.WriteLine(json);
        }
        return report.Status == "COMPLETE" ? 0 : 1;
    }

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= 1e-6;
    private static PreflightResult Ready(string json) => MilestoneTwoProgram.Preflight(Parse(json));
    private static CadProgram Parse(string json)
    {
        var parsed = new CadProgramJson().Parse(json);
        if (!parsed.IsValid) throw new TestFailure(parsed.Issues[0].Code, parsed.Issues[0].Message);
        return parsed.Program!;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
