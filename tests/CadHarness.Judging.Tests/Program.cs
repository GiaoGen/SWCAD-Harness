using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CadHarness.Ir;
using CadHarness.State;

namespace CadHarness.Judging.Tests;

internal sealed class FakeJudge : IBoundedJudge
{
    internal int Calls;
    internal BoundedJudgeRequest? Request;
    internal Func<BoundedJudgeRequest, CancellationToken, Task<BoundedJudgeDecision>> Respond =
        (request, _) => Task.FromResult(Program.Select(request));
    public Task<BoundedJudgeDecision> SelectCandidateAsync(BoundedJudgeRequest request, CancellationToken cancellationToken)
    { Calls++; Request = request; return Respond(request, cancellationToken); }
}

internal static class Program
{
    private const string Intent = "选择朝上的最高宿主面以放置通孔";
    private static readonly InputContract Host = new("host", SemanticRole.HostSurface, new[] { SemanticType.PlanarFace });
    private static readonly BindingQuery Any = new();
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    internal static BoundedJudgeDecision Select(BoundedJudgeRequest request) =>
        new(request.RequestId, BoundedJudgeChoice.Select,
            request.Candidates.OrderByDescending(c => c.Geometry!.OriginMm.Z).First().SemanticId,
            "候选的确定性位置证据显示该面最高，符合通孔宿主意图。");
    private static CadState State(int count = 2)
    {
        var reference = new NativePersistentReference("AQID");
        return new()
        {
            SchemaVersion = "0.2", Revision = 3,
            Document = new(Guid.NewGuid(), Guid.NewGuid(), "Default", ""),
            Features = new[]
            {
                new FeatureNode("plate_a", OperationKind.CreateExtrude, reference, ReferenceHealth.Healthy),
                new FeatureNode("plate_b", OperationKind.CreateExtrude, reference, ReferenceHealth.Healthy),
                new FeatureNode("hole", OperationKind.CreateThroughHole, reference, ReferenceHealth.Healthy)
            },
            Entities = Enumerable.Range(0, count).Select(i => new SemanticEntityNode("plate_a.face_" + i,
                SemanticType.PlanarFace, "plate_a", reference, ReferenceHealth.Healthy)
                { Geometry = new(new(0, 0, i * 10), new(0, 0, 1), RadiusMm: 4) }).ToArray(),
            Parameters = Array.Empty<ParameterNode>(), Bindings = Array.Empty<ParameterBinding>(),
            Relations = Array.Empty<JsonElement>(),
            Dependencies = StateRelationData.Encode(new[] { new DependencyEdge("plate_a", "hole", DependencyKind.NativeInput) })
        };
    }
    private static void Ambiguous(JudgedBindingResult result, BoundedJudgeStatus expected, int calls)
    {
        Check(!result.Binding.Succeeded && result.Binding.Entity is null && result.Binding.FailureCode == "BINDING_AMBIGUOUS",
            "Unjustified ambiguity escaped as success/unresolved or selected first candidate.");
        Check(result.JudgeStatus == expected && result.JudgeCalls == calls, "Wrong judge diagnostic or call bound.");
    }
    private static Task<JudgedBindingResult> Bind(CadState state, FakeJudge? judge = null, BindingQuery? query = null,
        string intent = Intent, CancellationToken token = default, TimeSpan? timeout = null) =>
        new SemanticEntityBinder(judge, timeout).BindAsync(state, Host, query ?? Any, intent, token);

    private static async Task<int> Main(string[] args)
    {
        var output = Path.Combine(Path.GetFullPath(args.Length == 0 ? "." : args[0]), "artifacts", "milestone8");
        Directory.CreateDirectory(output);
        var tests = new List<(string Name, Func<Task> Run)>();
        void Add(string name, Func<Task> run) => tests.Add((name, run));
        Add("unique candidate works with no judge or Jev", async () =>
        {
            var result = await Bind(State(1));
            Check(result.Binding.Succeeded && result.JudgeStatus == BoundedJudgeStatus.NotNeeded && result.JudgeCalls == 0,
                "Unique deterministic binding requires optional dependency.");
        });
        Add("no judge preserves explicit ambiguity without selecting first", async () =>
            Ambiguous(await Bind(State()), BoundedJudgeStatus.NotConfigured, 0));
        Add("configured judge never called for zero candidates", async () =>
        {
            var judge = new FakeJudge(); var result = await Bind(State(), judge, new(SemanticId: "missing"));
            Check(result.Binding.FailureCode == "BINDING_UNRESOLVED" && judge.Calls == 0 && result.JudgeCalls == 0,
                "Judge was asked to invent missing geometry.");
        });
        Add("configured judge never called for one deterministic candidate", async () =>
        {
            var judge = new FakeJudge(); var result = await Bind(State(), judge, new(OriginMm: new(0, 0, 10)));
            Check(result.Binding.Succeeded && result.Binding.Entity!.SemanticId == "plate_a.face_1" && judge.Calls == 0,
                "Unique geometry match invoked judge.");
        });
        Add("preferred owner resolves ambiguity deterministically without judge", async () =>
        {
            var state = State(); var entities = (SemanticEntityNode[])state.Entities;
            entities[1] = entities[1] with { OwnerFeatureSemanticId = "plate_b" };
            var judge = new FakeJudge(); var result = await Bind(state, judge, new(PreferredOwner: "plate_b"));
            Check(result.Binding.Succeeded && result.Binding.Entity!.SemanticId == entities[1].SemanticId && judge.Calls == 0,
                "Objective owner evidence delegated to judge.");
        });
        Add("synchronous binder remains deterministic even with judge configured", () =>
        {
            var judge = new FakeJudge(); var result = new SemanticEntityBinder(judge).Bind(State(), Host, Any);
            Check(result.FailureCode == "BINDING_AMBIGUOUS" && judge.Calls == 0, "Sync binder unexpectedly judges.");
            return Task.CompletedTask;
        });
        Add("controlled ambiguity selects nonfirst legal semantic candidate", async () =>
        {
            var judge = new FakeJudge(); var result = await Bind(State(), judge);
            Check(result.Binding.Succeeded && result.Binding.Entity!.SemanticId == "plate_a.face_1" &&
                result.JudgeStatus == BoundedJudgeStatus.Selected && result.JudgeCalls == 1 && judge.Calls == 1 &&
                result.Rationale == Select(judge.Request!).Rationale, "Legal semantic selection or provenance differs.");
        });
        Add("request is a compact immutable legal projection without native facts access", async () =>
        {
            var judge = new FakeJudge(); await Bind(State(), judge); var request = judge.Request!;
            Check(request.Intent == Intent && request.InputName == "host" && request.Role == SemanticRole.HostSurface &&
                request.Candidates.Count == 2 && request.Candidates.All(c => c.Type == SemanticType.PlanarFace &&
                    c.OwnerKind == OperationKind.CreateExtrude), "Candidate metadata or role missing.");
            var json = JsonSerializer.Serialize(request);
            foreach (var forbidden in new[] { "NativeReference", "AQID", "SavedPath", "DocumentId", "Parameters", "OperationRegistry", "CadState" })
                Check(!json.Contains(forbidden, StringComparison.Ordinal), "Judge projection leaked " + forbidden);
            Check(typeof(BoundedJudgeRequest).GetConstructors().Length == 0 &&
                ((IList<BoundedJudgeCandidate>)request.Candidates).IsReadOnly, "Request can be constructed/mutated outside binder.");
            try { ((IList<BoundedJudgeCandidate>)request.Candidates)[0] = request.Candidates[1]; throw new Exception("Candidate list mutable."); }
            catch (NotSupportedException) { }
        });
        Add("wrong type unhealthy entity and unhealthy owner never enter judge set", async () =>
        {
            var state = State(5); var entities = (SemanticEntityNode[])state.Entities;
            entities[2] = entities[2] with { Type = SemanticType.CylindricalFace };
            entities[3] = entities[3] with { ReferenceHealth = ReferenceHealth.Stale };
            entities[4] = entities[4] with { OwnerFeatureSemanticId = "plate_b" };
            var features = (FeatureNode[])state.Features; features[1] = features[1] with { ReferenceHealth = ReferenceHealth.Suppressed };
            var judge = new FakeJudge(); var result = await Bind(state, judge);
            Check(result.Binding.Succeeded && judge.Request!.Candidates.Select(c => c.SemanticId)
                .SequenceEqual(new[] { "plate_a.face_0", "plate_a.face_1" }), "Illegal candidates delegated.");
        });
        Add("geometry direction radius ownership and dependency remain hard filters", async () =>
        {
            foreach (var query in new[] { new BindingQuery(Direction: new(1, 0, 0)), new BindingQuery(RadiusMm: 9),
                new BindingQuery(OwnerFeature: "plate_b"), new BindingQuery(DependentFeature: "plate_b"),
                new BindingQuery(Type: SemanticType.CylindricalFace), new BindingQuery(OriginMm: new(99, 0, 0)) })
            {
                var judge = new FakeJudge(); var result = await Bind(State(), judge, query);
                Check(result.Binding.FailureCode == "BINDING_UNRESOLVED" && judge.Calls == 0, "Hard constraint relaxed.");
            }
            var validJudge = new FakeJudge(); var valid = await Bind(State(), validJudge, new(DependentFeature: "hole", RadiusMm: 4, Direction: new(0, 0, 1)));
            Check(valid.Binding.Succeeded && validJudge.Request!.Candidates.Count == 2, "Legal constrained set lost.");
        });
        Add("eight candidates are allowed as a complete set", async () =>
        {
            var judge = new FakeJudge(); var result = await Bind(State(8), judge);
            Check(result.Binding.Succeeded && judge.Request!.Candidates.Count == 8 && result.Binding.Entity!.SemanticId == "plate_a.face_7",
                "Small set incomplete or wrong selection.");
        });
        foreach (var count in new[] { 9, 65, 100 })
        {
            var candidateCount = count;
            Add("oversized legal set " + count + " never truncated for judge", async () =>
            {
                var judge = new FakeJudge(); var result = await Bind(State(candidateCount), judge);
                Ambiguous(result, BoundedJudgeStatus.CandidateLimitExceeded, 0); Check(judge.Calls == 0, "Oversized set was sampled/ranked.");
            });
        }
        foreach (var invalidIntent in new[] { "", " ", new string('x', SemanticEntityBinder.MaximumJudgeIntentBytes + 1), new string('中', 683) })
        {
            var value = invalidIntent;
            Add("empty or oversized UTF8 intent rejected (bytes=" + Encoding.UTF8.GetByteCount(value) + ")", async () =>
            {
                var judge = new FakeJudge(); Ambiguous(await Bind(State(), judge, intent: value), BoundedJudgeStatus.InvalidRequest, 0);
                Check(judge.Calls == 0, "Invalid intent reached judge.");
            });
        }
        Add("judge abstention leaves ambiguity explicit", async () =>
        {
            var judge = new FakeJudge { Respond = (r, _) => Task.FromResult(new BoundedJudgeDecision(r.RequestId, BoundedJudgeChoice.Abstain, null, "证据不足")) };
            var result = await Bind(State(), judge); Ambiguous(result, BoundedJudgeStatus.Abstained, 1);
            Check(judge.Calls == 1 && result.Rationale == "证据不足", "Abstention retried or rationale lost.");
        });
        var badResponses = new (string Name, Func<BoundedJudgeRequest, BoundedJudgeDecision> Make)[]
        {
            ("out of set", r => Select(r) with { SelectedSemanticId = "plate_b.face_0" }),
            ("wrong semantic ID case", r => Select(r) with { SelectedSemanticId = "PLATE_A.FACE_1" }),
            ("stale correlation", r => Select(r) with { RequestId = Guid.NewGuid() }),
            ("unknown choice enum", r => Select(r) with { Choice = (BoundedJudgeChoice)123 }),
            ("missing selected ID", r => Select(r) with { SelectedSemanticId = null }),
            ("empty justification", r => Select(r) with { Rationale = " " }),
            ("missing rationale", r => Select(r) with { Rationale = null! }),
            ("oversized UTF8 rationale", r => Select(r) with { Rationale = new string('中', 342) }),
            ("abstention with selection", r => Select(r) with { Choice = BoundedJudgeChoice.Abstain }),
            ("null response", _ => null!)
        };
        foreach (var bad in badResponses)
        {
            var sample = bad;
            Add("invalid response rejected: " + bad.Name, async () =>
            {
                var judge = new FakeJudge { Respond = (r, _) => Task.FromResult(sample.Make(r)) };
                Ambiguous(await Bind(State(), judge), BoundedJudgeStatus.InvalidResponse, 1);
                Check(judge.Calls == 1, "Invalid response triggered repair/retry.");
            });
        }
        Add("filtered unhealthy candidate cannot be selected by judge", async () =>
        {
            var state = State(3); var entities = (SemanticEntityNode[])state.Entities;
            entities[2] = entities[2] with { ReferenceHealth = ReferenceHealth.Deleted };
            var judge = new FakeJudge { Respond = (r, _) => Task.FromResult(Select(r) with { SelectedSemanticId = entities[2].SemanticId }) };
            Ambiguous(await Bind(state, judge), BoundedJudgeStatus.InvalidResponse, 1);
        });
        foreach (var asynchronous in new[] { false, true })
        {
            var asyncFailure = asynchronous;
            Add((asynchronous ? "asynchronous" : "synchronous") + " adapter failure retains ambiguity and hides provider secrets", async () =>
            {
                var judge = new FakeJudge { Respond = (_, _) => asyncFailure ? Task.FromException<BoundedJudgeDecision>(new Exception("credential-secret")) : throw new Exception("credential-secret") };
                var result = await Bind(State(), judge); Ambiguous(result, BoundedJudgeStatus.Failed, 1);
                Check(judge.Calls == 1 && !result.Binding.Message.Contains("credential-secret"), "Error leaked or retried.");
            });
        }
        Add("null adapter task fails safely without retry", async () =>
        {
            var judge = new FakeJudge { Respond = (_, _) => null! };
            Ambiguous(await Bind(State(), judge), BoundedJudgeStatus.Failed, 1);
        });
        Add("pre cancelled ambiguity never calls judge", async () =>
        {
            using var source = new CancellationTokenSource(); source.Cancel(); var judge = new FakeJudge();
            Ambiguous(await Bind(State(), judge, token: source.Token), BoundedJudgeStatus.Cancelled, 0);
            Check(judge.Calls == 0, "Cancelled request called judge.");
        });
        Add("caller cancellation bounds an adapter ignoring cancellation", async () =>
        {
            using var source = new CancellationTokenSource(); var pending = new TaskCompletionSource<BoundedJudgeDecision>();
            var judge = new FakeJudge { Respond = (_, _) => { source.Cancel(); return pending.Task; } };
            Ambiguous(await Bind(State(), judge, token: source.Token), BoundedJudgeStatus.Cancelled, 1);
            pending.SetException(new Exception("late fault")); Check(judge.Calls == 1, "Cancellation retried.");
        });
        Add("deadline bounds noncooperative asynchronous adapter with no retry", async () =>
        {
            var pending = new TaskCompletionSource<BoundedJudgeDecision>(); CancellationToken observed = default;
            var judge = new FakeJudge { Respond = (_, token) => { observed = token; return pending.Task; } };
            Ambiguous(await Bind(State(), judge, timeout: TimeSpan.FromMilliseconds(40)), BoundedJudgeStatus.TimedOut, 1);
            Check(observed.IsCancellationRequested && judge.Calls == 1, "Deadline did not cancel adapter or retried.");
            pending.SetResult(Select(judge.Request!));
        });
        Add("adapter independent cancellation is failure without claiming deadline", async () =>
        {
            var judge = new FakeJudge { Respond = (_, _) => Task.FromException<BoundedJudgeDecision>(new OperationCanceledException()) };
            Ambiguous(await Bind(State(), judge), BoundedJudgeStatus.Failed, 1);
        });
        var mutations = new (string Name, Action<CadState> Apply)[]
        {
            ("selected health", s => { var e = (SemanticEntityNode[])s.Entities; e[1] = e[1] with { ReferenceHealth = ReferenceHealth.Stale }; }),
            ("owner native reference", s => { var f = (FeatureNode[])s.Features; f[0] = f[0] with { NativeReference = new("BAUG") }; }),
            ("candidate geometry", s => { var e = (SemanticEntityNode[])s.Entities; e[1] = e[1] with { Geometry = new(new(0, 0, 20)) }; }),
            ("owner health", s => { var f = (FeatureNode[])s.Features; f[0] = f[0] with { ReferenceHealth = ReferenceHealth.Suppressed }; }),
            ("invalid state", s => { var e = (SemanticEntityNode[])s.Entities; e[1] = e[1] with { OwnerFeatureSemanticId = "missing" }; }),
            ("dependency graph", s => { var d = (JsonElement[])s.Dependencies; d[0] = StateRelationData.Encode(new[] { new DependencyEdge("plate_b", "hole", DependencyKind.NativeInput) })[0]; })
        };
        foreach (var mutation in mutations)
        {
            var change = mutation;
            Add("state change during judgement rejected: " + change.Name, async () =>
            {
                var state = State(); var judge = new FakeJudge { Respond = (r, _) => { change.Apply(state); return Task.FromResult(Select(r)); } };
                Ambiguous(await Bind(state, judge), BoundedJudgeStatus.StateChanged, 1);
                Check(judge.Request!.Candidates[1].Geometry!.OriginMm.Z == 10, "Request followed mutable state change.");
            });
        }
        Add("accepted type contract change during judgement rejected", async () =>
        {
            var accepted = new[] { SemanticType.PlanarFace }; var input = Host with { AcceptedTypes = accepted };
            var judge = new FakeJudge { Respond = (r, _) => { accepted[0] = SemanticType.CylindricalFace; return Task.FromResult(Select(r)); } };
            Ambiguous(await new SemanticEntityBinder(judge).BindAsync(State(), input, Any, Intent), BoundedJudgeStatus.StateChanged, 1);
        });
        Add("actual asynchronous suspension keeps snapshot and rejects a later topology change", async () =>
        {
            var state = State(); var pending = new TaskCompletionSource<BoundedJudgeDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            var judge = new FakeJudge { Respond = (_, _) => pending.Task };
            var binding = Bind(state, judge);
            Check(!binding.IsCompleted && judge.Request is not null, "Fixture did not suspend on judge.");
            var entities = (SemanticEntityNode[])state.Entities;
            entities[1] = entities[1] with { NativeReference = new("BAUG") };
            pending.SetResult(Select(judge.Request!));
            Ambiguous(await binding, BoundedJudgeStatus.StateChanged, 1);
        });
        Add("cancellation cannot be bypassed by an immediate successful judge response", async () =>
        {
            using var source = new CancellationTokenSource();
            var judge = new FakeJudge { Respond = (r, _) => { source.Cancel(); return Task.FromResult(Select(r)); } };
            Ambiguous(await Bind(State(), judge, token: source.Token), BoundedJudgeStatus.Cancelled, 1);
        });
        Add("selection does not mutate state native references or revision", async () =>
        {
            var state = State(); var before = JsonSerializer.Serialize(state); var judge = new FakeJudge();
            Check((await Bind(state, judge)).Binding.Succeeded && JsonSerializer.Serialize(state) == before && state.Revision == 3,
                "Semantic ranking modified CAD state.");
        });
        Add("judge deadline configuration is finite and capped", () =>
        {
            foreach (var timeout in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(31) })
            {
                try { _ = new SemanticEntityBinder(new FakeJudge(), timeout); throw new Exception("Unbounded timeout accepted."); }
                catch (ArgumentOutOfRangeException) { }
            }
            return Task.CompletedTask;
        });
        var results = new List<object>(); var passed = 0;
        foreach (var test in tests)
        {
            try { await test.Run(); passed++; Console.WriteLine("PASS " + test.Name); results.Add(new { test.Name, Passed = true }); }
            catch (Exception error) { Console.WriteLine("FAIL " + test.Name + ": " + error.Message); results.Add(new { test.Name, Passed = false, Error = error.Message }); }
        }
        File.WriteAllText(Path.Combine(output, "pure-result.json"), JsonSerializer.Serialize(new
        {
            Status = passed == tests.Count ? "COMPLETE" : "BLOCKED", Passed = passed, Total = tests.Count,
            NativePartsCreated = 0, NativePartsClosed = 0, LiveJudgeRequests = 0, JevAdapterConfigured = false, Tests = results
        }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine($"M8 {passed}/{tests.Count} PASS; native Parts=0; live judge requests=0.");
        return passed == tests.Count ? 0 : 1;
    }
}
