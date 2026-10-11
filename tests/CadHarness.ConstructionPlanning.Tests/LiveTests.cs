using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;

internal sealed class EvidenceSource(IStructuredPlanSource inner, string output) : IStructuredPlanSource
{
    public bool IsModelBacked => inner.IsModelBacked;
    private int calls;
    public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken token)
    {
        var path = Path.Combine(output, "call-" + (++calls).ToString("D2"));
        Program.Write(path + "-request.json", new { utc = DateTime.UtcNow, source = IsModelBacked ? "live_provider" : "fixture", requestedModel = IsModelBacked ? "deepseek-chat" : null, prompt });
        try
        {
            var response = await inner.GenerateAsync(prompt, token).ConfigureAwait(false);
            string? returnedModel = null;
            if (response.ProviderJson is { } raw)
            { using var doc = JsonDocument.Parse(raw); if (doc.RootElement.TryGetProperty("model", out var m)) returnedModel = m.GetString(); }
            Program.Write(path + "-response.json", new { utc = DateTime.UtcNow, requestedModel = IsModelBacked ? "deepseek-chat" : null, returnedModel, response });
            return response;
        }
        catch (PlannerException e)
        { Program.Write(path + "-failure.json", new { utc = DateTime.UtcNow, e.Code, e.Message, e.InputTokens, e.OutputTokens, e.ProviderJson }); throw; }
    }
}
internal static partial class Program
{
    internal static IStructuredPlanSource Provider(System.Net.Http.HttpClient client) =>
        new DeepSeekPlanSource(client, "deepseek-chat", System.Environment.GetEnvironmentVariable("CAD_HARNESS_LLM_API_KEY") ??
            throw new Exception("Missing existing authorized provider credential."), 8192, 120);
    private static int Live(string root, string run, bool targeted = false)
    {
        Check(ContractValidation.Id(run), "Invalid run ID");
        var output = Path.Combine(root, "artifacts/milestone17", run);
        Check(!Directory.Exists(output), "Never overwrite a run"); Directory.CreateDirectory(output);
        var plate = Cases.Plate();
        var explicitInput = ConstructionRequirements.Normalize(plate);
        var explicitRequest = new ConstructionPlanRequest(explicitInput with { Intent = "Create the specified centered rectangular prism. All given input is authoritative; nominal geometry only.",
            Requirements = explicitInput.Requirements.Select(r => r with { Source = RequirementSource.Given, RuleId = null, RuleVersion = null }).ToArray() }, Array.Empty<RequirementRule>());
        var tests = new List<(string Id, ConstructionPlanRequest Request, ConstructionPlanningStatus Expected)>
        {
            ("explicit", explicitRequest, ConstructionPlanningStatus.Planned),
            ("omitted_layout", plate, ConstructionPlanningStatus.Planned),
            ("derived_radius", Cases.Cylinder, ConstructionPlanningStatus.Planned),
            ("heldout_dimensions", Cases.Plate(90, 53, 7), ConstructionPlanningStatus.Planned),
            ("missing_envelope", plate with { Input = plate.Input with { Requirements = plate.Input.Requirements.Select(r => r.SemanticKey == "width" ? Cases.Given("width", null) : r).ToArray() } }, ConstructionPlanningStatus.NeedsClarification),
            ("missing_interface", Cases.Cylinder with { Input = Cases.Cylinder.Input with { Intent = "Create the requested cylinder. The critical mating diameter is unknown; ask for it, do not guess.", Requirements = new[] { Cases.Given("diameter", null), Cases.Given("depth", 14) } }, Rules = Cases.Cylinder.Rules.Where(r => r.RuleId != "half_dimension").ToArray() }, ConstructionPlanningStatus.NeedsClarification),
            ("unsupported", plate with { Input = plate.Input with { Intent = "Create the specified prism AND certify its strength and guaranteed fatigue life. Those guarantees are required." } }, ConstructionPlanningStatus.Unsupported)
        };
        if (targeted) tests = tests.Where(t => t.Id == "derived_radius").ToList();
        Write(Path.Combine(output, "schedule.json"), new { model = "deepseek-chat", endpoint = DeepSeekPlanSource.Endpoint, noRetry = true, nativeParts = 0, tests,
            frozenSource = SourceHashes(root) });
        using var client = OpenAiPlanSource.CreateClient();
        var source = new EvidenceSource(Provider(client), output);
        var results = new List<object>(); var passed = true;
        foreach (var test in tests)
        {
            var runtime = SolidWorksConstructionPlanningRuntime.ForNewModel();
            var result = new ConstructionPlanner(runtime, source).PlanAsync(test.Request).GetAwaiter().GetResult();
            var ok = result.Status == test.Expected;
            results.Add(new { test.Id, expected = test.Expected.ToString(), passed = ok, result });
            Write(Path.Combine(output, test.Id + "-result.json"), results[^1]);
            Console.WriteLine(test.Id + ": " + result.Status + " " + result.Message); passed &= ok;
        }
        Write(Path.Combine(output, "results.json"), new { passed, total = tests.Count, results, nativeParts = 0, source = "live_provider" });
        return passed ? 0 : 1;
    }
    internal static object[] SourceHashes(string root) => Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(Path.Combine(root, "tests/CadHarness.ConstructionPlanning.Tests"), "*.cs", SearchOption.AllDirectories))
        .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
        .Select(p => (object)new { path = Path.GetRelativePath(root, p), sha256 = CadHarness.State.ManagedRevisionStore.Hash(p) }).ToArray();
}
