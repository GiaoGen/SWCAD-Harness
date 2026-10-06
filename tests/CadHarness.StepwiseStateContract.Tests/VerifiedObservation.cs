using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Benchmark.Tests;

// Qualification-only historical snapshots. Never used as native state or sent
// as future plans. The provider chooses all IDs; mapping is internal to the test.
internal sealed class VerifiedObservation
{
    internal CadProgram Oracle { get; }
    internal CadState State { get; }
    internal VerifiedObservation(string root, string task, string stage = "creation")
    {
        var folder = Path.Combine(root, "artifacts/milestone9g", task);
        var parsed = new CadProgramJson().Parse(File.ReadAllText(Path.Combine(folder, stage + "-current-program.json")));
        Program.Check(parsed.IsValid, "Invalid verified M9G program."); Oracle = parsed.Program!;
        State = new AtomicStateStore(Path.Combine(folder, stage + "-state.json")).Load();
        _ = SolidWorksPlanningRuntime.ForEditSnapshot(Oracle, State);
    }
    internal void Match(CadProgram actual, bool prefix)
    {
        Program.Check(actual.Operations.Count <= Oracle.Operations.Count && (prefix || actual.Operations.Count == Oracle.Operations.Count), "Task operation composition differs.");
        var mapping = actual.Operations.Select((o, i) => (o.SemanticId!, Oracle.Operations[i].SemanticId!)).ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal);
        string Map(string id) { var parts = id.Split('.', 2); Program.Check(mapping.ContainsKey(parts[0]), "Uncommitted reference."); return mapping[parts[0]] + (parts.Length == 1 ? "" : "." + parts[1]); }
        var normalized = new DesignRelationEngine().Solve(actual).Program;
        for (var i = 0; i < normalized.Operations.Count; i++)
        {
            var a = normalized.Operations[i]; var b = Oracle.Operations[i];
            Program.Check(a.Kind == b.Kind && a.Parameters.Count == b.Parameters.Count && a.Inputs.Count == b.Inputs.Count, "Task operation fields differ.");
            foreach (var p in b.Parameters) Program.Check(a.Parameters.TryGetValue(p.Key, out var value) && Equals(value, p.Value), "Task dimension/placement differs: " + p.Key);
            foreach (var input in b.Inputs)
            {
                var refs = a.Input(input.Name)?.References;
                Program.Check(refs is not null && refs.Count == input.References.Count && refs.Select(r => new SemanticReference(Map(r.SemanticId), r.Type)).SequenceEqual(input.References), "Task typed input differs: " + input.Name);
            }
        }
        var owners = Oracle.Operations.Take(actual.Operations.Count).Select(o => o.SemanticId!).ToHashSet();
        var expected = Oracle.Relations.Where(r => owners.Contains(r.Subject)).ToHashSet();
        var relations = actual.Relations.Select(r => new DesignRelation(r.Kind, Map(r.Subject), r.Reference is null ? null : Map(r.Reference))).ToHashSet();
        Program.Check(expected.IsSubsetOf(relations), "Required task relation is absent.");
    }
    internal CadState Project(CadProgram committed)
    {
        Match(committed, true);
        var owners = Oracle.Operations.Take(committed.Operations.Count).Select(o => o.SemanticId!).ToHashSet();
        var mapping = Oracle.Operations.Take(committed.Operations.Count).Select((o, i) => (o.SemanticId!, committed.Operations[i].SemanticId!)).ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal);
        string Map(string id) { var parts = id.Split('.', 2); return mapping[parts[0]] + (parts.Length == 1 ? "" : "." + parts[1]); }
        var bindings = State.Bindings.Where(b => owners.Contains(b.OwnerFeatureSemanticId)).ToArray();
        var parameterIds = bindings.Select(b => b.ParameterSemanticId).ToHashSet();
        var projected = State with {
            Revision = committed.Operations.Count,
            Features = State.Features.Where(f => owners.Contains(f.SemanticId)).Select(f => f with { SemanticId = Map(f.SemanticId) }).ToArray(),
            Entities = State.Entities.Where(e => owners.Contains(e.OwnerFeatureSemanticId)).Select(e => e with { SemanticId = Map(e.SemanticId), OwnerFeatureSemanticId = Map(e.OwnerFeatureSemanticId) }).ToArray(),
            Parameters = State.Parameters.Where(p => parameterIds.Contains(p.SemanticId)).Select(p => p with { SemanticId = Map(p.SemanticId) }).ToArray(),
            Bindings = bindings.Select(b => b with { ParameterSemanticId = Map(b.ParameterSemanticId), OwnerFeatureSemanticId = Map(b.OwnerFeatureSemanticId) }).ToArray(),
            Relations = StateRelationData.Encode(committed.Relations),
            Dependencies = StateRelationData.Encode(new DesignRelationEngine().Solve(committed).Dependencies.Edges)
        };
        StateValidation.Validate(projected); return projected;
    }
}
