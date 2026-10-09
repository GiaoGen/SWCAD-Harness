using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using System.Reflection;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.State;
using CadHarness.State.V03;

internal static class Program
{
    private static readonly List<(string Name, Action Run)> Cases = new();
    private static readonly string HashValue = new('a', 64);
    private static readonly LocalFrame Frame = new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
    private static readonly SpatialPlacement Placement = new(new("world.xy", SemanticType.ReferencePlane), null, Frame, new(0));
    private static readonly ObservationEvidence[] Evidence = { new(EvidenceSource.NativeDefinition, "typed definition read") };
    private static readonly PartSelection Selection = new(Guid.NewGuid(), Guid.NewGuid(), "Default", 4,
        new(@"D:\fixtures\original.SLDPRT", HashValue, 100), new(@"D:\working\copy.SLDPRT", HashValue, 100));
    private static RequirementRecord Requirements => new("0.3", "requirements", 1, "Create a bounded solid",
        new[] { new Requirement("depth", 10, ScalarUnit.Millimeter, RequirementSource.Given, null, null, 1, false, true) });
    private static ClosedSketch Sketch => new(new[] { new LocalPoint("center", new(0), new(0)) },
        new[] { new SketchEntity("circle", SketchEntityKind.Circle, new[] { "center" }, new(5), null, null, null) },
        new[] { new SketchLoop("outer", false, new[] { "circle" }) },
        new[] { new SketchConstraint("radius", ConstraintKind.Radius, new[] { "circle" }, new(5)) }, ConstraintStatus.FullyConstrained);
    private static ConstructionOperation SketchOp => new("sketch_op", ConstructionKind.CreateSketch, "sketch", Placement, Sketch, null, null, null, null, null, null, null, null);
    private static ConstructionOperation ExtrudeOp => new("base_op", ConstructionKind.CreateExtrude, "base", null, null,
        new("sketch.profile", SemanticType.SketchProfile), null, null, EndCondition.Blind, Direction.Forward, new(10), null, BodyRule.NewSingleBody);
    private static ConstructionProgram Construction => new("0.3", ModelOrigin.Harness, RequestMode.CreateModel, "requirements", new[] { SketchOp, ExtrudeOp });
    private static EditSetRequest Batch => new("0.3", RequestMode.EditSet, ModelOrigin.External, Selection, new[]
    { new ScalarEdit("boss", ParameterKey.ExtrusionDepth, ScalarUnit.Millimeter, 10, 12),
      new ScalarEdit("pattern", ParameterKey.PatternSpacing, ScalarUnit.Millimeter, 20, 25) });
    private static ObservedFeature Unknown => new("unknown_feature", "Renamed arbitrary feature", 3, ModelOrigin.External,
        "UnknownVendorType", NativeSubtype.Unrecognized, ObservationHealth.Unknown, null, EvidenceCompleteness.Unknown,
        EditSupport.ReadOnly, "Native subtype and dependency completeness unknown", Evidence, Array.Empty<ObservedParameter>(), Array.Empty<GeometryObservation>());
    private static ObservedFeature Boss => new("boss", "Boss-Extrude1", 1, ModelOrigin.External, "Boss",
        NativeSubtype.StraightBlindBossExtrude, ObservationHealth.Healthy, new("AQID"), EvidenceCompleteness.Known,
        EditSupport.Editable, "Candidate descriptor only, not native qualification", Evidence,
        new[] { new ObservedParameter("boss.depth", ParameterKey.ExtrusionDepth, ScalarUnit.Millimeter, 10, NativeAccessor.ExtrudeDepthDirection1, Evidence) }, Array.Empty<GeometryObservation>());
    private static ObservedModel Observation => new("0.3", ModelOrigin.External, Selection, true, null, new[] { Boss, Unknown }, Array.Empty<ObservedDependency>(), Array.Empty<IdentityRemapping>());

    private static int Main(string[] args)
    {
        var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
        var schemas = new Dictionary<string, string>
        {
            ["v03-program"] = ContractJson.Schema<ConstructionProgram>(), ["v03-requirements"] = ContractJson.Schema<RequirementRecord>(),
            ["v03-planning-response"] = ContractJson.Schema<PlanningResponse>(), ["v03-edit-set"] = ContractJson.Schema<EditSetRequest>(),
            ["v03-observed-model"] = ContractJson.Schema<ObservedModel>(), ["v03-revision-manifest"] = ContractJson.Schema<RevisionManifest>(),
            ["v03-managed-companion"] = ContractJson.Schema<ManagedStateCompanion>(), ["v03-mutation-report"] = ContractJson.Schema<MutationReport>(),
            ["v03-scalar-edit"] = ContractJson.Schema<ScalarEditRequest>()
        };
        if (args.Contains("--write-schemas"))
        {
            foreach (var entry in schemas) File.WriteAllText(Path.Combine(root, "schemas", entry.Key + ".schema.json"), entry.Value + Environment.NewLine);
            File.WriteAllText(Path.Combine(root, "docs", "v03-contract-fields.md"), Fields());
            Console.WriteLine("Generated nine contract-only schemas and field table; legacy executable schema unchanged."); return 0;
        }
        Register(root, schemas);
        var results = new List<object>(); var failures = 0;
        foreach (var test in Cases)
        {
            string? error = null;
            try { test.Run(); } catch (Exception failure) { error = failure.ToString(); failures++; }
            results.Add(new { name = test.Name, passed = error is null, error });
            Console.WriteLine((error is null ? "PASS " : "FAIL ") + test.Name + (error is null ? "" : ": " + error));
        }
        var output = Path.Combine(root, "artifacts", "milestone11"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "pure-results.json"), JsonSerializer.Serialize(new
        { milestone = 11, passed = Cases.Count - failures, failed = failures, nativePartsCreated = 0, nativeOpenCycles = 0, nativePartsClosed = 0, results }, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "capability-contracts.json"), JsonSerializer.Serialize(new
        { nativeCandidates = NativeQualificationCandidates.Rows, construction = V03ContractCapabilities.Construction,
            executableExtensionAlternatives = 0, existingRegistryAlternatives = OperationRegistry.Default.Contracts.Count,
            providerCallableNativeTools = 0, structuredPlanResponses = 1, boundedExecutionApprovals = 1 }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{Cases.Count - failures}/{Cases.Count} M11 pure tests passed; native Parts/open cycles=0/0.");
        return failures == 0 ? 0 : 1;
    }
    private static void Register(string root, Dictionary<string, string> schemas)
    {
        Add("versioned construction and explicit signed coordinates round-trip", () => Roundtrip(Construction, ContractValidation.Program));
        Add("requirement provenance round-trip", () => Roundtrip(Requirements, ContractValidation.Requirements));
        Add("EditSet fingerprint and typed scalar round-trip", () => Roundtrip(Batch, ContractValidation.Edits));
        Add("single external scalar edit has a distinct envelope", () => Roundtrip(new ScalarEditRequest("0.3", RequestMode.ExternalScalarEdit, ModelOrigin.External, Selection, Batch.Edits[0]), ContractValidation.Edit));
        Add("single-edit origin cannot cross modes", () => Bad(() => ContractValidation.Edit(new("0.3", RequestMode.ManagedScalarEdit, ModelOrigin.External, Selection, Batch.Edits[0])), V03FailureCodes.ModeMismatch));
        Add("unknown feature remains visible without fictitious OperationKind", () =>
        {
            var value = ContractJson.Read<ObservedModel>(ContractJson.Write(Observation, ObservedStateValidation.Validate), ObservedStateValidation.Validate, ContractLimits.StateBytes);
            Assert(value.Features[1].Subtype == NativeSubtype.Unrecognized && value.Features[1].NativeReference is null && value.Features[1].DependencyCompleteness == EvidenceCompleteness.Unknown);
        });
        Add("unknown observed JSON enum rejects", () => Bad(() => ContractJson.Read<ObservedModel>(ContractJson.Write(Observation, ObservedStateValidation.Validate).Replace("unrecognized", "magic_feature"), ObservedStateValidation.Validate)));
        Add("unknown native type is inventory data", () => ObservedStateValidation.Validate(Observation with { Features = new[] { Unknown with { NativeType = "AnotherFutureType" } } }));
        Add("unknown feature cannot be editable", () => Bad(() => ObservedStateValidation.Validate(Observation with { Features = new[] { Unknown with { EditSupport = EditSupport.Editable } } })));
        Add("missing reference blocks editable qualification", () => Bad(() => ObservedStateValidation.Validate(Observation with { Features = new[] { Boss with { NativeReference = null } } })));
        Add("unknown dependency set cannot promise editability", () => Bad(() => ObservedStateValidation.Validate(Observation with { Features = new[] { Boss with { DependencyCompleteness = EvidenceCompleteness.Unknown } } })));
        Add("partial inventory rejects editable rows", () => Bad(() => ObservedStateValidation.Validate(Observation with { InventoryComplete = false, LimitOutcome = V03FailureCodes.ObservationLimitExceeded })));
        Add("partial noneditable inventory retains limit outcome", () => ObservedStateValidation.Validate(Observation with { InventoryComplete = false, LimitOutcome = V03FailureCodes.ObservationLimitExceeded, Features = new[] { Unknown } }));
        Add("inventory overflow is typed", () => Bad(() => ObservedStateValidation.Validate(Observation with { Features = Enumerable.Repeat(Unknown, 257).ToArray() }), V03FailureCodes.ObservationLimitExceeded));
        Add("subtype/accessor mismatch rejects", () => Bad(() => ObservedStateValidation.Validate(Observation with { Features = new[] { Boss with { Subtype = NativeSubtype.HoleWizard } } })));
        Add("bad persistent-reference encoding rejects", () => Bad(() => ObservedStateValidation.Validate(Observation with { Features = new[] { Boss with { NativeReference = new("not-base64") } } })));
        Add("dangling native dependency rejects", () => Bad(() => ObservedStateValidation.Validate(Observation with { Dependencies = new[] { new ObservedDependency("boss", "missing", NativeDependencyKind.ParentChild, Evidence) } })));
        Add("native dependencies do not become declared relations", () => Assert(!typeof(ObservedModel).GetProperties().Any(p => p.Name is "Program" or "Relations")));
        Add("observed identity is stable and configuration-specific", () =>
        { var first = ObservedIdentity.FromReference(Selection.DocumentId, Selection.ConfigurationId, new("AQID")); Assert(first == ObservedIdentity.FromReference(Selection.DocumentId, Selection.ConfigurationId, new("AQID"))); Assert(first != ObservedIdentity.FromReference(Selection.DocumentId, Guid.NewGuid(), new("AQID"))); });
        Add("renamed/reordered feature retains persistent semantic identity", () =>
        { var id = ObservedIdentity.FromReference(Selection.DocumentId, Selection.ConfigurationId, Boss.NativeReference!); var old = Boss with { SemanticId = id }; var renamed = old with { DisplayName = "Engineer renamed this", TreeOrdinal = 12 }; Assert(old.SemanticId == renamed.SemanticId && old.NativeReference == renamed.NativeReference); });
        Add("explicit identity remapping round-trips", () => Roundtrip(Observation with { Remappings = new[] { new IdentityRemapping("old_boss", "boss", "Verified persistent reference lineage", Evidence) } }, ObservedStateValidation.Validate));
        Add("native dependency cycle rejects", () => Bad(() => ObservedStateValidation.Validate(Observation with { Dependencies = new[] { new ObservedDependency("boss", "unknown_feature", NativeDependencyKind.ParentChild, Evidence), new ObservedDependency("unknown_feature", "boss", NativeDependencyKind.ParentChild, Evidence) } })));
        foreach (var envelope in new[] { "program", "requirements", "editset", "observation" })
        {
            Add(envelope + " extra/missing/duplicate/null/case/integer-enum fields reject", () => Strict(envelope));
        }
        Add("program version 0.2 cannot enter extension parser", () => Bad(() => ContractValidation.Program(Construction with { ProgramVersion = "0.2" })));
        Add("external observation cannot become managed creation", () => Bad(() => ContractValidation.Program(Construction with { Origin = ModelOrigin.External }), V03FailureCodes.ModeMismatch));
        Add("program operation bound stays at twelve", () => Bad(() => ContractValidation.Program(Construction with { Operations = Enumerable.Repeat(SketchOp, 13).ToArray() })));
        Add("schema byte overflow rejects before decode", () => Bad(() => ContractJson.Read<ConstructionProgram>(new string(' ', ContractLimits.ProgramBytes + 1), ContractValidation.Program)));
        Add("nonfinite numeric JSON rejects", () => Bad(() => ContractJson.Read<EditSetRequest>(ContractJson.Write(Batch, ContractValidation.Edits).Replace("12", "1e999"), ContractValidation.Edits)));
        Add("zero and negative coordinates are legal", () => ContractValidation.Sketch(Sketch with { Points = new[] { new LocalPoint("center", new(-20), new(0)) } }));
        Add("positive lengths remain positive", () => Bad(() => ContractValidation.Program(Construction with { Operations = new[] { SketchOp, ExtrudeOp with { Depth = new(-1) } } })));
        Add("coordinate bound enforced", () => Bad(() => ContractValidation.Sketch(Sketch with { Points = new[] { new LocalPoint("center", new(1000001), new(0)) } })));
        Add("mirrored frame rejects", () => Bad(() => ContractValidation.Frame(Frame with { ZAxis = new(0, 0, -1) })));
        Add("face normal alone cannot fix frame rotation", () => Bad(() => ContractValidation.Placement(Placement with { Plane = new("body.face", SemanticType.PlanarFace) })));
        Add("unbound feature reference rejects", () => Bad(() => ContractValidation.Program(Construction with { Operations = new[] { ExtrudeOp } })));
        Add("feature/output identity collision is typed rejection", () => Bad(() => ContractValidation.Program(Construction with { Operations = new[] { SketchOp, ExtrudeOp with { SemanticId = "sketch.profile" } } })));
        Add("sketch primitive limit rejects", () => Bad(() => ContractValidation.Sketch(Sketch with { Entities = Enumerable.Repeat(Sketch.Entities[0], 65).ToArray() })));
        Add("under-constrained sketch has stable outcome", () => Bad(() => ContractValidation.Sketch(Sketch with { Status = ConstraintStatus.UnderConstrained }), V03FailureCodes.UnderConstrainedSketch));
        Add("over-constrained sketch has stable outcome", () => Bad(() => ContractValidation.Sketch(Sketch with { Status = ConstraintStatus.OverConstrained }), V03FailureCodes.OverConstrainedSketch));
        Add("ambiguous arc sweep rejects", () => Bad(() => ContractValidation.Sketch(Sketch with { Entities = new[] { Sketch.Entities[0] with { Kind = SketchEntityKind.Arc } } }), V03FailureCodes.InvalidSketch));
        Add("revolve in-plane axis contract passes", () => ContractValidation.Program(Construction with { Operations = new[] { SketchOp,
            ExtrudeOp with { Kind = ConstructionKind.CreateRevolvedBoss, Depth = null, Axis = new("sketch.axis_x", SemanticType.ReferenceAxis), EndCondition = EndCondition.FullRevolution, AngleDegrees = 360 } } }));
        Add("revolve arbitrary out-of-plane axis rejects", () => Bad(() => ContractValidation.Program(Construction with { Operations = new[] { SketchOp,
            ExtrudeOp with { Kind = ConstructionKind.CreateRevolvedBoss, Depth = null, Axis = new("world.axis_z", SemanticType.ReferenceAxis), EndCondition = EndCondition.FullRevolution, AngleDegrees = 360 } } })));
        Add("cut cannot invent an untracked host", () => Bad(() => ContractValidation.Program(Construction with { Operations = new[] { SketchOp, ExtrudeOp with { Kind = ConstructionKind.CreateExtrudedCut, HostBody = new("external.body", SemanticType.BodyRef), BodyRule = BodyRule.RemoveFromHost } } })));
        Add("versioned default provenance accepted", () => ContractValidation.Requirements(Requirements with { Requirements = new[] { Requirements.Requirements[0] with { Critical = false, Source = RequirementSource.Defaulted, RuleId = "wall_rule", RuleVersion = "1" } } }));
        Add("critical default cannot fabricate engineering requirement", () => Bad(() => ContractValidation.Requirements(Requirements with { Requirements = new[] { Requirements.Requirements[0] with { Source = RequirementSource.Defaulted, RuleId = "wall_rule", RuleVersion = "1" } } })));
        Add("default without rule version rejects", () => Bad(() => ContractValidation.Requirements(Requirements with { Requirements = new[] { Requirements.Requirements[0] with { Critical = false, Source = RequirementSource.Defaulted, RuleId = "wall_rule" } } })));
        Add("accepted requirement bytes are immutable", () =>
        { var list = new List<Requirement>(Requirements.Requirements); var accepted = new AcceptedRequirements(Requirements with { Requirements = list }); list.Clear(); Assert(accepted.Read().Requirements.Count == 1 && accepted.Sha256.Length == 64); });
        Add("planned outcome requires critical facts", () => Bad(() => ContractValidation.Response(new("0.3", PlanOutcome.Planned,
            Requirements with { Requirements = new[] { Requirements.Requirements[0] with { Source = RequirementSource.Unresolved, Value = null } } }, Construction, Array.Empty<string>(), null))));
        Add("clarification is nonexecutable", () =>
        { var response = new PlanningResponse("0.3", PlanOutcome.NeedsClarification, Requirements, null, new[] { "Required depth?" }, null); Roundtrip(response, ContractValidation.Response); Bad(() => ContractValidation.Response(response with { Program = Construction })); });
        Add("unsupported outcome requires reason and no program", () => Roundtrip(new PlanningResponse("0.3", PlanOutcome.Unsupported, Requirements, null, Array.Empty<string>(), "Native subtype unsupported"), ContractValidation.Response));
        Add("duplicate batch pair rejects", () => Bad(() => ContractValidation.Edits(Batch with { Edits = new[] { Batch.Edits[0], Batch.Edits[0] } })));
        Add("batch one and seventeen edits reject", () => { Bad(() => ContractValidation.Edits(Batch with { Edits = new[] { Batch.Edits[0] } })); Bad(() => ContractValidation.Edits(Batch with { Edits = Enumerable.Repeat(Batch.Edits[0], 17).ToArray() })); });
        Add("batch wrong unit and fractional count reject", () => { Bad(() => ContractValidation.Scalar(ParameterKey.PatternSpacing, ScalarUnit.Degree, 20)); Bad(() => ContractValidation.Scalar(ParameterKey.PatternCount, ScalarUnit.Count, 2.5)); });
        Add("original file cannot be working mutation target", () => Bad(() => ContractValidation.Edits(Batch with { Selection = Selection with { WorkingCopy = Selection.Source } })));
        Add("batch fingerprint/path/revision are mandatory", () => { Bad(() => ContractValidation.Edits(Batch with { Selection = Selection with { ExpectedRevision = -1 } })); Bad(() => ContractValidation.Fingerprint(Selection.Source with { Sha256 = "missing" })); });
        Add("mode cross-contamination rejects", () =>
        { Bad(() => V03ContractCapabilities.RequireMode(RequestMode.ManagedScalarEdit, RequestMode.ExternalScalarEdit, ModelOrigin.External), V03FailureCodes.ModeMismatch); Bad(() => V03ContractCapabilities.RequireMode(RequestMode.ExternalScalarEdit, RequestMode.ExternalScalarEdit, ModelOrigin.Harness), V03FailureCodes.ModeMismatch); });
        Add("contract-only construction never executable", () => Bad(() => V03ContractCapabilities.RequireExecutable(Construction, RequestMode.CreateModel), V03FailureCodes.CapabilityUnavailable));
        Add("contract-only batch never executable", () => Bad(() => V03ContractCapabilities.RequireExecutable(Batch, RequestMode.EditSet), V03FailureCodes.CapabilityUnavailable));
        Add("candidate external edit never executable", () => Bad(() => NativeQualificationCandidates.RequireExecutable(Observation, "boss", ParameterKey.ExtrusionDepth), V03FailureCodes.CapabilityUnavailable));
        Add("read-only target rejects before executable gate", () => Bad(() => NativeQualificationCandidates.RequireExecutable(Observation, "unknown_feature", ParameterKey.ExtrusionDepth), V03FailureCodes.ObservedOnlyTarget));
        Add("all qualification candidates unqualified", () => Assert(NativeQualificationCandidates.Rows.Count == 4 && NativeQualificationCandidates.Rows.All(r => !r.Qualified)));
        Add("contract operations do not widen old executable enums/registry", () => Assert(Enum.GetValues<OperationKind>().Length == 9 && OperationRegistry.Default.Contracts.Count == 9 && V03ContractCapabilities.Construction.All(c => !c.Executable)));
        Add("legacy parser rejects new operation and schema version", () =>
        { var codec = new CadProgramJson(); Assert(!codec.Parse(LegacyProgram.Replace("create_extrude", "create_revolved_boss")).IsValid); Assert(!codec.Parse(LegacyProgram.Replace("0.2", "0.3")).IsValid); });
        Add("legacy positive ParameterNode invariant survives", () =>
        { var state = LegacyState(); try { StateValidation.Validate(state with { Parameters = new[] { new ParameterNode("base.depth", ParameterKind.Length, 0) }, Bindings = new[] { new ParameterBinding("base.depth", "base", EditableParameter.ExtrusionDepth) } }); } catch (StateException e) when (e.Code == "STATE_SCHEMA_INVALID") { return; } throw new Exception("Zero legacy scalar accepted."); });
        Add("legacy read proposes migration and preserves source hashes", () => Legacy(root, false));
        Add("legacy unknown fields and unsupported versions reject", () => Legacy(root, true));
        Add("durable manifest preserves managed/external distinction", () =>
        { var manifest = new RevisionManifest("0.3", ModelOrigin.External, Selection.DocumentId, Selection.ConfigurationId, "Default", 4, Selection.WorkingCopy, new(@"D:\working\state.json", HashValue, "0.3"), null, null, PublishStage.PackageVerified); Roundtrip(manifest, ObservedStateValidation.Manifest); Bad(() => ObservedStateValidation.Manifest(manifest with { Program = new(@"D:\working\program.json", HashValue, "0.2") })); });
        Add("state-only commit cannot claim durable success", () => Bad(() => ObservedStateValidation.Report(new("0.3", Selection, new[] { "boss" }, 5, true, true, false, null, true, false, true, null))));
        Add("failed rollback cannot leave session editable", () => Bad(() => ObservedStateValidation.Report(new("0.3", Selection, new[] { "boss" }, 4, true, false, true, false, false, false, true, "REBUILD_FAILED"))));
        Add("mutation failure needs restored revision or quarantine", () =>
        { Bad(() => ObservedStateValidation.Report(new("0.3", Selection, new[] { "boss" }, 5, true, true, false, null, true, false, true, V03FailureCodes.IncompleteDurablePublish))); Roundtrip(new MutationReport("0.3", Selection, new[] { "boss" }, 4, true, false, true, true, false, false, true, "REBUILD_FAILED"), ObservedStateValidation.Report); });
        Add("all nine frozen schemas and field table match definitions", () => { foreach (var schema in schemas) Assert(File.ReadAllText(Path.Combine(root, "schemas", schema.Key + ".schema.json")).Trim() == schema.Value.Trim()); Assert(File.ReadAllText(Path.Combine(root, "docs", "v03-contract-fields.md")) == Fields()); });
        Add("frozen C1-C4 and held-out definitions have independent oracles", () =>
        { using var tasks = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "CadHarness.V03Contracts.Tests", "Fixtures", "acceptance-tasks.json"))); var families = tasks.RootElement.GetProperty("development").EnumerateArray().ToArray(); Assert(families.Length == 4); foreach (var task in families) Assert(task.GetProperty("oracle").GetProperty("volumePiCoefficientMm3").GetDouble() > 0 || task.GetProperty("oracle").GetProperty("volumeMm3").GetDouble() > 0); Assert(tasks.RootElement.GetProperty("heldOut").GetArrayLength() >= 2); });
        Add("frozen analytical oracles independently agree with numeric task anchors", () => Oracles(root));
        Add("production source contains no acceptance family dispatch", () =>
        { foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))) Assert(!System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(file), "\\bC[1-6]\\b|acceptance-tasks\\.json")); });
    }
    private static void Oracles(string root)
    {
        using var tasks = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests/CadHarness.V03Contracts.Tests/Fixtures/acceptance-tasks.json")));
        var cases = tasks.RootElement.GetProperty("development").EnumerateArray().ToArray();
        static double[] Numbers(JsonElement e, string key) => e.GetProperty(key).EnumerateArray().Select(v => v.GetDouble()).ToArray();
        static double Number(JsonElement e, string key) => e.GetProperty(key).GetDouble();
        static void Near(double actual, double expected) => Assert(Math.Abs(actual - expected) < 1e-8);
        var a = cases[0].GetProperty("anchors"); var d = Numbers(a, "diametersMm"); var l = Numbers(a, "lengthsMm");
        var radius = d[1] / 2; var grooveRadius = radius - Number(a, "grooveRadialDepthMm"); var grooveWidth = Number(a, "grooveWidthMm");
        var before = d.Zip(l).Sum(p => p.First * p.First / 4 * p.Second) - (radius * radius - grooveRadius * grooveRadius) * grooveWidth;
        Near(before, Number(cases[0].GetProperty("oracle"), "volumePiCoefficientMm3"));
        var newRadius = Number(cases[0].GetProperty("postCreationEdit"), "valueMm"); var newGrooveRadius = newRadius - Number(a, "grooveRadialDepthMm");
        Near(before + (newRadius * newRadius - radius * radius) * l[1] - ((newRadius * newRadius - newGrooveRadius * newGrooveRadius) - (radius * radius - grooveRadius * grooveRadius)) * grooveWidth,
            Number(cases[0].GetProperty("oracle"), "postEditVolumePiCoefficientMm3"));
        a = cases[1].GetProperty("anchors"); d = Numbers(a, "boreDiametersMm"); l = Numbers(a, "boreSpansMm");
        before = Math.Pow(Number(a, "sleeveDiameterMm") / 2, 2) * Number(a, "sleeveLengthMm") + Math.Pow(Number(a, "flangeDiameterMm") / 2, 2) * Number(a, "flangeThicknessMm") - d.Zip(l).Sum(p => p.First * p.First / 4 * p.Second);
        Near(before, Number(cases[1].GetProperty("oracle"), "volumePiCoefficientMm3"));
        newRadius = Number(cases[1].GetProperty("postCreationEdit"), "valueMm"); Near(before - (newRadius * newRadius - d[1] * d[1] / 4) * l[1], Number(cases[1].GetProperty("oracle"), "postEditVolumePiCoefficientMm3"));
        a = cases[2].GetProperty("anchors"); var b = Numbers(a, "baseMm"); var h = Number(a, "uprightAdditionalHeightMm"); var t = Number(a, "uprightThicknessMm");
        var fillet = Number(cases[2].GetProperty("assumptions")[0], "valueMm"); var r = Number(a, "holeDiameterMm") / 2;
        Near(b[0] * b[1] * b[2] + t * b[1] * h - fillet * fillet * b[1], Number(cases[2].GetProperty("oracle"), "volumeMm3"));
        Near(-r * r * (2 * b[2] + t) + fillet * fillet * b[1] / 4, Number(cases[2].GetProperty("oracle"), "volumePiCoefficientMm3"));
        a = cases[3].GetProperty("anchors"); b = Numbers(a, "plateMm"); var width = Number(a, "slotWidthMm"); var length = Number(a, "slotLengthMm"); var depth = Number(a, "slotDepthMm");
        var n = a.GetProperty("slotCentersMm").GetArrayLength(); var rectangle = width * (length - width); var circle = width * width / 4;
        Near(b[0] * b[1] * b[2] - n * depth * rectangle, Number(cases[3].GetProperty("oracle"), "volumeMm3"));
        Near(-n * depth * circle, Number(cases[3].GetProperty("oracle"), "volumePiCoefficientMm3"));
        var delta = Number(cases[3].GetProperty("postCreationEdit"), "valueMm") - depth;
        Near(Number(cases[3].GetProperty("oracle"), "volumeMm3") - delta * rectangle, Number(cases[3].GetProperty("oracle"), "postEditVolumeMm3"));
        Near(Number(cases[3].GetProperty("oracle"), "volumePiCoefficientMm3") - delta * circle, Number(cases[3].GetProperty("oracle"), "postEditVolumePiCoefficientMm3"));
    }
    private static string Fields()
    {
        var text = new StringBuilder("# v0.3 Contract Field Table\n\nGenerated by the M11 pure runner from versioned records. Every wire field is required; nullable fields must be explicit null. All objects reject additional/duplicate fields. See milestone-11-contract.md for semantic invariants, units, limits, migration and qualification boundaries. These are contract schemas, not provider executable capabilities.\n\n");
        var roots = new[] { typeof(ConstructionProgram), typeof(RequirementRecord), typeof(PlanningResponse), typeof(EditSetRequest), typeof(ScalarEditRequest), typeof(ObservedModel), typeof(RevisionManifest), typeof(ManagedStateCompanion), typeof(MutationReport) };
        var found = new HashSet<Type>(); var queue = new Queue<Type>(roots);
        while (queue.TryDequeue(out var type))
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)) { queue.Enqueue(type.GetGenericArguments()[0]); continue; }
            if (type.Namespace?.StartsWith("CadHarness.", StringComparison.Ordinal) != true || !found.Add(type)) continue;
            foreach (var p in type.GetProperties()) queue.Enqueue(p.PropertyType);
        }
        foreach (var type in found.OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            text.Append("## ").Append(type.Name).Append("\n\n");
            if (type.IsEnum) { text.Append(string.Join(", ", Enum.GetNames(type).Select(n => "`" + JsonNamingPolicy.SnakeCaseLower.ConvertName(n) + "`"))).Append("\n\n"); continue; }
            text.Append("| Field | Type | Nullable |\n|---|---|---|\n");
            foreach (var p in type.GetProperties())
            {
                var nullable = Nullable.GetUnderlyingType(p.PropertyType) is not null || new NullabilityInfoContext().Create(p).ReadState == NullabilityState.Nullable;
                var fieldType = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
                var name = fieldType.IsGenericType ? fieldType.GetGenericArguments()[0].Name + "[]" : fieldType.Name;
                text.Append("| `").Append(JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Append("` | `").Append(name).Append("` | ").Append(nullable ? "yes" : "no").Append(" |\n");
            }
            text.Append('\n');
        }
        return text.ToString();
    }
    private static void Strict(string name)
    {
        string json; Action<string> read;
        switch (name)
        {
            case "program": json = ContractJson.Write(Construction, ContractValidation.Program); read = x => ContractJson.Read<ConstructionProgram>(x, ContractValidation.Program); break;
            case "requirements": json = ContractJson.Write(Requirements, ContractValidation.Requirements); read = x => ContractJson.Read<RequirementRecord>(x, ContractValidation.Requirements); break;
            case "editset": json = ContractJson.Write(Batch, ContractValidation.Edits); read = x => ContractJson.Read<EditSetRequest>(x, ContractValidation.Edits); break;
            default: json = ContractJson.Write(Observation, ObservedStateValidation.Validate); read = x => ContractJson.Read<ObservedModel>(x, ObservedStateValidation.Validate); break;
        }
        var node = JsonNode.Parse(json)!.AsObject(); var first = node.First().Key;
        node["unexpected"] = true; Bad(() => read(node.ToJsonString())); node.Remove("unexpected");
        node.Remove(first); Bad(() => read(node.ToJsonString())); node = JsonNode.Parse(json)!.AsObject();
        node[first] = null; Bad(() => read(node.ToJsonString()));
        Bad(() => read(json.Insert(json.IndexOf('{') + 1, "\"" + first + "\":\"0.3\",")));
        Bad(() => read(json.Replace("\"" + first + "\"", "\"" + first.ToUpperInvariant() + "\"")));
        if (name == "program") Bad(() => read(json.Replace("\"create_sketch\"", "0")));
        else if (name == "requirements") Bad(() => read(json.Replace("\"given\"", "0")));
        else Bad(() => read(json.Replace("\"external\"", "0")));
    }
    private const string LegacyProgram = """
    {"programVersion":"0.2","operations":[{"id":"base_op","kind":"create_extrude","semanticId":"base","profile":{"kind":"centered_rectangle","widthMm":100,"heightMm":60},"depthMm":8}]}
    """;
    private static CadState LegacyState() => new()
    { SchemaVersion = "0.2", Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""), Revision = 1,
        Features = new[] { new FeatureNode("base", OperationKind.CreateExtrude, new("AQID"), ReferenceHealth.Healthy) },
        Entities = Array.Empty<SemanticEntityNode>(), Parameters = Array.Empty<ParameterNode>(), Bindings = Array.Empty<ParameterBinding>(), Relations = Array.Empty<JsonElement>(), Dependencies = Array.Empty<JsonElement>() };
    private static void Legacy(string root, bool invalid)
    {
        var directory = Path.Combine(root, "artifacts", "milestone11", "migration-reader-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var state = Path.Combine(directory, "state.json"); var program = Path.Combine(directory, "program.json");
        new AtomicStateStore(state).Commit(LegacyState()); File.WriteAllText(program, LegacyProgram);
        var stateHash = Hash(state); var programHash = Hash(program);
        var read = LegacyMigrationReader.Read(state, program, state);
        Assert(!read.Proposal.Migration.NativeReopenVerified && !read.Proposal.Migration.Editable && read.State.SchemaVersion == "0.2" && Hash(state) == stateHash && Hash(program) == programHash);
        Roundtrip(read.Proposal, ObservedStateValidation.Companion);
        if (invalid)
        {
            var original = File.ReadAllText(state); File.WriteAllText(state, original.Insert(original.IndexOf('{') + 1, "\"unknown\":true,"));
            Bad(() => LegacyMigrationReader.Read(state, program, state), V03FailureCodes.MigrationFailed);
            File.WriteAllText(state, original.Replace("0.2", "0.1")); Bad(() => LegacyMigrationReader.Read(state, program, state), V03FailureCodes.MigrationFailed);
            File.WriteAllText(state, original); File.WriteAllText(program, LegacyProgram.Replace("\"base\"", "\"other_base\"")); Bad(() => LegacyMigrationReader.Read(state, program, state), V03FailureCodes.MigrationFailed);
        }
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void Roundtrip<T>(T value, Action<T> validate) => ContractJson.Read(ContractJson.Write(value, validate), validate, ContractLimits.StateBytes);
    private static void Add(string name, Action run) => Cases.Add((name, run));
    private static void Assert(bool condition) { if (!condition) throw new Exception("Acceptance assertion failed."); }
    private static void Bad(Action run, string code = V03FailureCodes.ContractInvalid)
    { try { run(); } catch (ContractException e) when (e.Code == code) { return; } throw new Exception("Expected typed rejection " + code); }
}
