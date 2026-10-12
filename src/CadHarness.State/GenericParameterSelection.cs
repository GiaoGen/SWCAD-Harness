using System.Collections.Generic;
using System.Linq;
using CadHarness.Ir.V03;

namespace CadHarness.State;

public static class GenericParameterSelection
{
    public static GenericNativeParameter? Resolve(IReadOnlyList<GenericNativeParameter> parameters,
        string owner, ParameterKey key, string? identity)
    {
        ContractValidation.Require(ContractValidation.Id(owner) && (identity is null || ContractValidation.Id(identity)), "Invalid parameter identity.");
        var matches = parameters.Where(p => p.Owner == owner && p.Key == key && (identity is null || p.SemanticId == identity)).ToArray();
        ContractValidation.Require(matches.Length <= 1, "Parameter key has multiple native drivers; exact semantic parameter identity is required.", "AMBIGUOUS_NATIVE_DIMENSION");
        return matches.SingleOrDefault();
    }
}
