using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.State;

namespace CadHarness.State.Tests;

internal static class PureTests
{
    internal static int Run(string root)
    {
        var scratchRoot = Path.GetFullPath(Path.Combine(root, "artifacts", "milestone3", "unit"));
        var scratch = Path.GetFullPath(Path.Combine(scratchRoot, Guid.NewGuid().ToString("N")));
        if (!scratch.StartsWith(scratchRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Scratch directory escaped the test workspace.");
        Directory.CreateDirectory(scratch);
        try
        {
            var path = Path.Combine(scratch, "state.json");
            var state = Sample(scratch);
            var store = new AtomicStateStore(path);
            var cases = new List<(string Name, Action Run)>
            {
                ("v0.2 state round trip retains identities, ownership and binding", () =>
                {
                    store.Commit(state); var loaded = store.Load();
                    Assert(loaded.Document == state.Document && loaded.Features[0] == state.Features[0] && loaded.Entities[0] == state.Entities[0] &&
                        loaded.Bindings[0] == state.Bindings[0] && loaded.Parameters[0] == state.Parameters[0], "State lost identity or binding data.");
                }),
                ("atomic replacement publishes complete new revision", () =>
                {
                    store.Commit(state); store.Commit(state with { Revision = 1 }); Assert(store.Load().Revision == 1, "Replacement was not committed.");
                    Assert(Directory.GetFiles(scratch, "*.tmp").Length == 0, "Temporary file leaked.");
                }),
                ("invalid state leaves prior commit unchanged", () =>
                {
                    store.Commit(state); var before = File.ReadAllBytes(path);
                    Expect("STATE_SCHEMA_INVALID", () => store.Commit(state with { Revision = -1 }));
                    Assert(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(path)), "Prior file changed.");
                }),
                ("failed native file replacement preserves prior commit", () =>
                {
                    store.Commit(state); var before = File.ReadAllBytes(path);
                    File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                    try { Expect("STATE_COMMIT_FAILED", () => store.Commit(state with { Revision = 2 })); }
                    finally { File.SetAttributes(path, FileAttributes.Normal); }
                    Assert(Convert.ToBase64String(before) == Convert.ToBase64String(File.ReadAllBytes(path)), "Failed replacement changed prior state.");
                    Assert(Directory.GetFiles(scratch, "*.tmp").Length == 0, "Failed replacement leaked its temporary file.");
                }),
                ("v0.1 state is rejected without migration", () => Expect("STATE_SCHEMA_INVALID", () => store.Commit(state with { SchemaVersion = "0.1" }))),
                ("document/configuration/path mismatches reject identity", () =>
                {
                    Assert(!state.Document.Matches(state.Document with { DocumentId = Guid.NewGuid() }), "Wrong document ID matched.");
                    Assert(!state.Document.Matches(state.Document with { ConfigurationId = Guid.NewGuid() }), "Wrong configuration ID matched.");
                    Assert(!state.Document.Matches(state.Document with { ConfigurationName = "other" }), "Wrong configuration matched.");
                    Assert(!state.Document.Matches(state.Document with { SavedPath = Path.Combine(scratch, "other.sldprt") }), "Wrong path matched.");
                }),
                ("unknown feature ownership rejects", () => Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with
                    { Entities = new[] { state.Entities[0] with { OwnerFeatureSemanticId = "missing" } } }))),
                ("unbound and duplicate parameters reject", () =>
                {
                    Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with { Bindings = Array.Empty<ParameterBinding>() }));
                    Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with { Bindings = new[] { state.Bindings[0], state.Bindings[0] } }));
                }),
                ("invalid and empty persistent payloads reject", () =>
                {
                    Expect("STATE_SCHEMA_INVALID", () => StateValidation.DecodeReference(new("not base64")));
                    Expect("STATE_SCHEMA_INVALID", () => StateValidation.DecodeReference(new("")));
                }),
                ("parameter binding enforces its declared scalar type", () => Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with
                    { Parameters = new[] { state.Parameters[0] with { Kind = ParameterKind.Count } } }))),
                ("nonfinite parameter rejects before commit", () => Expect("STATE_SCHEMA_INVALID", () => store.Commit(state with
                    { Parameters = new[] { state.Parameters[0] with { Value = double.NaN } } }))),
                ("duplicate and unknown JSON fields reject", () =>
                {
                    store.Commit(state); var valid = File.ReadAllText(path);
                    File.WriteAllText(path, valid.Replace("\"revision\": 0", "\"revision\": 0, \"revision\": 1"));
                    Expect("STATE_SCHEMA_INVALID", () => store.Load());
                    File.WriteAllText(path, valid[..^1] + ",\"unknown\":true}");
                    Expect("STATE_SCHEMA_INVALID", () => store.Load());
                }),
                ("reserved relation slot cannot implement later milestones", () =>
                {
                    using var json = JsonDocument.Parse("{}");
                    Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with { Relations = new[] { json.RootElement.Clone() } }));
                })
            };
            var failed = 0;
            foreach (var test in cases)
            {
                try { test.Run(); Console.WriteLine("PASS " + test.Name); }
                catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
            }
            Console.WriteLine($"{cases.Count - failed}/{cases.Count} M3 pure tests passed; native Parts created=0, closed=0.");
            return failed == 0 ? 0 : 1;
        }
        finally { Directory.Delete(scratch, true); }
    }
    private static CadState Sample(string directory)
    {
        var reference = new NativePersistentReference(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        return new()
        {
            SchemaVersion = "0.2", Revision = 0,
            Document = new(Guid.NewGuid(), Guid.NewGuid(), "default", Path.Combine(directory, "plate.sldprt")),
            Features = new[] { new FeatureNode("plate", OperationKind.CreateExtrude, reference, ReferenceHealth.Healthy) },
            Entities = new[] { new SemanticEntityNode("plate", SemanticType.FeatureRef, "plate", reference, ReferenceHealth.Healthy) },
            Parameters = new[] { new ParameterNode("plate.extrusion_depth", ParameterKind.Length, 10) },
            Bindings = new[] { new ParameterBinding("plate.extrusion_depth", "plate", EditableParameter.ExtrusionDepth) },
            Relations = Array.Empty<JsonElement>(), Dependencies = Array.Empty<JsonElement>()
        };
    }
    private static void Expect(string code, Action action)
    {
        try { action(); } catch (StateException error) when (error.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
