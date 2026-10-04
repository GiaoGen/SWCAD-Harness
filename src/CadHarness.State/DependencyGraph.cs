using System;
using System.Collections.Generic;
using System.Linq;

namespace CadHarness.State;

public sealed class DependencyGraph
{
    public IReadOnlyList<DependencyEdge> Edges { get; }
    public DependencyGraph(IEnumerable<DependencyEdge> edges) => Edges = edges.Distinct().ToArray();
    // Constraint influence may form cycles (seed -> pattern -> seed). A finite
    // visited set gives closure; native construction edges alone must be a DAG.
    public IReadOnlyList<string> AffectedBy(IEnumerable<string> roots)
    {
        var visited = new HashSet<string>(roots, StringComparer.Ordinal);
        var queue = new Queue<string>(visited);
        while (queue.TryDequeue(out var current))
            foreach (var edge in Edges.Where(e => e.Prerequisite == current))
                if (visited.Add(edge.Dependent)) queue.Enqueue(edge.Dependent);
        return visited.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
    public bool HasPath(string prerequisite, string dependent) => AffectedBy(new[] { prerequisite }).Contains(dependent, StringComparer.Ordinal);
    public IReadOnlyList<string> NativeOrder(IEnumerable<string> features)
    {
        var nodes = features.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var remaining = new HashSet<string>(nodes, StringComparer.Ordinal);
        var order = new List<string>();
        while (remaining.Count != 0)
        {
            var ready = nodes.Where(n => remaining.Contains(n) && !Edges.Any(e => e.Kind == DependencyKind.NativeInput && e.Dependent == n && remaining.Contains(e.Prerequisite))).ToArray();
            if (ready.Length == 0) throw new StateException("RELATION_VIOLATED", "Native dependency edges contain a cycle.");
            foreach (var node in ready) { remaining.Remove(node); order.Add(node); }
        }
        return order.AsReadOnly();
    }
}
