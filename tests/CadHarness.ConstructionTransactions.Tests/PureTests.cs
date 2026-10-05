using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.ConstructionTransactions.Tests;

internal sealed record FakePrepared(CadProgram Program, ChangeSet ChangeSet) : MutationPreparation(ChangeSet, FullValidationReason.NewModelFinalization);
internal sealed class FakeStore : ICadStateStore
{
    internal CadState State = TestData.Empty(); internal bool FailCommit; internal int Commits;
    public CadState Load() => State;
    public void Commit(CadState next) { if (FailCommit) throw new StateException("STATE_COMMIT_FAILED", "Mock atomic commit failure."); State = next; Commits++; }
}
internal sealed class FakeBackend : IRequestMutationBackend<CadProgram, FakePrepared, int>
{
    internal string Failure = ""; internal int Bodies; internal int Revision; internal int Rollbacks; internal bool Invalidated;
    internal List<string> Calls = new();
    private void At(string stage) { Calls.Add(stage); if (Failure == stage) throw new StateException("TEST_FAILURE", stage); }
    public FakePrepared ResolveInputs(CadState state, CadProgram program)
    {
        At("resolve"); var created = program.Operations.Select(o => o.SemanticId!).ToArray();
        return new(program, new(created, Array.Empty<string>(), Array.Empty<string>()) { CreatedFeatures = created, CreatedEntities = created });
    }
    public void Preflight(CadState state, FakePrepared prepared) => At("preflight");
    public int CaptureRollback(CadState state, FakePrepared prepared) { At("capture"); return Bodies; }
    public ChangeSet Execute(FakePrepared prepared) { Bodies++; At("execute"); return prepared.Changes; }
    public bool Rebuild() { At("rebuild"); return true; }
    public void ValidatePostconditions(FakePrepared prepared) => At("postconditions");
    public bool RecoveryAllowed => false;
    public bool Recover(FakePrepared prepared) => false;
    public CadState ValidateFinal(CadState state, FakePrepared prepared, ValidationScope scope)
    {
        At("final"); var reference = new NativePersistentReference("AQID");
        return state with { Revision = state.Revision + 1,
            Features = prepared.Program.Operations.Select(o => new FeatureNode(o.SemanticId!, o.Kind, reference, ReferenceHealth.Healthy)).ToArray(),
            Entities = prepared.Program.Operations.Select(o => new SemanticEntityNode(o.SemanticId!, SemanticType.FeatureRef, o.SemanticId!, reference, ReferenceHealth.Healthy)).ToArray() };
    }
    public void StageState(FakePrepared prepared, CadState validated) { Revision = (int)validated.Revision; At("stage"); }
    public void Rollback(int rollback) { Rollbacks++; Bodies = rollback; Revision = 0; At("rollback"); }
    public void ValidateRestored(CadState state, int rollback, ValidationScope scope) { TestData.Check(scope.FullModel && scope.Reasons == FullValidationReason.Rollback, "Restore did not use full validation."); At("restored"); }
    public void Invalidate() => Invalidated = true;
}

internal static class PureTests
{
    internal static int Run(string root)
    {
        var tests = new List<(string Name, Action Run)>(); void Add(string name, Action run) => tests.Add((name, run));
        Add("empty CADState is a valid transaction baseline", () => StateValidation.Validate(TestData.Empty()));
        Add("empty state cannot own a phantom entity", () =>
        {
            var state = TestData.Empty() with { Entities = new[] { new SemanticEntityNode("phantom", SemanticType.FeatureRef, "phantom", new("AQID"), ReferenceHealth.Healthy) } };
            try { StateValidation.Validate(state); throw new Exception("Invalid entity accepted."); } catch (StateException) { }
        });
        Add("construction can declare new ChangeSet identities", () =>
        {
            var changes = new ChangeSet(new[] { "new_feature" }, Array.Empty<string>(), Array.Empty<string>())
                { CreatedFeatures = new[] { "new_feature" }, CreatedEntities = new[] { "new_feature" } };
            var dirty = DirtySet.Expand(TestData.Empty(), changes); TestData.Check(dirty.Features.Contains("new_feature") && dirty.Entities.Contains("new_feature"), "Created identities missing.");
        });
        Add("undeclared foreign mutation still rejected", () =>
        {
            try { DirtySet.Expand(TestData.Empty(), new(new[] { "foreign" }, Array.Empty<string>(), Array.Empty<string>())); throw new Exception("Foreign identity accepted."); } catch (StateException e) { TestData.Check(e.Code == "CHANGESET_INVALID", e.Code); }
        });
        Add("impossible fillet passes structural preflight for native feasibility", () => TestData.Check(new RelationBackend().Preflight(TestData.ImpossibleFresh()).IsValid, "Native feasibility incorrectly treated as a known pure error."));
        Add("invalid pattern rejected by whole-program preflight", () => TestData.Check(!new RelationBackend().Preflight(TestData.InvalidPattern()).IsValid, "Invalid pattern accepted."));
        Add("invalid placement rejected by whole-program preflight", () => TestData.Check(!new RelationBackend().Preflight(TestData.InvalidPlacement()).IsValid, "Invalid placement accepted."));
        Add("valid append program reuses prior semantic outputs", () => TestData.Check(new RelationBackend().Preflight(TestData.Extension(false), TestData.Base()).IsValid, "Valid append rejected."));
        Add("duplicate semantic feature in append rejected", () => TestData.Check(!new RelationBackend().Preflight(new("0.2", new[] { TestData.Hole("original_hole", 4, new(20, 10)) }, Array.Empty<DesignRelation>()), TestData.Base()).IsValid, "Duplicate accepted."));
        Add("append placement checked against original profile", () => TestData.Check(!new RelationBackend().Preflight(new("0.2", new[] { TestData.Hole("added_hole", 4, new(80, 10)) }, Array.Empty<DesignRelation>()), TestData.Base()).IsValid, "Out-of-bounds append accepted."));
        Add("append cannot alter existing relation-driven seed", () =>
        {
            var pattern = new OperationNode("pattern", OperationKind.CreateLinearPattern, "new_pattern", new[] { new OperationInput("seed", new[] { new SemanticReference("original_hole", SemanticType.FeatureRef) }) },
                new Dictionary<string, OperationParameter> { ["count"] = new CountParameter(2), ["spacingMm"] = new LengthParameter(10) });
            var append = new CadProgram("0.2", new[] { pattern }, new[] { new DesignRelation(RelationKind.CenteredAbout, "new_pattern", "plate.local_frame") });
            TestData.Check(!new RelationBackend().Preflight(append, TestData.Base()).IsValid, "Existing seed moved through construction.");
        });
        foreach (var stage in new[] { "resolve", "preflight", "capture", "execute", "postconditions", "final", "stage" })
        {
            var point = stage; Add("shared coordinator failure at " + point, () =>
            {
                var store = new FakeStore(); var before = JsonSerializer.Serialize(store.State); var backend = new FakeBackend { Failure = point };
                var result = new RequestMutationTransaction<CadProgram, FakePrepared, int>(store, backend).Execute(TestData.Base());
                var mutated = point is "execute" or "postconditions" or "final" or "stage";
                TestData.Check(!result.Succeeded && result.MutationStarted == mutated && result.RollbackAttempted == mutated && result.RollbackSucceeded == mutated &&
                    !result.StateCommitted && store.Commits == 0 && before == JsonSerializer.Serialize(store.State) && backend.Bodies == 0 && backend.Revision == 0, "Failure boundary/restoration differs.");
            });
        }
        Add("construction commit failure restores native and staged metadata", () =>
        {
            var store = new FakeStore { FailCommit = true }; var before = JsonSerializer.Serialize(store.State); var backend = new FakeBackend();
            var result = new RequestMutationTransaction<CadProgram, FakePrepared, int>(store, backend).Execute(TestData.Base());
            TestData.Check(result.FailureCode == "STATE_COMMIT_FAILED" && result.RollbackSucceeded && !result.StateCommitted && backend.Bodies == 0 && backend.Revision == 0 &&
                before == JsonSerializer.Serialize(store.State), "Commit failure did not restore baseline.");
        });
        Add("success publishes all new identities with full final scope", () =>
        {
            var store = new FakeStore(); var backend = new FakeBackend(); var result = new RequestMutationTransaction<CadProgram, FakePrepared, int>(store, backend).Execute(TestData.Base());
            TestData.Check(result.Succeeded && result.MutationStarted && result.StateCommitted && !result.RollbackAttempted && store.State.Revision == 1 &&
                result.Validation!.FullModel && result.Validation.Entities.Count == 2 && backend.Calls.SequenceEqual(new[] { "resolve", "preflight", "capture", "execute", "rebuild", "postconditions", "final", "stage" }), "Success scope/order differs.");
        });
        Add("rollback failure invalidates session and is explicit", () =>
        {
            var store = new FakeStore { FailCommit = true }; var backend = new FakeBackend { Failure = "rollback" };
            var result = new RequestMutationTransaction<CadProgram, FakePrepared, int>(store, backend).Execute(TestData.Base());
            TestData.Check(result.RollbackAttempted && !result.RollbackSucceeded && !result.StateCommitted && result.RollbackFailureCode == "TEST_FAILURE" && backend.Invalidated, "Failed rollback hidden.");
        });
        Add("composition result reports transaction flags rather than operation guesses", () =>
        {
            var backend = new FakeBackend { Failure = "execute" }; var result = new RequestMutationTransaction<CadProgram, FakePrepared, int>(new FakeStore(), backend).Execute(TestData.Base());
            var composition = new CompositionExecutionResult(false, result.FailureCode, result.Message, Array.Empty<OperationExecutionResult>()) { Transaction = result };
            TestData.Check(composition.MutationStarted && composition.RollbackAttempted && composition.RollbackSucceeded && !composition.StateCommitted, "Flags dropped.");
        });
        var results = new List<object>(); var failed = 0;
        foreach (var test in tests) try { test.Run(); results.Add(new { test.Name, Passed = true }); Console.WriteLine("PASS " + test.Name); }
            catch (Exception e) { failed++; results.Add(new { test.Name, Passed = false, Error = e.Message }); Console.WriteLine("FAIL " + test.Name + ": " + e.Message); }
        var output = Path.Combine(root, "artifacts/milestone9c"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "pure-result.json"), JsonSerializer.Serialize(new { Total = tests.Count, Passed = tests.Count - failed, Failed = failed, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"M9C PURE: {tests.Count - failed}/{tests.Count} passed; zero Parts."); return failed == 0 ? 0 : 1;
    }
}
