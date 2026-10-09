using System;
using System.Collections.Generic;
using CadHarness.Ir.V03;

namespace CadHarness.State.V03;

public sealed record NativeQualificationCandidate(NativeSubtype Subtype, ParameterKey Parameter,
    NativeAccessor Accessor, string NativeDefinition, string ReadContract, string WriteContract,
    string RequiredSubtypeEvidence, string IndependentOracle, string ApiEvidence, bool Qualified);

public static class NativeQualificationCandidates
{
    // API feasibility from the existing compiled backend is not external-history qualification.
    public static IReadOnlyList<NativeQualificationCandidate> Rows { get; } = Array.AsReadOnly(new[]
    {
        new NativeQualificationCandidate(NativeSubtype.StraightBlindBossExtrude, ParameterKey.ExtrusionDepth,
            NativeAccessor.ExtrudeDepthDirection1, "IExtrudeFeatureData2", "GetDepth(true) meters -> mm",
            "AccessSelections; SetDepth(true, mm/1000); ModifyDefinition; release on failure",
            "Boss extrusion; direction 1 blind; no direction 2/draft/thin/offset/contour override; one body; persistent owner; no equation/design-table/link/configuration driver; known dependencies",
            "Definition depth plus independent axial bounds, unchanged cross-section and dependent feature health",
            "src/CadHarness.SolidWorks/ParameterMutationHandlers.cs:ExtrusionDepthMutationHandler", false),
        new NativeQualificationCandidate(NativeSubtype.SingleCircleThroughAllCut, ParameterKey.HoleDiameter,
            NativeAccessor.SingleCircleRadius, "IExtrudeFeatureData2 + ISketchArc + IDimension",
            "Unique native driving radius/diameter SystemValue; rebuilt profile GetRadius()*2000 must agree",
            "Re-resolve unique driving circle dimension; SetSystemValue3 in active configuration; rebuild; profile/geometry agreement; no feature-name dimension lookup",
            "Single circular cut-extrude; one profile circle; one cylindrical wall; one direction through-all; no Hole Wizard/thin/taper/equation/link/table/configuration override; known host/dependencies",
            "Cylindrical wall radius, axis, both through boundaries, removed volume and unchanged unrelated holes",
            "src/CadHarness.SolidWorks/ObservedParameterMutationHandlers.cs; src/CadHarness.SolidWorks/ExternalNativeQualification.cs:HoleDimension", false),
        new NativeQualificationCandidate(NativeSubtype.SingleDirectionLinearPattern, ParameterKey.PatternCount,
            NativeAccessor.LinearPatternDirection1Count, "ILinearPatternFeatureData", "D1TotalInstances integer",
            "AccessSelections; assign D1TotalInstances; ModifyDefinition; preserve seed/direction selections",
            "One active linear direction; known feature seed; no skipped/varying/body/geometry-only instances; no second direction; no equation/link/table/configuration override; known dependencies",
            "Count and independently measured instance centers; seed unchanged; clearance and downstream health",
            "src/CadHarness.SolidWorks/NativePatternEditor.cs; src/CadHarness.SolidWorks/ParameterMutationHandlers.cs:PatternScalarMutationHandler", false),
        new NativeQualificationCandidate(NativeSubtype.SingleDirectionLinearPattern, ParameterKey.PatternSpacing,
            NativeAccessor.LinearPatternDirection1Spacing, "ILinearPatternFeatureData", "D1Spacing meters -> mm",
            "AccessSelections; assign D1Spacing=mm/1000; ModifyDefinition; preserve seed/direction selections",
            "Same single-active-direction history as pattern count; inactive direction spacing never advertised",
            "Spacing plus independent instance centers, seed invariance, dependent clearance and feature health",
            "src/CadHarness.SolidWorks/NativePatternEditor.cs; src/CadHarness.SolidWorks/RelationNativeReadback.cs", false)
    });
    public static void RequireExecutable(ObservedModel model, string target, ParameterKey parameter)
    {
        ObservedStateValidation.Validate(model);
        var feature = System.Linq.Enumerable.SingleOrDefault(model.Features, f => f.SemanticId == target);
        ContractValidation.Require(feature is not null && feature.EditSupport == EditSupport.Editable, "Target is observed-only or unresolved.", V03FailureCodes.ObservedOnlyTarget);
        ContractValidation.Require(System.Linq.Enumerable.Any(feature!.Parameters, p => p.Key == parameter &&
            ObservedStateValidation.Matches(feature.Subtype, parameter, p.Accessor)), "Native subtype/parameter is unsupported.", V03FailureCodes.UnsupportedNativeSubtype);
        throw new ContractException(V03FailureCodes.CapabilityUnavailable, "External native candidates are unqualified in M11; no mutation is available.");
    }
}
