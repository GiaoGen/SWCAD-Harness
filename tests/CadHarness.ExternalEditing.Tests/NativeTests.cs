using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;

internal static class NativeTests
{
    internal static int Run(string root, string mode, string evidence, string source)
    {
        var output = Path.Combine(root, "artifacts", "milestone14", evidence);
        if (!File.Exists(Path.Combine(output, "native-freeze.json"))) throw new InvalidOperationException("Freeze exact source, binaries and fixture before native calls.");
        using var connection = SolidWorksConnection.Connect();
        using var ledger = new Ledger(Path.Combine(root, "artifacts", "milestone14"), connection.Application, evidence + mode);
        var package = Path.Combine(output, "package"); var original = ManagedRevisionStore.Fingerprint(source);
        if (mode == "--intake")
        {
            var opened = ExternalPartSession.CreateCopy(connection, package, source, lifecycle: ledger);
            var state = opened.Session?.CurrentExternal;
            var before = ledger.Titles();
            opened.Session?.Dispose();
            var preserved = ManagedRevisionStore.Hash(source) == original.Sha256;
            Program.WriteNew(Path.Combine(output, "intake-result.json"), new { opened.Status, opened.FailureCode, opened.Message,
                opened.Observation, state, original, preserved, before, after = ledger.Titles(), budget = ledger.Data });
            Console.WriteLine($"M14 intake: {opened.Status}; {opened.FailureCode}; {opened.Message}; opens={ledger.Data.OpenAttempts}");
            return preserved && opened.Status == ReopenStatus.Editable ? 0 : 2;
        }
        throw new ArgumentException("Native schedule mode is not implemented: " + mode);
    }
    internal sealed class Budget
    {
        public int Milestone { get; set; } = 14;
        public int MaximumNewParts { get; set; }
        public int MaximumOpenCycles { get; set; } = 12;
        public int OpenAttempts { get; set; }
        public int DocumentsClosed { get; set; }
        public List<string> AttemptedSteps { get; set; } = new();
        public List<string> OwnedTitles { get; set; } = new();
        public List<object> Events { get; set; } = new();
    }
    internal sealed class Ledger : IManagedDocumentLifecycle, IDisposable
    {
        private readonly ISldWorks app;
        private readonly string path;
        private readonly FileStream lease;
        internal Budget Data { get; }
        internal Ledger(string root, ISldWorks app, string slot)
        {
            this.app = app; path = Path.Combine(root, "native-budget.json");
            lease = new FileStream(Path.Combine(root, "native-budget.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Data = JsonSerializer.Deserialize<Budget>(File.ReadAllText(path))!;
            if (Data.AttemptedSteps.Contains(slot)) throw new StateException("NATIVE_SLOT_ALREADY_ATTEMPTED", "Preserve attempts; never rerun a slot.");
            Data.AttemptedSteps.Add(slot); Record("controller-start", slot);
        }
        internal string[] Titles() => app.GetDocuments() is Array docs ? docs.Cast<object>().OfType<IModelDoc2>().Select(d => d.GetTitle()).ToArray() : Array.Empty<string>();
        public void BeforeOpen(string path)
        {
            if (Data.OwnedTitles.Count != 0 || Data.OpenAttempts >= 12) throw new StateException("NATIVE_BUDGET_EXHAUSTED", "M14 permits one owned document and twelve cumulative opens.");
            Data.OpenAttempts++; Record("reserve-open", path);
        }
        public void Opened(string path, string title) { Data.OwnedTitles.Add(title); Record("opened-owned", path); }
        public void Closed(string path, string title)
        { Program.Check(Data.OwnedTitles.Remove(title), "Closing unowned document."); Data.DocumentsClosed++; Record("closed-owned", path); }
        private void Record(string kind, string value)
        {
            using var process = Process.GetProcessById(app.GetProcessID()); process.Refresh();
            var gdi = GetGuiResources(process.Handle, 0);
            if (!process.Responding || gdi == 0 || gdi >= 7000) throw new StateException("TEST_RESOURCE_LIMIT", "Native process/GDI guard.");
            Data.Events.Add(new { kind, value, controller = System.Environment.ProcessId, native = app.GetProcessID(), utc = DateTime.UtcNow, gdi });
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, Data, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true); }
            File.Replace(temp, path, null);
        }
        public void Dispose() { try { Record("controller-exit", ""); } finally { lease.Dispose(); } }
        [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr handle, uint flags);
    }
}
