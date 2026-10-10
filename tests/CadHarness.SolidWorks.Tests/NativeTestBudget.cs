using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CadHarness.SolidWorks.Tests;

// Test accounting only. This contains no model state, geometry or semantic IDs.
internal sealed record BudgetSnapshot
{
    public int CreationAttempts { get; set; }
    public int PartsCreated { get; set; }
    public int PartsClosed { get; set; }
    public int OpenAttempts { get; set; }
    public List<string> OpenTestOwnedTitles { get; init; } = new();
}

internal sealed class NativeTestBudget : IDisposable
{
    internal const int MaximumParts = 2;
    private readonly FileStream ledger;
    private readonly string milestone;
    private readonly int maximumParts;
    internal BudgetSnapshot Snapshot { get; }
    internal NativeTestBudget(string path, string milestone = "Milestone 2", int maximumParts = MaximumParts)
    {
        this.milestone = milestone;
        if (maximumParts < 1) throw new ArgumentOutOfRangeException(nameof(maximumParts));
        this.maximumParts = maximumParts;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ledger = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            Snapshot = ledger.Length == 0 ? new() : JsonSerializer.Deserialize<BudgetSnapshot>(ledger)
                ?? throw new InvalidDataException("Native test budget ledger is invalid.");
            if (Snapshot.CreationAttempts < 0 || Snapshot.PartsCreated < 0 || Snapshot.PartsClosed < 0 ||
                Snapshot.OpenAttempts < 0 || Snapshot.PartsClosed > Snapshot.PartsCreated + Snapshot.OpenAttempts || Snapshot.PartsCreated > Snapshot.CreationAttempts)
                throw new InvalidDataException("Native test budget counters are invalid.");
        }
        catch { ledger.Dispose(); throw; }
    }
    internal void ReserveCreation()
    {
        if (Snapshot.CreationAttempts >= maximumParts)
            throw new TestFailure("ADDITIONAL_NATIVE_VALIDATION_RECOMMENDED", milestone + " native Part budget is exhausted; no further Part was created.");
        if (Snapshot.OpenTestOwnedTitles.Count != 0)
            throw new TestFailure("TEST_RESOURCE_LIMIT", "The budget ledger contains an unclosed test-owned document.");
        Snapshot.CreationAttempts++;
        Write(); // Reserve before NewDocument, even if that call later fails.
    }
    internal void RegisterCreated(string title)
    { Snapshot.PartsCreated++; Snapshot.OpenTestOwnedTitles.Add(title); Write(); }
    internal void ReserveOpen(int maximum)
    {
        if (Snapshot.OpenAttempts >= maximum || Snapshot.OpenTestOwnedTitles.Count != 0) throw new TestFailure("NATIVE_BUDGET_EXHAUSTED", "Owned reopen budget or lifecycle guard refused.");
        Snapshot.OpenAttempts++; Write();
    }
    internal void RegisterOpened(string title)
    { Snapshot.OpenTestOwnedTitles.Add(title); Write(); }
    internal void RenameOwned(string oldTitle, string newTitle)
    { Snapshot.OpenTestOwnedTitles.Remove(oldTitle); Snapshot.OpenTestOwnedTitles.Add(newTitle); Write(); }
    internal void RegisterClosed(string title)
    { Snapshot.PartsClosed++; Snapshot.OpenTestOwnedTitles.Remove(title); Write(); }
    private void Write()
    {
        ledger.Position = 0;
        ledger.SetLength(0);
        JsonSerializer.Serialize(ledger, Snapshot, new JsonSerializerOptions { WriteIndented = true });
        ledger.Flush(true);
    }
    public void Dispose() => ledger.Dispose();
}

internal sealed class TestFailure : Exception
{
    internal string Code { get; }
    internal TestFailure(string code, string message) : base(message) => Code = code;
}
