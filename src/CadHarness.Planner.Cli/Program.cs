using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Planner.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) } };
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (Array.IndexOf(args, "--v03-request") >= 0) return await RunConstruction(args);
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var llm = false;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--llm") { if (llm) throw new ArgumentException("Duplicate --llm."); llm = true; continue; }
                if (args[i] is not ("--intent" or "--fixtures" or "--model-program" or "--state" or "--output") || i + 1 == args.Length || !values.TryAdd(args[i], args[++i]))
                    throw new ArgumentException("Usage: --intent <text> (--fixtures <map.json> | --llm) [--model-program <program.json> --state <state.json>] [--output <plan.json>]");
            }
            if (!values.TryGetValue("--intent", out var intent) || llm == values.ContainsKey("--fixtures") || values.ContainsKey("--model-program") != values.ContainsKey("--state"))
                throw new ArgumentException("Supply intent, exactly one source, and both model-program/state when planning an edit.");
            var runtime = SolidWorksPlanningRuntime.ForConstruction();
            if (values.TryGetValue("--model-program", out var modelFile))
            {
                var parsed = new CadProgramJson(runtime.Capabilities.Registry).Parse(File.ReadAllText(modelFile));
                if (!parsed.IsValid) throw new ArgumentException(parsed.Issues[0].Message);
                runtime = SolidWorksPlanningRuntime.ForEditSnapshot(parsed.Program!, new AtomicStateStore(values["--state"]).Load());
            }
            using var client = llm ? OpenAiPlanSource.CreateClient() : null;
            IStructuredPlanSource source;
            if (llm) source = new OpenAiPlanSource(client!, OpenAiPlannerOptions.FromEnvironment());
            else
            {
                using var fixtures = JsonDocument.Parse(File.ReadAllText(values["--fixtures"]));
                if (fixtures.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("Fixture map must be an object keyed by exact intent text.");
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var fixture in fixtures.RootElement.EnumerateObject())
                    if (!map.TryAdd(fixture.Name, fixture.Value.GetRawText())) throw new ArgumentException("Duplicate fixture intent.");
                source = new FixturePlanSource(map);
            }
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            PlanningResult result;
            try { result = await new CadPlanner(runtime, source).PlanAsync(intent, cancellation.Token); }
            finally { Console.CancelKeyPress -= cancel; }
            if (result.Succeeded && values.TryGetValue("--output", out var output))
                File.WriteAllText(output, new CadProgramJson(runtime.Capabilities.Registry).Serialize(result.Program!) + Environment.NewLine);
            Console.WriteLine(JsonSerializer.Serialize(new { result.Status, result.FailureCode, result.Message, result.Issues,
                result.ModelCalls, result.InputTokens, result.OutputTokens, NativePartsCreated = 0,
                Program = result.Program is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(new CadProgramJson(runtime.Capabilities.Registry).Serialize(result.Program)) }, JsonOptions));
            return result.Succeeded ? 0 : result.Status == PlanningStatus.Unsupported ? 2 : 1;
        }
        catch (PlannerException error) { Console.Error.WriteLine(error.Code + ": " + error.Message); return 1; }
        catch (CadHarness.Ir.V03.ContractException error) { Console.Error.WriteLine(error.Code + ": " + error.Message); return 1; }
        catch (Exception error) when (error is ArgumentException or IOException or JsonException or StateException)
        { Console.Error.WriteLine("PLANNER_INPUT_INVALID: " + error.Message); return 1; }
    }

    private static async Task<int> RunConstruction(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal); var llm = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--llm") { if (llm) throw new ArgumentException("Duplicate --llm."); llm = true; continue; }
            if (args[i] is not ("--v03-request" or "--fixtures" or "--output") || i + 1 == args.Length || !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Usage: --v03-request <request.json> (--fixtures <map.json> | --llm) [--output <preview.json>]. No CAD is executed.");
        }
        if (!values.ContainsKey("--v03-request") || llm == values.ContainsKey("--fixtures")) throw new ArgumentException("Choose exactly one plan source.");
        var request = CadHarness.Ir.V03.ContractJson.Read<ConstructionPlanRequest>(File.ReadAllText(values["--v03-request"]),
            r => { _ = ConstructionRequirements.Normalize(r); });
        using var client = llm ? OpenAiPlanSource.CreateClient() : null;
        IStructuredPlanSource source;
        if (llm) source = new OpenAiPlanSource(client!, OpenAiPlannerOptions.FromEnvironment());
        else
        {
            using var map = JsonDocument.Parse(File.ReadAllText(values["--fixtures"]));
            source = new FixturePlanSource(map.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText(), StringComparer.Ordinal));
        }
        var result = await new ConstructionPlanner(SolidWorksConstructionPlanningRuntime.ForNewModel(), source).PlanAsync(request);
        var json = JsonSerializer.Serialize(new { result, NativePartsCreated = 0, ExecutionAuthorized = false }, JsonOptions);
        if (values.TryGetValue("--output", out var output))
        {
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(file); writer.WriteLine(json);
        }
        Console.WriteLine(json);
        return result.Status == ConstructionPlanningStatus.Planned ? 0 : result.Status is ConstructionPlanningStatus.Unsupported or ConstructionPlanningStatus.NeedsClarification ? 2 : 1;
    }
}
