using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

internal static class NativeTests
{
    internal static int Run(string root, string run, string scenario, string? blank)
    {
        Program.Check(ContractValidation.Id(run) && scenario is "torus" or "quarter" or "boss" or "cut" or "composition" or "public_torus" or "public_composition", "Unknown native slot.");
        var openLimit = int.TryParse(Environment.GetEnvironmentVariable("CAD_HARNESS_M16_OPEN_LIMIT"), out var configuredLimit) ? configuredLimit : 5;
        Program.Check(openLimit >= 5, "Native open limit invalid.");
        var output = Path.Combine(root, "artifacts/milestone16", run);
        if (Directory.Exists(output)) throw new Exception("Never reuse native evidence namespaces.");
        Directory.CreateDirectory(output);
        var schedule = scenario.Contains("torus", StringComparison.Ordinal) ? Cases.Torus : scenario == "quarter" ? Cases.Quarter : scenario == "boss" ? Cases.Boss : scenario == "cut" ? Cases.Cut : Cases.Composition;
        var source = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root, "tests/CadHarness.FeatureConstruction.Tests"), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)).Select(p => new { path = p, sha256 = Hash(p) }).ToArray();
        Program.Write(Path.Combine(output, "schedule.json"), new { scenario, source, schedule, candidates = !scenario.StartsWith("public_", StringComparison.Ordinal), limits = new { newParts = 4, openCycles = openLimit } });
        foreach (var file in source) { var target = Path.Combine(output, "source", Path.GetRelativePath(root, file.path)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file.path, target, false); }
        Directory.CreateDirectory(Path.Combine(output, "bin"));
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(output, "bin", Path.GetFileName(file)), false);
        NativeTestBudget? ledger = null; SolidWorksConnection? connection = null; TestPartScope? scope = null; string? failure = null; var steps = 0;
        var before = Array.Empty<string>(); var after = Array.Empty<string>();
        void Evidence(string name, object value) { Program.Write(Path.Combine(output, "steps", (++steps).ToString("D3") + "-" + name + ".json"), new { utc = DateTime.UtcNow, controller = Environment.ProcessId, name, value }); Console.WriteLine("STEP " + name); }
        try
        {
            ledger = new(Path.Combine(root, "artifacts/milestone16/native-budget.json"), "M16", 4);
            connection = SolidWorksConnection.Connect(false); NativeResourceGuard.TestTitlePrefix = "CADHarnessM16Test_";
            var app = connection.Application; before = NativeResourceGuard.DocumentTitles(app);
            var resources = NativeResourceGuard.Inspect(app, ledger);
            Program.Check(resources.Responding && resources.GdiCount < 7000 && resources.OpenTestOwnedParts == 0, "Resource/ownership guard refused.");
            scope = new(app, ledger); SolidWorksExecutionContext context;
            if (blank is null)
            {
                var template = connection.ResolvePartTemplate(Environment.GetEnvironmentVariable("CAD_HARNESS_PART_TEMPLATE"));
                context = scope.Create(connection, template);
                Evidence("owned-created", new { title = context.Document.GetTitle(), path = context.Document.GetPathName(), budget = ledger.Snapshot, resources });
                var blankPath = Path.Combine(output, "blank-source.SLDPRT"); var err = 0; var warn = 0;
                Program.Check(context.Document.Extension.SaveAs(blankPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null!, ref err, ref warn) && err == 0 && warn == 0, "Cannot save pristine owned source.");
                scope.RefreshOwnedTitle(); Program.Write(Path.Combine(output, "blank-source.json"), new { path = blankPath, sha256 = ManagedRevisionStore.Hash(blankPath) });
                var workingPath = Path.Combine(output, "working.SLDPRT");
                Program.Check(context.Document.Extension.SaveAs(workingPath, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null!, ref err, ref warn) && err == 0 && warn == 0, "Cannot save distinct working copy.");
                scope.RefreshOwnedTitle();
            }
            else
            {
                var sourcePath = Path.GetFullPath(blank);
                Program.Check(sourcePath.StartsWith(Path.GetFullPath(Path.Combine(root, "artifacts/milestone16")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Only owned M16 blank files may be copied.");
                using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(sourcePath)!, "blank-source.json")));
                Program.Check(string.Equals(Path.GetFullPath(record.RootElement.GetProperty("path").GetString()!), sourcePath, StringComparison.OrdinalIgnoreCase) && record.RootElement.GetProperty("sha256").GetString() == Hash(sourcePath), "Blank identity drifted.");
                var copy = Path.Combine(output, "working.SLDPRT"); File.Copy(sourcePath, copy, false); context = scope.OpenOwnedCopy(copy, openLimit);
                Evidence("owned-opened", new { title = context.Document.GetTitle(), path = context.Document.GetPathName(), budget = ledger.Snapshot, resources });
            }
            var candidate = !scenario.StartsWith("public_", StringComparison.Ordinal);
            context.FeatureEvidenceSink = trace => Evidence("feature-api", trace);
            context.SketchEvidenceSink = trace => Evidence("sketch-api", trace);
            OracleEvidence = Evidence;
            MutationResult Construct(ConstructionProgram p) => candidate ? context.ConstructGenericCandidate(p) : context.ConstructGeneric(p);
            MutationResult Edit(string target, ParameterKey key, double oldValue, double value)
            {
                var request = new GenericScalarCommand(target, key, oldValue, value, context.CaptureGenericConstructionState().Revision);
                return candidate ? context.EditGenericScalarCandidate(request) : context.EditGenericScalar(request);
            }
            void Succeeded(string name, MutationResult result)
            {
                Evidence(name, new { result, state = result.RollbackFailureCode is null ? context.CaptureGenericConstructionState() : null });
                Program.Check(result.Succeeded && result.StateCommitted && result.RebuildSucceeded, name + ": " + result.FailureCode + " " + result.Message + " rollback=" + result.RollbackFailureMessage);
                var r = NativeResourceGuard.Inspect(app, ledger);
                Program.Check(r.Responding && r.GdiCount < 7000 && r.OpenTestOwnedParts == 1, "Mid-session resource guard refused.");
            }
            void Refused(string name, Func<MutationResult> request)
            {
                var old = context.CaptureGenericConstructionState(); var result = request();
                var live = context.CaptureGenericConstructionState();
                Evidence(name, new { result, unchanged = GenericConstructionStatePolicy.MatchesNativeSnapshot(old, live) });
                Program.Check(!result.Succeeded && !result.MutationStarted && !result.RollbackAttempted && result.Stage == "resolve inputs" &&
                    GenericConstructionStatePolicy.MatchesNativeSnapshot(old, live), "Invalid construction/edit did not refuse before checkpoint.");
            }
            if (!candidate)
            {
                if (scenario == "public_torus") Refused("public-invalid-axis-refusal", () => Construct(Cases.Torus with { Operations = new[] {
                    Cases.Torus.Operations[0], Cases.Torus.Operations[1] with { Axis = new("world.axis_y", CadHarness.Ir.SemanticType.ReferenceAxis) } } }));
                else Refused("public-unproved-overlap-refusal", () => Construct(Cases.Composition with { Operations = Cases.Composition.Operations.Select(o =>
                    o.SemanticId == "through_sketch" ? o with { Sketch = Cases.Circle(20, 0, 4) } : o).ToArray() }));
            }
            if (scenario.Contains("torus", StringComparison.Ordinal))
            {
                Succeeded("revolve-created", Construct(Cases.Torus)); Oracle(context, "torus", 3, 6, 4); Evidence("independent-oracle-before", context.CaptureGenericConstructionState());
                var original = context.CaptureGenericConstructionState().Features.ToArray();
                Succeeded("revolve-radius-edit", Edit("revolve", ParameterKey.RevolveRadius, 3, 4)); Oracle(context, "torus", 4, 6, 4);
                Program.Check(original.Select(f => f.Reference).SequenceEqual(context.CaptureGenericConstructionState().Features.Select(f => f.Reference)), "Edit changed feature identities.");
                Evidence("independent-oracle-after", context.CaptureGenericConstructionState());
                FaultEdit(context, () => Edit("revolve", ParameterKey.RevolveRadius, 4, 5), Evidence);
                Oracle(context, "torus", 4, 6, 4);
                if (!candidate)
                {
                    Refused("public-invalid-radius-refusal", () => Edit("revolve", ParameterKey.RevolveRadius, 4, 0));
                    Refused("public-unqualified-axial-edit-refusal", () => Edit("revolve", ParameterKey.RevolveAxialSpan, 8, 10));
                }
            }
            else if (scenario == "quarter")
            { Succeeded("finite-revolve-created", Construct(Cases.Quarter)); Oracle(context, "quarter", 3, 6, 4); Evidence("independent-quarter-oracle", context.CaptureGenericConstructionState()); }
            else if (scenario == "boss")
            {
                Succeeded("additive-created", Construct(Cases.Boss)); Oracle(context, "boss", 3, 6, 4);
                Succeeded("additive-depth-edit", Edit("boss", ParameterKey.AdditiveBossDepth, 6, 8)); Oracle(context, "boss", 3, 8, 4);
                FaultEdit(context, () => Edit("boss", ParameterKey.AdditiveBossDepth, 8, 9), Evidence); Oracle(context, "boss", 3, 8, 4);
                Evidence("independent-boss-oracle", context.CaptureGenericConstructionState());
            }
            else if (scenario == "cut")
            {
                Succeeded("minimal-cut-created", Construct(Cases.Cut)); Oracle(context, "cut", 3, 6, 4);
                Succeeded("minimal-cut-depth-edit", Edit("pocket", ParameterKey.CutDepth, 4, 5)); Oracle(context, "cut", 3, 6, 5);
                FaultEdit(context, () => Edit("pocket", ParameterKey.CutDepth, 5, 6), Evidence); Oracle(context, "cut", 3, 6, 5);
            }
            else
            {
                Succeeded("initial-root", Construct(Cases.Root));
                var initial = context.CaptureGenericConstructionState(); var thrown = false;
                context.FeatureEvidenceSink = trace =>
                {
                    Evidence("feature-api", trace);
                    if (!thrown && trace.Stage == "after" && trace.SemanticId == "pocket") { thrown = true; throw new InvalidOperationException("M16 controlled late-feature postcondition failure"); }
                };
                var late = Construct(Cases.LateFailure);
                context.FeatureEvidenceSink = trace => Evidence("feature-api", trace);
                Evidence("late-feature-failure", new { late, initial, restored = late.RollbackSucceeded ? context.CaptureGenericConstructionState() : null });
                Program.Check(thrown && !late.Succeeded && late.RollbackSucceeded && !late.StateCommitted && context.CaptureGenericConstructionState().Revision == initial.Revision, "Late failure did not restore complete addition: " + late.RollbackFailureMessage);
                Oracle(context, "root", 3, 6, 4);
                Succeeded("minimal-additive-boss", Construct(Cases.Boss)); Oracle(context, "boss", 3, 6, 4);
                Succeeded("minimal-additive-depth-edit", Edit("boss", ParameterKey.AdditiveBossDepth, 6, 8)); Oracle(context, "boss", 3, 8, 4);
                FaultEdit(context, () => Edit("boss", ParameterKey.AdditiveBossDepth, 8, 9), Evidence); Oracle(context, "boss", 3, 8, 4);
                Succeeded("restore-boss-for-continuation", Edit("boss", ParameterKey.AdditiveBossDepth, 8, 6)); Oracle(context, "boss", 3, 6, 4);
                Succeeded("composition", Construct(Cases.Composition)); Oracle(context, "composition", 3, 6, 4);
                Succeeded("composition-boss-edit", Edit("boss", ParameterKey.AdditiveBossDepth, 6, 8)); Oracle(context, "composition", 3, 8, 4);
                Succeeded("composition-cut-edit", Edit("pocket", ParameterKey.CutDepth, 4, 5)); Oracle(context, "composition", 3, 8, 5);
                FaultEdit(context, () => Edit("pocket", ParameterKey.CutDepth, 5, 6), Evidence); Oracle(context, "composition", 3, 8, 5);
                Evidence("independent-composition-oracle", context.CaptureGenericConstructionState());
                var invalid = Edit("through", ParameterKey.CutDepth, 10, 11);
                Program.Check(!invalid.Succeeded && !invalid.MutationStarted && !invalid.RollbackAttempted, "Through-all has no blind depth edit qualification.");
                Evidence("unqualified-pair-refusal", invalid);
            }
            var errors = 0; var warnings = 0; var nativePath = context.Document.GetPathName(); var beforeSave = context.CaptureGenericConstructionState();
            Program.Check(context.Document.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings) && errors == 0 && warnings == 0, "Artifact save failed."); scope.RefreshOwnedTitle();
            Program.Check(GenericConstructionStatePolicy.MatchesNativeSnapshot(beforeSave, context.CaptureGenericConstructionState()), "Saving changed native/state identity or geometry.");
            Evidence("saved-native", new { path = nativePath, sha256 = ManagedRevisionStore.Hash(nativePath), controller = Environment.ProcessId, state = context.CaptureGenericConstructionState(), coldReopenClaimed = false });
        }
        catch (Exception e) { failure = e.ToString(); Program.Write(Path.Combine(output, "failure.json"), new { error = failure, steps, utc = DateTime.UtcNow }); Console.Error.WriteLine(failure); }
        finally
        {
            OracleEvidence = null;
            scope?.Cleanup();
            if (connection is not null) after = NativeResourceGuard.DocumentTitles(connection.Application);
            var cleanup = scope is null || (!(scope.Created || scope.Opened) || scope.Closed) && scope.CleanupError is null && scope.OriginalActiveRestored;
            if (!cleanup && failure is null) failure = "Owned lifecycle cleanup failed.";
            Program.Write(Path.Combine(output, "result.json"), new { passed = failure is null && before.OrderBy(x => x).SequenceEqual(after.OrderBy(x => x)), failure, steps, before, after, cleanup, budget = ledger?.Snapshot, controller = Environment.ProcessId });
            ledger?.Dispose(); connection?.Dispose();
        }
        return failure is null && before.OrderBy(x => x).SequenceEqual(after.OrderBy(x => x)) ? 0 : 1;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static Action<string, object>? OracleEvidence;
    private static void FaultEdit(SolidWorksExecutionContext c, Func<MutationResult> edit, Action<string, object> evidence)
    {
        var state = c.CaptureGenericConstructionState(); var injected = false;
        c.FeatureEvidenceSink = trace => { evidence("feature-api", trace); if (!injected && trace.Stage == "setter-return") { injected = true; throw new InvalidOperationException("M16 post-Setter controlled failure"); } };
        var result = edit(); c.FeatureEvidenceSink = trace => evidence("feature-api", trace);
        evidence("scalar-failure-rollback", new { result, before = state, after = result.RollbackSucceeded ? c.CaptureGenericConstructionState() : null });
        Program.Check(injected && !result.Succeeded && result.RollbackSucceeded && result.Revision == state.Revision && !result.StateCommitted, "Qualified edit rollback did not validate: " + result.RollbackFailureMessage);
    }
    private static void Oracle(SolidWorksExecutionContext c, string kind, double radius, double bossDepth, double pocketDepth)
    {
        var bodies = ((IPartDoc)c.Document).GetBodies2(0, false) as Array;
        Program.Check(bodies is { Length: 1 }, "Independent body-count oracle failed.");
        var body = (IBody2)bodies!.GetValue(0)!; var mass = (double[])body.GetMassProperties(1);
        var added = kind is "boss" or "composition" ? Math.PI * 25 * bossDepth : 0;
        var removedPocket = kind is "cut" or "composition" ? (32 + 4 * Math.PI) * pocketDepth : 0;
        var removedHole = kind == "composition" ? 90 * Math.PI : 0;
        var expected = kind == "torus" ? 2 * Math.PI * Math.PI * 15 * radius * radius : kind == "quarter" ? Math.PI * 100 * 20 / 4 :
            48000 + added - removedPocket - removedHole;
        var supports = new List<double>(); var surfaces = new List<object>();
        Program.Check(Math.Abs(mass[3] * 1e9 - expected) <= Math.Max(1e-3, expected * 1e-8), "Independent material volume differs.");
        var centroid = kind is "torus" or "root" ? new[] { 0.0, 0.0, kind == "root" ? 5 : 0 } :
            kind == "quarter" ? new[] { 10.0, 40 / (3 * Math.PI), 40 / (3 * Math.PI) } : new[] {
                (added * 18 - removedPocket * -18 - removedHole * -5) / expected,
                -removedHole * 20 / expected,
                (48000 * 5 + added * (10 + bossDepth / 2) - removedPocket * (10 - pocketDepth / 2) - removedHole * 5) / expected };
        for (var i = 0; i < 3; i++) Program.Check(Math.Abs(mass[i] * 1000 - centroid[i]) < ContractLimits.LinearToleranceMm, "Independent center-of-volume differs.");
        var bounds = kind == "torus" ? new[] { -radius, -15-radius, -15-radius, radius, 15+radius, 15+radius } : kind == "quarter" ? new[] { 0.0, 0, 0, 20, 10, 10 } : new[] { -40.0, -30, 0, 40, 30, added == 0 ? 10 : 10 + bossDepth };
        for (var i = 0; i < 6; i++)
        {
            var axis = i % 3; var sign = i < 3 ? -1 : 1;
            Program.Check(body.GetExtremePoint(axis == 0 ? sign : 0, axis == 1 ? sign : 0, axis == 2 ? sign : 0, out var x, out var y, out var z), "Independent support missing.");
            supports.Add(new[] { x, y, z }[axis] * 1000);
            Program.Check(Math.Abs(new[] { x, y, z }[axis] * 1000 - bounds[i]) < ContractLimits.LinearToleranceMm, "Independent support envelope differs.");
        }
        var faces = body.GetFaces() as Array ?? throw new Exception("Independent face inventory missing.");
        var cylinders = 0; var tori = 0;
        foreach (IFace2 face in faces)
        {
            var s = (ISurface)face.GetSurface();
            if (s.IsTorus())
            {
                tori++; var p = (double[])s.TorusParams;
                surfaces.Add(new { type = "torus", nativeSi = p });
                Program.Check(kind == "torus" && Math.Abs(p[6] * 1000 - 15) < 0.01 && Math.Abs(p[7] * 1000 - radius) < 0.01 && Math.Abs(Math.Abs(p[3]) - 1) < 1e-8, "Independent toroidal radii/axis differ.");
            }
            else if (s.IsCylinder())
            {
                cylinders++; var p = (double[])s.CylinderParams;
                surfaces.Add(new { type = "cylinder", nativeSi = p });
                if (kind == "quarter") Program.Check(Math.Abs(p[6] * 1000 - 10) < 0.01 && Math.Abs(Math.Abs(p[3]) - 1) < 1e-8, "Quarter revolve radial axis differs.");
                else
                {
                    var allowed = new List<(double X, double Y, double Radius)>();
                    if (added > 0) allowed.Add((18, 0, 5));
                    if (removedPocket > 0) allowed.AddRange(new[] { (-22.0, 0.0, 2.0), (-14.0, 0.0, 2.0) });
                    if (removedHole > 0) allowed.Add((-5, 20, 3));
                    Program.Check(allowed.Any(a => Math.Abs(a.X - p[0] * 1000) < 0.01 && Math.Abs(a.Y - p[1] * 1000) < 0.01 && Math.Abs(a.Radius - p[6] * 1000) < 0.01) && Math.Abs(Math.Abs(p[5]) - 1) < 1e-8, "Independent cylinder center/radius/axis differs.");
                }
            }
            else Program.Check(s.IsPlane(), "Independent Oracle found unexpected surface.");
        }
        Program.Check(kind == "torus" ? tori == 1 : cylinders == (kind == "quarter" || kind == "boss" ? 1 : kind == "composition" ? 4 : kind == "cut" ? 2 : 0), "Independent analytic surface count differs.");
        OracleEvidence?.Invoke("independent-native-oracle", new { kind, radius, bossDepth, pocketDepth, expectedVolumeMm3 = expected, measuredVolumeMm3 = mass[3] * 1e9,
            volumeDifferenceMm3 = mass[3] * 1e9 - expected, expectedCentroidMm = centroid, measuredCentroidMm = mass.Take(3).Select(v => v * 1000).ToArray(),
            expectedBoundsMm = bounds, measuredSupportsMm = supports, surfaces, passed = true });
    }
}
