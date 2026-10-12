using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.State;
using Vector3 = CadHarness.Ir.V03.Vector3;

internal static partial class T3Tests
{
    internal static int Pure(string root, string run)
    {
        var results = new List<object>(); var failed = 0;
        void Test(string name, Action action) { try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; results.Add(new { name, passed = false, error = e.ToString() }); Console.WriteLine("FAIL " + name + " " + e.Message); } }
        void Bad(ConstructionProgram p) { try { ConstructionPreflight.EvaluateGeometry(p); } catch (ContractException) { return; } throw new Exception("Unproved material accepted"); }
        var model = T3Cases.Cross();
        Test("public qualification stays within existing scalar accessors",()=>Program.Check(CadHarness.SolidWorks.GenericFeatureCapabilities.CrossAxisExecutable &&
            CadHarness.SolidWorks.GenericFeatureCapabilities.ConvexFilletExecutable && CadHarness.SolidWorks.GenericFeatureCapabilities.IsOperationQualified(ConstructionKind.ApplyFillet) &&
            !CadHarness.SolidWorks.GenericFeatureCapabilities.IsQualified(ConstructionKind.ApplyFillet,ParameterKey.AdditiveBossDepth) &&
            !CadHarness.SolidWorks.GenericFeatureCapabilities.IsQualified(ConstructionKind.CreateRevolvedBoss,ParameterKey.RevolveAxialSpan),"Qualification widened parameters"));
        Test("selector zero and multiple candidates never choose first",()=>{
            foreach(var count in new[]{0,2,3}) { var refused=false;try {ConvexFilletProof.RequireUniqueCandidateCount(count);}catch(ContractException e){refused=e.Code==V03FailureCodes.AmbiguousTargetSet;}Program.Check(refused,"Ambiguous selector accepted"); }
            ConvexFilletProof.RequireUniqueCandidateCount(1);
        });
        Test("old program wire does not acquire mandatory edge fields",()=>Program.Check(!ContractJson.Write(Cases.Torus,ContractValidation.Program).Contains("edgeTreatment"),"Old wire broken"));
        Test("unsupported edge treatment version refuses",()=>Bad(T3Cases.Changed(T3Cases.Full(),"corner",o=>o with { EdgeTreatment=o.EdgeTreatment! with { Version=2 } })));
        Test("edge selector cannot own a cut",()=>Bad(T3Cases.Changed(T3Cases.Full(),"corner",o=>o with { EdgeTreatment=o.EdgeTreatment! with { HostFeature=new("wall_hole",CadHarness.Ir.SemanticType.FeatureRef) } })));
        Test("convex fillet exact material delta and following host top",()=>{
            var initial=ConstructionPreflight.EvaluateGeometry(T3Cases.Full()); var edited=ConstructionPreflight.EvaluateGeometry(T3Cases.Full(44));
            Program.Check(Math.Abs(initial.VolumeMm3-(32000-216*Math.PI-(1-Math.PI/4)*40))<1e-6 &&
                ConvexFilletProof.Describe(T3Cases.Full(44),edited).Single().Start[2]==52,"Fillet material/host relation differs");
        });
        Test("overlarge convex fillet refuses",()=>Bad(T3Cases.Changed(T3Cases.Full(),"corner",o=>o with { EdgeTreatment=o.EdgeTreatment! with { Radius=new(4) } })));
        Test("covered concave attachment corner refuses",()=>Bad(T3Cases.Changed(T3Cases.Full(),"corner",o=>o with { EdgeTreatment=o.EdgeTreatment! with { SecondSide=BoundarySide.Minimum } })));
        Test("multiple treatments on the same edge refuse",()=>Bad(T3Cases.Full() with { Operations=T3Cases.Full().Operations.Append(T3Cases.Full().Operations.Last() with { Id="another_op",SemanticId="another" }).ToArray() }));
        Test("new typed fillet strict wire roundtrip",()=>ContractJson.Read<ConstructionProgram>(ContractJson.Write(T3Cases.Full(),ContractValidation.Program),ContractValidation.Program));
        Test("exact two-axis union and material spans", () => {
            var proof = ConstructionPreflight.EvaluateGeometry(model);
            Program.Check(proof.Orthogonal?.Bores.Count == 3 && Math.Abs(proof.VolumeMm3 - (60 * 40 * 8 + 8 * 40 * 40 - 3 * Math.PI * 9 * 8)) < 1e-6 && proof.MaximumLocalMm.Z == 48, "Wrong exact material");
        });
        Test("height edit preserves bore locations and spans", () => {
            var proof = ConstructionPreflight.EvaluateGeometry(T3Cases.Cross(44));
            Program.Check(proof.MaximumLocalMm.Z == 52 && proof.Orthogonal!.Bores.All(b => b.End - b.Start == 8), "Height/bore span differs");
        });
        Test("parallel strategy regression", () => Program.Check(ConstructionPreflight.EvaluateGeometry(Cases.Composition).Orthogonal is null, "Old strategy displaced"));
        Test("rotational strategy regression", () => Program.Check(ConstructionPreflight.EvaluateGeometry(T2Cases.Program(false)).RevolveRadiusMm == 15, "Revolve displaced"));
        Test("empty cut misses material", () => Bad(T3Cases.Changed(model,"wall_hole_profile",o=>o with { Sketch=Cases.Circle(100,30,3) })));
        Test("partial disk intersection refuses", () => Bad(T3Cases.Changed(model,"wall_hole_profile",o=>o with { Sketch=Cases.Circle(19,30,3) })));
        Test("cut crossing support attachment refuses", () => Bad(T3Cases.Changed(model,"wall_hole_profile",o=>o with { Sketch=Cases.Circle(0,8,3) })));
        Test("detached addition refuses", () => Bad(T3Cases.Changed(model,"upright_profile",o=>o with { Sketch=Cases.Rectangle(70,-20,78,20) })));
        Test("intersecting cross-axis bores refuse", () => Bad(T3Cases.Changed(model,"wall_hole_profile",o=>o with { Sketch=Cases.Circle(-10,4,3) })));
        Test("coaxial overlapping partial removals are not double counted", () => {
            var same=T3Cases.Changed(T3Cases.Changed(model,"hole_a",o=>o with { EndCondition=EndCondition.Blind,Depth=new(4) }),"hole_b_profile",o=>o with { Sketch=Cases.Circle(30,-10,3) });
            var e=ConstructionPreflight.EvaluateGeometry(same); Program.Check(e.Orthogonal!.Bores.Count==2 && Math.Abs(e.VolumeMm3-(32000-2*Math.PI*72))<1e-6,"Duplicate counted twice");
        });
        Test("entirely repeated removal refuses before native mutation",()=>Bad(T3Cases.Changed(model,"hole_b_profile",o=>o with { Sketch=Cases.Circle(30,-10,3) })));
        Test("near-coaxial overlap is not silently treated as identical",()=>Bad(T3Cases.Changed(model,"hole_b_profile",o=>o with { Sketch=Cases.Circle(30+1e-8,-10,3) })));
        Test("oblique material frame refuses", () => Bad(T3Cases.Changed(model,"wall_hole_profile",o=>o with { Placement=o.Placement! with {
            Frame=new(new(0,0,0),new(0,1,0),new(-Math.Sqrt(.5),0,Math.Sqrt(.5)),new(Math.Sqrt(.5),0,Math.Sqrt(.5))) } })));
        Test("rigid transform has no world-axis special case", () => {
            var angle=.37; Vector3 Rotate(Vector3 v)=>new(v.X*Math.Cos(angle)-v.Y*Math.Sin(angle),v.X*Math.Sin(angle)+v.Y*Math.Cos(angle),v.Z);
            var transformed=model with { Operations=model.Operations.Select(o=>o.Placement is not { } p ? o : o with { Placement=p with { Frame=p.Frame with {
                OriginMm=SketchFrames.Add(Rotate(p.Frame.OriginMm),new(71,-23,17)),XAxis=Rotate(p.Frame.XAxis),YAxis=Rotate(p.Frame.YAxis),ZAxis=Rotate(p.Frame.ZAxis) } } }).ToArray() };
            var sketches=transformed.Operations.Where(o=>o.Sketch is not null).ToDictionary(o=>o.SemanticId);
            var e=FeatureConstructionPreflight.Evaluate(transformed.Operations.Where(o=>o.Profile is not null).Select(o=>new PreparedFeatureProfile(o,sketches[o.Profile!.SemanticId[..^8]].Placement!,PlanarSketchPreflight.Prepare(sketches[o.Profile.SemanticId[..^8]].Sketch!))).ToArray());
            Program.Check(Math.Abs(e.VolumeMm3-ConstructionPreflight.EvaluateGeometry(model).VolumeMm3)<1e-6,"Rigid transform changes material");
        });
        Test("fillet selector follows transformed host boundaries",()=>{
            var p=T3Cases.Full(); var angle=.29;Vector3 R(Vector3 v)=>new(v.X*Math.Cos(angle)-v.Y*Math.Sin(angle),v.X*Math.Sin(angle)+v.Y*Math.Cos(angle),v.Z);
            p=p with { Operations=p.Operations.Select(o=>o.Placement is not { } placement?o:o with { Placement=placement with { Frame=placement.Frame with {
                OriginMm=SketchFrames.Add(R(placement.Frame.OriginMm),new(-18,63,29)),XAxis=R(placement.Frame.XAxis),YAxis=R(placement.Frame.YAxis),ZAxis=R(placement.Frame.ZAxis) } } }).ToArray() };
            var sketches=p.Operations.Where(o=>o.Sketch is not null).ToDictionary(o=>o.SemanticId);
            var material=OrthogonalMaterialProof.Evaluate(p.Operations.Where(o=>o.Profile is not null).Select(o=>new PreparedFeatureProfile(o,sketches[o.Profile!.SemanticId[..^8]].Placement!,PlanarSketchPreflight.Prepare(sketches[o.Profile.SemanticId[..^8]].Sketch!))).ToArray());
            var g=ConvexFilletProof.Describe(p,material).Single();Program.Check(Math.Abs(g.Start[0])<1e-6 && Math.Abs(g.Start[2]-48)<1e-6 && Math.Abs(ConvexFilletProof.Apply(p,material).VolumeMm3-ConstructionPreflight.EvaluateGeometry(T3Cases.Full()).VolumeMm3)<1e-6,"Transformed selector changed corner/material");
        });
        var pointerPath=Path.Combine(root,"artifacts/m20-preparation/T3/candidate_v1/cross/package/current.json");
        if(File.Exists(pointerPath))
        {
            using var pointer=JsonDocument.Parse(File.ReadAllText(pointerPath));using var manifest=JsonDocument.Parse(File.ReadAllText(pointer.RootElement.GetProperty("manifest").GetProperty("path").GetString()!));
            var packet=ContractJson.Read<GenericRevisionPackage>(File.ReadAllText(manifest.RootElement.GetProperty("state").GetProperty("path").GetString()!),GenericRevisionValidation.Validate);
            void BadPackage(GenericRevisionPackage p){var refused=false;try{GenericRevisionValidation.Validate(p);}catch(ContractException){refused=true;}Program.Check(refused,"Invalid edge package accepted");}
            if(packet.FormatVersion==3)
            {
                Test("explicit format 3 edge packet strict roundtrip",()=>ContractJson.Read<GenericRevisionPackage>(ContractJson.Write(packet,GenericRevisionValidation.Validate),GenericRevisionValidation.Validate));
                Test("edge packet cannot masquerade as old format",()=>{BadPackage(packet with { FormatVersion=1 });BadPackage(packet with { FormatVersion=2 });BadPackage(packet with { FormatVersion=4 });});
                Test("missing persisted edge refuses",()=>BadPackage(packet with { FeatureBindings=packet.FeatureBindings.Select(b=>b.Edge is null?b:b with { Edge=null }).ToArray() }));
                Test("wrong persisted edge host refuses",()=>BadPackage(packet with { FeatureBindings=packet.FeatureBindings.Select(b=>b.Edge is null?b:b with { Edge=b.Edge with { HostFeature=packet.State.Features.Single(f=>f.SemanticId=="base").Reference } }).ToArray() }));
                Test("unknown adjacent face owner refuses",()=>BadPackage(packet with { FeatureBindings=packet.FeatureBindings.Select(b=>b.Edge is null?b:b with { Edge=b.Edge with { FaceOwners=new[]{b.Edge.HostFeature,new NativePersistentReference("AQ==")} } }).ToArray() }));
                Test("missing edge selector field refuses strict decode",()=>{var json=ContractJson.Write(packet,GenericRevisionValidation.Validate).Replace("\"firstSide\": \"minimum\",","");var refused=false;try{ContractJson.Read<GenericRevisionPackage>(json,GenericRevisionValidation.Validate);}catch(ContractException){refused=true;}Program.Check(refused,"Missing selector accepted");});
            }
        }
        Program.Write(Path.Combine(root,"artifacts/m20-preparation/T3",run,"result.json"),new { passed=failed==0,total=results.Count,failed,results }); return failed==0?0:1;
    }
}
