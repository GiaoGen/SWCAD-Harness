using System.Collections.Generic;
using System.Linq;

namespace CadHarness.Ir;

// Finite profile-dependent semantic vocabulary; this grants no runtime capability.
public static class ProfileOutputs
{
    public static IReadOnlyList<SemanticOutput> For(ProfileKind profile) => OperationRegistry.Default.Get(OperationKind.CreateExtrude).Outputs
        .Where(o => profile == ProfileKind.Circle ? o.Type != SemanticType.LinearEdge : o.Suffix != ".rotational_reference").ToArray();
    public static IReadOnlyList<SemanticOutput> For(OperationNode operation) => operation.Kind == OperationKind.CreateExtrude
        ? For(operation.Parameter<ProfileParameter>("profile").Value.Kind) : OperationRegistry.Default.Get(operation.Kind).Outputs;
}
