using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using CadHarness.State.V03;

internal static partial class T1Tests
{
    private sealed class GenericFake : IGenericRevisionNative
    {
        internal string Path = "";
        public GenericRevisionPackage CurrentGeneric { get; set; } = null!;
        public void SaveNative() => File.WriteAllText(Path, "native-revision-" + CurrentGeneric.State.Revision);
        public void VerifySavedGeneric(GenericRevisionPackage package) => Program.Check(File.ReadAllText(Path) == "native-revision-" + package.State.Revision, "Fake saved read differs");
    }
    private sealed class LegacyFake : IManagedRevisionNative
    {
        public CadProgram CurrentProgram => throw new NotSupportedException();
        public void SaveNative() => throw new NotSupportedException();
        public void VerifySavedRevision(CadState state, CadProgram program) => throw new NotSupportedException();
    }
    internal static int Pure(string root, string run)
    {
        var output = Path.Combine(root, "artifacts/m20-preparation/T1", run); Directory.CreateDirectory(output);
        var results = new List<object>(); var failures = 0;
        void Test(string name, Action test) { try { test(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + " " + e.Message); } }
        void Bad(Action test) { try { test(); } catch (Exception e) when (e is ContractException or StateException or JsonException) { return; } throw new Exception("Unsafe Generic package accepted"); }
        var packet = Synthetic(Path.Combine(output, "owned.SLDPRT"));
        Test("strict Generic package roundtrip", () => GenericRevisionValidation.Validate(ContractJson.Read<GenericRevisionPackage>(ContractJson.Write(packet, GenericRevisionValidation.Validate), GenericRevisionValidation.Validate)));
        Test("unknown field refusal", () => Bad(() => ContractJson.Read<GenericRevisionPackage>(ContractJson.Write(packet, GenericRevisionValidation.Validate).Replace("\"formatVersion\": 1", "\"formatVersion\": 1, \"extra\": 3"), GenericRevisionValidation.Validate)));
        Test("missing field refusal", () => Bad(() => ContractJson.Read<GenericRevisionPackage>(ContractJson.Write(packet, GenericRevisionValidation.Validate).Replace("\"formatVersion\": 1,", ""), GenericRevisionValidation.Validate)));
        Test("wrong package version refusal", () => Bad(() => GenericRevisionValidation.Validate(packet with { FormatVersion = 2 })));
        Test("wrong package type refusal", () => Bad(() => GenericRevisionValidation.Validate(packet with { PackageType = "legacy" })));
        Test("profile association refusal", () => Bad(() => GenericRevisionValidation.Validate(packet with { FeatureBindings = packet.FeatureBindings.Select((b, i) => i == 0 ? b with { ProfileId = "unknown" } : b).ToArray() })));
        Test("transform shape refusal", () => Bad(() => GenericRevisionValidation.Validate(packet with { SketchBindings = packet.SketchBindings.Select((b, i) => i == 0 ? b with { ModelToSketchTransform = new[] { 1.0 } } : b).ToArray() })));
        Test("native feature association refusal", () => Bad(() => GenericRevisionValidation.Validate(packet with { State = packet.State with { Features = packet.State.Features.Skip(1).ToArray() } })));
        foreach (var point in Enum.GetValues<DurableFaultPoint>())
        {
            Test("durable boundary " + point, () =>
            {
                var dir = Path.Combine(output, point.ToString()); var fake = new GenericFake(); DurableFaultPoint? injected = null;
                using var store = new ManagedRevisionStore(dir, fake, p => { if (p == injected) throw new StateException("INJECTED", p.ToString()); });
                Directory.CreateDirectory(Path.GetDirectoryName(store.WorkingPath)!); File.WriteAllText(store.WorkingPath, "blank");
                fake.Path = store.WorkingPath; fake.CurrentGeneric = Synthetic(store.WorkingPath);
                store.PrepareInitialGenericCheckpoint(1); store.Initialize(GenericRevisionValidation.Envelope(fake.CurrentGeneric.State));
                var old = store.ReadCurrent(); var oldPointer = ManagedRevisionStore.Hash(store.PointerPath);
                injected = point; store.PrepareCheckpoint(); fake.CurrentGeneric = fake.CurrentGeneric with { State = fake.CurrentGeneric.State with { Revision = 2 } };
                var committed = false;
                try { store.Commit(GenericRevisionValidation.Envelope(fake.CurrentGeneric.State)); committed = true; } catch (StateException e) when (e.Code == "INJECTED") { }
                Program.Check(committed == (point == DurableFaultPoint.AfterPointerPublish), "Commit boundary incorrect");
                var inspection = store.Inspect(store.WorkingPath);
                Program.Check(inspection.RequiresNativeRecovery && inspection.Revision!.State.Revision == (committed ? 2 : 1), "Wrong recovery authority");
                if (!committed) Program.Check(ManagedRevisionStore.Hash(store.PointerPath) == oldPointer, "Pointer advanced on failure");
                store.RestoreWorkingCopy(inspection); fake.CurrentGeneric = inspection.Revision!.Generic!; store.CompleteRecovery(inspection);
                Program.Check(!File.Exists(store.RecoveryPath) && store.Load().Revision == (committed ? 2 : old.State.Revision), "Recovery unfinished");
            });
        }
        Test("initial failed publish remains isolated", () =>
        {
            var dir = Path.Combine(output, "initial-fault"); var fake = new GenericFake();
            using var store = new ManagedRevisionStore(dir, fake, p => { if (p == DurableFaultPoint.AfterNativeSave) throw new StateException("INJECTED", "initial"); });
            fake.Path = store.WorkingPath; fake.CurrentGeneric = Synthetic(store.WorkingPath); Directory.CreateDirectory(Path.GetDirectoryName(fake.Path)!); File.WriteAllText(fake.Path, "blank");
            store.PrepareInitialGenericCheckpoint(1); Bad(() => store.Initialize(GenericRevisionValidation.Envelope(fake.CurrentGeneric.State)));
            Program.Check(store.Inspect(fake.Path).Status == ReopenStatus.Quarantined && !File.Exists(store.PointerPath), "Invented initial authority");
        });
        Test("artifact drift before native open refuses", () =>
        {
            var dir = Path.Combine(output, "artifact-drift"); var fake = new GenericFake();
            using var store = new ManagedRevisionStore(dir, fake);
            fake.Path = store.WorkingPath; fake.CurrentGeneric = Synthetic(store.WorkingPath); Directory.CreateDirectory(Path.GetDirectoryName(fake.Path)!); File.WriteAllText(fake.Path, "blank");
            store.Initialize(GenericRevisionValidation.Envelope(fake.CurrentGeneric.State));
            var revision = store.ReadCurrent(); File.AppendAllText(revision.Manifest.Program!.Path, " "); Bad(() => store.ReadCurrent());
        });
        Test("Generic is never interpreted by legacy controller", () =>
        {
            var dir = Path.Combine(output, "mode"); var fake = new GenericFake();
            using (var store = new ManagedRevisionStore(dir, fake))
            {
                fake.Path = store.WorkingPath; fake.CurrentGeneric = Synthetic(store.WorkingPath); Directory.CreateDirectory(Path.GetDirectoryName(fake.Path)!); File.WriteAllText(fake.Path, "blank");
                store.Initialize(GenericRevisionValidation.Envelope(fake.CurrentGeneric.State));
            }
            using var legacy = new ManagedRevisionStore(dir, new LegacyFake()); Bad(() => legacy.ReadCurrent());
        });
        foreach (var mismatch in new[] { "document", "configuration", "configuration-name", "revision", "working-path", "envelope-association" })
        {
            Test("Generic identity guard " + mismatch, () =>
            {
                var dir = Path.Combine(output, mismatch); var fake = new GenericFake(); using var store = new ManagedRevisionStore(dir, fake);
                fake.Path = store.WorkingPath; fake.CurrentGeneric = Synthetic(store.WorkingPath); Directory.CreateDirectory(Path.GetDirectoryName(fake.Path)!); File.WriteAllText(fake.Path, "blank");
                store.Initialize(GenericRevisionValidation.Envelope(fake.CurrentGeneric.State)); store.PrepareCheckpoint();
                var state = fake.CurrentGeneric.State with { Revision = 2 };
                state = mismatch switch {
                    "document" => state with { Document = state.Document with { DocumentId = Guid.NewGuid() } },
                    "configuration" => state with { Document = state.Document with { ConfigurationId = Guid.NewGuid() } },
                    "configuration-name" => state with { Document = state.Document with { ConfigurationName = "changed" } },
                    "revision" => state with { Revision = 3 },
                    "working-path" => state with { Document = state.Document with { SavedPath = Path.Combine(dir, "other.SLDPRT") } }, _ => state };
                fake.CurrentGeneric = fake.CurrentGeneric with { State = state };
                var envelope = GenericRevisionValidation.Envelope(state);
                if (mismatch == "envelope-association") envelope = envelope with { Parameters = new[] { new ParameterNode("untracked", ParameterKind.Length, 2) } };
                var pointer = ManagedRevisionStore.Hash(store.PointerPath); Bad(() => store.Commit(envelope));
                Program.Check(ManagedRevisionStore.Hash(store.PointerPath) == pointer, "Identity guard advanced pointer");
            });
        }
        Program.Write(Path.Combine(output, "result.json"), new { passed = failures == 0, count = results.Count, failures, results, nativeParts = 0, nativeOpens = 0 }); return failures == 0 ? 0 : 1;
    }
    private static GenericRevisionPackage Synthetic(string path)
    {
        var program = Cases.Cut; var ids = program.Operations.Select((o, i) => (o.SemanticId, Ref: new NativePersistentReference(Convert.ToBase64String(BitConverter.GetBytes(i + 1))))).ToDictionary(p => p.SemanticId, p => p.Ref);
        var features = program.Operations.Select(o => new GenericNativeFeature(o.SemanticId, o.Kind, "synthetic", ids[o.SemanticId], Array.Empty<NativePersistentReference>())).ToArray();
        var sketches = program.Operations.Where(o => o.Sketch is not null).Select(o => new GenericNativeSketch(o.SemanticId, ids[o.SemanticId], new("AQ=="), o.Placement!, o.Sketch!,
            PlanarSketchPreflight.Prepare(o.Sketch!).Primitives.Select((p, i) => new NativePersistentReference(Convert.ToBase64String(BitConverter.GetBytes(i + 100)))).ToArray())).ToArray();
        var state = new GenericConstructionState(new(Guid.NewGuid(), Guid.NewGuid(), "Default", path), 1, program, features, sketches,
            Array.Empty<GenericNativeParameter>(), Array.Empty<DependencyEdge>(), null);
        return new("0.3", "generic_construction", 1, state, sketches.Select(s => new GenericSketchImage(s.SemanticId, s.Primitives, Array.Empty<GenericDimensionImage>(), new double[16], 0)).ToArray(),
            program.Operations.Where(o => o.Profile is not null).Select(o => new GenericFeatureImage(o.SemanticId, o.Profile!.SemanticId[..^8], false, null, null, null)).ToArray(), null);
    }
}
