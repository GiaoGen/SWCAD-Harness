using System;
using System.Collections.Generic;
using System.Linq;

namespace CadHarness.Ir;

public enum OperationKind
{
    CreateExtrude, CreateThroughHole, CreateBlindHole,
    CreateLinearPattern, CreateRectangularPattern, CreateCircularPattern,
    ApplyFillet, ApplyChamfer, EditParameter
}

public enum SemanticType
{
    FeatureRef, BodyRef, SketchProfile, PlanarFace, CylindricalFace,
    LinearEdge, CircularEdge, ReferenceAxis, ReferencePlane, PointRef, LocalFrame
}

public enum SemanticRole
{
    HostSurface, PatternSeed, PatternDirection, FilletEdgeSet,
    ChamferEdgeSet, RotationalReference, PlacementReference
}

public enum ParameterKind { Length, Count, Angle, Point2D, Profile, ParameterName, EditValue }
public enum EditableParameter
{
    ProfileWidth, ProfileHeight, ProfileDiameter, ExtrusionDepth,
    HoleDiameter, BlindHoleDepth, PatternSpacing, PatternSpacingX, PatternSpacingY,
    PatternCount, PatternCountX, PatternCountY, PatternAngle, FilletRadius, ChamferDistance
}
public enum ProfileKind { CenteredRectangle, Circle }
public enum RelationKind
{
    HostedOn, PatternSeed, CenteredAbout, SymmetricAboutAxis,
    EqualSpacing, ThroughAll, AlignedWith, DependsOn
}

// These contain semantic identifiers and units only. No native objects or names.
public sealed record SemanticReference(string SemanticId, SemanticType Type);
public sealed record Point2D(double XMm, double YMm);
public abstract record SketchProfile(ProfileKind Kind);
public sealed record CenteredRectangleProfile(double WidthMm, double HeightMm)
    : SketchProfile(ProfileKind.CenteredRectangle);
public sealed record CircleProfile(double DiameterMm) : SketchProfile(ProfileKind.Circle);

public abstract record OperationParameter(ParameterKind Kind);
public sealed record LengthParameter(double Millimeters) : OperationParameter(ParameterKind.Length);
public sealed record CountParameter(int Value) : OperationParameter(ParameterKind.Count);
public sealed record AngleParameter(double Degrees) : OperationParameter(ParameterKind.Angle);
public sealed record PlacementParameter(Point2D Value) : OperationParameter(ParameterKind.Point2D);
public sealed record ProfileParameter(SketchProfile Value) : OperationParameter(ParameterKind.Profile);
public sealed record ParameterNameParameter(EditableParameter Value) : OperationParameter(ParameterKind.ParameterName);
public sealed record EditValueParameter(OperationParameter Value) : OperationParameter(ParameterKind.EditValue);

public sealed record OperationInput(string Name, IReadOnlyList<SemanticReference> References);

public sealed record OperationNode(
    string Id,
    OperationKind Kind,
    string? SemanticId,
    IReadOnlyList<OperationInput> Inputs,
    IReadOnlyDictionary<string, OperationParameter> Parameters)
{
    public OperationInput? Input(string name) => Inputs.FirstOrDefault(input => input.Name == name);
    public T Parameter<T>(string name) where T : OperationParameter => (T)Parameters[name];
}

// Declarative IR only; deterministic relation execution lives in the state layer.
public sealed record DesignRelation(RelationKind Kind, string Subject, string? Reference);
public sealed record CadProgram(
    string ProgramVersion,
    IReadOnlyList<OperationNode> Operations,
    IReadOnlyList<DesignRelation> Relations);

public sealed record ValidationIssue(string Code, string Path, string Message);
public sealed record ProgramValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}
public sealed record ProgramParseResult(CadProgram? Program, IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Program is not null && Issues.Count == 0;
}

public static class FailureCodes
{
    public const string SchemaInvalid = "PROGRAM_SCHEMA_INVALID";
    public const string OperationUnsupported = "OPERATION_UNSUPPORTED";
    public const string PreconditionFailed = "OPERATION_PRECONDITION_FAILED";
}

public static class SemanticTypes
{
    public static IReadOnlyList<SemanticType> ForRole(SemanticRole role) => role switch
    {
        SemanticRole.HostSurface => Array.AsReadOnly(new[] { SemanticType.PlanarFace, SemanticType.ReferencePlane }),
        SemanticRole.PatternSeed => Array.AsReadOnly(new[] { SemanticType.FeatureRef }),
        SemanticRole.PatternDirection => Array.AsReadOnly(new[] { SemanticType.LinearEdge, SemanticType.ReferenceAxis }),
        SemanticRole.FilletEdgeSet or SemanticRole.ChamferEdgeSet =>
            Array.AsReadOnly(new[] { SemanticType.LinearEdge, SemanticType.CircularEdge }),
        SemanticRole.RotationalReference => Array.AsReadOnly(new[]
            { SemanticType.CylindricalFace, SemanticType.CircularEdge, SemanticType.ReferenceAxis }),
        SemanticRole.PlacementReference => Array.AsReadOnly(new[]
            { SemanticType.PointRef, SemanticType.LocalFrame, SemanticType.ReferencePlane, SemanticType.ReferenceAxis }),
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
}
