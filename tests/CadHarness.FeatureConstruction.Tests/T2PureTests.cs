using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.State;

internal static partial class T2Tests
{
    internal static int Pure(string root, string run)
    {
        var output = Path.Combine(root, "artifacts/m20-preparation/T2", run); var results = new List<object>(); var failed = 0;
        void Test(string name, Action action) { try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + " " + e.Message); } }
        void Bad(Action action) { try { action(); } catch (Exception e) when (e is ContractException or StateException or JsonException) { return; } throw new Exception("Unsafe radial contract accepted."); }
        var shaft = T2Cases.Section(false); var sleeve = T2Cases.Section(true);
        Test("orthogonal shaft half-section", () => PlanarSketchPreflight.Prepare(shaft));
        Test("radial driver policy does not claim fixed primitives or native equations",()=>Program.Check(PlanarSketchPreflight.Prepare(shaft).CoordinateDriverPolicy.Contains("native radial driver dimensions") && PlanarSketchPreflight.Prepare(shaft).CoordinateDriverPolicy.Contains("not through inferred native equations"),"Incorrect driver policy"));
        Test("public radial qualification does not open axial editing",()=>Program.Check(CadHarness.SolidWorks.GenericFeatureCapabilities.RadialProfileExecutable && !CadHarness.SolidWorks.GenericFeatureCapabilities.IsQualified(ConstructionKind.CreateRevolvedBoss,ParameterKey.RevolveAxialSpan),"Qualification scope widened"));
        Test("positive-wall stepped bore", () => PlanarSketchPreflight.Prepare(sleeve));
        Test("strict program roundtrip with radial extension", () => ContractJson.Read<ConstructionProgram>(ContractJson.Write(T2Cases.Program(false), ContractValidation.Program), ContractValidation.Program));
        Test("old wire without radial field remains valid", () => { var json = ContractJson.Write(Cases.Torus, ContractValidation.Program); Program.Check(!json.Contains("radialProfile"), "Old wire changed"); ContractJson.Read<ConstructionProgram>(json, ContractValidation.Program); });
        Test("offset propagation and unrelated stations preserved", () => {
            var changed = RadialProfilePolicy.Edit(shaft, "middle", 12, 13); var values = changed.RadialProfile!.Drivers.ToDictionary(d => d.Id, d => d.Radius.Millimeters);
            Program.Check(values["middle"] == 13 && values["groove"] == 11 && values["head"] == 15 && values["tip"] == 9 && shaft.Points.Zip(changed.Points).All(p => p.First.X == p.Second.X), "Propagation differs");
        });
        Test("bore edit keeps outer radii and positive wall", () => { var changed = RadialProfilePolicy.Edit(sleeve, "large_bore", 12, 13); Program.Check(changed.RadialProfile!.Drivers.Single(d => d.Id == "outside").Radius.Millimeters == 16, "Outer radius drifted"); });
        Test("derived target refuses direct edit", () => Bad(() => RadialProfilePolicy.Edit(shaft, "groove", 10, 11)));
        Test("expected old driver mismatch", () => Bad(() => RadialProfilePolicy.Edit(shaft, "middle", 11, 13)));
        Test("missing driver", () => Bad(() => RadialProfilePolicy.Edit(shaft, "missing", 12, 13)));
        Test("negative radius", () => Bad(() => RadialProfilePolicy.Edit(shaft, "middle", 12, -1)));
        Test("nonfinite radius", () => Bad(() => RadialProfilePolicy.Edit(shaft, "middle", 12, double.NaN)));
        Test("collapsed wall refuses before mutation", () => Bad(() => RadialProfilePolicy.Edit(sleeve, "large_bore", 12, 25)));
        Test("collapsed derived radius", () => Bad(() => RadialProfilePolicy.Edit(shaft, "middle", 12, 1)));
        Test("relation cycle", () => Bad(() => PlanarSketchPreflight.Prepare(shaft with { RadialProfile = shaft.RadialProfile! with { Relations = new[] {
            new RadialOffsetRelation("one", "middle", "groove", new(2)), new("two", "groove", "middle", new(2)) } } })));
        Test("unknown relation endpoint", () => Bad(() => PlanarSketchPreflight.Prepare(shaft with { RadialProfile = shaft.RadialProfile! with { Relations = new[] { new RadialOffsetRelation("unknown", "other", "groove", new(2)) } } })));
        Test("wrong point driver", () => Bad(() => PlanarSketchPreflight.Prepare(shaft with { RadialProfile = shaft.RadialProfile! with { Points = shaft.RadialProfile.Points.Select((p, i) => i == 4 ? p with { DriverId = "head" } : p).ToArray() } })));
        Test("same-key selection ambiguity", () => Bad(() => GenericParameterSelection.Resolve(new[] { new GenericNativeParameter("rot.radius.a", "rot", ParameterKey.RevolveRadius, 12, true), new("rot.radius.b", "rot", ParameterKey.RevolveRadius, 12, true) }, "rot", ParameterKey.RevolveRadius, null)));
        Test("explicit parameter semantic identity selects exact driver", () => { var p = GenericParameterSelection.Resolve(new[] { new GenericNativeParameter("rot.radius.a", "rot", ParameterKey.RevolveRadius, 12, true), new("rot.radius.b", "rot", ParameterKey.RevolveRadius, 12, true) }, "rot", ParameterKey.RevolveRadius, "rot.radius.b"); Program.Check(p!.SemanticId == "rot.radius.b", "Wrong driver"); });
        Test("different owner never substitutes same identity", () => Program.Check(GenericParameterSelection.Resolve(new[] { new GenericNativeParameter("rot.radius.a", "rot", ParameterKey.RevolveRadius, 12, true) }, "other", ParameterKey.RevolveRadius, "rot.radius.a") is null, "Wrong owner accepted"));
        Test("legacy unique radius key still resolves", () => Program.Check(GenericParameterSelection.Resolve(new[] { new GenericNativeParameter("rot.radius", "rot", ParameterKey.RevolveRadius, 3, true) }, "rot", ParameterKey.RevolveRadius, null) is not null, "Legacy broken"));
        Test("different rail layout and parameter names", () => {
            var alternate = sleeve with { Points = sleeve.Points.Select(p => p with { X = new(p.X.Millimeters * 1.7 + 3) }).ToArray(), RadialProfile = sleeve.RadialProfile! with { Drivers = sleeve.RadialProfile.Drivers.Select(d => d with { Id = "dim_" + d.Id }).ToArray(), Points = sleeve.RadialProfile.Points.Select(p => p with { DriverId = "dim_" + p.DriverId }).ToArray() } };
            _ = RadialProfilePolicy.Edit(alternate, "dim_large_bore", 12, 13);
        });
        Test("different segment count has no fixed vertex assumptions", () => {
            var points = new[] { new LocalPoint("a",new(2),new(0)),new("b",new(24),new(0)),new("c",new(24),new(6)),new("d",new(12),new(6)),new("e",new(12),new(9)),new("f",new(2),new(9)) };
            var profile = new ClosedSketch(points,new[]{new SketchEntity("section",SketchEntityKind.Polyline,points.Select(p=>p.Id).ToArray(),null,null,null,null)},new[]{new SketchLoop("loop",false,new[]{"section"})},Array.Empty<SketchConstraint>(),ConstraintStatus.FullyConstrained) {
                RadialProfile = new(1,new[]{new RadialDriver("first",new(9)),new("second",new(6))},new[]{new RadialPointBinding("a",null),new("b",null),new("c","second"),new("d","second"),new("e","first"),new("f","first")},Array.Empty<RadialOffsetRelation>()) };
            var changed = RadialProfilePolicy.Edit(profile,"second",6,7); Program.Check(changed.Points.Single(p=>p.Id=="c").Y.Millimeters==7 && changed.Points.Single(p=>p.Id=="e").Y.Millimeters==9,"Different topology edit failed");
        });
        Test("post-cold fault increment stays in supported geometry", () => {
            var changed = RadialProfilePolicy.Edit(RadialProfilePolicy.Edit(shaft,"middle",12,14),"middle",14,14.5);
            Program.Check(changed.RadialProfile!.Drivers.Single(d=>d.Id=="groove").Radius.Millimeters==12.5,"Fault input is not legal");
        });
        Test("decrease chooses dependent first to avoid zero intermediate rail",()=>{
            var before=RadialProfilePolicy.Edit(shaft,"middle",12,13); var after=RadialProfilePolicy.Edit(before,"middle",13,11);
            Program.Check(RadialProfilePolicy.NativeOrder(before,after).SequenceEqual(new[]{"groove","middle"}),"Unsafe source-first decrease");
        });
        Test("growth chooses source first and rollback has reverse safe order",()=>{
            var after=RadialProfilePolicy.Edit(shaft,"middle",12,13);
            Program.Check(RadialProfilePolicy.NativeOrder(shaft,after).SequenceEqual(new[]{"middle","groove"}) &&
                RadialProfilePolicy.NativeOrder(after,shaft).SequenceEqual(new[]{"groove","middle"}),"Wrong growth/restore order");
        });
        var pointerPath = Path.Combine(root,"artifacts/m20-preparation/T2/candidate_v4/sleeve/package/current.json");
        if (File.Exists(pointerPath))
        {
            using var pointer = JsonDocument.Parse(File.ReadAllText(pointerPath));
            var manifestPath = pointer.RootElement.GetProperty("manifest").GetProperty("path").GetString()!;
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var statePath = manifest.RootElement.GetProperty("state").GetProperty("path").GetString()!;
            var packet = ContractJson.Read<GenericRevisionPackage>(File.ReadAllText(statePath),GenericRevisionValidation.Validate);
            Test("format 2 native-derived packet strict roundtrip",()=>ContractJson.Read<GenericRevisionPackage>(ContractJson.Write(packet,GenericRevisionValidation.Validate),GenericRevisionValidation.Validate));
            Test("radial packet cannot masquerade as format 1",()=>Bad(()=>GenericRevisionValidation.Validate(packet with { FormatVersion=1 })));
            Test("radial-only packet cannot masquerade as edge format 3",()=>Bad(()=>GenericRevisionValidation.Validate(packet with { FormatVersion=3 })));
            var image=packet.SketchBindings.Single();
            Test("missing radial binding refuses",()=>Bad(()=>GenericRevisionValidation.Validate(packet with { SketchBindings=new[]{image with { Radial=null }} })));
            Test("wrong radial primitive driver refuses",()=>Bad(()=>GenericRevisionValidation.Validate(packet with { SketchBindings=new[]{image with { Radial=image.Radial! with { Dimensions=image.Radial!.Dimensions.Select((d,i)=>i==0?d with { PrimitiveOrdinal=999 }:d).ToArray() } }} })));
            Test("unknown dimension accessor refuses",()=>Bad(()=>GenericRevisionValidation.Validate(packet with { SketchBindings=new[]{image with { Radial=image.Radial! with { Dimensions=image.Radial!.Dimensions.Select((d,i)=>i==0?d with { NativeDimensionType=2 }:d).ToArray() } }} })));
            Test("wrong exact profile owner refuses",()=>Bad(()=>GenericRevisionValidation.Validate(packet with { FeatureBindings=packet.FeatureBindings.Select(b=>b with { ProfileId="wrong_owner" }).ToArray() })));
            Test("missing versioned driver wire field refuses",()=>Bad(()=>ContractJson.Read<GenericRevisionPackage>(ContractJson.Write(packet,GenericRevisionValidation.Validate).Replace("\"version\": 1,",""),GenericRevisionValidation.Validate)));
            Test("unknown versioned driver wire field refuses",()=>Bad(()=>ContractJson.Read<GenericRevisionPackage>(ContractJson.Write(packet,GenericRevisionValidation.Validate).Replace("\"version\": 1,","\"version\": 1, \"unsupported\": true,"),GenericRevisionValidation.Validate)));
        }
        Program.Write(Path.Combine(output, "result.json"), new { passed = failed == 0, count = results.Count, failures = failed, results, nativeOpens = 0, newParts = 0 }); return failed == 0 ? 0 : 1;
    }
}
