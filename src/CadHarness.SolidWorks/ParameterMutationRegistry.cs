using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CadHarness.Ir;
using CadHarness.State;
using CadHarness.Ir.V03;
using CadHarness.State.V03;

namespace CadHarness.SolidWorks;

public sealed record ParameterMutationDescriptor(OperationKind OwnerKind, EditableParameter Parameter, string Field);

// Each handler owns native access, transition restrictions, topology risk and
// its opaque rollback payload. The transaction coordinator knows none of these.
public interface IParameterMutationHandler
{
    IReadOnlyList<ParameterMutationDescriptor> Descriptors { get; }
    bool CanExecute(OperationNode owner, EditableParameter parameter);
    void ValidateTransition(OperationNode before, OperationNode after, EditableParameter parameter);
    FullValidationReason ValidationReasons(OperationNode owner, EditableParameter parameter);
    double Read(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter);
    object Capture(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter);
    void Apply(SolidWorksExecutionContext context, OperationNode owner, EditableParameter parameter);
    void Restore(SolidWorksExecutionContext context, object rollback);
    void ValidateNative(SolidWorksExecutionContext context, CadProgram expected, string target, EditableParameter parameter);
}

public sealed class ParameterMutationRegistry
{
    public static ParameterMutationRegistry Default { get; } = new(new IParameterMutationHandler[]
        { new PatternScalarMutationHandler(), new ExtrusionDepthMutationHandler(), new HoleDiameterMutationHandler() });
    private readonly IReadOnlyDictionary<(OperationKind, EditableParameter), IParameterMutationHandler> handlers;
    public IReadOnlyList<ParameterMutationDescriptor> Descriptors { get; }
    public IReadOnlyDictionary<EditableParameter, string> Fields { get; }

    public ParameterMutationRegistry(IEnumerable<IParameterMutationHandler> registered)
    {
        var map = new Dictionary<(OperationKind, EditableParameter), IParameterMutationHandler>();
        var descriptors = new List<ParameterMutationDescriptor>();
        var fields = new Dictionary<EditableParameter, string>();
        foreach (var handler in registered)
        {
            if (handler is null || handler.Descriptors.Count == 0) throw new ArgumentException("Mutation handler requires finite descriptors.", nameof(registered));
            foreach (var descriptor in handler.Descriptors)
            {
                if (descriptor is null || !Enum.IsDefined(descriptor.OwnerKind) || !Enum.IsDefined(descriptor.Parameter) ||
                    !OperationRegistry.Default.Get(descriptor.OwnerKind).Parameters.Any(p => p.Name == descriptor.Field && p.Kind == EditableParameters.Contract(descriptor.Parameter).Kind) ||
                    !map.TryAdd((descriptor.OwnerKind, descriptor.Parameter), handler))
                    throw new ArgumentException("Invalid or duplicate native mutation descriptor.", nameof(registered));
                if (fields.TryGetValue(descriptor.Parameter, out var field) && field != descriptor.Field)
                    throw new ArgumentException("A semantic parameter must map consistently to one IR field.", nameof(registered));
                fields[descriptor.Parameter] = descriptor.Field; descriptors.Add(descriptor);
            }
        }
        handlers = new ReadOnlyDictionary<(OperationKind, EditableParameter), IParameterMutationHandler>(map);
        Descriptors = descriptors.AsReadOnly(); Fields = new ReadOnlyDictionary<EditableParameter, string>(fields);
    }
    public bool TryGet(OperationNode owner, EditableParameter parameter, out IParameterMutationHandler handler)
    {
        if (handlers.TryGetValue((owner.Kind, parameter), out handler!) && EditableParameters.IsOwnedBy(parameter, owner) &&
            owner.Parameters.ContainsKey(Fields[parameter]) && handler.CanExecute(owner, parameter)) return true;
        handler = null!; return false;
    }
    public IParameterMutationHandler Get(OperationNode owner, EditableParameter parameter) => TryGet(owner, parameter, out var handler) ? handler :
        throw new StateException(FailureCodes.OperationUnsupported, "No registered native mutation handler for this active owner/parameter pair.");
    public IObservedParameterMutationHandler GetObserved(NativeSubtype subtype, ParameterKey parameter) =>
        handlers.Values.Distinct().OfType<IObservedParameterMutationHandler>().SingleOrDefault(h => h.Supports(subtype, parameter)) ??
        throw new StateException(V03FailureCodes.UnsupportedNativeSubtype, "No registered observed-feature parameter handler.");
    public CadProgram ApplyProgram(CadProgram current, OperationNode edit)
    {
        var proposed = RelationParameterEditor.Apply(current, edit, Fields);
        var target = edit.Input("target")!.References[0].SemanticId;
        var parameter = edit.Parameter<ParameterNameParameter>("parameter").Value;
        var before = current.Operations.Single(o => o.SemanticId == target);
        var after = proposed.Operations.Single(o => o.SemanticId == target);
        Get(before, parameter).ValidateTransition(before, after, parameter);
        return proposed;
    }
    // Read-only captured parameters need validation even if no mutation handler
    // has been registered for them. This table grants no editing capability.
    private static readonly IReadOnlyDictionary<EditableParameter, string> ReadOnlyFields = new Dictionary<EditableParameter, string>(RelationParameterEditor.SupportedFields)
    {
        [EditableParameter.ExtrusionDepth] = "depthMm", [EditableParameter.HoleDiameter] = "diameterMm",
        [EditableParameter.BlindHoleDepth] = "depthMm", [EditableParameter.FilletRadius] = "radiusMm",
        [EditableParameter.ChamferDistance] = "distanceMm", [EditableParameter.PatternAngle] = "angleDeg"
    };
    public double Expected(OperationNode owner, EditableParameter parameter) => parameter switch
    {
        EditableParameter.ProfileDiameter => ((CircleProfile)owner.Parameter<ProfileParameter>("profile").Value).DiameterMm,
        EditableParameter.PatternAngle when owner.Kind == OperationKind.CreateCircularPattern => PatternGeometry.AngleDegrees(owner),
        _ => Scalar(owner.Parameters[Fields.TryGetValue(parameter, out var field) ? field : ReadOnlyFields[parameter]])
    };
    public static double Scalar(OperationParameter value) => value switch
    {
        LengthParameter length => length.Millimeters, CountParameter count => count.Value, AngleParameter angle => angle.Degrees,
        _ => throw new StateException(FailureCodes.OperationUnsupported, "Native mutations support finite scalar parameters.")
    };
}
