using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.State;

namespace CadHarness.SolidWorks;

// The native backend owns projection. An offline new-Part preview grants no native ownership.
public sealed class SolidWorksConstructionPlanningRuntime : IConstructionPlanningRuntime
{
    private sealed class SessionIdentity { internal string Id { get; } = Guid.NewGuid().ToString("N"); }
    private static readonly ConditionalWeakTable<SolidWorksExecutionContext, SessionIdentity> Sessions = new();
    private readonly string? committedJson;
    public string ContextIdentity { get; }
    public long Revision { get; }
    public IReadOnlyList<ConstructionKind> ExecutableKinds { get; }
    public IReadOnlyList<ConstructionOperation> Prefix => committedJson is null ? Array.Empty<ConstructionOperation>() :
        ContractJson.Read<ConstructionProgram>(committedJson, ContractValidation.Program).Operations;
    public string CapabilityJson { get; }
    private SolidWorksConstructionPlanningRuntime(string identity, long revision, ConstructionProgram? committed)
    {
        ContextIdentity = identity; Revision = revision;
        committedJson = committed is null || committed.Operations.Count == 0 ? null : ContractJson.Write(committed, ContractValidation.Program);
        var rotational = committed?.Operations.Any(o => o.Kind == ConstructionKind.CreateRevolvedBoss) == true;
        var terminal = committed?.Operations.Any(o => o.Kind == ConstructionKind.ApplyFillet) == true;
        var hasBody = committed?.Operations.Any(o => o.Profile is not null) == true;
        ExecutableKinds = Array.AsReadOnly(V03ContractCapabilities.Construction.Where(c => c.Executable && GenericFeatureCapabilities.IsOperationQualified(c.Kind))
            .Where(c => !rotational && !terminal && (!hasBody || c.Kind is not (ConstructionKind.CreateExtrude or ConstructionKind.CreateRevolvedBoss))).Select(c => c.Kind).ToArray());
        CapabilityJson = JsonSerializer.Serialize(new
        {
            backend = "solidworks-v0.3-qualified-construction", mode = "create_model", origin = "harness", revision,
            semanticCompiler=new {version=SemanticConstructionCompiler.Version,newModelOnly=true,qualifiedScalarPairs=new[]{"create_revolved_boss/revolve_radius (independent radial driver)","create_additive_boss/additive_boss_depth","create_extruded_cut/cut_depth"}},
            canonicalPlanes=new[]{new{id="world.xy",origin=new[]{0,0,0},x=new[]{1,0,0},y=new[]{0,1,0},normal=new[]{0,0,1}},new{id="world.xz",origin=new[]{0,0,0},x=new[]{1,0,0},y=new[]{0,0,-1},normal=new[]{0,1,0}},new{id="world.yz",origin=new[]{0,0,0},x=new[]{0,1,0},y=new[]{0,0,1},normal=new[]{1,0,0}}},
            operations = ExecutableKinds.Select(k => JsonNamingPolicy.SnakeCaseLower.ConvertName(k.ToString())),
            initialReferences = new[] { new { semanticId = "world.xy", type = "reference_plane" }, new { semanticId = "world.xz", type = "reference_plane" },
                new { semanticId = "world.yz", type = "reference_plane" }, new { semanticId = "world.axis_x", type = "reference_axis" },
                new { semanticId = "world.axis_y", type = "reference_axis" }, new { semanticId = "world.axis_z", type = "reference_axis" } },
            outputRules = new[] { "Every operation produces semanticId (feature_ref).",
                "create_sketch produces semanticId.profile (sketch_profile) and semanticId.axis_x (reference_axis). Profile references MUST include .profile.",
                "create_datum_plane produces semanticId.plane (reference_plane).",
                "Every solid feature produces semanticId.body (body_ref). Host references MUST include .body.",
                "All references bind earlier typed outputs or initialReferences, never invented datum identifiers." },
            featureRules = new[] { "create_extrude: blind, positive depth, direction forward/reverse, body_rule new_single_body, host_body/axis/angle_degrees null.",
                "create_revolved_boss: same sketch's semanticId.axis_x, full_revolution 360 degrees or finite_angle (0,360), body_rule new_single_body, depth/host_body null.",
                "create_additive_boss: blind positive depth, merge_with_host and exact earlier host body.",
                "create_extruded_cut: blind positive depth or through_all with depth null, remove_from_host and exact earlier host body.",
                "apply_fillet: terminal edgeTreatment version=1, hostFeature=prior positive extrusion semanticId feature_ref; hostBody=prior .body body_ref. edgeAxis=root-local x/y/z, FirstSide/SecondSide refer to the OTHER root axes in ascending x,y,z order. radius is positive; other sweep fields null. Simple full-length convex edge only, no crossing holes/attachments or oversized radius.",
                "A stepped orthogonal half-section uses radialProfile version=1. Declare positive radius drivers and EVERY point binding (driverId or null for y=0 baseline). One implicit closed counterclockwise polyline, constraints=[], fixed axial stations, radial offset relations target=source-offset; one full single-body revolve about its profile.axis_x. No thin walls or arbitrary point edits.",
                "Sketch geometry must be fully_constrained explicit coordinates. A rectangular polyline lists exactly four distinct counterclockwise corner points; closing is implicit, DO NOT repeat the first point at the end.",
                "A circle requires EXACTLY ONE driving radius OR diameter constraint on its entity ID, NEVER both. Both geometry binding metrics are still required, but they are not two native constraints.",
                "horizontal/vertical constraints take exactly one LINE ENTITY reference, never two point references and never a polyline. A fixed-coordinate rectangle may use constraints=[].",
                "distance takes one line entity or two points; radius/diameter takes one circle/arc entity. Slots use two distinct centerline endpoints with positive width/length; coordinates are already authoritative." },
            profileShapes = new[] { "single_circle", "local_rectangle", "local_x_slot", "orthogonal_radial_half_section" },
            planningFrames = "Positive principal normal with a disclosed signed in_plane_angle (-180..180 degrees).",
            rules = new { version = ConstructionRequirements.RuleVersion, ids = ConstructionRequirements.RuleIds },
            committedProgram = committedJson is null ? (JsonElement?)null : JsonSerializer.Deserialize<JsonElement>(committedJson),
            limitations = new[] { "Owned verified Generic session, including qualified cold-restored sessions; offline snapshot never grants native ownership. No external observed construction.",
                "One intended body. Exact preserved prefix, typed prior profiles/hosts/datums. Parallel laminar proof OR rectangular positive-box union with principal-axis circle cuts. Additions precede mixed-axis cuts; disks strictly inside transverse material; cuts cannot cross attachment regions. Unproved intersections refuse.",
                "Revolve about exact profile-local X only; no mixed rotational/prismatic history.",
                "Fixed local coordinates/centers with independent circle dimension; no generic associative constraint claim.",
                "Placement uses an explicit frame with inPlaneDirection=null. Offset sketches require an explicit tracked datum and sketch offset=0; no implicit offset-plane ownership.",
                "No detached additive material, opposite profile normals or untracked native history. A rotational or terminal-fillet history cannot receive new construction operations; qualified scalar edits remain separate.",
                "Planning does not authorize execution. Fresh native context/preflight and exact one-use approval required." }
        });
    }
    public static SolidWorksConstructionPlanningRuntime ForNewModel() => new("offline:" + Guid.NewGuid().ToString("N"), 0, null);
    public static SolidWorksConstructionPlanningRuntime ForSnapshotPreview(GenericConstructionState state)
    {
        new GenericConstructionStatePolicy().Validate(state);
        ContractValidation.Require(state.Features.Count == state.Program.Operations.Count && state.Program.Operations.All(o =>
            state.Features.Count(f => f.SemanticId == o.SemanticId && f.Kind == o.Kind) == 1), "Snapshot program/native inventory association differs.");
        return new("offline:" + Guid.NewGuid().ToString("N"), state.Revision, state.Program);
    }
    public static SolidWorksConstructionPlanningRuntime ForManagedSession(SolidWorksExecutionContext context)
    {
        var state = context.CaptureGenericConstructionState();
        return new(Sessions.GetValue(context, _ => new()).Id + ":" + state.Document.DocumentId + ":" + state.Document.ConfigurationId,
            state.Revision, state.Program);
    }
    public FeatureSolidExpectation Preflight(ConstructionProgram program)
    {
        ContractValidation.Require(program.Operations.Skip(Prefix.Count).All(o => ExecutableKinds.Contains(o.Kind)),
            "Construction not qualified in current document context.", V03FailureCodes.CapabilityUnavailable);
        return ConstructionPreflight.Evaluate(program, Prefix);
    }
}

public sealed partial class SolidWorksExecutionContext
{
    // No provider is invoked here. Execution checks the live identity and full native snapshot again.
    public MutationResult ExecuteConstructionPlan(ConstructionExecutionApproval approval)
    {
        CheckThread();
        ContractValidation.Require(GenericDurableCommit is null,"Durable approved execution must use GenericPartSession.ExecuteConstructionPlan to retain accepted provenance.",V03FailureCodes.CapabilityUnavailable);
        var current = SolidWorksConstructionPlanningRuntime.ForManagedSession(this);
        return ConstructGeneric(approval.Consume(current));
    }
}
