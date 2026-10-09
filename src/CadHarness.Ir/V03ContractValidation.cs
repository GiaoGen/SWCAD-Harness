using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CadHarness.Ir.V03;

public static class ContractValidation
{
    public static void Require(bool condition, string message, string code = V03FailureCodes.ContractInvalid)
    { if (!condition) throw new ContractException(code, message); }
    public static bool Id(string? value) => value is not null && value.Length <= 128 && Regex.IsMatch(value,
        @"^[a-z][a-z0-9]*(?:_[a-z0-9]+)*(?:\.[a-z][a-z0-9]*(?:_[a-z0-9]+)*)*$", RegexOptions.CultureInvariant);
    public static bool Hash(string? value) => value is not null && Regex.IsMatch(value, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant);
    public static bool Text(string? value, int maximum = 2048) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;
    public static void Version(string version) => Require(version == ContractLimits.Version, "Only the explicit v0.3 contract is accepted.");
    public static bool Signed(double value) => double.IsFinite(value) && Math.Abs(value) <= ContractLimits.SpatialBoundMm;
    public static bool Length(double value) => double.IsFinite(value) && value > 0 && value <= ContractLimits.LengthBoundMm;
    public static void Fingerprint(FileFingerprint file) => Require(file is not null && Path.IsPathFullyQualified(file.Path) &&
        string.Equals(Path.GetExtension(file.Path), ".sldprt", StringComparison.OrdinalIgnoreCase) && Hash(file.Sha256) && file.SizeBytes > 0,
        "Fingerprint requires an absolute native Part path, SHA256 and positive byte size.");
    public static void Selection(PartSelection selection)
    {
        Require(selection.DocumentId != Guid.Empty && selection.ConfigurationId != Guid.Empty &&
            Text(selection.ConfigurationName, 256) && selection.ExpectedRevision >= 0, "Selection identity/revision is invalid.");
        Fingerprint(selection.Source); Fingerprint(selection.WorkingCopy);
        Require(!string.Equals(Path.GetFullPath(selection.Source.Path), Path.GetFullPath(selection.WorkingCopy.Path), StringComparison.OrdinalIgnoreCase),
            "Mutations require a distinct working copy.");
    }
    public static ScalarUnit Unit(ParameterKey key) => key == ParameterKey.PatternCount ? ScalarUnit.Count : ScalarUnit.Millimeter;
    public static void Scalar(ParameterKey key, ScalarUnit unit, double value)
    {
        Require(Enum.IsDefined(key) && unit == Unit(key), "Parameter key/unit mismatch.");
        Require(key is ParameterKey.SlotCenterX or ParameterKey.SlotCenterY ? Signed(value) :
            key == ParameterKey.PatternCount ? double.IsFinite(value) && value == Math.Truncate(value) && value is >= 2 and <= 256 : Length(value),
            "Parameter scalar exceeds its finite signed/positive/count contract.");
    }
    public static void Requirements(RequirementRecord record)
    {
        Version(record.SchemaVersion);
        Require(Id(record.RecordId) && record.Revision >= 0 && Text(record.Intent, 4096) &&
            record.Requirements.Count is >= 1 and <= ContractLimits.Requirements, "Invalid requirement record identity or bound.");
        Unique(record.Requirements.Select(r => r.SemanticKey), "requirement key");
        foreach (var r in record.Requirements)
        {
            Require(Id(r.SemanticKey) && Enum.IsDefined(r.Source) && Enum.IsDefined(r.Unit) &&
                double.IsFinite(r.Confidence) && r.Confidence is >= 0 and <= 1, "Invalid requirement metadata.");
            Require((r.Source == RequirementSource.Unresolved) == (r.Value is null), "Only unresolved requirements have null value.");
            if (r.Value is { } value) Require(double.IsFinite(value) && (r.Unit == ScalarUnit.Count ? value >= 1 && value <= 256 && value == Math.Truncate(value) :
                r.Unit == ScalarUnit.Degree ? Math.Abs(value) <= 360 : Signed(value)), "Invalid requirement scalar.");
            var ruled = r.Source is RequirementSource.DerivedByRule or RequirementSource.Defaulted;
            Require(ruled ? Id(r.RuleId) && Text(r.RuleVersion, 64) && !r.Critical : r.RuleId is null && r.RuleVersion is null,
                "Rules/defaults need versioned provenance and cannot invent critical requirements.");
        }
    }
    public static void Response(PlanningResponse response)
    {
        Version(response.SchemaVersion); Requirements(response.Requirements);
        Require(Enum.IsDefined(response.Outcome) && response.Questions.Count <= ContractLimits.Questions &&
            response.Questions.All(q => Text(q, 512)), "Invalid response outcome/questions.");
        if (response.Outcome == PlanOutcome.Planned)
        {
            Require(response.Program is not null && response.Questions.Count == 0 && response.Reason is null &&
                !response.Requirements.Requirements.Any(r => r.Critical && (r.Source == RequirementSource.Unresolved || r.Ambiguous)),
                "Planned responses require resolved critical facts and no clarification fields.");
            Program(response.Program!);
            Require(response.Program!.RequirementRecordId == response.Requirements.RecordId, "Program requirement identity differs.");
        }
        else Require(response.Program is null && (response.Outcome == PlanOutcome.NeedsClarification ?
            response.Questions.Count > 0 && response.Reason is null : response.Questions.Count == 0 && Text(response.Reason)),
            "Nonplanned responses cannot contain an executable program.");
    }
    public static void Edits(EditSetRequest request)
    {
        Version(request.SchemaVersion); Selection(request.Selection);
        Require(request.Mode == RequestMode.EditSet && Enum.IsDefined(request.Origin), "EditSet requires an explicit mode and origin.", V03FailureCodes.ModeMismatch);
        Require(request.Edits.Count is >= ContractLimits.BatchMinimum and <= ContractLimits.BatchMaximum, "EditSet requires 2..16 edits.");
        Unique(request.Edits.Select(e => e.Target + ":" + e.Parameter), "target/parameter pair");
        foreach (var edit in request.Edits)
        {
            Require(Id(edit.Target), "Invalid target semantic identity.");
            Scalar(edit.Parameter, edit.Unit, edit.ExpectedOldValue); Scalar(edit.Parameter, edit.Unit, edit.Value);
        }
    }
    public static void Edit(ScalarEditRequest request)
    {
        Version(request.SchemaVersion); Selection(request.Selection);
        Require(request.Mode == RequestMode.ManagedScalarEdit && request.Origin == ModelOrigin.Harness ||
            request.Mode == RequestMode.ExternalScalarEdit && request.Origin == ModelOrigin.External,
            "Single scalar edit requires matching managed/external mode and origin.", V03FailureCodes.ModeMismatch);
        Require(Id(request.Edit.Target), "Invalid target semantic identity.");
        Scalar(request.Edit.Parameter, request.Edit.Unit, request.Edit.ExpectedOldValue); Scalar(request.Edit.Parameter, request.Edit.Unit, request.Edit.Value);
    }
    public static void Frame(LocalFrame frame)
    {
        static double Dot(Vector3 a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static bool Finite(Vector3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);
        Require(Finite(frame.OriginMm) && Signed(frame.OriginMm.X) && Signed(frame.OriginMm.Y) && Signed(frame.OriginMm.Z), "Invalid millimeter origin.");
        var x = frame.XAxis; var y = frame.YAxis; var z = frame.ZAxis;
        var cross = new Vector3(x.Y * y.Z - x.Z * y.Y, x.Z * y.X - x.X * y.Z, x.X * y.Y - x.Y * y.X);
        Require(Finite(x) && Finite(y) && Finite(z) && Math.Abs(Dot(x, x) - 1) < 1e-8 && Math.Abs(Dot(y, y) - 1) < 1e-8 &&
            Math.Abs(Dot(z, z) - 1) < 1e-8 && Math.Abs(Dot(x, y)) < 1e-8 && Math.Abs(Dot(cross, z) - 1) < 1e-8,
            "Frame must be orthonormal and right-handed with local +Z normal.");
    }
    public static void Placement(SpatialPlacement placement)
    {
        Reference(placement.Plane, SemanticType.PlanarFace, SemanticType.ReferencePlane);
        if (placement.InPlaneDirection is { } reference) Reference(reference, SemanticType.ReferenceAxis, SemanticType.LinearEdge);
        Require(placement.Plane.Type != SemanticType.PlanarFace || placement.InPlaneDirection is not null,
            "Face frame requires an explicit in-plane direction.");
        Frame(placement.Frame); Require(Signed(placement.Offset.Millimeters), "Invalid signed offset.");
    }
    public static void Sketch(ClosedSketch sketch)
    {
        Require(sketch.Entities.Count is >= 1 and <= ContractLimits.SketchEntities && sketch.Points.Count <= 128 &&
            sketch.Loops.Count is >= 1 and <= ContractLimits.SketchLoops && sketch.Constraints.Count <= ContractLimits.SketchConstraints, "Sketch entity/loop/constraint limit exceeded.");
        Require(sketch.Status == ConstraintStatus.FullyConstrained, "Driving sketch must be fully constrained.",
            sketch.Status == ConstraintStatus.OverConstrained ? V03FailureCodes.OverConstrainedSketch : V03FailureCodes.UnderConstrainedSketch);
        var allIds = sketch.Points.Select(p => p.Id).Concat(sketch.Entities.Select(e => e.Id)).Concat(sketch.Loops.Select(l => l.Id)).Concat(sketch.Constraints.Select(c => c.Id)).ToArray();
        Require(allIds.All(Id), "Invalid sketch local identity."); Unique(allIds, "sketch identity");
        var points = sketch.Points.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in sketch.Points) Require(Signed(p.X.Millimeters) && Signed(p.Y.Millimeters), "Sketch coordinates must be finite signed millimeters.");
        foreach (var e in sketch.Entities)
        {
            Require(Enum.IsDefined(e.Kind) && e.Points.All(points.Contains) && e.Points.Distinct().Count() == e.Points.Count, "Invalid sketch point references.");
            Require(e.Kind switch
            {
                SketchEntityKind.Line => e.Points.Count == 2 && e.Radius is null && e.SweepDegrees is null && e.Width is null && e.Length is null,
                SketchEntityKind.Arc => e.Points.Count == 3 && e.Radius is not null && e.SweepDegrees is { } sweep && double.IsFinite(sweep) && Math.Abs(sweep) is > 0 and < 360 && e.Width is null && e.Length is null,
                SketchEntityKind.Circle => e.Points.Count == 1 && e.Radius is not null && e.SweepDegrees is null && e.Width is null && e.Length is null,
                SketchEntityKind.Polyline => e.Points.Count is >= 3 and <= 64 && e.Radius is null && e.SweepDegrees is null && e.Width is null && e.Length is null,
                SketchEntityKind.Slot => e.Points.Count == 2 && e.Width is not null && e.Length is not null && e.Length.Millimeters > e.Width.Millimeters && e.Radius is null && e.SweepDegrees is null,
                _ => false
            }, "Sketch entity fields do not match the selected primitive.", V03FailureCodes.InvalidSketch);
            foreach (var length in new[] { e.Radius, e.Width, e.Length }) if (length is not null) Require(Length(length.Millimeters), "Sketch lengths must be positive.");
        }
        var entities = sketch.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        Unique(sketch.Loops.SelectMany(l => l.Entities), "loop membership");
        Require(sketch.Loops.Any(l => !l.Inner) && sketch.Loops.All(l => l.Entities.Count > 0 && l.Entities.All(entities.Contains)) &&
            sketch.Loops.Sum(l => l.Entities.Count) == sketch.Entities.Count, "Each entity must belong to one explicit loop.");
        foreach (var c in sketch.Constraints)
            Require(Enum.IsDefined(c.Kind) && c.References.Count is >= 1 and <= 4 && c.References.All(r => points.Contains(r) || entities.Contains(r)) &&
                (c.Kind is ConstraintKind.Distance or ConstraintKind.Radius or ConstraintKind.Diameter ? c.Dimension is not null && Length(c.Dimension.Millimeters) : c.Dimension is null), "Invalid sketch constraint fields.");
    }
    public static void Program(ConstructionProgram program)
    {
        Version(program.ProgramVersion);
        Require(program.Origin == ModelOrigin.Harness && program.Mode == RequestMode.CreateModel, "Construction cannot reinterpret external observations.", V03FailureCodes.ModeMismatch);
        Require(Id(program.RequirementRecordId) && program.Operations.Count is >= 1 and <= ContractLimits.Operations, "Invalid program requirement identity or operation bound.");
        Unique(program.Operations.Select(o => o.Id), "operation ID"); Unique(program.Operations.Select(o => o.SemanticId), "feature ID");
        var available = new Dictionary<string, SemanticType>(StringComparer.Ordinal)
        { ["world.xy"] = SemanticType.ReferencePlane, ["world.xz"] = SemanticType.ReferencePlane, ["world.yz"] = SemanticType.ReferencePlane,
            ["world.axis_x"] = SemanticType.ReferenceAxis, ["world.axis_y"] = SemanticType.ReferenceAxis, ["world.axis_z"] = SemanticType.ReferenceAxis };
        var bodyCreated = false;
        void Output(string id, SemanticType type)
        { Require(!available.ContainsKey(id), "Feature/output identity collides with another semantic output."); available.Add(id, type); }
        foreach (var o in program.Operations)
        {
            Require(Id(o.Id) && Id(o.SemanticId) && !o.SemanticId.StartsWith("world.", StringComparison.Ordinal) && Enum.IsDefined(o.Kind), "Invalid operation identity/kind.");
            foreach (var r in new[] { o.Profile, o.Axis, o.HostBody, o.Placement?.Plane, o.Placement?.InPlaneDirection }.OfType<SemanticReference>())
                Require(available.TryGetValue(r.SemanticId, out var type) && type == r.Type, "Reference must bind an earlier typed output or principal datum.");
            if (o.Placement is not null) Placement(o.Placement);
            if (o.Depth is not null) Require(Length(o.Depth.Millimeters), "Feature depth must be positive.");
            Require(o.Direction is null || Enum.IsDefined(o.Direction.Value), "Invalid direction.");
            if (o.Kind == ConstructionKind.CreateSketch)
            {
                Require(o.Placement is not null && o.Sketch is not null && o.Profile is null && o.Axis is null && o.HostBody is null &&
                    o.EndCondition is null && o.Direction is null && o.Depth is null && o.AngleDegrees is null && o.BodyRule is null, "Invalid sketch operation fields.");
                Sketch(o.Sketch!); Output(o.SemanticId + ".profile", SemanticType.SketchProfile);
                Output(o.SemanticId + ".axis_x", SemanticType.ReferenceAxis);
            }
            else if (o.Kind == ConstructionKind.CreateDatumPlane)
            {
                Require(o.Placement is not null && o.Sketch is null && o.Profile is null && o.Axis is null && o.HostBody is null &&
                    o.EndCondition is null && o.Direction is null && o.Depth is null && o.AngleDegrees is null && o.BodyRule is null, "Invalid datum operation fields.");
                Output(o.SemanticId + ".plane", SemanticType.ReferencePlane);
            }
            else
            {
                Require(o.Sketch is null && o.Placement is null && o.Profile is not null && o.Profile.Type == SemanticType.SketchProfile && o.Direction is not null, "Feature consumes a bound closed sketch profile.");
                if (o.Kind == ConstructionKind.CreateRevolvedBoss)
                {
                    Require(!bodyCreated && o.Axis is not null && o.Axis.Type == SemanticType.ReferenceAxis && o.HostBody is null && o.Depth is null && o.BodyRule == BodyRule.NewSingleBody &&
                        (o.EndCondition == EndCondition.FullRevolution ? o.AngleDegrees == 360 : o.EndCondition == EndCondition.FiniteAngle && o.AngleDegrees is { } a && double.IsFinite(a) && a is > 0 and < 360), "Invalid revolve axis/angle/single-body contract.");
                    Require(o.Axis!.SemanticId == o.Profile!.SemanticId[..^8] + ".axis_x", "Revolve axis must lie in its profile sketch plane.");
                }
                else if (o.Kind == ConstructionKind.CreateExtrude)
                {
                    Require(!bodyCreated && o.HostBody is null && o.Axis is null && o.AngleDegrees is null && o.BodyRule == BodyRule.NewSingleBody &&
                        o.EndCondition == EndCondition.Blind && o.Depth is not null, "Initial sketch extrusion requires one new solid body.");
                }
                else
                {
                    Require(bodyCreated && o.HostBody?.Type == SemanticType.BodyRef && o.Axis is null && o.AngleDegrees is null &&
                        (o.Kind == ConstructionKind.CreateAdditiveBoss ? o.BodyRule == BodyRule.MergeWithHost && o.EndCondition == EndCondition.Blind && o.Depth is not null :
                        o.BodyRule == BodyRule.RemoveFromHost && (o.EndCondition == EndCondition.Blind ? o.Depth is not null : o.EndCondition == EndCondition.ThroughAll && o.Depth is null)), "Invalid additive/cut host or end condition.");
                }
                bodyCreated = true; Output(o.SemanticId + ".body", SemanticType.BodyRef);
            }
            Output(o.SemanticId, SemanticType.FeatureRef);
        }
    }
    public static void Reference(SemanticReference reference, params SemanticType[] types) =>
        Require(Id(reference.SemanticId) && types.Contains(reference.Type), "Invalid semantic reference type/ID.");
    public static void Unique(IEnumerable<string> values, string label)
    { var items = values.ToArray(); Require(items.Distinct(StringComparer.Ordinal).Count() == items.Length, "Duplicate " + label + "."); }
}

public sealed class AcceptedRequirements
{
    public string Json { get; }
    public string Sha256 { get; }
    public AcceptedRequirements(RequirementRecord record)
    {
        Json = ContractJson.Write(record, ContractValidation.Requirements);
        Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json))).ToLowerInvariant();
    }
    public RequirementRecord Read() => ContractJson.Read<RequirementRecord>(Json, ContractValidation.Requirements);
}
