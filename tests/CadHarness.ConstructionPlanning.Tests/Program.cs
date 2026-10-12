using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;

[assembly: SupportedOSPlatform("windows")]
internal sealed class RecordingSource(string response) : IStructuredPlanSource
{
    public bool IsModelBacked => false;
    internal int Calls;
    internal PlannerPrompt? Prompt;
    public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Calls++; Prompt = prompt; return Task.FromResult(new StructuredPlanResponse(response)); }
}
internal static partial class Program
{
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if(args.Length==3 && args[1]=="--t4-pure")return T4Tests.Pure(args[0],args[2]);
            if(args.Length is 3 or 4 && args[1]=="--t4-live")return T4Tests.Live(args[0],args[2],args.Length==4?args[3]:null);
            if(args.Length==7 && args[1]=="--t4-native")return T4Tests.Native(args[0],args[2],args[3],args[4],args[5],args[6]);
            if (args.Length == 2 && args[1] == "--pure") return Pure(args[0]);
            if (args.Length == 3 && args[1] == "--live") return Live(args[0], args[2]);
            if (args.Length == 3 && args[1] == "--live-targeted") return Live(args[0], args[2], true);
            if (args.Length == 3 && args[1] == "--native") return Native(args[0], args[2]);
            if (args.Length == 4 && args[1] == "--native-copy") return Native(args[0], args[2], args[3]);
            if (args.Length == 4 && args[1] == "--native-replay") return Native(args[0], args[2], args[3], true);
            return 2;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static int Pure(string root)
    {
        var output = Path.Combine(root, "artifacts/milestone17/pure", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff"));
        var results = new List<object>();
        void Test(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); Write(Path.Combine(output, name + "-test.json"), new { name, passed = true }); }
            catch (Exception e) { Write(Path.Combine(output, name + "-test.json"), new { name, passed = false, error = e.ToString() }); throw; }
        }
        var runtime = SolidWorksConstructionPlanningRuntime.ForNewModel();
        var request = Cases.Plate(); var program = Cases.Program(Cases.Rectangle(62, 46), 9); var response = Cases.Response(request, program);
        ConstructionPlanningResult Plan(ConstructionPlanResponse r, ConstructionPlanRequest? q = null) =>
            new ConstructionPlanner(runtime, new RecordingSource(Cases.Wire(r))).PlanAsync(q ?? request).GetAwaiter().GetResult();
        Test("strict_schema", () => Check(ProviderSchemaCompatibility.Audit(ConstructionPlanner.ResponseSchema(runtime)).Count == 0, "Invalid schema"));
        Test("tagged_operation_schema", () =>
        {
            using var doc = JsonDocument.Parse(ConstructionPlanner.ResponseSchema(runtime));
            var alternatives = doc.RootElement.GetProperty("properties").GetProperty("program").GetProperty("anyOf")[0].GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("anyOf");
            Check(alternatives.GetArrayLength() == runtime.ExecutableKinds.Count, "Unprojected alternatives");
            var sketch = alternatives.EnumerateArray().Single(a => a.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString() == "create_sketch");
            Check(sketch.GetProperty("properties").GetProperty("profile").GetProperty("type").GetString() == "null", "Schema permits sketch self-reference");
        });
        Test("given_defaulted_trace", () => { var r = Plan(response); Check(r.Status == ConstructionPlanningStatus.Planned && r.ModelCalls == 0 && r.Provenance == "fixture", r.Message); Write(Path.Combine(output, "plate-trace.json"), r); });
        Test("given_derived_defaulted_trace", () => { var q = Cases.Cylinder; var r = Plan(Cases.Response(q, Cases.Program(Cases.Circle(9), 14)), q); Check(r.Preview is not null, r.Message); Write(Path.Combine(output, "cylinder-trace.json"), r); });
        Test("heldout_dimensions", () => { var q = Cases.Plate(73, 41, 11); var r = Plan(Cases.Response(q, Cases.Program(Cases.Rectangle(73, 41), 11)), q); Check(r.Preview is not null, r.Message); });
        foreach (var key in new[] { "width", "depth" })
            Test("critical_unknown_" + key, () =>
            {
                var q = request with { Input = request.Input with { Requirements = request.Input.Requirements.Select(r => r.SemanticKey == key ? Cases.Given(key, null) : r).ToArray() } };
                var r = Plan(new("0.3", PlanOutcome.NeedsClarification, ConstructionRequirements.Normalize(q), null, new[] { "Provide " + key + " in mm." }, null, Array.Empty<GeometryRequirementBinding>()), q);
                Check(r.Status == ConstructionPlanningStatus.NeedsClarification && r.Preview is null, r.Message); Write(Path.Combine(output, "critical-" + key + ".json"), r);
            });
        Test("provider_does_not_fill_critical", () =>
        {
            var q = request with { Input = request.Input with { Requirements = request.Input.Requirements.Select(r => r.SemanticKey == "depth" ? Cases.Given("depth", null) : r).ToArray() } };
            Check(Plan(response, q).Status == ConstructionPlanningStatus.Rejected, "Invented critical input accepted");
        });
        Test("unsupported_no_program", () => Check(Plan(response with { Outcome = PlanOutcome.Unsupported, Program = null, Bindings = Array.Empty<GeometryRequirementBinding>(), Reason = "No qualified chamfer handler." }).Status == ConstructionPlanningStatus.Unsupported, "Unsupported failed"));
        Test("contradictory_dimensions", () => Check(Plan(response with { Program = Cases.Program(Cases.Rectangle(63, 46), 9) }).Status == ConstructionPlanningStatus.Rejected, "Contradiction accepted"));
        Test("undisclosed_dimension", () => Check(Plan(response with { Bindings = response.Bindings.Skip(1).ToArray() }).Status == ConstructionPlanningStatus.Rejected, "Unrecorded size accepted"));
        Test("default_not_size", () => Check(Plan(response with { Bindings = response.Bindings.Select(b => b.Metric == RequirementMetric.ProfileWidth ? b with { SemanticKey = "center_x" } : b).ToArray() }).Status == ConstructionPlanningStatus.Rejected, "Defaulted size accepted"));
        Test("echo_identity", () => Check(Plan(response with { Requirements = response.Requirements with { Intent = "different" } }).Status == ConstructionPlanningStatus.Rejected, "Record rewrite accepted"));
        Test("unknown_rule", () => { try { ConstructionRequirements.Normalize(request with { Rules = new[] { new RequirementRule("invented", "strength_rule", "width") } }); throw new Exception("Accepted"); } catch (ContractException) { } });
        Test("unbound_critical_claim", () =>
        {
            var q = request with { Input = request.Input with { Requirements = request.Input.Requirements.Append(Cases.Given("required_strength", 100)).ToArray() } };
            Check(Plan(response with { Requirements = ConstructionRequirements.Normalize(q) }, q).Status == ConstructionPlanningStatus.Rejected, "Engineering claim ignored");
        });
        Test("one_complete_plan_no_retry", () => { var s = new RecordingSource(Cases.Wire(response)); var r = new ConstructionPlanner(runtime, s).PlanAsync(request).GetAwaiter().GetResult(); Check(s.Calls == 1 && r.Preview!.Read().Program!.Operations.Count == 2 && !s.Prompt!.Instructions.Contains("C1"), "Hidden loop/preset"); });
        Test("invalid_json", () => Check(new ConstructionPlanner(runtime, new RecordingSource("{}")).PlanAsync(request).GetAwaiter().GetResult().Status == ConstructionPlanningStatus.Rejected, "Malformed response accepted"));
        Test("duplicate_field", () => { var json = Cases.Wire(response).Replace("\"schemaVersion\": \"0.3\"", "\"schemaVersion\": \"0.3\", \"schemaVersion\": \"0.3\"", StringComparison.Ordinal); Check(new ConstructionPlanner(runtime, new RecordingSource(json)).PlanAsync(request).GetAwaiter().GetResult().Status == ConstructionPlanningStatus.Rejected, "Duplicate accepted"); });
        Test("unknown_field", () => Check(new ConstructionPlanner(runtime, new RecordingSource(Cases.Wire(response).Insert(1, "\"code\":\"exec\","))).PlanAsync(request).GetAwaiter().GetResult().Status == ConstructionPlanningStatus.Rejected, "Extra field accepted"));
        Test("approval_once", () => { var preview = Plan(response).Preview!; var approval = preview.Approve(preview.Sha256); approval.Consume(runtime); try { approval.Consume(runtime); throw new Exception("Consumed twice"); } catch (ContractException e) { Check(e.Code == V03FailureCodes.PreviewExpired, e.Message); } });
        Test("approval_context", () => { var preview = Plan(response).Preview!; try { preview.Approve(preview.Sha256).Consume(SolidWorksConstructionPlanningRuntime.ForNewModel()); throw new Exception("Wrong context"); } catch (ContractException) { } });
        Test("immutable_preview", () => { var preview = Plan(response).Preview!; var read = preview.Read(); ((IList<ConstructionOperation>)read.Program!.Operations)[1] = read.Program.Operations[1] with { Depth = new(100) }; Check(preview.Read().Program!.Operations[1].Depth!.Millimeters == 9, "Preview mutable"); });
        Test("bad_principal_frame", () => { var altered = program with { Operations = program.Operations.Select(o => o.Placement is null ? o : o with { Placement = o.Placement with { Frame = o.Placement.Frame with { OriginMm = new(0, 0, 1) } } }).ToArray() }; Check(Plan(response with { Program = altered }).Status == ConstructionPlanningStatus.Rejected, "Invalid plane accepted"); });
        Test("unqualified_direction_refused_pure", () =>
        {
            var altered = program with { Operations = program.Operations.Select(o => o.Placement is null ? o : o with { Placement = o.Placement with { InPlaneDirection = new("world.axis_x", CadHarness.Ir.SemanticType.ReferenceAxis) } }).ToArray() };
            Check(Plan(response with { Program = altered }).FailureCode == V03FailureCodes.CapabilityUnavailable, "Unqualified direction passed preflight");
        });
        Test("implicit_offset_plane_refused_pure", () =>
        {
            var altered = program with { Operations = program.Operations.Select(o => o.Placement is null ? o : o with { Placement = o.Placement with { Offset = new(1) } }).ToArray() };
            Check(Plan(response with { Program = altered }).FailureCode == V03FailureCodes.CapabilityUnavailable, "Implicit offset-plane ownership passed preflight");
        });
        Test("prefix_redefinition", () => { try { ConstructionPreflight.Evaluate(program, new[] { program.Operations[0] with { Id = "other" } }); throw new Exception("Wrong prefix"); } catch (ContractException) { } });
        Test("unavailable_external_origin", () => Check(Plan(response with { Program = program with { Origin = ModelOrigin.External } }).Status == ConstructionPlanningStatus.Rejected, "External construction accepted"));
        Test("cancelled", () => { using var c = new CancellationTokenSource(); c.Cancel(); Check(new ConstructionPlanner(runtime, new RecordingSource(Cases.Wire(response))).PlanAsync(request, c.Token).GetAwaiter().GetResult().Status == ConstructionPlanningStatus.Cancelled, "Cancellation failed"); });
        var continuation = new PrefixRuntime(runtime, program.Operations, 1);
        var h = Cases.Hole(1); var p = Cases.Program(Cases.Circle(3), null, program.Operations);
        Test("complete_continuation", () =>
        {
            var result = new ConstructionPlanner(continuation, new RecordingSource(Cases.Wire(Cases.Response(h, p, 2)))).PlanAsync(h).GetAwaiter().GetResult();
            Check(result.Preview is not null, result.Message);
        });
        Test("continuation_schema_represents_prefix", () =>
        {
            using var schema = JsonDocument.Parse(ConstructionPlanner.ResponseSchema(continuation));
            var alternatives = schema.RootElement.GetProperty("properties").GetProperty("program").GetProperty("anyOf")[0].GetProperty("properties").GetProperty("operations").GetProperty("items").GetProperty("anyOf");
            Check(alternatives.EnumerateArray().Any(a => a.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString() == "create_extrude") && !continuation.ExecutableKinds.Contains(ConstructionKind.CreateExtrude), "Prefix construction confused with executable new operation");
        });
        Test("continuation_changed_prefix_rejected", () =>
        {
            var altered = p with { Operations = p.Operations.Select(o => o.Kind == ConstructionKind.CreateExtrude ? o with { Depth = new(10) } : o).ToArray() };
            var result = new ConstructionPlanner(continuation, new RecordingSource(Cases.Wire(Cases.Response(h, altered, 2)))).PlanAsync(h).GetAwaiter().GetResult(); Check(result.Status == ConstructionPlanningStatus.Rejected, "Redefined prefix accepted");
        });
        Test("continuation_new_root_rejected", () =>
        {
            var bad = p with { Operations = p.Operations.Select(o => o.Kind == ConstructionKind.CreateExtrudedCut ? o with { Kind = ConstructionKind.CreateExtrude, HostBody = null, EndCondition = EndCondition.Blind, Depth = new(2), BodyRule = BodyRule.NewSingleBody } : o).ToArray() };
            var result = new ConstructionPlanner(continuation, new RecordingSource(Cases.Wire(Cases.Response(h, bad, 2)))).PlanAsync(h).GetAwaiter().GetResult(); Check(result.Status == ConstructionPlanningStatus.Rejected, "New root allowed in continuation");
        });
        Test("explicit_noncritical_dimensions_not_ignored", () =>
        {
            var q = request with { Input = request.Input with { Requirements = request.Input.Requirements.Append(Cases.Given("unrepresented", 5, false)).ToArray() } };
            Check(Plan(response with { Requirements = ConstructionRequirements.Normalize(q) }, q).Status == ConstructionPlanningStatus.Rejected, "Explicit dimension ignored");
        });
        Test("critical_ambiguity", () =>
        {
            var q = request with { Input = request.Input with { Requirements = request.Input.Requirements.Select(r => r.SemanticKey == "width" ? r with { Ambiguous = true } : r).ToArray() } };
            var refusal = new ConstructionPlanResponse("0.3", PlanOutcome.NeedsClarification, ConstructionRequirements.Normalize(q), null, new[] { "Confirm the width." }, null, Array.Empty<GeometryRequirementBinding>());
            Check(Plan(refusal, q).Status == ConstructionPlanningStatus.NeedsClarification, "Critical ambiguity accepted");
        });
        Test("provider_failure_not_clarification", () => Check(new ConstructionPlanner(runtime, new FailingSource()).PlanAsync(request).GetAwaiter().GetResult().Status == ConstructionPlanningStatus.Failed, "Provider error disguised"));
        Test("snapshot_is_preview_not_execution_authority", () =>
        {
            var reference = new CadHarness.State.NativePersistentReference("AQID");
            var state = new CadHarness.State.GenericConstructionState(new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""), 1, program,
                program.Operations.Select(o => new CadHarness.State.GenericNativeFeature(o.SemanticId, o.Kind, "pure_fixture", reference, Array.Empty<CadHarness.State.NativePersistentReference>())).ToArray(),
                Array.Empty<CadHarness.State.GenericNativeSketch>(), Array.Empty<CadHarness.State.GenericNativeParameter>(), Array.Empty<CadHarness.State.DependencyEdge>(), null);
            var projected = SolidWorksConstructionPlanningRuntime.ForSnapshotPreview(state);
            Check(projected.ContextIdentity.StartsWith("offline:", StringComparison.Ordinal) && projected.Revision == 1 && !projected.ExecutableKinds.Contains(ConstructionKind.CreateExtrude), "Snapshot granted native authority");
            Check(ProviderSchemaCompatibility.Audit(ConstructionPlanner.ResponseSchema(projected)).Count == 0, "Snapshot schema invalid");
        });
        Write(Path.Combine(output, "request-example.json"), JsonSerializer.Deserialize<JsonElement>(ContractJson.Write(request, r => { _ = ConstructionRequirements.Normalize(r); })));
        Write(Path.Combine(output, "response-schema.json"), JsonSerializer.Deserialize<JsonElement>(ConstructionPlanner.ResponseSchema(runtime)));
        Write(Path.Combine(output, "results.json"), new { passed = true, total = results.Count, results, nativeParts = 0, providerCalls = 0 });
        Console.WriteLine($"M17 pure: {results.Count}/{results.Count} PASS; {output}"); return 0;
    }
}

internal sealed class PrefixRuntime(IConstructionPlanningRuntime empty, IReadOnlyList<ConstructionOperation> prefix, long revision) : IConstructionPlanningRuntime
{
    public string ContextIdentity => "pure_prefix_context";
    public long Revision => revision;
    public string CapabilityJson => empty.CapabilityJson;
    public IReadOnlyList<ConstructionOperation> Prefix => prefix;
    public IReadOnlyList<ConstructionKind> ExecutableKinds => empty.ExecutableKinds.Where(k => k is not (ConstructionKind.CreateExtrude or ConstructionKind.CreateRevolvedBoss)).ToArray();
    public FeatureSolidExpectation Preflight(ConstructionProgram p) => ConstructionPreflight.Evaluate(p, prefix);
}
internal sealed class FailingSource : IStructuredPlanSource
{
    public bool IsModelBacked => true;
    public Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt p, CancellationToken c) => throw new PlannerException("PLANNER_PROVIDER_ERROR", "Controlled transport failure");
}
