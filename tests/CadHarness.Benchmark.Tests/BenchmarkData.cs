using System;
using System.Collections.Generic;
using System.Linq;
using CadHarness.Generalization.Tests;
using CadHarness.Ir;

namespace CadHarness.Benchmark.Tests;
internal sealed record EditIntent(string Intent, EditableParameter Parameter, double Value);
internal sealed record BenchmarkTask(string Name, string Intent, OperationKind[] Composition, ExpectedModel Initial, EditIntent[] Edits);
internal sealed record RunSlot(int Order, string Task, string Mode, int Repetition, bool Warmup)
{ public string Id => $"{Order:D2}-{Task}-{Mode}-{(Warmup ? "warmup" : "measured-" + Repetition)}"; }
internal static class BenchmarkData
{
    internal const int MaximumParts = 24;
    internal static BenchmarkTask[] Tasks()
    {
        ExpectedModel Expected(string name) => CaseData.All().Single(c => c.Name == name).Expected!;
        const string common = " Use one centered rectangle extrusion on XY, one through-hole seed, and one native pattern. " +
            "Use hosted_on, pattern_seed, equal_spacing, centered_about relations for the layout. " +
            "Do not add unspecified geometry or edge treatments. The initial creation target excludes later edits. " +
            "When appending operations, previously created operations cannot be moved or edited: place the seed at the first hole in the final centered layout before creating its pattern.";
        return new[] {
            new BenchmarkTask("G2", "Create a 100 x 60 x 8 mm plate with a centered 2 x 2 rectangular pattern of diameter 6 mm through holes, " +
                "60 mm spacing along local X and 30 mm along local Y. Hole centers are (-30,-15), (-30,15), (30,-15), (30,15)." + common,
                new[] { OperationKind.CreateExtrude, OperationKind.CreateThroughHole, OperationKind.CreateRectangularPattern }, Expected("G2"),
                new[] { new EditIntent("Change only the plate thickness from 8 to 10 mm, retaining all holes and relations.", EditableParameter.ExtrusionDepth, 10),
                    new EditIntent("Change only the through-hole seed diameter from 6 to 8 mm. All four pattern holes must update together.", EditableParameter.HoleDiameter, 8) }),
            new BenchmarkTask("HeldOut", "Create a 137 x 91 x 13 mm plate with three diameter 7 mm through holes centered along local X, " +
                "29 mm linear spacing, centers (-29,0), (0,0), (29,0). Add one diameter 11 mm blind hole, depth 4 mm from the top face, at local (0,27)." + common +
                " The blind hole is a separate native blind-hole operation with a hosted_on relation.",
                new[] { OperationKind.CreateExtrude, OperationKind.CreateThroughHole, OperationKind.CreateLinearPattern, OperationKind.CreateBlindHole }, Expected("HeldOut"),
                new[] { new EditIntent("Change only the through-hole seed diameter from 7 to 9 mm. All three instances must update. Keep the diameter 11 mm blind hole and its 4 mm depth unchanged.", EditableParameter.HoleDiameter, 9) })
        };
    }
    internal static RunSlot[] Schedule()
    {
        var list = new List<RunSlot>();
        for (var round = 0; round <= 5; round++)
            foreach (var task in round % 2 == 0 ? Tasks() : Tasks().Reverse())
                foreach (var mode in (round + (task.Name == "HeldOut" ? 1 : 0)) % 2 == 0 ? new[] { "Harness", "Stepwise" } : new[] { "Stepwise", "Harness" })
                    list.Add(new(list.Count + 1, task.Name, mode, round, round == 0));
        return list.ToArray();
    }
    internal static ExpectedModel AfterEdit(ExpectedModel m, EditIntent edit) => edit.Parameter == EditableParameter.ExtrusionDepth ?
        m with { Thickness = edit.Value, Volume = m.Volume / m.Thickness * edit.Value,
            Holes = m.Holes.Select(h => h with { Top = edit.Value, Bottom = h.Bottom == 0 ? 0 : edit.Value - (h.Top - h.Bottom) }).ToArray() } :
        m with { Volume = m.Volume - m.Holes.Where(h => h.Bottom == 0).Sum(h => Math.PI * (edit.Value * edit.Value - h.Diameter * h.Diameter) / 4 * m.Thickness),
            Holes = m.Holes.Select(h => h.Bottom == 0 ? h with { Diameter = edit.Value } : h).ToArray() };
}
