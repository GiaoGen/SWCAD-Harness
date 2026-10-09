using System.Linq;
using CadHarness.Ir.V03;

namespace CadHarness.State.V03;

public static class ExternalInventoryIdentity
{
    public static bool Matches(ObservedModel current,ObservedModel expected)=>current.InventoryComplete&&expected.InventoryComplete&&
        current.Features.Count==expected.Features.Count&&
        current.Features.Select(f=>f.SemanticId).Distinct().Count()==current.Features.Count&&
        expected.Features.Select(f=>f.SemanticId).Distinct().Count()==expected.Features.Count&&
        current.Features.Select(f=>(f.SemanticId,f.NativeReference?.Base64,f.NativeType,f.Subtype,f.Health,f.DependencyCompleteness)).ToHashSet().SetEquals(
            expected.Features.Select(f=>(f.SemanticId,f.NativeReference?.Base64,f.NativeType,f.Subtype,f.Health,f.DependencyCompleteness)))&&
        current.Dependencies.Select(e=>(e.Prerequisite,e.Dependent,e.Kind)).ToHashSet().SetEquals(expected.Dependencies.Select(e=>(e.Prerequisite,e.Dependent,e.Kind)));
}
