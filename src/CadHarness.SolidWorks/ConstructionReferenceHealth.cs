using System.Linq;
using CadHarness.Ir;
using CadHarness.State;

namespace CadHarness.SolidWorks;

// Intentional edge consumption is represented as unavailable topology in state;
// it never grants binding permission. Datums and all other refs stay mandatory.
internal static class ConstructionReferenceHealth
{
    internal static bool IntentionalConsumption(CadProgram program, SemanticEntityNode entity) => entity.Type == SemanticType.LinearEdge &&
        program.Operations.Where(o => o.Kind is OperationKind.ApplyFillet or OperationKind.ApplyChamfer)
            .SelectMany(o => o.Input("edges")!.References).Any(r => r.SemanticId == entity.SemanticId);
}
