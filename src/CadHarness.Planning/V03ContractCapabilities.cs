using System;
using System.Collections.Generic;
using CadHarness.Ir.V03;

namespace CadHarness.Planning;

public sealed record ConstructionContractDescriptor(ConstructionKind Kind, string WireName,
    string GeometricRole, bool Executable);

public static class V03ContractCapabilities
{
    public static IReadOnlyList<ConstructionContractDescriptor> Construction { get; } = Array.AsReadOnly(new[]
    {
        new ConstructionContractDescriptor(ConstructionKind.CreateSketch, "create_sketch", "Bound closed planar profile with declared driving constraints", false),
        new ConstructionContractDescriptor(ConstructionKind.CreateDatumPlane, "create_datum_plane", "Bound right-handed offset reference plane", false),
        new ConstructionContractDescriptor(ConstructionKind.CreateExtrude, "create_extrude", "Initial single-body extrusion of a bound generic sketch", false),
        new ConstructionContractDescriptor(ConstructionKind.CreateRevolvedBoss, "create_revolved_boss", "Single-body revolution about an in-plane profile axis", false),
        new ConstructionContractDescriptor(ConstructionKind.CreateAdditiveBoss, "create_additive_boss", "Positive-depth extrusion merged with a tracked host body", false),
        new ConstructionContractDescriptor(ConstructionKind.CreateExtrudedCut, "create_extruded_cut", "Blind or through-all removal from a tracked host body", false)
    });

    public static void RequireMode(RequestMode expected, RequestMode actual, ModelOrigin origin)
    {
        ContractValidation.Require(Enum.IsDefined(expected) && Enum.IsDefined(actual) && Enum.IsDefined(origin) && expected == actual &&
            (actual != RequestMode.CreateModel || origin == ModelOrigin.Harness) &&
            (actual != RequestMode.ManagedScalarEdit || origin == ModelOrigin.Harness) &&
            (actual != RequestMode.ExternalScalarEdit || origin == ModelOrigin.External), "Request mode/origin mismatch.", V03FailureCodes.ModeMismatch);
    }

    // Contract acceptance is separate from executable runtime projection until native qualification.
    public static void RequireExecutable(ConstructionProgram program, RequestMode mode)
    {
        RequireMode(mode, program.Mode, program.Origin); ContractValidation.Program(program);
        throw new ContractException(V03FailureCodes.CapabilityUnavailable, "M11 construction contracts have no registered native handlers.");
    }
    public static void RequireExecutable(EditSetRequest request, RequestMode mode)
    {
        RequireMode(mode, request.Mode, request.Origin); ContractValidation.Edits(request);
        throw new ContractException(V03FailureCodes.CapabilityUnavailable, "EditSet execution requires a verified external Part session; contract acceptance alone grants no native capability.");
    }
}
