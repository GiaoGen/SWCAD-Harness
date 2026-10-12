using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using CadHarness.Ir.V03;

namespace CadHarness.Planning;

public sealed record RequirementRule(string SemanticKey, string RuleId, string? InputKey)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? Inputs { get; init; }
}
public sealed record ConstructionPlanRequest(RequirementRecord Input, IReadOnlyList<RequirementRule> Rules);
public enum RequirementMetric { Depth, Angle, ProfileWidth, ProfileHeight, ProfileCenterX, ProfileCenterY,
    CircleRadius, CircleDiameter, SlotWidth, SlotLength, OriginX, OriginY, OriginZ, Offset, InPlaneAngle,
    AxialStation, AxialSpan, RadialDriverRadius, RadialOffset, FilletRadius, SlotEndRadius }
public sealed record GeometryRequirementBinding(string SemanticKey, string Target, RequirementMetric Metric)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Element { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? EndElement { get; init; }
}
public sealed record ConstructionPlanResponse(string SchemaVersion, PlanOutcome Outcome, RequirementRecord Requirements,
    ConstructionProgram? Program, IReadOnlyList<string> Questions, string? Reason, IReadOnlyList<GeometryRequirementBinding> Bindings);

// These rules transform disclosed input, never infer functional engineering dimensions.
public static class ConstructionRequirements
{
    public const string RuleVersion = "1";
    public static IReadOnlyList<string> RuleIds { get; } = Array.AsReadOnly(new[] { "layout_zero", "layout_zero_angle", "half_dimension", "copy_dimension", "sum_dimensions", "difference_dimensions", "negative_dimension", "full_revolution", "cosmetic_convex_radius", "bracket_cosmetic_corner", "capsule_end_radius" });
    public static IReadOnlyList<string> InputKeys(RequirementRule rule) => rule.Inputs ?? (rule.InputKey is null ? Array.Empty<string>() : new[] { rule.InputKey });
    public static RequirementRecord Normalize(ConstructionPlanRequest request)
    {
        ContractValidation.Requirements(request.Input);
        ContractValidation.Require(request.Input.Requirements.All(r => r.Source is RequirementSource.Given or RequirementSource.Unresolved),
            "Input anchors must be given or explicitly unresolved; rules are applied separately.");
        ContractValidation.Require(request.Rules.Count <= ContractLimits.Requirements, "Rule count exceeds limit.");
        var values = request.Input.Requirements.ToList();
        foreach (var rule in request.Rules)
        {
            ContractValidation.Require(ContractValidation.Id(rule.SemanticKey) && values.All(v => v.SemanticKey != rule.SemanticKey), "Rule key must be new.");
            var keys = InputKeys(rule);
            ContractValidation.Require(!(rule.Inputs is not null && rule.InputKey is not null) && keys.Count <= 16 && keys.All(ContractValidation.Id), "Invalid finite rule input inventory.");
            var inputs = keys.Select(k => values.SingleOrDefault(v => v.SemanticKey == k)).ToArray();
            ContractValidation.Require(inputs.All(i => i is { Value: not null, Ambiguous: false, Unit: ScalarUnit.Millimeter }), "Rule input must resolve an earlier millimeter fact; cycles/forward references refuse.");
            var input = inputs.FirstOrDefault();
            var r = rule.RuleId switch
            {
                "layout_zero" when keys.Count == 0 => new Requirement(rule.SemanticKey, 0, ScalarUnit.Millimeter,
                    RequirementSource.Defaulted, rule.RuleId, RuleVersion, 1, false, false),
                "layout_zero_angle" when keys.Count == 0 => new Requirement(rule.SemanticKey, 0, ScalarUnit.Degree,
                    RequirementSource.Defaulted, rule.RuleId, RuleVersion, 1, false, false),
                "half_dimension" or "copy_dimension" or "negative_dimension" or "capsule_end_radius" when keys.Count == 1 =>
                    new Requirement(rule.SemanticKey, input!.Value * (rule.RuleId is "half_dimension" or "capsule_end_radius" ? .5 : rule.RuleId == "negative_dimension" ? -1 : 1), input.Unit,
                        RequirementSource.DerivedByRule, rule.RuleId, RuleVersion, input.Confidence, false, false),
                "sum_dimensions" or "difference_dimensions" when (rule.RuleId == "sum_dimensions" ? keys.Count >= 2 : keys.Count == 2) =>
                    new Requirement(rule.SemanticKey, rule.RuleId == "sum_dimensions" ? inputs.Sum(i => i!.Value!.Value) : inputs[0]!.Value - inputs[1]!.Value,
                        ScalarUnit.Millimeter, RequirementSource.DerivedByRule, rule.RuleId, RuleVersion, inputs.Min(i => i!.Confidence), false, false),
                "full_revolution" when keys.Count == 0 => new Requirement(rule.SemanticKey,360,ScalarUnit.Degree,RequirementSource.DerivedByRule,rule.RuleId,RuleVersion,1,false,false),
                "cosmetic_convex_radius" or "bracket_cosmetic_corner" when keys.Count == 0 => new Requirement(rule.SemanticKey,1,ScalarUnit.Millimeter,RequirementSource.Defaulted,rule.RuleId,RuleVersion,1,false,false),
                _ => throw new ContractException(V03FailureCodes.ContractInvalid, "Unknown rule or unresolved/incompatible rule input.")
            };
            values.Add(r);
        }
        var record = request.Input with { Requirements = values.ToArray() };
        ContractValidation.Requirements(record);
        return ContractJson.Read<RequirementRecord>(ContractJson.Write(record, ContractValidation.Requirements), ContractValidation.Requirements);
    }

    public static void ValidateResponse(ConstructionPlanResponse response)
    {
        ContractValidation.Response(new(response.SchemaVersion, response.Outcome, response.Requirements, response.Program, response.Questions, response.Reason));
        ContractValidation.Require(response.Bindings.Count <= ContractLimits.Requirements &&
            (response.Outcome == PlanOutcome.Planned || response.Bindings.Count == 0), "Nonplanned responses cannot bind executable geometry.");
        ContractValidation.Unique(response.Bindings.Select(b => b.Target + ":" + b.Metric + ":" + b.Element + ":" + b.EndElement), "geometry requirement binding");
        foreach (var b in response.Bindings)
            ContractValidation.Require(ContractValidation.Id(b.SemanticKey) && ContractValidation.Id(b.Target) && Enum.IsDefined(b.Metric) &&
                (b.Metric is RequirementMetric.AxialStation or RequirementMetric.AxialSpan or RequirementMetric.RadialDriverRadius or RequirementMetric.RadialOffset ? ContractValidation.Id(b.Element) : b.Element is null) &&
                (b.Metric == RequirementMetric.AxialSpan ? ContractValidation.Id(b.EndElement) : b.EndElement is null), "Invalid exact geometry binding selector.");
    }

    public static void VerifyGeometry(ConstructionPlanResponse response, IReadOnlyList<ConstructionOperation> prefix, ConstructionPlanRequest? request = null)
    {
        if(request is not null)ContractValidation.Require(new AcceptedRequirements(Normalize(request)).Json==new AcceptedRequirements(response.Requirements).Json,"Rule provenance does not reproduce accepted facts.");
        var requirements = response.Requirements.Requirements.ToDictionary(r => r.SemanticKey, StringComparer.Ordinal);
        var operations = response.Program!.Operations.ToDictionary(o => o.SemanticId, StringComparer.Ordinal);
        var measured = new Dictionary<(string Target, RequirementMetric Metric, string? Element, string? End), double>();
        void Add(string id, RequirementMetric metric, double value, string? element = null, string? end = null) => measured.Add((id, metric, element, end), value);
        foreach (var o in response.Program.Operations.Skip(prefix.Count))
        {
            if (o.Depth is { } depth) Add(o.SemanticId, RequirementMetric.Depth, depth.Millimeters);
            if (o.AngleDegrees is { } angle) Add(o.SemanticId, RequirementMetric.Angle, angle);
            if (o.EdgeTreatment is { } edge) Add(o.SemanticId,RequirementMetric.FilletRadius,edge.Radius.Millimeters);
            if (o.Placement is { } placement)
            {
                Add(o.SemanticId, RequirementMetric.OriginX, placement.Frame.OriginMm.X);
                Add(o.SemanticId, RequirementMetric.OriginY, placement.Frame.OriginMm.Y);
                Add(o.SemanticId, RequirementMetric.OriginZ, placement.Frame.OriginMm.Z);
                Add(o.SemanticId, RequirementMetric.Offset, placement.Offset.Millimeters);
                var z = placement.Frame.ZAxis;
                var baseline = Math.Abs(z.Z - 1) < 1e-8 || Math.Abs(z.Y - 1) < 1e-8 ? new Vector3(1, 0, 0) : new Vector3(0, 1, 0);
                ContractValidation.Require(Math.Abs(z.X - 1) < 1e-8 || Math.Abs(z.Y - 1) < 1e-8 || Math.Abs(z.Z - 1) < 1e-8,
                    "Planner frames require a positive principal-plane normal.", V03FailureCodes.CapabilityUnavailable);
                var lateral = new Vector3(z.Y * baseline.Z - z.Z * baseline.Y, z.Z * baseline.X - z.X * baseline.Z, z.X * baseline.Y - z.Y * baseline.X);
                Add(o.SemanticId, RequirementMetric.InPlaneAngle, Math.Atan2(SketchFrames.Dot(placement.Frame.XAxis, lateral), SketchFrames.Dot(placement.Frame.XAxis, baseline)) * 180 / Math.PI);
            }
            if (o.Sketch is not { } sketch) continue;
            // M17's planning subset is explicit; the M15 native vocabulary remains unchanged.
            var prepared = PlanarSketchPreflight.Prepare(sketch);
            ContractValidation.Require(sketch.Entities.Count==1 && sketch.Entities[0].Points.ToHashSet().SetEquals(sketch.Points.Select(p=>p.Id)),"Planner profile has unconsumed point coordinates.");
            if(sketch.RadialProfile is { } radial)
            {
                RadialProfilePolicy.Validate(sketch);
                foreach(var point in sketch.Points) Add(o.SemanticId,RequirementMetric.AxialStation,point.X.Millimeters,point.Id);
                foreach(var driver in radial.Drivers) Add(o.SemanticId,RequirementMetric.RadialDriverRadius,driver.Radius.Millimeters,driver.Id);
                foreach(var relation in radial.Relations) Add(o.SemanticId,RequirementMetric.RadialOffset,relation.Offset.Millimeters,relation.Id);
                foreach(var binding in response.Bindings.Where(b=>b.Target==o.SemanticId && b.Metric==RequirementMetric.AxialSpan))
                {
                    var a=sketch.Points.SingleOrDefault(p=>p.Id==binding.Element);var b=sketch.Points.SingleOrDefault(p=>p.Id==binding.EndElement);
                    ContractValidation.Require(a is not null && b is not null && b.X.Millimeters>a.X.Millimeters,"Span requires ordered exact axial points.");
                    Add(o.SemanticId,RequirementMetric.AxialSpan,b!.X.Millimeters-a!.X.Millimeters,binding.Element,binding.EndElement);
                }
                continue;
            }
            ContractValidation.Require(sketch.Entities.Count == 1 && sketch.Loops.Count == 1 && !sketch.Loops[0].Inner,
                "Planning requires a single circle, slot or rectangular outline; other explicit sketches remain native-only.", V03FailureCodes.CapabilityUnavailable);
            var e = sketch.Entities[0];
            var bounds = FeatureConstructionPreflight.Bounds(prepared.Primitives);
            Add(o.SemanticId, RequirementMetric.ProfileCenterX, (bounds.MinX + bounds.MaxX) / 2);
            Add(o.SemanticId, RequirementMetric.ProfileCenterY, (bounds.MinY + bounds.MaxY) / 2);
            if (e.Kind == SketchEntityKind.Circle)
            {
                Add(o.SemanticId, RequirementMetric.CircleRadius, e.Radius!.Millimeters);
                Add(o.SemanticId, RequirementMetric.CircleDiameter, 2 * e.Radius.Millimeters);
            }
            else if (e.Kind == SketchEntityKind.Slot)
            {
                var points = sketch.Points.ToDictionary(p => p.Id);
                ContractValidation.Require(Math.Abs(points[e.Points[0]].Y.Millimeters - points[e.Points[1]].Y.Millimeters) < PlanarSketchPreflight.ToleranceMm,
                    "Planner slot orientation must be explicit local X.", V03FailureCodes.CapabilityUnavailable);
                Add(o.SemanticId, RequirementMetric.SlotWidth, e.Width!.Millimeters);
                Add(o.SemanticId, RequirementMetric.SlotLength, e.Length!.Millimeters);
                Add(o.SemanticId, RequirementMetric.SlotEndRadius, e.Width.Millimeters / 2);
            }
            else
            {
                var vertices = sketch.Points;
                ContractValidation.Require(e.Kind == SketchEntityKind.Polyline && vertices.Count == 4 &&
                    vertices.All(p => (Math.Abs(p.X.Millimeters - bounds.MinX) < 1e-7 || Math.Abs(p.X.Millimeters - bounds.MaxX) < 1e-7) &&
                        (Math.Abs(p.Y.Millimeters - bounds.MinY) < 1e-7 || Math.Abs(p.Y.Millimeters - bounds.MaxY) < 1e-7)),
                    "Planner outline must be an axis-aligned local rectangle.", V03FailureCodes.CapabilityUnavailable);
                Add(o.SemanticId, RequirementMetric.ProfileWidth, bounds.MaxX - bounds.MinX);
                Add(o.SemanticId, RequirementMetric.ProfileHeight, bounds.MaxY - bounds.MinY);
            }
        }
        var covered = new HashSet<(string, RequirementMetric, string?, string?)>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        var ruleGraph=request?.Rules.ToDictionary(r=>r.SemanticKey);
        void CheckSource(Requirement r,GeometryRequirementBinding b,HashSet<string> visited)
        {
            if(!visited.Add(r.SemanticKey))return;
            var cosmeticDefault=b.Metric==RequirementMetric.FilletRadius && r.RuleId is "cosmetic_convex_radius" or "bracket_cosmetic_corner" && !r.Critical && requirements[b.SemanticKey].Value==1;
            var layoutDefault=r.RuleId is "layout_zero" or "layout_zero_angle" && b.Metric is RequirementMetric.ProfileCenterX or RequirementMetric.ProfileCenterY or RequirementMetric.OriginX or RequirementMetric.OriginY or RequirementMetric.OriginZ or RequirementMetric.Offset or RequirementMetric.InPlaneAngle or RequirementMetric.AxialStation;
            ContractValidation.Require(r.Source != RequirementSource.Defaulted || cosmeticDefault || layoutDefault,
                "Disclosed defaults, including derived ancestors, cannot fund functional dimensions.");
            if(r.RuleId=="full_revolution") ContractValidation.Require(b.Metric==RequirementMetric.Angle && operations[b.Target].EndCondition==EndCondition.FullRevolution,"Structural full-turn rule cannot fund another dimension.");
            if(ruleGraph?.TryGetValue(r.SemanticKey,out var rule)==true)foreach(var key in InputKeys(rule)) CheckSource(requirements[key],b,visited);
        }
        foreach (var b in response.Bindings)
        {
            ContractValidation.Require(requirements.TryGetValue(b.SemanticKey, out var r) && r.Value is not null && !r.Ambiguous &&
                operations.ContainsKey(b.Target) && measured.TryGetValue((b.Target, b.Metric,b.Element,b.EndElement), out _), "Requirement binding must refer to resolved, new geometry.");
            var expectedUnit = b.Metric is RequirementMetric.Angle or RequirementMetric.InPlaneAngle ? ScalarUnit.Degree : ScalarUnit.Millimeter;
            ContractValidation.Require(r!.Unit == expectedUnit && Math.Abs(measured[(b.Target, b.Metric,b.Element,b.EndElement)] - r.Value!.Value) <= PlanarSketchPreflight.ToleranceMm,
                "Planned geometry contradicts an accepted requirement.");
            CheckSource(r,b,new HashSet<string>(StringComparer.Ordinal));
            covered.Add((b.Target, b.Metric,b.Element,b.EndElement)); used.Add(b.SemanticKey);
        }
        ContractValidation.Require(covered.SetEquals(measured.Keys), "Every independent geometry dimension/placement requires disclosed input or a finite rule.");
        if(request is not null)
        {
            var rules=request.Rules.ToDictionary(r=>r.SemanticKey);
            void Trace(string key) { if(!rules.TryGetValue(key,out var rule))return; foreach(var input in InputKeys(rule)) if(used.Add(input)) Trace(input); }
            foreach(var key in used.ToArray()) Trace(key);
        }
        ContractValidation.Require(requirements.Values.Where(r => r.Critical || r.Source == RequirementSource.Given && r.Value is not null).All(r => used.Contains(r.SemanticKey)),
            "Every critical or explicit given requirement must bind verified geometry; unsupported claims cannot be silently ignored.");
    }
}
