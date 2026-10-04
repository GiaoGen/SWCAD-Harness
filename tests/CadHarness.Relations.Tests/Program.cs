using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using Environment = System.Environment;

[assembly: SupportedOSPlatform("windows")]

namespace CadHarness.Relations.Tests;

internal sealed record HoleObservation(double XMm, double YMm, double DiameterMm, double[] BoundaryZMm);
internal sealed record LayoutObservation(int SolidBodies, double VolumeMm3, IReadOnlyList<HoleObservation> Holes);
internal sealed class NativeReport
{
    public string Status { get; set; } = "BLOCKED";
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public string? SolidWorksRevision { get; set; }
    public ResourceSnapshot? ResourceGuard { get; set; }
    public CompositionExecutionResult? Creation { get; set; }
    public RelationEditResult? Edit { get; set; }
    public List<RelationEditResult> Edits { get; set; } = new();
    public LayoutObservation? Before { get; set; }
    public LayoutObservation? After { get; set; }
    public List<LayoutObservation> Intermediate { get; set; } = new();
    public long? StateRevision { get; set; }
    public int? Relations { get; set; }
    public int? Dependencies { get; set; }
    public double? BoundParameterAfter { get; set; }
    public bool TargetPersistentReferencePreserved { get; set; }
    public bool OriginalActiveRestored { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public string? CleanupError { get; set; }
    public BudgetSnapshot? MilestoneBudget { get; set; }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[1] == "--pure") return PureTests.Run(args[0]);
        if (args.Length is >= 3 and <= 4 && args[1] == "--live" && args[2] is "linear" or "rect2x2" or "rect2x3" or "combined")
            return Live(args[0], args[2], args.Length == 4 ? args[3] : null);
        Console.WriteLine("Usage: CadHarness.Relations.Tests <workspace> --pure | --live linear|rect2x2|rect2x3|combined [part.prtdot]"); return 2;
    }
    private static int Live(string root, string name, string? template)
    {
        var output = Path.Combine(root, "artifacts", "milestone5"); Directory.CreateDirectory(output);
        NativeResourceGuard.TestTitlePrefix = "CADHarnessM5Test_";
        var report = new NativeReport(); NativeTestBudget? budget = null; SolidWorksConnection? connection = null; TestPartScope? part = null;
        try
        {
            var fixture = PureTests.Fixture(root, name); var backend = new RelationBackend();
            var preflight = backend.Preflight(fixture);
            if (!preflight.IsValid) throw new TestFailure(preflight.FailureCode!, preflight.Message);
            budget = new(Path.Combine(output, "native-budget.json"), "Milestone 5", 3);
            if (budget.Snapshot.CreationAttempts >= 3) throw new TestFailure("ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED", "M5 Part budget is exhausted; no Part created.");
            Console.WriteLine("M5 " + name + ": connecting; no Part created yet.");
            connection = SolidWorksConnection.Connect(); report.SolidWorksRevision = connection.Application.RevisionNumber();
            var partTemplate = connection.ResolvePartTemplate(template);
            report.ResourceGuard = NativeResourceGuard.Inspect(connection.Application, budget);
            var guard = report.ResourceGuard; Console.WriteLine(JsonSerializer.Serialize(guard));
            var limit = NativeResourceGuard.Evaluate(guard.Responding, guard.GdiCount, guard.OpenTestOwnedParts);
            if (limit is not null) throw new TestFailure(limit, "Resource guard refused Part creation.");
            part = new(connection.Application, budget); var context = part.Create(connection, partTemplate);
            report.Creation = backend.Create(context, fixture); Console.WriteLine(JsonSerializer.Serialize(report.Creation));
            if (!report.Creation.Succeeded) throw new TestFailure(report.Creation.FailureCode!, report.Creation.Message);
            report.Before = Observe(context);
            if (name == "combined") VerifyCombined(report.Before, false, false); else Verify(name, false, report.Before);
            var before = context.CaptureBindingState();
            var parameter = name == "linear" ? EditableParameter.PatternSpacing : name == "rect2x2" ? EditableParameter.PatternSpacingX : EditableParameter.PatternCountY;
            var value = name == "linear" ? 50.0 : name == "rect2x2" ? 70.0 : 4.0;
            var editRequests = name == "combined" ? new[]
                { PureTests.Edit(EditableParameter.PatternSpacing, 50, "linear_holes"), PureTests.Edit(EditableParameter.PatternSpacingX, 70) } :
                new[] { PureTests.Edit(parameter, value) };
            for (var i = 0; i < editRequests.Length; i++)
            {
                report.Edit = backend.Edit(context, context.CaptureBindingState(), editRequests[i]); report.Edits.Add(report.Edit);
                Console.WriteLine(JsonSerializer.Serialize(report.Edit));
                if (!report.Edit.Succeeded) throw new TestFailure(report.Edit.FailureCode!, report.Edit.Message);
                report.After = Observe(context); report.Intermediate.Add(report.After);
                if (name == "combined") VerifyCombined(report.After, true, i == 1); else Verify(name, true, report.After);
            }
            if (name == "combined") { parameter = EditableParameter.PatternSpacingX; value = 70; }
            var after = context.CaptureBindingState();
            report.StateRevision = after.Revision; report.Relations = StateRelationData.Relations(after).Count; report.Dependencies = StateRelationData.Dependencies(after).Count;
            var binding = after.Bindings.Single(b => b.OwnerFeatureSemanticId == "holes" && b.Parameter == parameter);
            report.BoundParameterAfter = after.Parameters.Single(p => p.SemanticId == binding.ParameterSemanticId).Value;
            report.TargetPersistentReferencePreserved = before.Features.Single(f => f.SemanticId == "holes").NativeReference == after.Features.Single(f => f.SemanticId == "holes").NativeReference;
            Check(after.Revision == editRequests.Length && report.Relations == (name == "combined" ? 10 : 5) && Math.Abs(report.BoundParameterAfter.Value - value) < 1e-6 && report.TargetPersistentReferencePreserved,
                "Bound native parameter, revision, relations or feature identity did not survive the edit.");
            if (name == "combined")
            {
                var linearBinding = after.Bindings.Single(b => b.OwnerFeatureSemanticId == "linear_holes" && b.Parameter == EditableParameter.PatternSpacing);
                Near(after.Parameters.Single(p => p.SemanticId == linearBinding.ParameterSemanticId).Value, 50);
                Check(before.Features.Single(f => f.SemanticId == "linear_holes").NativeReference == after.Features.Single(f => f.SemanticId == "linear_holes").NativeReference, "Linear pattern identity was lost.");
            }
            var seed = after.Entities.Single(e => e.SemanticId == "hole_seed.profile");
            Check(seed.ReferenceHealth == ReferenceHealth.Healthy && seed.Geometry is not null, "Edited seed profile is not healthy in CADState.");
            report.Status = "COMPLETE"; report.Message = "Required centered edit passed with native geometry, relation binding and persistent feature identity checks.";
        }
        catch (TestFailure error) { report.FailureCode = error.Code; report.Message = error.Message; }
        catch (Exception error) { report.FailureCode = "NATIVE_TEST_FAILED"; report.Message = error.GetType().Name + ": " + error.Message; }
        finally
        {
            if (part is not null)
            {
                part.Cleanup(); report.PartsCreated = part.Created ? 1 : 0; report.PartsClosed = part.Closed ? 1 : 0;
                report.OriginalActiveRestored = part.OriginalActiveRestored; report.CleanupError = part.CleanupError;
                if (part.CleanupError is not null) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; }
            }
            if (connection is not null)
                try { connection.Dispose(); }
                catch (Exception error) { report.Status = "BLOCKED"; report.FailureCode = "TEST_CLEANUP_FAILED"; report.CleanupError = (report.CleanupError ?? "") + error.Message; }
            if (budget is not null) { report.MilestoneBudget = budget.Snapshot; budget.Dispose(); }
            var path = Path.Combine(output, "native-result.json");
            var reports = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, NativeReport>>(File.ReadAllText(path))! : new();
            // A budget/resource rejection without a created Part must preserve
            // prior native verification evidence for this case.
            if (report.PartsCreated > 0 || !reports.ContainsKey(name)) reports[name] = report;
            File.WriteAllText(path, JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        return report.Status == "COMPLETE" ? 0 : 1;
    }
    private static LayoutObservation Observe(SolidWorksExecutionContext context)
    {
        var bodies = (Array)((IPartDoc)context.Document).GetBodies2(0, false);
        Check(bodies.Length == 1, "Expected one native solid body."); var body = (IBody2)bodies.GetValue(0)!;
        var holes = new List<HoleObservation>();
        foreach (var face in ((Array)body.GetFaces()).Cast<object>().Cast<IFace2>())
        {
            var surface = (ISurface)face.GetSurface(); if (!surface.IsCylinder()) continue;
            var c = (double[])surface.CylinderParams;
            var levels = ((Array)face.GetEdges()).Cast<object>().Cast<IEdge>().Select(e => (ICurve)e.GetCurve()).Where(curve => curve.IsCircle())
                .Select(curve => ((double[])curve.CircleParams)[2] * 1000).Distinct().OrderBy(z => z).ToArray();
            holes.Add(new(c[0] * 1000, c[1] * 1000, c[6] * 2000, levels));
        }
        return new(1, ((double[])body.GetMassProperties(1))[3] * 1e9, holes.OrderBy(h => h.XMm).ThenBy(h => h.YMm).ToArray());
    }
    private static void Verify(string name, bool edited, LayoutObservation geometry)
    {
        var xs = name == "linear" ? new[] { edited ? -25.0 : -20.0, edited ? 25.0 : 20.0 } :
            name == "rect2x2" ? new[] { edited ? -35.0 : -30.0, edited ? 35.0 : 30.0 } : new[] { -30.0, 30.0 };
        var ys = name == "linear" ? new[] { 0.0 } : name == "rect2x2" ? new[] { -15.0, 15.0 } :
            edited ? new[] { -30.0, -10.0, 10.0, 30.0 } : new[] { -20.0, 0.0, 20.0 };
        var positions = xs.SelectMany(x => ys.Select(y => (x, y))).ToArray();
        var diameter = name == "rect2x2" ? 6.0 : 8.0; var thickness = name == "rect2x2" ? 8.0 : 10.0;
        Check(geometry.Holes.Count == positions.Length, "Native hole count differs from edited layout.");
        for (var i = 0; i < positions.Length; i++)
        {
            var hole = geometry.Holes[i]; Near(hole.XMm, positions[i].x); Near(hole.YMm, positions[i].y); Near(hole.DiameterMm, diameter);
            Check(hole.BoundaryZMm.Length == 2, "Native through-hole boundaries are missing."); Near(hole.BoundaryZMm[0], 0); Near(hole.BoundaryZMm[1], thickness);
        }
        Near(geometry.Holes.Average(h => h.XMm), 0); Near(geometry.Holes.Average(h => h.YMm), 0);
        var width = name == "linear" ? 80.0 : name == "rect2x2" ? 100.0 : 120.0;
        var height = name == "linear" ? 50.0 : name == "rect2x2" ? 60.0 : 80.0;
        var volume = width * height * thickness - positions.Length * Math.PI * diameter * diameter / 4 * thickness;
        Check(Math.Abs(geometry.VolumeMm3 - volume) < 0.001, "Native solid volume differs from expected holes.");
    }
    private static void VerifyCombined(LayoutObservation geometry, bool linearEdited, bool rectangleEdited)
    {
        var linear = geometry.Holes.Where(h => Math.Abs(h.DiameterMm - 8) < 1e-6).ToArray();
        var rectangle = geometry.Holes.Where(h => Math.Abs(h.DiameterMm - 6) < 1e-6).ToArray();
        Check(linear.Length == 2 && rectangle.Length == 4 && geometry.Holes.Count == 6, "Combined composition must contain 2 linear and 4 rectangular holes.");
        var lx = linearEdited ? 25.0 : 20.0; var rx = rectangleEdited ? 35.0 : 30.0;
        var expectedLinear = new[] { (-lx, 0.0), (lx, 0.0) };
        var expectedRect = new[] { (-rx, -15.0), (-rx, 15.0), (rx, -15.0), (rx, 15.0) };
        for (var i = 0; i < 2; i++) { Near(linear[i].XMm, expectedLinear[i].Item1); Near(linear[i].YMm, expectedLinear[i].Item2); }
        for (var i = 0; i < 4; i++) { Near(rectangle[i].XMm, expectedRect[i].Item1); Near(rectangle[i].YMm, expectedRect[i].Item2); }
        foreach (var hole in geometry.Holes)
        { Check(hole.BoundaryZMm.Length == 2, "Missing through-hole wall boundaries."); Near(hole.BoundaryZMm[0], 0); Near(hole.BoundaryZMm[1], 8); }
        Near(linear.Average(h => h.XMm), 0); Near(linear.Average(h => h.YMm), 0);
        Near(rectangle.Average(h => h.XMm), 0); Near(rectangle.Average(h => h.YMm), 0);
        var expectedVolume = 100 * 60 * 8 - (2 * Math.PI * 4 * 4 + 4 * Math.PI * 3 * 3) * 8;
        Check(Math.Abs(geometry.VolumeMm3 - expectedVolume) < 0.001, "Combined native volume differs.");
    }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) <= 1e-6, $"Native measurement {actual} differs from {expected}.");
    private static void Check(bool condition, string message) { if (!condition) throw new TestFailure("RELATION_VIOLATED", message); }
}
