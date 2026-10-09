using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

// This journal supplements the final report; an interrupted controller need not reach finally.
internal sealed class StepJournal : IReadOnlyList<object>
{
    private readonly List<object> entries = new();
    private readonly string directory;
    private readonly string testId;
    private readonly object sourceVersion;
    private readonly Func<object> budget;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    internal StepJournal(string directory, string testId, object sourceVersion, Func<object> budget)
    { this.directory = directory; this.testId = testId; this.sourceVersion = sourceVersion; this.budget = budget; }
    internal void Add(object evidence)
    {
        var sequence = entries.Count + 1;
        Program.WriteNew(Path.Combine(directory, sequence.ToString("D5") + ".json"),
            new { testId, sourceVersion, sequence, utc = DateTime.UtcNow, elapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
                controller = Environment.ProcessId, budget = budget(), evidence });
        entries.Add(evidence);
    }
    public int Count => entries.Count;
    public object this[int index] => entries[index];
    public IEnumerator<object> GetEnumerator() => entries.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
