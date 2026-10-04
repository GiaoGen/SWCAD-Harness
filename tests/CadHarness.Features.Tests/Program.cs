using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using SolidWorks.Interop.sldworks;
using Environment = System.Environment;

[assembly: SupportedOSPlatform("windows")]

namespace CadHarness.Features.Tests;

internal sealed class NativeReport
{
    public string Status { get; set; } = "BLOCKED";
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public string? SolidWorksRevision { get; set; }
    public ResourceSnapshot? ResourceGuard { get; set; }
    public CompositionExecutionResult? Execution { get; set; }
    public GeometryReport? Geometry { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public string? CleanupError { get; set; }
    public BudgetSnapshot? MilestoneBudget { get; set; }
}
internal sealed record CylinderReport(double XMm, double YMm, double RadiusMm, double[] CircleLevelsMm);
internal sealed record GeometryReport(ExtrudeMeasurement Extents, double VolumeMm3, IReadOnlyList<CylinderReport> Cylinders);

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[1] == "--pure") return PureTests.Run(args[0]);
        if (args.Length is >= 3 and <= 4 && args[1] == "--live" && args[2] is "g1" or "g2" or "aux")
            return Live(args[0], args[2], args.Length == 4 ? args[3] : null);
        Console.WriteLine("Usage: CadHarness.Features.Tests <workspace> --pure | --live g1|g2|aux [part.prtdot]");
        return 2;
    }
    private static int Live(string root, string name, string? template)
    {
        var output = Path.Combine(root, "artifacts", "milestone4");
        Directory.CreateDirectory(output);
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM4Test_";
        var report = new NativeReport();
        NativeTestBudget? budget = null;
        SolidWorksConnection? connection = null;
        TestPartScope? part = null;
        try
        {
            var program = PureTests.Fixture(root, name);
            var backend = new CompositionBackend();
            var check = backend.Preflight(program);
            if (!check.IsValid) throw new TestFailure(check.FailureCode!, check.Message);
            budget = new(Path.Combine(output, "native-budget.json"), "Milestone 4", 4);
            if (budget.Snapshot.CreationAttempts >= 4)
                throw new TestFailure("ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED", "M4 Part budget is exhausted; no Part created.");
            Console.WriteLine("M4 " + name + ": connecting; no Part created yet.");
            connection = SolidWorksConnection.Connect();
            report.SolidWorksRevision = connection.Application.RevisionNumber();
            var partTemplate = connection.ResolvePartTemplate(template);
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget);
            var guard = report.ResourceGuard;
            Console.WriteLine(JsonSerializer.Serialize(guard));
            var failure = NativeResourceGuard.Evaluate(guard.Responding, guard.GdiCount, guard.OpenTestOwnedParts);
            if (failure is not null) throw new TestFailure(failure, "Resource guard refused Part creation.");
            part = new(connection.Application, budget);
            var context = part.Create(connection, partTemplate);
            report.Execution = backend.Execute(context, program);
            Console.WriteLine(JsonSerializer.Serialize(report.Execution));
            if (!report.Execution.Succeeded) throw new TestFailure(report.Execution.FailureCode!, report.Execution.Message);
            report.Geometry = ReadGeometry(context);
            Verify(name, context, report.Geometry);
            report.Status = "COMPLETE";
            report.Message = "Generic native composition and independent final geometry checks passed.";
        }
        catch (TestFailure error) { report.FailureCode = error.Code; report.Message = error.Message; }
        catch (Exception error) { report.FailureCode = "NATIVE_TEST_FAILED"; report.Message = error.GetType().Name + ": " + error.Message; }
        finally
        {
            if (part is not null)
            {
                part.Cleanup();
                report.PartsCreated = part.Created ? 1 : 0; report.PartsClosed = part.Closed ? 1 : 0;
                report.OriginalActiveRestored = part.OriginalActiveRestored; report.CleanupError = part.CleanupError;
                if (part.CleanupError is not null) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            }
            if (connection is not null)
                try { connection.Dispose(); }
                catch (Exception error) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; report.CleanupError = (report.CleanupError ?? "") + error.Message; }
            if (budget is not null) { report.MilestoneBudget = budget.Snapshot; budget.Dispose(); }
            var path = Path.Combine(output, "native-result.json");
            var reports = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, NativeReport>>(File.ReadAllText(path))! : new();
            reports[name] = report;
            File.WriteAllText(path, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return report.Status == "COMPLETE" ? 0 : 1;
    }
    private static GeometryReport ReadGeometry(SolidWorksExecutionContext context)
    {
        var bodyArray = (Array)((IPartDoc)context.Document).GetBodies2(0, false);
        Check(bodyArray.Length == 1, "Expected exactly one native solid body.");
        var body = (IBody2)bodyArray.GetValue(0)!;
        var cylinders = new List<CylinderReport>();
        foreach (var face in ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>())
        {
            var surface = (ISurface)face.GetSurface();
            if (!surface.IsCylinder()) continue;
            var c = (double[])surface.CylinderParams;
            var levels = ((Array)face.GetEdges()).Cast<object>().Cast<IEdge>().Select(e => (ICurve)e.GetCurve())
                .Where(curve => curve.IsCircle()).Select(curve => ((double[])curve.CircleParams)[2] * 1000).Distinct().OrderBy(z => z).ToArray();
            cylinders.Add(new(c[0] * 1000, c[1] * 1000, c[6] * 1000, levels));
        }
        var mass = (double[])body.GetMassProperties(1);
        return new(ExtrudeMeasurementReader.Read(context), mass[3] * 1e9, cylinders);
    }
    private static void Verify(string name, SolidWorksExecutionContext context, GeometryReport geometry)
    {
        var size = name switch { "g1" => (80.0, 50.0, 10.0), "g2" => (100.0, 60.0, 8.0), _ => (70.0, 45.0, 12.0) };
        Near(geometry.Extents.WidthMm, size.Item1); Near(geometry.Extents.HeightMm, size.Item2); Near(geometry.Extents.DepthMm, size.Item3);
        var holeRadius = name == "g1" ? 4 : name == "g2" ? 3 : 5;
        var holes = geometry.Cylinders.Where(c => Math.Abs(c.RadiusMm - holeRadius) < 1e-6).OrderBy(c => c.XMm).ThenBy(c => c.YMm).ToArray();
        var positions = name switch
        {
            "g1" => new[] { (-20.0, 0.0), (20.0, 0.0) },
            "g2" => new[] { (-30.0, -15.0), (-30.0, 15.0), (30.0, -15.0), (30.0, 15.0) },
            _ => new[] { (7.0, -4.0) }
        };
        Check(holes.Length == positions.Length, "Native hole instance count differs.");
        for (var i = 0; i < positions.Length; i++)
        {
            Near(holes[i].XMm, positions[i].Item1); Near(holes[i].YMm, positions[i].Item2);
            Check(holes[i].CircleLevelsMm.Length == 2, "Hole wall must have two circular boundaries.");
            Near(holes[i].CircleLevelsMm[0], name == "aux" ? 7 : 0); Near(holes[i].CircleLevelsMm[1], size.Item3);
        }
        var volume = size.Item1 * size.Item2 * size.Item3 - holes.Length * Math.PI * holeRadius * holeRadius * (name == "aux" ? 5 : size.Item3);
        if (name == "g1")
        {
            var fillets = geometry.Cylinders.Where(c => Math.Abs(c.RadiusMm - 3) < 1e-6).ToArray();
            Check(fillets.Length == 4, "Expected four native R3 outer corner fillets.");
            volume -= (4 - Math.PI) * 3 * 3 * size.Item3;
        }
        if (name == "aux")
        {
            volume -= 4 * 2 * 2 / 2.0 * size.Item3;
            var feature = (IFeature)context.NativeFeature("corner_bevel");
            Check(feature.GetDefinition() is IChamferFeatureData2, "Native chamfer definition missing.");
        }
        Check(Math.Abs(geometry.VolumeMm3 - volume) < 0.001, "Native solid volume differs from the constructed geometry; actual=" + geometry.VolumeMm3 + ", expected=" + volume);
    }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) <= 1e-6, $"Native measurement {actual} differs from {expected}.");
    private static void Check(bool condition, string message) { if (!condition) throw new TestFailure("GEOMETRY_INVALID", message); }
}
