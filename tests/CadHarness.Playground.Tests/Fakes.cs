using System.Net;
using System.Text;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.Playground;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Playground.Tests;

internal sealed class MockProvider : IProviderFactory
{
    internal string Mode = "valid";
    internal int Calls;
    internal bool SchemaChecked;
    internal TaskCompletionSource? Wait;
    internal static readonly string Creation = """
        {"programVersion":"0.2","operations":[{"id":"base","kind":"create_extrude","semanticId":"plate","profile":{"kind":"centered_rectangle","widthMm":80,"heightMm":50},"depthMm":10}],"relations":[]}
        """;
    internal static readonly string EditPlan = """
        {"programVersion":"0.2","operations":[{"id":"edit","kind":"edit_parameter","target":{"semanticId":"plate","type":"feature_ref"},"parameter":"extrusion_depth","value":12}],"relations":[]}
        """;
    public IStructuredPlanSource Create(LlmSettings settings, string key) => new OpenAiPlanSource(new HttpClient(new Handler(this)) { Timeout = Timeout.InfiniteTimeSpan },
        new(settings.Model, key, new Uri(settings.Endpoint), settings.MaxOutputTokens, settings.TimeoutSeconds));
    private sealed class Handler(MockProvider parent) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            parent.Calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var format = body.RootElement.GetProperty("text").GetProperty("format");
            if (format.GetProperty("type").GetString() != "json_schema" || !format.GetProperty("strict").GetBoolean()) throw new Exception("Not strict schema");
            ProviderSchemaCompatibility.RequireCompatible(format.GetProperty("schema").GetRawText()); parent.SchemaChecked = true;
            if (parent.Mode == "wait") await (parent.Wait ??= new()).Task.WaitAsync(cancellationToken);
            if (parent.Mode == "timeout") await Task.Delay(Timeout.Infinite, cancellationToken);
            if (parent.Mode == "error") return new(HttpStatusCode.Unauthorized) { Content = new StringContent("Unauthorized") };
            var cap = format.GetProperty("schema").GetRawText();
            var program = cap.Contains("edit_parameter", StringComparison.Ordinal) ? EditPlan : Creation;
            if (parent.Mode == "preflight") program = """
                {"programVersion":"0.2","operations":[{"id":"base","kind":"create_extrude","semanticId":"plate","profile":{"kind":"centered_rectangle","widthMm":80,"heightMm":50},"depthMm":10},
                {"id":"h1","kind":"create_through_hole","semanticId":"hole_one","host":{"semanticId":"plate.top_face","type":"planar_face"},"diameterMm":6,"placement":{"xMm":0,"yMm":0}},
                {"id":"h2","kind":"create_through_hole","semanticId":"hole_two","host":{"semanticId":"plate.top_face","type":"planar_face"},"diameterMm":6,"placement":{"xMm":0,"yMm":0}}],"relations":[]}
                """;
            if (parent.Mode == "capability") program = Creation.Replace("centered_rectangle", "rectangle");
            var envelope = parent.Mode == "invalid" ? "{}" : "{\"outcome\":\"planned\",\"program\":" + program + ",\"reason\":\"\"}";
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                status = "completed", model = "mock-response-alias", usage = new { input_tokens = 100, output_tokens = 40 },
                output = new[] { new { type = "message", role = "assistant", content = new[] { new { type = "output_text", text = envelope } } } }
            }), Encoding.UTF8, "application/json") };
        }
    }
}

// This double has no SolidWorksConnection and cannot activate COM. Its state
// supports the real snapshot projection/parser/preflight, not native validation.
internal sealed class FakeNative : INativeSession
{
    public NativeStatus Status { get; private set; } = new();
    public CadState? State { get; private set; }
    private CadProgram? current;
    internal bool RejectPreflight;
    internal bool FailEdit;
    internal ManualResetEventSlim? BlockCreation;
    public NativeStatus Connect() => Status = Status with { Connected = true, ConnectionMode = "MOCK ONLY" };
    public IPlanningRuntime EditRuntime() => RejectPreflight ? new RejectedRuntime(SolidWorksPlanningRuntime.ForEditSnapshot(current!, State!)) : SolidWorksPlanningRuntime.ForEditSnapshot(current!, State!);
    public NativeOutcome Create(CadProgram program, bool autoClose)
    {
        BlockCreation?.Wait(); current = new DesignRelationEngine().Solve(program).Program;
        State = Sample(current, 1); Status = Status with { PartOpen = true, Revision = 1, PartsCreated = Status.PartsCreated + 1 };
        var captured = State; if (autoClose) Close();
        return Outcome(true, captured);
    }
    public NativeOutcome Edit(CadProgram program, long revision)
    {
        if (FailEdit) return Outcome(false, State);
        current = new DesignRelationEngine().Solve(ParameterMutationRegistry.Default.ApplyProgram(current!, program.Operations.Single())).Program;
        State = Sample(current, revision + 1); Status = Status with { Revision = State.Revision }; return Outcome(true, State);
    }
    public NativeStatus Close() { current = null; State = null; return Status = Status with { PartOpen = false, Revision = null, PartsClosed = Status.PartsClosed + (Status.PartOpen ? 1 : 0) }; }
    private NativeOutcome Outcome(bool pass, CadState? state) => new(pass, pass ? null : "INJECTED_MOCK_FAILURE", "Mock native outcome (no COM).",
        new(pass, pass ? null : "INJECTED_MOCK_FAILURE", "Mock transaction", "final validation", true, true, !pass, !pass, pass, false, state?.Revision, ChangeSet.Empty, null, null),
        Array.Empty<object>(), new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), Status, state);
    public void Dispose() { Close(); }
    private sealed class RejectedRuntime(IPlanningRuntime inner) : IPlanningRuntime
    {
        public RuntimeCapabilityCatalog Capabilities => inner.Capabilities;
        public string ModelContextJson => inner.ModelContextJson;
        public ProgramValidationResult Preflight(CadProgram program) => new(new[] { new ValidationIssue("INJECTED_PREFLIGHT_FAILURE", "$", "Mock changed observation.") });
    }
    internal static CadState Sample(CadProgram program, long revision)
    {
        var reference = new NativePersistentReference("AQID"); var registry = ParameterMutationRegistry.Default;
        var frame = new LocalFrameGeometry(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1));
        var bindings = program.Operations.SelectMany(o => registry.Descriptors.Where(d => d.OwnerKind == o.Kind && registry.TryGet(o, d.Parameter, out _))
            .Select(d => new ParameterBinding(o.SemanticId + "." + WireNames.Of(d.Parameter), o.SemanticId!, d.Parameter))).ToArray();
        return new()
        {
            SchemaVersion = "0.2", Revision = revision, Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""),
            Features = program.Operations.Select(o => new FeatureNode(o.SemanticId!, o.Kind, reference, ReferenceHealth.Healthy)).ToArray(),
            Entities = program.Operations.SelectMany(o => ProfileOutputs.For(o).Select(e => new SemanticEntityNode(o.SemanticId + e.Suffix, e.Type, o.SemanticId!, reference, ReferenceHealth.Healthy)
            { Geometry = e.Type == SemanticType.LocalFrame ? new(new(0, 0, 0), Frame: frame) : e.Type == SemanticType.ReferenceAxis ? new(new(0, 0, 0), new(1, 0, 0)) : null })).ToArray(),
            Parameters = bindings.Select(b => new ParameterNode(b.ParameterSemanticId, EditableParameters.Contract(b.Parameter).Kind, registry.Expected(program.Operations.Single(o => o.SemanticId == b.OwnerFeatureSemanticId), b.Parameter))).ToArray(),
            Bindings = bindings, Relations = StateRelationData.Encode(program.Relations), Dependencies = StateRelationData.Encode(new DesignRelationEngine().Solve(program).Dependencies.Edges)
        };
    }
}
