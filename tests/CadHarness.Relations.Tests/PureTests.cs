using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.SolidWorks;
using CadHarness.State;

namespace CadHarness.Relations.Tests;

internal static class PureTests
{
    internal static CadProgram Fixture(string root, string name)
    {
        var parsed = new CadProgramJson().Parse(File.ReadAllText(Path.Combine(root, "tests", "CadHarness.Relations.Tests", "Fixtures", name + ".json")));
        if (!parsed.IsValid) throw new Exception(parsed.Issues[0].Code + ": " + parsed.Issues[0].Message);
        return parsed.Program!;
    }
    internal static OperationNode Edit(EditableParameter parameter, double value, string target = "holes") => new("edit", OperationKind.EditParameter, null,
        new[] { new OperationInput("target", new[] { new SemanticReference(target, SemanticType.FeatureRef) }) },
        new Dictionary<string, OperationParameter>
        {
            ["parameter"] = new ParameterNameParameter(parameter),
            ["value"] = new EditValueParameter(EditableParameters.Contract(parameter).Kind == ParameterKind.Count ? new CountParameter(checked((int)value)) : new LengthParameter(value))
        });
    internal static int Run(string root)
    {
        var linear = Fixture(root, "linear"); var rect = Fixture(root, "rect2x2"); var six = Fixture(root, "rect2x3");
        var engine = new DesignRelationEngine(); var binder = new SemanticEntityBinder();
        var state = Sample(engine.Solve(rect));
        var host = OperationRegistry.Default.Get(OperationKind.CreateThroughHole).Inputs[0];
        var scratch = Path.Combine(root, "artifacts", "milestone5", "pure-state.json");
        var cases = new List<(string Name, Action Run)>
        {
            ("two-hole layout resolves centered seed", () => Seed(engine.Solve(linear).Program, -20, 0)),
            ("2x2 layout resolves centered seed", () => Seed(engine.Solve(rect).Program, -30, -15)),
            ("2x3 layout resolves centered seed", () => Seed(engine.Solve(six).Program, -30, -20)),
            ("linear spacing edit propagates through relation handler", () => Seed(engine.Solve(RelationParameterEditor.Apply(linear, Edit(EditableParameter.PatternSpacing, 50))).Program, -25, 0)),
            ("rectangular X spacing edit preserves both centers", () => Seed(engine.Solve(RelationParameterEditor.Apply(rect, Edit(EditableParameter.PatternSpacingX, 70))).Program, -35, -15)),
            ("rectangular Y spacing edit preserves both centers", () => Seed(engine.Solve(RelationParameterEditor.Apply(rect, Edit(EditableParameter.PatternSpacingY, 34))).Program, -30, -17)),
            ("count edit uses general count minus one formula", () => Seed(engine.Solve(RelationParameterEditor.Apply(six, Edit(EditableParameter.PatternCountY, 4))).Program, -30, -30)),
            ("symmetry alone constrains only perpendicular coordinate", () =>
            {
                var seed = WithPlacement(rect, new(-35, 7));
                var plan = seed with { Relations = new[] { new DesignRelation(RelationKind.SymmetricAboutAxis, "holes", "plate.axis_x") } };
                Seed(engine.Solve(plan).Program, -35, -15);
            }),
            ("constraint order does not change solved placement", () => Seed(engine.Solve(rect with { Relations = rect.Relations.Reverse().ToArray() }).Program, -30, -15)),
            ("shared-seed relation conflicts reject deterministically", () =>
            {
                var other = rect.Operations[2] with { Id = "pattern_other", SemanticId = "other_holes",
                    Parameters = new Dictionary<string, OperationParameter>(rect.Operations[2].Parameters) { ["spacingXMm"] = new LengthParameter(20) } };
                var conflict = rect with { Operations = rect.Operations.Concat(new[] { other }).ToArray(),
                    Relations = rect.Relations.Concat(new[] { new DesignRelation(RelationKind.CenteredAbout, "other_holes", "plate.local_frame") }).ToArray() };
                Expect("RELATION_VIOLATED", () => engine.Solve(conflict));
            }),
            ("local Y linear direction is solved geometrically", () =>
            {
                var ops = linear.Operations.ToArray(); var p = ops[2];
                ops[2] = p with { Inputs = p.Inputs.Concat(new[] { new OperationInput("direction", new[] { new SemanticReference("plate.direction_y", SemanticType.LinearEdge) }) }).ToArray(),
                    Parameters = new Dictionary<string, OperationParameter>(p.Parameters) { ["spacingMm"] = new LengthParameter(30) } };
                Seed(engine.Solve(linear with { Operations = ops }).Program, 0, -15);
            }),
            ("wrong hosted_on input is rejected", () => Expect("RELATION_VIOLATED", () => engine.Solve(rect with
                { Relations = new[] { new DesignRelation(RelationKind.HostedOn, "hole_seed", "plate.bottom_face") } }))),
            ("wrong pattern_seed is rejected", () => Expect("RELATION_VIOLATED", () => engine.Solve(rect with
                { Relations = new[] { new DesignRelation(RelationKind.PatternSeed, "holes", "plate") } }))),
            ("wrong equal_spacing seed is rejected", () => Expect("RELATION_VIOLATED", () => engine.Solve(rect with
                { Relations = new[] { new DesignRelation(RelationKind.EqualSpacing, "holes", "plate") } }))),
            ("unknown symmetry output is rejected", () => Expect(FailureCodes.SchemaInvalid, () => engine.Solve(rect with
                { Relations = new[] { new DesignRelation(RelationKind.SymmetricAboutAxis, "holes", "plate.unknown_axis") } }))),
            ("spacing is never invented by equal_spacing", () =>
            {
                var ops = rect.Operations.ToArray(); var p = new Dictionary<string, OperationParameter>(ops[2].Parameters); p.Remove("spacingXMm"); ops[2] = ops[2] with { Parameters = p };
                Expect("RELATION_VIOLATED", () => engine.Solve(rect with { Operations = ops }));
            }),
            ("out-of-host relation solution rejects before COM", () =>
            {
                var bad = RelationParameterEditor.Apply(rect, Edit(EditableParameter.PatternSpacingX, 110));
                Expect(FailureCodes.PreconditionFailed, () => engine.Solve(bad));
                var result = new RelationBackend().Create(null!, bad);
                Assert(!result.Succeeded && !result.MutationStarted, "Invalid layout accessed COM or reported mutation.");
            }),
            ("unowned edit parameter rejects", () => Expect(FailureCodes.PreconditionFailed, () => RelationParameterEditor.Apply(rect, Edit(EditableParameter.HoleDiameter, 8)))),
            ("unsupported M5 relation rejects before COM", () =>
            {
                var bad = rect with { Relations = new[] { new DesignRelation(RelationKind.AlignedWith, "holes", "plate.local_frame") } };
                var result = new RelationBackend().Create(null!, bad);
                Assert(!result.Succeeded && result.FailureCode == FailureCodes.OperationUnsupported && !result.MutationStarted, "Unsupported relation was not rejected.");
            }),
            ("binder selects one role/type/geometry candidate", () =>
            {
                var r = binder.Bind(state, host, new(OwnerFeature: "plate", OriginMm: new(0, 0, 8), Direction: new(0, 0, 1)));
                Assert(r.Succeeded && r.Entity!.SemanticId == "plate.top_face", "Unique host binding failed.");
            }),
            ("binder zero candidates is unresolved", () => Assert(binder.Bind(state, host, new(SemanticId: "absent")).FailureCode == "BINDING_UNRESOLVED", "Zero candidates did not reject.")),
            ("binder multiple hosts never selects the first", () => Assert(binder.Bind(state, host, new()).FailureCode == "BINDING_AMBIGUOUS", "Ambiguous hosts silently bound.")),
            ("binder owner mismatch rejects", () => Assert(binder.Bind(state, host, new("plate.top_face", OwnerFeature: "hole_seed")).FailureCode == "BINDING_UNRESOLVED", "Wrong owner bound.")),
            ("binder type mismatch rejects", () => Assert(binder.Bind(state, host, new(Type: SemanticType.CylindricalFace)).FailureCode == "BINDING_UNRESOLVED", "Wrong type bound.")),
            ("binder radius/direction geometry disambiguates cylindrical candidates", () =>
            {
                var template = state.Entities.Single(e => e.SemanticId == "hole_seed");
                var a = template with { SemanticId = "hole_seed.wall_a", Type = SemanticType.CylindricalFace, Geometry = new(new(0, 0, 0), new(0, 0, 1), RadiusMm: 4) };
                var b = a with { SemanticId = "hole_seed.wall_b", Geometry = new(new(0, 0, 0), new(0, 0, 1), RadiusMm: 5) };
                var r = binder.Bind(state with { Entities = state.Entities.Concat(new[] { a, b }).ToArray() },
                    OperationRegistry.Default.Get(OperationKind.CreateCircularPattern).Inputs.Single(i => i.Name == "axis"), new(Type: SemanticType.CylindricalFace, RadiusMm: 4, Direction: new(0, 0, 1)));
                Assert(r.Succeeded && r.Entity!.SemanticId == a.SemanticId, "Geometry filter did not disambiguate.");
            }),
            ("binder stale entity rejects", () =>
            {
                var stale = state with { Entities = state.Entities.Select(e => e.SemanticId == "plate.top_face" ? e with { ReferenceHealth = ReferenceHealth.Stale } : e).ToArray() };
                Assert(binder.Bind(stale, host, new("plate.top_face")).FailureCode == "BINDING_UNRESOLVED", "Stale entity bound.");
            }),
            ("binder stale owner rejects", () =>
            {
                var stale = state with { Features = state.Features.Select(f => f.SemanticId == "plate" ? f with { ReferenceHealth = ReferenceHealth.Stale } : f).ToArray() };
                Assert(binder.Bind(stale, host, new("plate.top_face")).FailureCode == "BINDING_UNRESOLVED", "Stale owner bound.");
            }),
            ("binder dependency filter is enforced", () =>
            {
                Assert(binder.Bind(state, host, new("plate.top_face", DependentFeature: "holes")).Succeeded, "Host ancestor did not bind.");
                Assert(binder.Bind(state, host, new("plate.top_face", DependentFeature: "unrelated")).FailureCode == "BINDING_UNRESOLVED", "Absent dependency bound.");
            }),
            ("bounded ownership ranking requires unique evidence", () =>
            {
                var extra = state.Entities.Single(e => e.SemanticId == "plate.top_face") with { SemanticId = "hole_seed.other_face", OwnerFeatureSemanticId = "hole_seed" };
                var extended = state with { Entities = state.Entities.Concat(new[] { extra }).ToArray() };
                var r = binder.Bind(extended, host, new(PreferredOwner: "hole_seed"));
                Assert(r.Succeeded && r.Entity!.SemanticId == extra.SemanticId, "Justified ranking did not select the unique preferred owner.");
                Assert(binder.Bind(extended, host, new(PreferredOwner: "plate")).FailureCode == "BINDING_AMBIGUOUS", "A ranking tie silently bound.");
            }),
            ("candidate ranking is bounded", () =>
            {
                var template = state.Entities.Single(e => e.SemanticId == "plate.top_face");
                var extras = Enumerable.Range(0, 65).Select(i => template with { SemanticId = "plate.face_" + i });
                var r = binder.Bind(state with { Entities = state.Entities.Concat(extras).ToArray() }, host, new(PreferredOwner: "plate"));
                Assert(r.FailureCode == "BINDING_AMBIGUOUS" && r.Candidates.Count == 64, "Ranking exceeded its bound or silently selected.");
            }),
            ("constraint dependency cycle has finite closure", () =>
            {
                var graph = engine.Solve(rect).Dependencies;
                Assert(graph.AffectedBy(new[] { "holes" }).SequenceEqual(new[] { "hole_seed", "holes" }), "Constraint influence graph lost coupled seed or failed to terminate.");
                Assert(graph.NativeOrder(new[] { "holes", "hole_seed", "plate" }).SequenceEqual(new[] { "plate", "hole_seed", "holes" }), "Native dependency order is invalid.");
            }),
            ("native dependency cycle rejects", () => Expect("RELATION_VIOLATED", () => new DependencyGraph(new[]
                { new DependencyEdge("a", "b", DependencyKind.NativeInput), new DependencyEdge("b", "a", DependencyKind.NativeInput) }).NativeOrder(new[] { "a", "b" }))),
            ("typed relations/dependencies and geometry persist strictly", () =>
            {
                var store = new AtomicStateStore(scratch); store.Commit(state); var loaded = store.Load();
                Assert(StateRelationData.Relations(loaded).SequenceEqual(rect.Relations) && StateRelationData.Dependencies(loaded).Count == StateRelationData.Dependencies(state).Count,
                    "Typed relation/dependency round trip failed.");
                Assert(binder.Bind(loaded, host, new("plate.top_face")).Succeeded, "Loaded state cannot bind its host.");
            }),
            ("unknown relation JSON field rejects", () =>
            {
                using var bad = JsonDocument.Parse("{\"kind\":\"hosted_on\",\"subject\":\"hole_seed\",\"reference\":\"plate.top_face\",\"code\":\"x\"}");
                Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with { Relations = new[] { bad.RootElement.Clone() } }));
            }),
            ("wrong state relation reference type rejects", () => Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with
                { Relations = StateRelationData.Encode(new[] { new DesignRelation(RelationKind.CenteredAbout, "holes", "plate.top_face") }) }))),
            ("dependency with absent owner rejects", () => Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with
                { Dependencies = StateRelationData.Encode(new[] { new DependencyEdge("absent", "holes", DependencyKind.NativeInput) }) }))),
            ("local frame without geometry rejects", () => Expect("STATE_SCHEMA_INVALID", () => StateValidation.Validate(state with
                { Entities = state.Entities.Select(e => e.Type == SemanticType.LocalFrame ? e with { Geometry = null } : e).ToArray() }))),
            ("all required programs pass native backend preflight", () =>
            {
                foreach (var program in new[] { linear, rect, six, Fixture(root, "combined") })
                { var p = new RelationBackend().Preflight(program); Assert(p.IsValid, p.FailureCode + ": " + p.Message); }
            }),
            ("shared-host independent pattern edits do not couple seeds", () =>
            {
                var combined = Fixture(root, "combined");
                var first = engine.Solve(RelationParameterEditor.Apply(combined, Edit(EditableParameter.PatternSpacing, 50, "linear_holes"))).Program;
                Seed(first, -30, -15);
                var lp = first.Operations.Single(o => o.SemanticId == "linear_seed").Parameter<PlacementParameter>("placement").Value;
                Assert(lp == new Point2D(-25, 0), "Linear relation seed was not independently updated.");
                var final = engine.Solve(RelationParameterEditor.Apply(first, Edit(EditableParameter.PatternSpacingX, 70))).Program;
                Seed(final, -35, -15);
                Assert(final.Operations.Single(o => o.SemanticId == "linear_seed").Parameter<PlacementParameter>("placement").Value == lp, "Rectangular edit incorrectly changed the independent linear layout.");
            })
        };
        var passed = 0;
        try
        {
            foreach (var test in cases)
                try { test.Run(); passed++; Console.WriteLine("PASS " + test.Name); }
                catch (Exception error) { Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        finally { if (File.Exists(scratch)) File.Delete(scratch); }
        Console.WriteLine($"M5 pure tests: {passed}/{cases.Count}; native Parts created=0.");
        return passed == cases.Count ? 0 : 1;
    }
    private static CadState Sample(RelationPlan plan)
    {
        var reference = new NativePersistentReference(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        var entities = new List<SemanticEntityNode>();
        foreach (var operation in plan.Program.Operations)
            entities.Add(new(operation.SemanticId!, SemanticType.FeatureRef, operation.SemanticId!, reference, ReferenceHealth.Healthy));
        void Entity(string id, SemanticType type, SemanticGeometry geometry) => entities.Add(new(id, type, "plate", reference, ReferenceHealth.Healthy) { Geometry = geometry });
        var origin = new Point3(0, 0, 0); var x = new Vector3(1, 0, 0); var y = new Vector3(0, 1, 0); var z = new Vector3(0, 0, 1);
        Entity("plate.top_face", SemanticType.PlanarFace, new(new(0, 0, 8), z));
        Entity("plate.bottom_face", SemanticType.PlanarFace, new(origin, new(0, 0, -1)));
        Entity("plate.local_frame", SemanticType.LocalFrame, new(origin, Frame: new(origin, x, y, z)));
        Entity("plate.axis_x", SemanticType.ReferenceAxis, new(origin, x)); Entity("plate.axis_y", SemanticType.ReferenceAxis, new(origin, y));
        var state = new CadState
        {
            SchemaVersion = "0.2", Document = new(Guid.NewGuid(), Guid.NewGuid(), "default", ""), Revision = 0,
            Features = plan.Program.Operations.Select(o => new FeatureNode(o.SemanticId!, o.Kind, reference, ReferenceHealth.Healthy)).ToArray(),
            Entities = entities, Parameters = new[] { new ParameterNode("holes.pattern_spacing_x", ParameterKind.Length, 60) },
            Bindings = new[] { new ParameterBinding("holes.pattern_spacing_x", "holes", EditableParameter.PatternSpacingX) },
            Relations = StateRelationData.Encode(plan.Program.Relations), Dependencies = StateRelationData.Encode(plan.Dependencies.Edges)
        };
        StateValidation.Validate(state); return state;
    }
    private static CadProgram WithPlacement(CadProgram program, Point2D point) => program with { Operations = program.Operations.Select(o => o.SemanticId == "hole_seed" ? o with
        { Parameters = new Dictionary<string, OperationParameter>(o.Parameters) { ["placement"] = new PlacementParameter(point) } } : o).ToArray() };
    private static void Seed(CadProgram program, double x, double y)
    {
        var p = program.Operations.Single(o => o.SemanticId == "hole_seed").Parameter<PlacementParameter>("placement").Value;
        Assert(Math.Abs(p.XMm - x) < 1e-6 && Math.Abs(p.YMm - y) < 1e-6, "Wrong relation-derived seed position: " + p);
    }
    private static void Expect(string code, Action action)
    { try { action(); } catch (StateException error) when (error.Code == code) { return; } throw new Exception("Expected " + code); }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
}
