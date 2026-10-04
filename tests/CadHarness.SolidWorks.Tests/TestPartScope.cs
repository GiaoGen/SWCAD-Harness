using System;
using System.Linq;
using CadHarness.SolidWorks;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.SolidWorks.Tests;

internal sealed class TestPartScope
{
    private readonly ISldWorks app;
    private readonly NativeTestBudget budget;
    private readonly string? originalTitle;
    private IModelDoc2? owned;
    private string? ownedTitle;
    internal bool Created { get; private set; }
    internal bool Closed { get; private set; }
    internal bool OriginalActiveRestored { get; private set; }
    internal string? CleanupError { get; private set; }
    internal TestPartScope(ISldWorks app, NativeTestBudget budget)
    {
        this.app = app; this.budget = budget;
        originalTitle = app.IActiveDoc2 is IModelDoc2 original ? original.GetTitle() : null;
    }
    internal SolidWorksExecutionContext Create(SolidWorksConnection connection, string template)
    {
        budget.ReserveCreation();
        var context = connection.CreatePart(template);
        // Register the returned document before any geometry or title mutation.
        owned = context.Document;
        Created = true;
        ownedTitle = owned.GetTitle();
        budget.RegisterCreated(ownedTitle);
        var newTitle = NativeResourceGuard.TestTitlePrefix + Guid.NewGuid().ToString("N");
        if (!owned.SetTitle2(newTitle)) throw new TestFailure("OPERATION_PRECONDITION_FAILED", "Could not label the native test-owned Part.");
        var actualTitle = owned.GetTitle();
        budget.RenameOwned(ownedTitle, actualTitle);
        ownedTitle = actualTitle;
        return context;
    }
    internal void Cleanup()
    {
        if (owned is not null && ownedTitle is not null)
        {
            try
            {
                // Native CloseDoc discards this unsaved test-owned document.
                // Never save, close all documents, or touch an unrelated Part.
                app.CloseDoc(ownedTitle);
                if (NativeResourceGuard.DocumentTitles(app).Contains(ownedTitle, StringComparer.Ordinal))
                    throw new InvalidOperationException("The test-owned Part is still open after CloseDoc.");
                Closed = true;
                budget.RegisterClosed(ownedTitle);
            }
            catch (Exception error) { CleanupError = "Close/discard failed: " + error.Message; }
        }
        try
        {
            if (originalTitle is null) OriginalActiveRestored = app.IActiveDoc2 is null;
            else
            {
                var error = 0;
                var restored = (IModelDoc2?)app.ActivateDoc3(originalTitle, false,
                    (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error);
                OriginalActiveRestored = error == 0 && restored?.GetTitle() == originalTitle &&
                    app.IActiveDoc2 is IModelDoc2 active && active.GetTitle() == originalTitle;
            }
            if (!OriginalActiveRestored) CleanupError = (CleanupError is null ? "" : CleanupError + "; ") + "Original active document was not restored.";
        }
        catch (Exception error) { CleanupError = (CleanupError is null ? "" : CleanupError + "; ") + "Active document restoration failed: " + error.Message; }
    }
    internal void RefreshOwnedTitle()
    {
        if (owned is null || ownedTitle is null) throw new InvalidOperationException("No test-owned Part is registered.");
        var current = owned.GetTitle();
        if (current != ownedTitle) { budget.RenameOwned(ownedTitle, current); ownedTitle = current; }
    }
}
