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
        new ConstructionContractDescriptor(ConstructionKind.CreateSketch, "create_sketch", "Bound closed planar profile with declared driving constraints", true),
        new ConstructionContractDescriptor(ConstructionKind.CreateDatumPlane, "create_datum_plane", "Bound right-handed offset reference plane", true),
        new ConstructionContractDescriptor(ConstructionKind.CreateExtrude, "create_extrude", "Initial single-body prism with bounded laminar material proof", true),
        new ConstructionContractDescriptor(ConstructionKind.CreateRevolvedBoss, "create_revolved_boss", "Single-body revolution about the exact profile-local X axis", true),
        new ConstructionContractDescriptor(ConstructionKind.CreateAdditiveBoss, "create_additive_boss", "Parallel-plane extrusion merged with retained tracked material", true),
        new ConstructionContractDescriptor(ConstructionKind.CreateExtrudedCut, "create_extruded_cut", "Blind or through-all removal with bounded single-body material proof", true)
    });

    public static void RequireMode(RequestMode expected, RequestMode actual, ModelOrigin origin)
    {
        ContractValidation.Require(Enum.IsDefined(expected) && Enum.IsDefined(actual) && Enum.IsDefined(origin) && expected == actual &&
            (actual != RequestMode.CreateModel || origin == ModelOrigin.Harness) &&
            (actual != RequestMode.ManagedScalarEdit || origin == ModelOrigin.Harness) &&
            (actual != RequestMode.ExternalScalarEdit || origin == ModelOrigin.External), "Request mode/origin mismatch.", V03FailureCodes.ModeMismatch);
    }

    public static void RequirePlanarOperation(ConstructionKind kind)
    {
        ContractValidation.Require(kind is ConstructionKind.CreateSketch or ConstructionKind.CreateDatumPlane,
            "Only M15 planar construction is qualified on an explicit native execution context.", V03FailureCodes.CapabilityUnavailable);
    }

    // Contract-only admission cannot establish the current native session's
    // ownership, host, configuration or material facts.
    public static void RequireExecutable(ConstructionProgram program, RequestMode mode)
    {
        RequireMode(mode, program.Mode, program.Origin); ContractValidation.Program(program);
        throw new ContractException(V03FailureCodes.CapabilityUnavailable, "Use ConstructGeneric on a verified native execution context; a context-free contract does not authorize mutation.");
    }
    public static void RequireExecutable(EditSetRequest request, RequestMode mode)
    {
        RequireMode(mode, request.Mode, request.Origin); ContractValidation.Edits(request);
        throw new ContractException(V03FailureCodes.CapabilityUnavailable, "EditSet execution requires a verified external Part session; contract acceptance alone grants no native capability.");
    }
}
