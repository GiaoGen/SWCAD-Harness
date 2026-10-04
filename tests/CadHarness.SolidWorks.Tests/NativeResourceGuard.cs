using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace CadHarness.SolidWorks.Tests;

internal sealed record ResourceSnapshot(int ProcessId, bool Responding, uint? GdiCount, int OpenTestOwnedParts);
internal static class NativeResourceGuard
{
    internal const uint MaximumGdi = 7000;
    internal static string TestTitlePrefix { get; set; } = "CADHarnessM2Test_";

    internal static string? Evaluate(bool responding, uint? gdiCount, int ownedParts) =>
        !responding || gdiCount >= MaximumGdi || ownedParts != 0 ? "TEST_RESOURCE_LIMIT" : null;

    internal static ResourceSnapshot Inspect(ISldWorks app, NativeTestBudget budget)
    {
        using var process = Process.GetProcessById(app.GetProcessID());
        process.Refresh();
        Marshal.SetLastPInvokeError(0);
        var gdi = GetGuiResources(process.Handle, 0);
        uint? gdiCount = gdi == 0 && Marshal.GetLastPInvokeError() != 0 ? null : gdi;
        var owned = DocumentTitles(app).Count(title => title.StartsWith("CADHarnessM", StringComparison.Ordinal) ||
            budget.Snapshot.OpenTestOwnedTitles.Contains(title, StringComparer.Ordinal));
        return new(process.Id, process.Responding, gdiCount, owned);
    }

    internal static string[] DocumentTitles(ISldWorks app)
    {
        if (app.GetDocuments() is not Array docs) return Array.Empty<string>();
        return docs.Cast<object>().Select(doc => ((IModelDoc2)doc).GetTitle()).ToArray();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr process, uint flag);
}
