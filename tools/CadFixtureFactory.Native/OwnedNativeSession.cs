using System.Diagnostics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadFixtureFactory;

// Only ownership/resource infrastructure is shared. Neither construction nor
// geometry scoring lives in this assembly.
public sealed class OwnedNativeSession : IDisposable
{
    private readonly FileStream lease;
    private readonly string ledgerPath;
    private readonly string? originalTitle;
    private readonly int thread=System.Environment.CurrentManagedThreadId;
    private readonly PreparationAuthorization authorization;
    private bool disposed;
    public ISldWorks Application { get; }
    public IModelDoc2? Owned { get; private set; }
    public PreparationBudget Budget { get; }
    public bool ActiveRestored { get; private set; }
    public OwnedNativeSession(string budgetRoot,string slot)
    {
        if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA) throw new InvalidOperationException("Factory requires STA.");
        authorization=FixtureFiles.Read<PreparationAuthorization>(Path.Combine(budgetRoot,"authorization.json"));
        if(!authorization.UserAuthorized||authorization.MaximumCreationAttempts!=6||authorization.MaximumOpenAttempts!=8||authorization.GenerateHeldOut||authorization.M14MaximumCumulativeOpens!=12)
            throw new InvalidOperationException("Separate explicit preparation authorization required.");
        var grantPath=Path.Combine(budgetRoot,"authorizations",slot.Split(':')[0]+".json");
        if(File.Exists(grantPath))
        {
            var grant=FixtureFiles.Read<AdditionalPreparationAuthorization>(grantPath);FixtureFiles.Verify(grant.OriginalAuthorization);
            if(grant.OriginalAuthorization!=FixtureFiles.Identity(Path.Combine(budgetRoot,"authorization.json")))throw new InvalidOperationException("Additional authorization belongs to a different original grant.");
            authorization=FixtureFiles.ApplyAdditionalAuthorization(authorization,grant);
        }
        ledgerPath=Path.Combine(budgetRoot,"preparation-budget.json");
        lease=new FileStream(Path.Combine(budgetRoot,"controller.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        try
        {
            Budget=File.Exists(ledgerPath)?FixtureFiles.Read<PreparationBudget>(ledgerPath):new();
            if(Budget.AttemptSlots.Contains(slot)||Budget.OwnedTitles.Count!=0) throw new InvalidOperationException("Attempt already used or unresolved owned document.");
            Marshal.ThrowExceptionForHR(CLSIDFromProgID("SldWorks.Application",out var clsid));
            Marshal.ThrowExceptionForHR(GetActiveObject(ref clsid,IntPtr.Zero,out var app)); Application=(ISldWorks)app;
            originalTitle=Application.IActiveDoc2 is IModelDoc2 active?active.GetTitle():null;
            Budget.AttemptSlots.Add(slot); Record("controller-start",slot);
            Record("authorized-cumulative-ceiling",$"{authorization.MaximumCreationAttempts} creations / {authorization.MaximumOpenAttempts} opens; no M14 acceptance");
        }
        catch { lease.Dispose(); throw; }
    }
    public void Guard()
    {
        if(disposed||thread!=System.Environment.CurrentManagedThreadId) throw new InvalidOperationException("Native session ownership/thread lost.");
        using var p=Process.GetProcessById(Application.GetProcessID());p.Refresh();Marshal.SetLastPInvokeError(0);
        var gdi=GetGuiResources(p.Handle,0);
        if(!p.Responding||gdi>=7000||gdi==0) throw new InvalidOperationException("Factory responsive/GDI guard refused native execution.");
    }
    public IModelDoc2 Create(string template)
    {
        Guard(); RequireNoOwned();
        if(Budget.CreationAttempts>=authorization.MaximumCreationAttempts) throw new InvalidOperationException("Preparation creation budget exhausted.");
        Budget.CreationAttempts++;Record("reserve-creation",template);
        Owned=Application.NewDocument(template,0,0,0) as IModelDoc2;
        if(Owned is null) throw new InvalidOperationException("NewDocument failed.");
        Register("created-owned"); return Owned;
    }
    public IModelDoc2 OpenReadOnly(string path)
    {
        Guard();RequireNoOwned(); path=Path.GetFullPath(path);
        if(Application.GetOpenDocumentByName(path) is not null) throw new InvalidOperationException("Never adopt an existing engineer document.");
        if(Budget.OpenAttempts>=authorization.MaximumOpenAttempts) throw new InvalidOperationException("Preparation open budget exhausted.");
        Budget.OpenAttempts++;Record("reserve-open",path);var errors=0;var warnings=0;
        Owned=Application.OpenDoc6(path,(int)swDocumentTypes_e.swDocPART,(int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly|(int)swOpenDocOptions_e.swOpenDocOptions_Silent,"",ref errors,ref warnings) as IModelDoc2;
        if(Owned is not null)Register("opened-owned");
        if(Owned is null||errors!=0||warnings!=0||!Owned.IsOpenedReadOnly()||Owned.GetSaveFlag()) throw new InvalidOperationException($"Read-only fixture reopen failed: {errors}/{warnings}.");
        return Owned;
    }
    private void RequireNoOwned(){if(Owned is not null||Budget.OwnedTitles.Count!=0)throw new InvalidOperationException("One owned fixture at a time.");}
    private void Register(string kind){Budget.OwnedTitles.Add(Owned!.GetTitle());Record(kind,Owned.GetTitle());}
    public void RefreshOwnedTitle()
    { if(Owned is null)throw new InvalidOperationException("No owned Part."); Budget.OwnedTitles.Clear();Budget.OwnedTitles.Add(Owned.GetTitle());Record("saved-title",Owned.GetTitle()); }
    public void Close()
    {
        if(Owned is null)return;var title=Owned.GetTitle();var path=Owned.GetPathName();Application.CloseDoc(title);
        if(!string.IsNullOrEmpty(path)&&Application.GetOpenDocumentByName(path) is not null||Application.GetDocuments() is Array docs&&docs.Cast<object>().OfType<IModelDoc2>().Any(d=>d.GetTitle()==title))
            throw new InvalidOperationException("Owned fixture remained open.");
        Owned=null;Budget.OwnedTitles.Clear();Budget.ClosedDocuments++;Record("closed-owned",title);
    }
    public void RestoreActive()
    { var error=0;if(originalTitle is not null)Application.ActivateDoc3(originalTitle,false,(int)swRebuildOnActivation_e.swDontRebuildActiveDoc,ref error);
      ActiveRestored=error==0&&(originalTitle is null?Application.IActiveDoc2 is null:Application.IActiveDoc2 is IModelDoc2 active&&active.GetTitle()==originalTitle);
      if(!ActiveRestored)throw new InvalidOperationException("Original active engineer document not restored."); }
    public void Record(string kind,string detail)
    {
        Guard();using var p=Process.GetProcessById(Application.GetProcessID());
        Budget.Events.Add(new(kind,detail,System.Environment.ProcessId,Application.GetProcessID(),GetGuiResources(p.Handle,0),DateTime.UtcNow));FixtureFiles.Atomic(ledgerPath,Budget);
    }
    public void Dispose()
    {if(disposed)return;try{Close();RestoreActive();Record("controller-exit",ActiveRestored.ToString());}finally{disposed=true;lease.Dispose();if(Marshal.IsComObject(Application))Marshal.ReleaseComObject(Application);}}
    [DllImport("ole32.dll",CharSet=CharSet.Unicode)]private static extern int CLSIDFromProgID(string id,out Guid clsid);
    [DllImport("oleaut32.dll")]private static extern int GetActiveObject(ref Guid clsid,IntPtr reserved,[MarshalAs(UnmanagedType.IUnknown)]out object app);
    [DllImport("user32.dll",SetLastError=true)]private static extern uint GetGuiResources(IntPtr handle,uint flag);
}
