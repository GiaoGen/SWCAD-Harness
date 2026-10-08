using System.Diagnostics;
using System.Runtime.InteropServices;
using CadHarness.Ir;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadHarness.Playground;

// Playground document ownership, not a CAD backend. Constructed lazily without
// COM activation; all methods are called on the dispatcher thread.
public sealed class NativeSession(string stateDirectory, string? template) : INativeSession
{
    private SolidWorksConnection? connection;
    private SolidWorksExecutionContext? context;
    private IModelDoc2? original;
    private AtomicStateStore? store;
    private CadProgram? current;
    public NativeStatus Status { get; private set; } = new();
    public CadState? State { get; private set; }
    public NativeStatus Connect()
    {
        if (connection is null) connection = SolidWorksConnection.Connect();
        Status = Status with { Connected = true, ConnectionMode = connection.StartedApplication ? "Started application" : "Attached existing session" };
        return Status;
    }
    public IPlanningRuntime EditRuntime()
    {
        OwnPart();
        if (!Status.Healthy || store is null || current is null || State is null)
            throw new StateException("SESSION_INVALID", "No healthy committed live model.");
        try { return SolidWorksPlanningRuntime.ForEdit(context!, store.Load()); }
        catch (StateException)
        { Status = Status with { Healthy = false }; throw; }
    }
    private ISldWorks App => connection?.Application ?? throw new StateException("SOLIDWORKS_NOT_CONNECTED", "Connect SOLIDWORKS explicitly first.");
    private IModelDoc2[] Documents() => App.GetDocuments() is Array docs ? docs.Cast<IModelDoc2>().ToArray() : [];
    private static bool Same(IModelDoc2 a, IModelDoc2 b) => ReferenceEquals(a, b) || a.Equals(b);
    private void OwnPart()
    {
        if (context is null) throw new StateException("NO_MANAGED_PART", "No Playground-owned Part.");
        if (!Documents().Any(d => Same(d, context.Document)))
        {
            Status = Status with { Healthy = false };
            throw new StateException("SESSION_INVALID", "Managed document was externally closed. Close Test Part to release the session.");
        }
        var error = 0;
        var active = App.ActivateDoc3(context.Document.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as IModelDoc2;
        if (error != 0 || active is null || !Same(active, context.Document))
            throw new StateException("SESSION_INVALID", "Cannot activate the exact owned document.");
    }
    public NativeOutcome Create(CadProgram program, bool autoClose)
    {
        if (Status.PartOpen || context is not null) throw new StateException("TEST_RESOURCE_LIMIT", "Close current Playground test Part first.");
        var app = App;
        using var process = Process.GetProcessById(app.GetProcessID()); process.Refresh();
        Marshal.SetLastPInvokeError(0); var gdi = GetGuiResources(process.Handle, 0);
        if (!process.Responding || (gdi == 0 && Marshal.GetLastPInvokeError() != 0) || gdi >= 7000 ||
            Documents().Any(d => d.GetTitle().StartsWith("CADHarnessM", StringComparison.Ordinal) || d.GetTitle().StartsWith("Playground_", StringComparison.Ordinal)))
            throw new StateException("TEST_RESOURCE_LIMIT", "SOLIDWORKS resource guard failed (responsive process, readable GDI <7000, no open test Parts required).");
        string path;
        try { path = connection!.ResolvePartTemplate(template); }
        catch (FileNotFoundException) { throw new StateException("TEMPLATE_MISSING", "Configure an existing SOLIDWORKS .prtdot Part template."); }
        // One NewDocument call only. Register ownership before any subsequent work.
        original = app.IActiveDoc2 as IModelDoc2;
        context = connection.CreatePart(path);
        Status = Status with { PartOpen = true, Healthy = false, PartsCreated = Status.PartsCreated + 1 };
        using var telemetry = ExecutionTelemetry.Start();
        CompositionExecutionResult result;
        string? wrapperCode = null; string? wrapperMessage = null;
        using (ExecutionTelemetry.Measure(ExecutionPhase.Runtime))
        {
            if (!context.Document.SetTitle2("Playground_" + Guid.NewGuid().ToString("N")))
                throw new StateException("SESSION_INVALID", "Created Part could not be labelled; use Close Test Part.");
            store = new AtomicStateStore(Path.Combine(stateDirectory, Guid.NewGuid().ToString("N"), "state.json"));
            store.Commit(context.CaptureConstructionState());
            result = new RelationBackend().Create(context, program, store);
            if (result.Succeeded)
            {
                try { State = store.Load(); current = new DesignRelationEngine().Solve(program).Program; }
                catch (StateException error) { wrapperCode = error.Code; wrapperMessage = error.Message; }
            }
            Status = Status with { Healthy = result.Succeeded && wrapperCode is null, Revision = result.Transaction?.Revision ?? State?.Revision };
        }
        var observed = State;
        if (autoClose && result.Succeeded)
        {
            try { Close(); }
            catch (Exception error) { wrapperCode = "CLEANUP_FAILED"; wrapperMessage = error.Message; }
        }
        // Wrapper read/cleanup failures must retain the actual committed flags.
        return new(result.Succeeded && wrapperCode is null, wrapperCode ?? result.FailureCode, wrapperMessage ?? result.Message,
            result.Transaction, result.Operations, telemetry.Snapshot(), Status, observed);
    }
    public NativeOutcome Edit(CadProgram program, long revision)
    {
        _ = EditRuntime();
        if (State!.Revision != revision) throw new StateException("STALE_REVISION", "Committed revision changed.");
        var proposed = new DesignRelationEngine().Solve(ParameterMutationRegistry.Default.ApplyProgram(current!, program.Operations.Single())).Program;
        var backend = new TransactionalParameterBackend(context!);
        using var telemetry = ExecutionTelemetry.Start();
        MutationResult result;
        using (ExecutionTelemetry.Measure(ExecutionPhase.Runtime))
            result = new MutationTransaction<NativeEditPreparation, NativeEditRollback>(store!, backend).Execute(program.Operations.Single());
        string? wrapperCode = null; string? wrapperMessage = null;
        if (result.Succeeded)
        {
            try { State = store!.Load(); current = proposed; }
            catch (StateException error) { wrapperCode = error.Code; wrapperMessage = error.Message; Status = Status with { Healthy = false }; }
        }
        if (result.RollbackAttempted && !result.RollbackSucceeded) Status = Status with { Healthy = false };
        if (result.FailureCode == "STATE_DRIFT_DETECTED") Status = Status with { Healthy = false };
        Status = Status with { Revision = result.Revision ?? State?.Revision };
        return new(result.Succeeded && wrapperCode is null, wrapperCode ?? result.FailureCode, wrapperMessage ?? result.Message,
            result, backend.ValidationReads, telemetry.Snapshot(), Status, State);
    }
    public NativeStatus Close()
    {
        if (context is null) return Status;
        var doc = context.Document;
        // Identity is checked before CloseDoc(title), never accept a browser title.
        if (Documents().Any(d => Same(d, doc)))
        {
            App.CloseDoc(doc.GetTitle());
            if (Documents().Any(d => Same(d, doc))) throw new StateException("CLEANUP_FAILED", "Owned Part remains open.");
        }
        context = null; current = null; store = null; State = null;
        Status = Status with { PartOpen = false, Healthy = true, Revision = null, PartsClosed = Status.PartsClosed + 1 };
        if (original is not null && Documents().Any(d => Same(d, original)))
        {
            var error = 0;
            var active = App.ActivateDoc3(original.GetTitle(), false, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref error) as IModelDoc2;
            if (error != 0 || active is null || !Same(active, original))
                throw new StateException("CLEANUP_FAILED", "Part closed; original active document restoration failed.");
        }
        else if (original is not null) throw new StateException("CLEANUP_FAILED", "Part closed; original document was externally closed and cannot be restored.");
        original = null;
        return Status;
    }
    public void Dispose()
    {
        try { Close(); } finally { connection?.Dispose(); connection = null; }
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetGuiResources(IntPtr process, uint flag);
}
