using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using V = CadHarness.Ir.V03.Vector3;
using Environment = System.Environment;

internal static class NativeTests
{
    internal static int RecoverOwned(string root, string interruptedRun)
    {
        var output = Path.Combine(root, "artifacts/milestone15", interruptedRun);
        using var created = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "steps/001-owned-created.json")));
        var title = created.RootElement.GetProperty("value").GetProperty("title").GetString()!;
        var expectedPath = created.RootElement.GetProperty("value").TryGetProperty("path", out var pathValue) ? pathValue.GetString()! : "";
        var controller = created.RootElement.GetProperty("controller").GetInt32();
        Program.Check(!System.Diagnostics.Process.GetProcesses().Any(p => { using(p) return p.Id == controller; }), "Old controller still running; do not take ownership.");
        using var connection = SolidWorksConnection.Connect(false);
        using var budget = new NativeTestBudget(Path.Combine(root, "artifacts/milestone15/native-budget.json"), "M15", 3);
        Program.Check(budget.Snapshot.OpenTestOwnedTitles.SequenceEqual(new[] { title }), "Ledger and immutable creation record do not uniquely agree.");
        var docs = connection.Application.GetDocuments() as Array;
        var matches = docs?.Cast<object>().Cast<IModelDoc2>().Where(d => d.GetTitle() == title).ToArray() ?? Array.Empty<IModelDoc2>();
        Program.Check(matches.Length == 1 && matches[0].GetPathName() == expectedPath, "Exact tool-created Part path/title cannot be confirmed; no document closed.");
        Program.Write(Path.Combine(output, "recovery-before-close.json"), new { utc = DateTime.UtcNow, controller = Environment.ProcessId, ownedTitle = title, path = matches[0].GetPathName(), source = "immutable creation step + continuous ledger + exact live title/unsaved path", bodyCount = ((IPartDoc)matches[0]).GetBodies2(0, false) is Array bodies ? bodies.Length : 0 });
        connection.Application.CloseDoc(title);
        Program.Check(!NativeResourceGuard.DocumentTitles(connection.Application).Contains(title), "Owned Part remains open.");
        budget.RegisterClosed(title);
        Program.Write(Path.Combine(output, "recovery-closed.json"), new { utc = DateTime.UtcNow, controller = Environment.ProcessId, ownedTitle = title, budget = budget.Snapshot, closed = true, nativeOpenCycles = 0 });
        return 0;
    }
    internal static int Run(string root, string run, string? blankSource = null)
    {
        Program.Check(CadHarness.Ir.V03.ContractValidation.Id(run), "Unique run ID invalid.");
        var output = Path.Combine(root, "artifacts/milestone15", run);
        if (Directory.Exists(output)) throw new Exception("Never reuse a native evidence namespace.");
        Directory.CreateDirectory(output);
        var source = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root, "tests/CadHarness.PlanarSketch.Tests"), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Select(p => new { path = p, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant() }).ToArray();
        var top = new SpatialPlacement(new("host.top_face", SemanticType.PlanarFace), new("host.axis_y", SemanticType.ReferenceAxis),
            new(new(5, -4, 8), new(0, 1, 0), new(-1, 0, 0), new(0, 0, 1)), new(0));
        Program.Write(Path.Combine(output, "schedule.json"), new { nativeVersion = "M15 bounded planar sketch", source,
            limits = new { newParts = 3, openCycles = 4 }, operations = new[] {
                SketchCases.Operation("composite", SketchCases.Composite), SketchCases.Operation("circle", SketchCases.Circle()),
                SketchCases.Operation("rotated", SketchCases.Arcs, top), SketchCases.Operation("rings", SketchCases.Rings) },
            circleEdit = new { entity = "circle", radiusBefore = 3, radiusAfter = 4 },
            datumOffsets = new[] { 12.0, -6.0 }, ambiguity = "Two genuine native origin XY reference planes; no arbitrary first selection." });
        foreach (var item in source)
        {
            var archived = Path.Combine(output, "source", Path.GetRelativePath(root, item.path)); Directory.CreateDirectory(Path.GetDirectoryName(archived)!); File.Copy(item.path, archived, false);
        }
        var binaryDir = Path.Combine(output, "bin"); Directory.CreateDirectory(binaryDir);
        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory)) File.Copy(file, Path.Combine(binaryDir, Path.GetFileName(file)), false);
        Program.Write(Path.Combine(output, "binary-identities.json"), Directory.GetFiles(binaryDir).Select(p => new { path = p, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant() }));
        string? failure = null; var steps = 0; bool cleanup = false; SolidWorksConnection? connection = null; NativeTestBudget? budget = null; TestPartScope? scope = null;
        ResourceSnapshot? resources = null; string[] before = Array.Empty<string>(); string[] after = Array.Empty<string>();
        void Evidence(string name, object value) { Program.Write(Path.Combine(output, "steps", (++steps).ToString("D3") + "-" + name + ".json"), new { utc = DateTime.UtcNow, controller = Environment.ProcessId, name, value }); Console.WriteLine("PASS " + name); }
        try
        {
            budget = new(Path.Combine(root, "artifacts/milestone15/native-budget.json"), "M15", 3);
            connection = SolidWorksConnection.Connect(false);
            NativeResourceGuard.TestTitlePrefix = "CADHarnessM15Test_";
            var app = connection.Application; before = NativeResourceGuard.DocumentTitles(app);
            resources = NativeResourceGuard.Inspect(app, budget);
            Program.Check(NativeResourceGuard.Evaluate(resources.Responding, resources.GdiCount, resources.OpenTestOwnedParts) is null, "Native resource/ownership guard refused.");
            var template = connection.ResolvePartTemplate(Environment.GetEnvironmentVariable("CAD_HARNESS_PART_TEMPLATE"));
            Program.Write(Path.Combine(output, "template.json"), new { path = template, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(template))).ToLowerInvariant() });
            scope = new(app, budget); SolidWorksExecutionContext context;
            if (blankSource is null)
            {
                context = scope.Create(connection, template);
                var blank = Path.Combine(output, "blank-source.SLDPRT"); var error = 0; var warning = 0;
                Program.Check(context.Document.Extension.SaveAs(blank, 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null!, ref error, ref warning) && error == 0 && warning == 0, "Could not preserve the owned pristine Part."); scope.RefreshOwnedTitle();
                Program.Write(Path.Combine(output, "blank-source.json"), new { path = blank, sha256 = ManagedRevisionStore.Hash(blank), creationCount = budget.Snapshot.CreationAttempts });
            }
            else
            {
                var path = Path.GetFullPath(blankSource); var allowed = Path.GetFullPath(Path.Combine(root, "artifacts/milestone15")) + Path.DirectorySeparatorChar;
                Program.Check(path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path) == "blank-source.SLDPRT", "Only the saved pristine tool-created M15 Part may be reopened.");
                var recordPath = Path.Combine(Path.GetDirectoryName(path)!, "blank-source.json"); using var record = System.Text.Json.JsonDocument.Parse(File.ReadAllText(recordPath));
                Program.Check(record.RootElement.GetProperty("path").GetString() == path && record.RootElement.GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(), "Pristine source identity drifted.");
                var working = Path.Combine(output, "working.SLDPRT"); File.Copy(path, working, false); context = scope.OpenOwnedCopy(working, 4);
                Program.Check(((IPartDoc)context.Document).GetBodies2(0, false) is not Array b || b.Length == 0, "Expected a pristine owned Part.");
            }
            context.SketchEvidenceSink = trace => Evidence("native-api", trace);
            Evidence("owned-created", new { resources, title = context.Document.GetTitle(), path = context.Document.GetPathName(), revision = app.RevisionNumber(), budget = budget.Snapshot });
            void Guard() { var r = NativeResourceGuard.Inspect(app, budget); Program.Check(r.Responding && r.GdiCount < 7000 && r.OpenTestOwnedParts == 1, "Mid-session resource guard refused."); }
            var composite = context.CreatePlanarSketch(SketchCases.Operation("composite", SketchCases.Composite));
            IndependentOracle(app, context, composite);
            Evidence("composite-outer-inner-slot", new { binding = composite, readback = context.ReadPlanarSketch(composite) }); Guard();
            var circle = context.CreatePlanarSketch(SketchCases.Operation("circle", SketchCases.Circle()));
            IndependentOracle(app, context, circle); var beforeReference = circle.Profile;
            Evidence("circle-before", new { binding = circle, readback = context.ReadPlanarSketch(circle) });
            circle = context.EditSketchRadius(circle, "circle", new(3), new(4));
            IndependentOracle(app, context, circle);
            Program.Check(circle.Revision == 1 && circle.Profile == beforeReference, "Edit did not preserve profile identity or increment one sketch revision.");
            Evidence("circle-supported-radius-edit", new { binding = circle, readback = context.ReadPlanarSketch(circle), persistentProfileUnchanged = true }); Guard();
            context.SketchEvidenceSink = trace =>
            {
                Evidence("native-api", trace);
                if (trace.Stage == "setter-return") throw new InvalidOperationException("M15 controlled post-Setter interruption");
            };
            try { context.EditSketchRadius(circle, "circle", new(4), new(5)); throw new Exception("Controlled failure was not injected."); }
            catch (InvalidOperationException e) when (e.Message == "M15 controlled post-Setter interruption")
            {
                IndependentOracle(app, context, circle);
                Evidence("radial-edit-rollback", new { binding = circle, readback = context.ReadPlanarSketch(circle), uncommittedAttempt = "4->5", restoredRadiusMm = 4, revisionUnchanged = circle.Revision == 1 });
            }
            finally { context.SketchEvidenceSink = trace => Evidence("native-api", trace); }
            var rings = context.CreatePlanarSketch(SketchCases.Operation("rings", SketchCases.Rings)); IndependentOracle(app, context, rings);
            Evidence("concentric-coincident-circles", context.ReadPlanarSketch(rings));
            var baseline = new CreateExtrudeHandler().Execute(context, new OperationNode("host_op", OperationKind.CreateExtrude, "host", Array.Empty<OperationInput>(),
                new Dictionary<string, OperationParameter> { ["profile"] = new ProfileParameter(new CenteredRectangleProfile(80, 60)), ["depthMm"] = new LengthParameter(8) }));
            Program.Check(baseline.Succeeded, baseline.Message); Evidence("managed-planar-host", baseline);
            var rotated = context.CreatePlanarSketch(SketchCases.Operation("rotated", SketchCases.Arcs, top)); IndependentOracle(app, context, rotated);
            Evidence("rotated-managed-face-arc-and-line", new { binding = rotated, readback = context.ReadPlanarSketch(rotated) }); Guard();
            foreach (var offset in new[] { 12.0, -6.0 })
            {
                var id = offset > 0 ? "datum_positive" : "datum_negative";
                var placement = SketchCases.Placement with { Offset = new(offset) };
                var datum = context.CreateOffsetDatum(new(id + "_op", ConstructionKind.CreateDatumPlane, id, placement, null, null, null, null, null, null, null, null, null));
                var placed = new SpatialPlacement(new(id + ".plane", SemanticType.ReferencePlane), null, SketchCases.XY with { OriginMm = new(0, 0, offset) }, new(0));
                var sketch = context.CreatePlanarSketch(SketchCases.Operation(id + "_sketch", SketchCases.Circle(10, -5, 2), placed)); IndependentOracle(app, context, sketch);
                Evidence(id + "-plane-and-sketch", new { datum, binding = sketch, readback = context.ReadPlanarSketch(sketch) }); Guard();
            }
            var reversed = top with { Frame = top.Frame with { YAxis = new(1, 0, 0), ZAxis = new(0, 0, -1) } };
            var count = FeatureCount(context.Document);
            try { context.CreatePlanarSketch(SketchCases.Operation("reverse", SketchCases.Arcs, reversed)); throw new Exception("Reversed face accepted."); }
            catch (ContractException e) when (e.Code == "SPATIAL_FRAME_MISMATCH") { Program.Check(FeatureCount(context.Document) == count, "Rejected frame changed feature inventory."); Evidence("native-reversed-face-refusal", new { e.Code, nativeFeaturesBefore = count, nativeFeaturesAfter = FeatureCount(context.Document), mutationStarted = false }); }
            // Manufacture a real competing spatial reference only in this owned
            // disposable Part. It is not a malformed string or duplicate request.
            var first = (IFeature?)context.Document.FirstFeature(); IFeature? xy = null;
            while (first is not null)
            {
                if (first.GetTypeName2() == "RefPlane") { var m = ((IRefPlane)first.GetSpecificFeature2()).Transform.ArrayData as double[]; if (m is not null && Math.Abs(Math.Abs(m[8]) - 1) < 1e-8 && Math.Abs(m[11]) < 1e-9) { xy = first; break; } }
                first = (IFeature?)first.GetNextFeature();
            }
            Program.Check(xy is not null && xy.Select2(false, 0), "Cannot select independent ambiguity setup plane.");
            var duplicate = (IFeature?)((IFeatureManager)context.Document.FeatureManager).InsertRefPlane((int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Coincident, 0, 0, 0, 0, 0);
            Program.Check(duplicate is not null && context.Document.ForceRebuild3(false), "Real coincident plane setup failed.");
            count = FeatureCount(context.Document);
            try { context.CreatePlanarSketch(SketchCases.Operation("ambiguous", SketchCases.Circle())); throw new Exception("Ambiguous principal plane accepted."); }
            catch (Exception e) when (e is ICadFailure f && f.Code == "BINDING_AMBIGUOUS")
            { Program.Check(FeatureCount(context.Document) == count, "Ambiguous request mutated native history."); Evidence("native-spatial-ambiguity-refusal", new { error = e.Message, code = "BINDING_AMBIGUOUS", original = PersistentReferenceAdapter.Capture(context, xy!), competing = PersistentReferenceAdapter.Capture(context, duplicate!), nativeFeaturesBefore = count, nativeFeaturesAfter = FeatureCount(context.Document), mutationStarted = false }); }
            _ = context.ReadPlanarSketch(circle); IndependentOracle(app, context, circle);
            Evidence("edit-reference-stable-after-later-features", new { reference = circle.Profile, readback = context.ReadPlanarSketch(circle) });
        }
        catch (Exception e) { failure = e.ToString(); Program.Write(Path.Combine(output, "failure.json"), new { utc = DateTime.UtcNow, controller = Environment.ProcessId, stepCount = steps, error = failure }); Console.Error.WriteLine(failure); }
        finally
        {
            scope?.Cleanup();
            if (connection is not null) after = NativeResourceGuard.DocumentTitles(connection.Application);
            cleanup = scope is null || (!(scope.Created || scope.Opened) || scope.Closed) && scope.OriginalActiveRestored && scope.CleanupError is null;
            Program.Write(Path.Combine(output, "result.json"), new { passed = failure is null && cleanup && before.OrderBy(x => x).SequenceEqual(after.OrderBy(x => x)), failure, steps, resources, before, after, cleanup, cleanupError = scope?.CleanupError, budget = budget?.Snapshot, nativeOpenCycles = budget?.Snapshot.OpenAttempts, controller = Environment.ProcessId });
            budget?.Dispose(); connection?.Dispose();
        }
        return failure is null && cleanup && before.OrderBy(x => x).SequenceEqual(after.OrderBy(x => x)) ? 0 : 1;
    }
    private static int FeatureCount(IModelDoc2 doc)
    { var count = 0; for (var f = (IFeature?)doc.FirstFeature(); f is not null; f = (IFeature?)f.GetNextFeature()) { if (++count > 256) throw new Exception("Inventory bound."); } return count; }
    private static void IndependentOracle(ISldWorks app, SolidWorksExecutionContext context, PlanarSketchBinding binding)
    {
        var feature = PersistentReferenceAdapter.Resolve<IFeature>(context, binding.Profile).NativeObject ?? throw new Exception("Oracle profile unresolved.");
        var sketch = (ISketch)feature.GetSpecificFeature2();
        Program.Check(sketch.GetConstrainedStatus() == 3 && sketch.GetSketchSegments() is Array array && array.Length == binding.Primitives.Count, "Oracle solver/entity count mismatch.");
        var utility = (IMathUtility)app.GetMathUtility();
        V ReadPoint(ISketchPoint p) { var point = (IMathPoint)utility.CreatePoint(new[] { p.X, p.Y, p.Z }); var world = (IMathPoint)point.MultiplyTransform(sketch.ModelToSketchTransform.IInverse()); var a = (double[])world.ArrayData; return new(a[0] * 1000, a[1] * 1000, a[2] * 1000); }
        // The independent check uses official MathPoint/MathTransform, not the
        // production array transform or geometry-validation implementation.
        V Expected(SketchPoint2 p)
        {
            var f = binding.Placement.Frame; var o = binding.Placement.Offset.Millimeters;
            return new(f.OriginMm.X + f.XAxis.X * p.X + f.YAxis.X * p.Y + f.ZAxis.X * o,
                f.OriginMm.Y + f.XAxis.Y * p.X + f.YAxis.Y * p.Y + f.ZAxis.Y * o,
                f.OriginMm.Z + f.XAxis.Z * p.X + f.YAxis.Z * p.Y + f.ZAxis.Z * o);
        }
        void Near(V a, V b) => Program.Check(Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01 && Math.Abs(a.Z - b.Z) < 0.01, "Independent world-coordinate oracle mismatch.");
        foreach (var item in binding.Primitives)
        {
            var native = PersistentReferenceAdapter.Resolve<ISketchSegment>(context, item.Reference).NativeObject ?? throw new Exception("Oracle segment unresolved.");
            if (native is ISketchArc arc)
            {
                Near(ReadPoint((ISketchPoint)arc.GetCenterPoint2()), Expected(item.Geometry.Center!.Value));
                Program.Check(Math.Abs(arc.GetRadius() * 1000 - item.Geometry.RadiusMm) < 0.01, "Independent radius mismatch.");
                if (!item.Geometry.FullCircle)
                {
                    Near(ReadPoint((ISketchPoint)arc.GetStartPoint2()), Expected(item.Geometry.Start)); Near(ReadPoint((ISketchPoint)arc.GetEndPoint2()), Expected(item.Geometry.End));
                    var z = binding.Placement.Frame.ZAxis;
                    var normal = (IMathVector)utility.CreateVector(new[] { z.X, z.Y, z.Z });
                    var localNormal = (double[])((IMathVector)normal.MultiplyTransform(sketch.ModelToSketchTransform)).ArrayData;
                    Program.Check(arc.GetRotationDir() == Math.Sign(item.Geometry.SweepRadians) * Math.Sign(localNormal[2]), "Independent signed arc sweep mismatch.");
                }
            }
            else
            { var line = (ISketchLine)native; Near(ReadPoint((ISketchPoint)line.GetStartPoint2()), Expected(item.Geometry.Start)); Near(ReadPoint((ISketchPoint)line.GetEndPoint2()), Expected(item.Geometry.End)); }
            var expectedLength = item.Geometry.Circular ? item.Geometry.RadiusMm * Math.Abs(item.Geometry.SweepRadians) : (item.Geometry.End - item.Geometry.Start).Length;
            Program.Check(Math.Abs(native.GetLength() * 1000 - expectedLength) < ContractLimits.LinearToleranceMm, "Independent primitive length mismatch.");
            Program.Check(feature.GetErrorCode2(out var warning) == 0 && !warning, "Native sketch health failure.");
        }
    }
}
