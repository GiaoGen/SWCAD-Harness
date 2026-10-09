using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using CadHarness.SolidWorks;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

internal sealed record NativeEvent(string Kind, string Path, string Title, int ControllerPid, DateTime Utc, uint? Gdi);
internal sealed class LedgerData
{
    public int NewPartAttempts { get; set; }
    public int OpenAttempts { get; set; }
    public int DocumentsClosed { get; set; }
    public List<string> AttemptedSteps { get; set; } = new();
    public List<string> OwnedTitles { get; set; } = new();
    public List<NativeEvent> Events { get; set; } = new();
    public int MaximumOpenCycles { get; set; } = 5;
    public List<string> Authorizations { get; set; } = new();
}
internal sealed class NativeLedger : IManagedDocumentLifecycle, IDisposable
{
    private readonly FileStream lease;
    private readonly string path;
    private readonly ISldWorks app;
    internal LedgerData Data { get; }
    internal NativeLedger(string output, ISldWorks app, string step, string? authorization = null)
    {
        this.app = app; path = Path.Combine(output, "native-budget.json");
        lease = new FileStream(Path.Combine(output, "native-budget.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Data = File.Exists(path) ? JsonSerializer.Deserialize<LedgerData>(File.ReadAllText(path))! : new();
        if (Data.AttemptedSteps.Contains(step)) { lease.Dispose(); throw new StateException("NATIVE_SLOT_ALREADY_ATTEMPTED", "Never rerun or delete a native budget slot."); }
        if (authorization is not null && !Data.Authorizations.Contains(authorization))
        {
            using var record = JsonDocument.Parse(File.ReadAllText(authorization));
            var grant = record.RootElement;
            if (!grant.GetProperty("userAuthorized").GetBoolean() || grant.GetProperty("addedOpenCycles").GetInt32() != 1 ||
                grant.GetProperty("newOpenCycleLimit").GetInt32() != 6 || Data.MaximumOpenCycles != 5 || Data.OpenAttempts != 1)
            { lease.Dispose(); throw new StateException("NATIVE_BUDGET_EXHAUSTED", "Explicit additional-open authorization is absent or inconsistent."); }
            Data.MaximumOpenCycles = 6; Data.Authorizations.Add(authorization); Record("explicit-budget-authorization", authorization, step);
        }
        Data.AttemptedSteps.Add(step); Record("controller-start", "", step);
    }
    internal uint Guard(bool opening)
    {
        using var process = Process.GetProcessById(app.GetProcessID()); process.Refresh();
        Marshal.SetLastPInvokeError(0); var gdi = GetGuiResources(process.Handle, 0);
        if (!process.Responding || gdi >= 7000 || gdi == 0 && Marshal.GetLastPInvokeError() != 0 ||
            opening && (Data.OwnedTitles.Count != 0 || Titles().Any(t => t.StartsWith("CADHarnessM", StringComparison.Ordinal))))
            throw new StateException("TEST_RESOURCE_LIMIT", "Native resource/ownership guard refused this slot.");
        return gdi;
    }
    internal void ReserveCreation()
    {
        Guard(true);
        if (Data.NewPartAttempts >= 2) throw new StateException("NATIVE_BUDGET_EXHAUSTED", "M12 permits at most two new Parts.");
        Data.NewPartAttempts++; Record("reserve-new-part", "", "");
    }
    internal void Created(IModelDoc2 doc)
    {
        var title = doc.GetTitle(); Data.OwnedTitles.Add(title); Record("created-owned", "", title);
    }
    internal void RefreshTitle(string old, string current)
    { Data.OwnedTitles.Remove(old); Data.OwnedTitles.Add(current); Record("owned-title", "", current); }
    public void BeforeOpen(string path)
    {
        Guard(true);
        if (Data.OpenAttempts >= Data.MaximumOpenCycles) throw new StateException("NATIVE_BUDGET_EXHAUSTED", "The recorded M12 open-cycle authorization is exhausted.");
        Data.OpenAttempts++; Record("reserve-open", path, "");
    }
    public void Opened(string path, string title)
    {
        if (Data.OwnedTitles.Count != 0) throw new StateException("TEST_RESOURCE_LIMIT", "A second owned document cannot be registered.");
        Data.OwnedTitles.Add(title); Record("opened-owned", path, title);
    }
    public void Closed(string path, string title)
    {
        if (!Data.OwnedTitles.Remove(title)) throw new StateException("TEST_CLEANUP_FAILED", "Closing an unregistered document is forbidden.");
        Data.DocumentsClosed++; Record("closed-owned", path, title);
    }
    internal string[] Titles() => app.GetDocuments() is Array docs ? docs.Cast<object>().Cast<IModelDoc2>().Select(d => d.GetTitle()).ToArray() : Array.Empty<string>();
    internal void Record(string kind, string nativePath, string title)
    {
        uint? gdi = null;
        try { gdi = Guard(false); } catch when (kind is "controller-exit" or "closed-owned") { }
        Data.Events.Add(new(kind, nativePath, title, System.Environment.ProcessId, DateTime.UtcNow, gdi));
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, Data, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true); }
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }
    public void Dispose() { try { Record("controller-exit", "", ""); } finally { lease.Dispose(); } }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetGuiResources(IntPtr process, uint flag);
}
