using System;
using System.Collections.Generic;

namespace CadHarness.Ir.V03;

// Contract-only vocabulary. It is intentionally absent from OperationKind and OperationRegistry.
public enum ConstructionKind { CreateSketch, CreateDatumPlane, CreateExtrude, CreateRevolvedBoss, CreateAdditiveBoss, CreateExtrudedCut }
public enum RequestMode { CreateModel, ManagedScalarEdit, ExternalScalarEdit, EditSet }
public enum ModelOrigin { Harness, External }
public enum RequirementSource { Given, DerivedByRule, Defaulted, Unresolved }
public enum ScalarUnit { Millimeter, Degree, Count }
public enum PlanOutcome { Planned, NeedsClarification, Unsupported }
public enum EndCondition { Blind, ThroughAll, FullRevolution, FiniteAngle }
public enum Direction { Forward, Reverse }
public enum BodyRule { NewSingleBody, MergeWithHost, RemoveFromHost }
public enum SketchEntityKind { Line, Arc, Circle, Polyline, Slot }
public enum ConstraintKind { Coincident, Horizontal, Vertical, Parallel, Perpendicular, Concentric, Equal, Distance, Radius, Diameter }
public enum ConstraintStatus { UnderConstrained, FullyConstrained, OverConstrained }
public enum ParameterKey { ExtrusionDepth, HoleDiameter, PatternCount, PatternSpacing, RevolveRadius, RevolveAxialSpan, AdditiveBossDepth, CutDepth, SlotWidth, SlotLength, SlotCenterX, SlotCenterY }

public static class ContractLimits
{
    public const string Version = "0.3";
    public const int ProgramBytes = CadProgramJson.MaximumJsonBytes;
    public const int StateBytes = 1048576;
    public const int Operations = ProgramValidator.MaximumOperations;
    public const int SketchEntities = 64, SketchLoops = 8, SketchConstraints = 64;
    public const int Requirements = 128, Questions = 8, BatchMinimum = 2, BatchMaximum = 16;
    public const int Features = 256, Geometry = 1024, Parameters = 4096, Dependencies = 4096;
    public const double SpatialBoundMm = 1000000, LengthBoundMm = 1000000;
    public const double LinearToleranceMm = 0.01, AngularToleranceDeg = 0.01;
}

public sealed record SignedCoordinate(double Millimeters);
public sealed record SignedOffset(double Millimeters);
public sealed record PositiveLength(double Millimeters);
public sealed record LocalPoint(string Id, SignedCoordinate X, SignedCoordinate Y);
public sealed record Vector3(double X, double Y, double Z);
public sealed record LocalFrame(Vector3 OriginMm, Vector3 XAxis, Vector3 YAxis, Vector3 ZAxis);
public sealed record SpatialPlacement(SemanticReference Plane, SemanticReference? InPlaneDirection,
    LocalFrame Frame, SignedOffset Offset);
public sealed record SketchEntity(string Id, SketchEntityKind Kind, IReadOnlyList<string> Points,
    PositiveLength? Radius, double? SweepDegrees, PositiveLength? Width, PositiveLength? Length);
public sealed record SketchLoop(string Id, bool Inner, IReadOnlyList<string> Entities);
public sealed record SketchConstraint(string Id, ConstraintKind Kind, IReadOnlyList<string> References,
    PositiveLength? Dimension);
public sealed record ClosedSketch(IReadOnlyList<LocalPoint> Points, IReadOnlyList<SketchEntity> Entities,
    IReadOnlyList<SketchLoop> Loops, IReadOnlyList<SketchConstraint> Constraints, ConstraintStatus Status);
public sealed record ConstructionOperation(string Id, ConstructionKind Kind, string SemanticId,
    SpatialPlacement? Placement, ClosedSketch? Sketch, SemanticReference? Profile, SemanticReference? Axis,
    SemanticReference? HostBody, EndCondition? EndCondition, Direction? Direction,
    PositiveLength? Depth, double? AngleDegrees, BodyRule? BodyRule);
public sealed record ConstructionProgram(string ProgramVersion, ModelOrigin Origin, RequestMode Mode,
    string RequirementRecordId, IReadOnlyList<ConstructionOperation> Operations);

public sealed record Requirement(string SemanticKey, double? Value, ScalarUnit Unit, RequirementSource Source,
    string? RuleId, string? RuleVersion, double Confidence, bool Ambiguous, bool Critical);
public sealed record RequirementRecord(string SchemaVersion, string RecordId, long Revision,
    string Intent, IReadOnlyList<Requirement> Requirements);
public sealed record PlanningResponse(string SchemaVersion, PlanOutcome Outcome, RequirementRecord Requirements,
    ConstructionProgram? Program, IReadOnlyList<string> Questions, string? Reason);
public sealed record FileFingerprint(string Path, string Sha256, long SizeBytes);
public sealed record PartSelection(Guid DocumentId, Guid ConfigurationId, string ConfigurationName,
    long ExpectedRevision, FileFingerprint Source, FileFingerprint WorkingCopy);
public sealed record ScalarEdit(string Target, ParameterKey Parameter, ScalarUnit Unit,
    double ExpectedOldValue, double Value);
public sealed record EditSetRequest(string SchemaVersion, RequestMode Mode, ModelOrigin Origin,
    PartSelection Selection, IReadOnlyList<ScalarEdit> Edits);
public sealed record ScalarEditRequest(string SchemaVersion, RequestMode Mode, ModelOrigin Origin,
    PartSelection Selection, ScalarEdit Edit);

public static class V03FailureCodes
{
    public const string ContractInvalid = "V03_CONTRACT_INVALID";
    public const string ModeMismatch = "REQUEST_MODE_MISMATCH";
    public const string CapabilityUnavailable = "CAPABILITY_UNAVAILABLE";
    public const string UnsupportedNativeSubtype = "UNSUPPORTED_NATIVE_SUBTYPE";
    public const string ObservedOnlyTarget = "OBSERVED_ONLY_TARGET";
    public const string AmbiguousTargetSet = "AMBIGUOUS_TARGET_SET";
    public const string SourceFileDrift = "SOURCE_FILE_DRIFT";
    public const string ConfigurationMismatch = "CONFIGURATION_MISMATCH";
    public const string MigrationFailed = "MIGRATION_FAILED";
    public const string NativeValidationRequired = "NATIVE_VALIDATION_REQUIRED";
    public const string IncompleteDurablePublish = "INCOMPLETE_DURABLE_PUBLISH";
    public const string InvalidSketch = "INVALID_SKETCH";
    public const string UnderConstrainedSketch = "UNDER_CONSTRAINED_SKETCH";
    public const string OverConstrainedSketch = "OVER_CONSTRAINED_SKETCH";
    public const string UnsupportedBodyTopology = "UNSUPPORTED_BODY_TOPOLOGY";
    public const string PreviewExpired = "PREVIEW_EXPIRED";
    public const string ObservationLimitExceeded = "OBSERVATION_LIMIT_EXCEEDED";
    public const string NeedsClarification = "NEEDS_CLARIFICATION";
}

public sealed class ContractException : Exception
{
    public string Code { get; }
    public ContractException(string code, string message) : base(message) => Code = code;
}
