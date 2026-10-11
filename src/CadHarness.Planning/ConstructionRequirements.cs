using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir.V03;

namespace CadHarness.Planning;

public sealed record RequirementRule(string SemanticKey, string RuleId, string? InputKey);
public sealed record ConstructionPlanRequest(RequirementRecord Input, IReadOnlyList<RequirementRule> Rules);
public enum RequirementMetric { Depth, Angle, ProfileWidth, ProfileHeight, ProfileCenterX, ProfileCenterY,
    CircleRadius, CircleDiameter, SlotWidth, SlotLength, OriginX, OriginY, OriginZ, Offset, InPlaneAngle }
public sealed record GeometryRequirementBinding(string SemanticKey, string Target, RequirementMetric Metric);
public sealed record ConstructionPlanResponse(string SchemaVersion, PlanOutcome Outcome, RequirementRecord Requirements,
    ConstructionProgram? Program, IReadOnlyList<string> Questions, string? Reason, IReadOnlyList<GeometryRequirementBinding> Bindings);

// These rules transform disclosed input, never infer functional engineering dimensions.
public static class ConstructionRequirements
{
    public const string RuleVersion = "1";
    public static IReadOnlyList<string> RuleIds { get; } = Array.AsReadOnly(new[] { "layout_zero", "layout_zero_angle", "half_dimension", "copy_dimension" });
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
            var input = values.SingleOrDefault(v => v.SemanticKey == rule.InputKey);
            var r = rule.RuleId switch
            {
                "layout_zero" when rule.InputKey is null => new Requirement(rule.SemanticKey, 0, ScalarUnit.Millimeter,
                    RequirementSource.Defaulted, rule.RuleId, RuleVersion, 1, false, false),
                "layout_zero_angle" when rule.InputKey is null => new Requirement(rule.SemanticKey, 0, ScalarUnit.Degree,
                    RequirementSource.Defaulted, rule.RuleId, RuleVersion, 1, false, false),
                "half_dimension" or "copy_dimension" when input is { Value: not null, Ambiguous: false, Unit: ScalarUnit.Millimeter } =>
                    new Requirement(rule.SemanticKey, input.Value / (rule.RuleId == "half_dimension" ? 2 : 1), input.Unit,
                        RequirementSource.DerivedByRule, rule.RuleId, RuleVersion, input.Confidence, false, false),
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
        ContractValidation.Unique(response.Bindings.Select(b => b.Target + ":" + b.Metric), "geometry requirement binding");
        foreach (var b in response.Bindings)
            ContractValidation.Require(ContractValidation.Id(b.SemanticKey) && ContractValidation.Id(b.Target) && Enum.IsDefined(b.Metric), "Invalid geometry binding.");
    }

    public static void VerifyGeometry(ConstructionPlanResponse response, IReadOnlyList<ConstructionOperation> prefix)
    {
        var requirements = response.Requirements.Requirements.ToDictionary(r => r.SemanticKey, StringComparer.Ordinal);
        var operations = response.Program!.Operations.ToDictionary(o => o.SemanticId, StringComparer.Ordinal);
        var measured = new Dictionary<(string Target, RequirementMetric Metric), double>();
        void Add(string id, RequirementMetric metric, double value) => measured.Add((id, metric), value);
        foreach (var o in response.Program.Operations.Skip(prefix.Count))
        {
            if (o.Depth is { } depth) Add(o.SemanticId, RequirementMetric.Depth, depth.Millimeters);
            if (o.AngleDegrees is { } angle) Add(o.SemanticId, RequirementMetric.Angle, angle);
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
        var covered = new HashSet<(string, RequirementMetric)>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var b in response.Bindings)
        {
            ContractValidation.Require(requirements.TryGetValue(b.SemanticKey, out var r) && r.Value is not null && !r.Ambiguous &&
                operations.ContainsKey(b.Target) && measured.TryGetValue((b.Target, b.Metric), out _), "Requirement binding must refer to resolved, new geometry.");
            var expectedUnit = b.Metric is RequirementMetric.Angle or RequirementMetric.InPlaneAngle ? ScalarUnit.Degree : ScalarUnit.Millimeter;
            ContractValidation.Require(r!.Unit == expectedUnit && Math.Abs(measured[(b.Target, b.Metric)] - r.Value!.Value) <= PlanarSketchPreflight.ToleranceMm,
                "Planned geometry contradicts an accepted requirement.");
            ContractValidation.Require(r.Source != RequirementSource.Defaulted || b.Metric is RequirementMetric.ProfileCenterX or RequirementMetric.ProfileCenterY or
                RequirementMetric.OriginX or RequirementMetric.OriginY or RequirementMetric.OriginZ or RequirementMetric.Offset or RequirementMetric.InPlaneAngle,
                "Layout defaults cannot invent size, fit or performance dimensions.");
            covered.Add((b.Target, b.Metric)); used.Add(b.SemanticKey);
        }
        ContractValidation.Require(covered.SetEquals(measured.Keys), "Every independent geometry dimension/placement requires disclosed input or a finite rule.");
        // A diameter may feed half_dimension; strength/fit/material facts cannot be silently ignored.
        ContractValidation.Require(requirements.Values.Where(r => r.Critical || r.Source == RequirementSource.Given && r.Value is not null).All(r => used.Contains(r.SemanticKey)),
            "Every critical or explicit given requirement must bind verified geometry; unsupported claims cannot be silently ignored.");
    }
}
