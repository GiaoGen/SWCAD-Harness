using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.State;

namespace CadHarness.Transactions.Tests;

internal sealed record FakePreparation(ChangeSet ChangeSet) : MutationPreparation(ChangeSet, FullValidationReason.None);
internal sealed record FakeRollback(double Value, long Revision);
internal sealed class FakeBackend : IMutationBackend<FakePreparation, FakeRollback>
{
    internal string? FailAt;
    internal double Value = 40;
    internal long Revision;
    internal bool Invalidated;
    internal bool AllowRecovery;
    internal bool BadChangeSet;
    internal bool BadRevision;
    internal bool BadIdentity;
    internal bool RollbackFails;
    internal int Recoveries;
    internal readonly List<string> Stages = new();
    internal ValidationScope? FinalScope;
    internal Action? DuringExecute;
    private void Step(string name)
    {
        Stages.Add(name);
        if (FailAt == name) throw new StateException("TEST_" + name.ToUpperInvariant(), name);
    }
    public FakePreparation ResolveInputs(CadState state, OperationNode operation)
    {
        Step("resolve");
        return new(new(new[] { "linear_holes", "linear_seed" }, new[] { "linear_holes.pattern_spacing" }, new[] { "linear_seed.profile" }));
    }
    public void Preflight(CadState state, FakePreparation prepared) => Step("preflight");
    public FakeRollback CaptureRollback(CadState state, FakePreparation prepared) { Step("capture"); return new(Value, Revision); }
    public ChangeSet Execute(FakePreparation prepared)
    {
        Value = 50; DuringExecute?.Invoke(); Step("execute");
        return BadChangeSet ? ChangeSet.Empty : prepared.Changes;
    }
    public bool Rebuild() { Step("rebuild"); return true; }
    public void ValidatePostconditions(FakePreparation prepared) => Step("postconditions");
    public bool RecoveryAllowed => AllowRecovery;
    public bool Recover(FakePreparation prepared) { Recoveries++; Step("recover"); FailAt = null; return true; }
    public CadState ValidateFinal(CadState state, FakePreparation prepared, ValidationScope scope)
    {
        FinalScope = scope; Step("validate");
        return state with { Revision = BadRevision ? state.Revision : state.Revision + 1,
            Document = BadIdentity ? state.Document with { DocumentId = Guid.NewGuid() } : state.Document,
            Parameters = state.Parameters.Select(p => p with { Value = Value }).ToArray() };
    }
    public void StageState(FakePreparation prepared, CadState validated) { Revision = validated.Revision; Step("stage"); }
    public void Rollback(FakeRollback rollback)
    {
        Step("rollback"); if (RollbackFails) throw new StateException("TEST_ROLLBACK", "Cannot restore.");
        Value = rollback.Value; Revision = rollback.Revision;
        if (FailAt == "rebuild") FailAt = null; // Mutation rebuild fails once; restoration rebuild succeeds.
    }
    public void ValidateRestored(CadState state, FakeRollback rollback, ValidationScope scope)
    {
        Step("restored"); TestData.Check(scope.FullModel && scope.Reasons == FullValidationReason.Rollback && Value == 40 && Revision == 0, "Rollback integrity/full validation differs.");
    }
    public void Invalidate() => Invalidated = true;
}
internal sealed class FailingStore : ICadStateStore
{
    private readonly AtomicStateStore inner;
    internal FailingStore(AtomicStateStore inner) => this.inner = inner;
    public CadState Load() => inner.Load();
    public void Commit(CadState state) => throw new StateException("STATE_COMMIT_FAILED", "Injected atomic commit failure.");
}

internal static class PureTests
{
    internal static int Run(string root)
    {
        var directory = Path.Combine(root, "artifacts", "milestone6"); Directory.CreateDirectory(directory);
        var state = TestData.Sample(TestData.Fixture(root)); StateValidation.Validate(state);
        var changes = new ChangeSet(new[] { "linear_holes" }, new[] { "linear_holes.pattern_spacing" }, new[] { "linear_seed.profile" });
        var dirty = DirtySet.Expand(state, changes);
        var tests = new List<(string Name, Action Run)>();
        void Add(string name, Action action) => tests.Add((name, action));
        Add("constraint closure includes coupled seed and excludes independent pattern", () =>
            TestData.Check(dirty.Features.SequenceEqual(new[] { "linear_holes", "linear_seed" }) && !dirty.Entities.Contains("hole_seed.profile"), "Dirty closure escaped mutation dependency boundary."));
        Add("referenced host is read without propagating to independent layout", () =>
            TestData.Check(dirty.Entities.Contains("plate.top_face") && dirty.Relations.Count == 5 && dirty.Relations.All(r => r.Subject.StartsWith("linear_", StringComparison.Ordinal)), "Shared host incorrectly dirtied other relations."));
        Add("Level 1 contains only directly changed parameter", () => TestData.Check(dirty.Parameters.SequenceEqual(changes.ChangedParameters), "Read set grew to unchanged parameters."));
        Add("invalidated relation reference expands dependent features", () =>
            TestData.Check(DirtySet.Expand(state, new(Array.Empty<string>(), Array.Empty<string>(), new[] { "plate.top_face" })).Features.Count == 4, "Invalidated host failed to propagate."));
        Add("unknown ChangeSet identity rejects", () => Expect("CHANGESET_INVALID", () => DirtySet.Expand(state, changes with { ChangedFeatures = new[] { "unknown" } })));
        Add("ordinary edits stay targeted", () => TestData.Check(!ValidationScope.Select(state, dirty, FullValidationReason.None).FullModel, "Ordinary edit escalated."));
        foreach (var reason in Enum.GetValues<FullValidationReason>().Where(r => r != FullValidationReason.None))
        {
            var captured = reason;
            Add("full escalation: " + reason, () =>
            {
                var scope = ValidationScope.Select(state, dirty, captured);
                TestData.Check(scope.FullModel && scope.Entities.Count == state.Entities.Count && scope.Relations.Count == state.Relations.Count && scope.Reasons == captured, "Full validation policy omitted managed state.");
            });
        }
        Add("unknown validation reason rejects", () => Expect("VALIDATION_POLICY_INVALID", () => ValidationScope.Select(state, dirty, (FullValidationReason)256)));
        var path = Path.Combine(directory, "pure-state.json");
        (AtomicStateStore Store, FakeBackend Backend, byte[] Bytes) Setup()
        { var store = new AtomicStateStore(path); store.Commit(state); return (store, new FakeBackend(), File.ReadAllBytes(path)); }
        Add("successful generic mutation validates then commits exactly one revision", () =>
        {
            var t = Setup(); var result = new MutationTransaction<FakePreparation, FakeRollback>(t.Store, t.Backend).Execute(TestData.Edit(50));
            TestData.Check(result.Succeeded && result.StateCommitted && !result.RollbackAttempted && t.Store.Load().Revision == 1 && t.Backend.Value == 50,
                "Successful transaction did not commit.");
            TestData.Check(t.Backend.Stages.SequenceEqual(new[] { "resolve", "preflight", "capture", "execute", "rebuild", "postconditions", "validate", "stage" }), "Transaction stage order differs.");
        });
        foreach (var failure in new[] { "resolve", "preflight", "capture", "execute", "rebuild", "postconditions", "validate", "stage" })
        {
            var captured = failure;
            Add("failure stage: " + failure, () =>
            {
                var t = Setup(); t.Backend.FailAt = captured;
                var result = new MutationTransaction<FakePreparation, FakeRollback>(t.Store, t.Backend).Execute(TestData.Edit(50));
                var started = captured is "execute" or "rebuild" or "postconditions" or "validate" or "stage";
                TestData.Check(!result.Succeeded && !result.StateCommitted && result.MutationStarted == started && result.RollbackAttempted == started && result.RollbackSucceeded == started &&
                    File.ReadAllBytes(path).SequenceEqual(t.Bytes) && t.Backend.Value == 40 && t.Backend.Revision == 0, "Failure stage leaked state/native mutation.");
            });
        }
        Add("atomic commit failure rolls back staged session and native value", () =>
        {
            var t = Setup(); var result = new MutationTransaction<FakePreparation, FakeRollback>(new FailingStore(t.Store), t.Backend).Execute(TestData.Edit(50));
            TestData.Check(result.FailureCode == "STATE_COMMIT_FAILED" && result.RollbackSucceeded && !result.StateCommitted && t.Backend.Revision == 0 && t.Backend.Value == 40 && File.ReadAllBytes(path).SequenceEqual(t.Bytes), "Commit failure leaked state.");
        });
        Add("executed ChangeSet mismatch rolls back", () =>
        {
            var t = Setup(); t.Backend.BadChangeSet = true;
            var result = new MutationTransaction<FakePreparation, FakeRollback>(t.Store, t.Backend).Execute(TestData.Edit(50));
            TestData.Check(result.FailureCode == "TRANSACTION_INTEGRITY_FAILED" && result.RollbackSucceeded, "ChangeSet mismatch accepted.");
        });
        foreach (var identity in new[] { false, true })
        {
            var captured = identity;
            Add(identity ? "candidate identity mismatch rolls back" : "candidate revision mismatch rolls back", () =>
            {
                var t = Setup(); t.Backend.BadIdentity = captured; t.Backend.BadRevision = !captured;
                var result = new MutationTransaction<FakePreparation, FakeRollback>(t.Store, t.Backend).Execute(TestData.Edit(50));
                TestData.Check(result.FailureCode == "TRANSACTION_INTEGRITY_FAILED" && result.RollbackSucceeded && !result.StateCommitted, "Integrity failure accepted.");
            });
        }
        Add("rollback failure reports both errors and invalidates session", () =>
        {
            var t = Setup(); t.Backend.FailAt = "execute"; t.Backend.RollbackFails = true;
            var result = new MutationTransaction<FakePreparation, FakeRollback>(t.Store, t.Backend).Execute(TestData.Edit(50));
            TestData.Check(result.FailureCode == "TEST_EXECUTE" && result.RollbackFailureCode == "TEST_ROLLBACK" && result.RollbackAttempted && !result.RollbackSucceeded &&
                !result.StateCommitted && t.Backend.Invalidated && File.ReadAllBytes(path).SequenceEqual(t.Bytes), "Rollback failure hidden or committed.");
        });
        Add("restoration validation failure never reports successful rollback", () =>
        {
            var t = Setup(); t.Backend.FailAt = "execute";
            t.Backend.DuringExecute = () => t.Backend.FailAt = "restored";
            // Force final validation failure, then fail restoration verification.
            t.Backend.BadRevision = true;
            var result = new MutationTransaction<FakePreparation, FakeRollback>(t.Store, t.Backend).Execute(TestData.Edit(50));
            TestData.Check(!result.RollbackSucceeded && result.RollbackFailureCode == "TEST_RESTORED" && t.Backend.Invalidated, "Restoration validation failure accepted.");
        });
        Add("allowed recovery is bounded and escalates final validation", () =>
        {
            var t = Setup(); t.Backend.AllowRecovery = true; t.Backend.FailAt = "postconditions";
            var result = new MutationTransaction<FakePreparation, FakeRollback>(t.Store, t.Backend).Execute(TestData.Edit(50));
            TestData.Check(result.Succeeded && result.RecoveryAttempted && t.Backend.Recoveries == 1 && t.Backend.FinalScope!.Reasons == FullValidationReason.Recovery, "Recovery escaped bound/full policy.");
        });
        Add("nested mutation rejects before another state load", () =>
        {
            var t = Setup(); var transaction = new MutationTransaction<FakePreparation, FakeRollback>(t.Store, t.Backend);
            MutationResult? nested = null; t.Backend.DuringExecute = () => nested = transaction.Execute(TestData.Edit(60));
            var result = transaction.Execute(TestData.Edit(50));
            TestData.Check(result.Succeeded && nested?.FailureCode == "TRANSACTION_BUSY" && nested.MutationStarted == false, "Nested mutation escaped guard.");
        });
        Add("invalid loaded state rejects before resolving inputs", () =>
        {
            File.WriteAllText(path, "{}"); var backend = new FakeBackend();
            var result = new MutationTransaction<FakePreparation, FakeRollback>(new AtomicStateStore(path), backend).Execute(TestData.Edit(50));
            TestData.Check(!result.MutationStarted && backend.Stages.Count == 0 && !result.StateCommitted, "Invalid state reached adapter.");
        });
        var report = tests.Select(test =>
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); return new { test.Name, Passed = true, Error = (string?)null }; }
            catch (Exception error) { Console.WriteLine("FAIL " + test.Name + ": " + error.Message); return new { test.Name, Passed = false, Error = (string?)error.Message }; }
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "pure-result.json"), JsonSerializer.Serialize(new { Status = report.All(r => r.Passed) ? "COMPLETE" : "BLOCKED", Passed = report.Count(r => r.Passed), Total = report.Length, Tests = report }, new JsonSerializerOptions { WriteIndented = true }));
        return report.All(r => r.Passed) ? 0 : 1;
    }
    private static void Expect(string code, Action action)
    { try { action(); } catch (StateException error) when (error.Code == code) { return; } throw new Exception("Expected " + code); }
}
