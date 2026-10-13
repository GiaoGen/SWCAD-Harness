using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;

namespace CadHarness.Playground;

public sealed partial class NativeSession
{
    private ExternalPartSession? external;
    private GenericPartSession? generic;
    public ModelView? Model { get; private set; }
    private string NewRoot() => Path.Combine(stateDirectory, Guid.NewGuid().ToString("N"));
    private void RequireAvailable()
    {
        _ = App;
        if (Status.PartOpen || context is not null || external is not null || generic is not null)
            throw new StateException("TEST_RESOURCE_LIMIT", "Close the owned Part before selecting another model.");
    }
    public ModelView OpenModel(ModelOpenRequest request)
    {
        RequireAvailable(); Model = null;
        if (request.Mode == "inspect")
        {
            var result = ExternalPartInspection.Inspect(connection!, request.Path, Path.Combine(NewRoot(), "inspection_" + Guid.NewGuid().ToString("N") + ".SLDPRT"), request.Configuration);
            Model = ModelViews.External(result.Observation.Model, false, false); return Model;
        }
        if (request.Mode == "external_copy")
        {
            var result = ExternalPartSession.CreateCopy(connection!, NewRoot(), request.Path, request.Configuration);
            if (result.Session is null)
            {
                if (result.Observation is not null) Model = ModelViews.External(result.Observation, false, false);
                throw new StateException(result.FailureCode ?? "EXTERNAL_INTAKE_FAILED", result.Message);
            }
            external = result.Session;
        }
        else if (request.Mode == "external_restore")
        {
            var result = ExternalPartSession.Open(connection!, request.Root!, request.Path);
            external = result.Session ?? throw new StateException(result.FailureCode ?? "EXTERNAL_REOPEN_FAILED", result.Message);
        }
        else if (request.Mode == "managed_restore")
        {
            var result = GenericPartSession.Open(connection!, request.Root!, request.Path);
            generic = result.Session ?? throw new StateException(result.FailureCode ?? "GENERIC_REOPEN_FAILED", result.Message);
        }
        else throw new StateException("REQUEST_INVALID", "Unknown model intake mode.");
        RefreshModern(); return Model!;
    }
    private void RefreshModern()
    {
        if (external is not null)
        {
            Model = ModelViews.External(external.CurrentExternal.Observation, true, external.Status == ReopenStatus.Editable, external.CurrentExternal.Geometry);
            if (external.Status != ReopenStatus.Editable) Model = Model with { Phase = "committed_quarantined" };
        }
        else if (generic is not null)
        {
            // Display the authoritative package, not an in-flight or quarantined native capture.
            if (File.Exists(generic.Store.PointerPath))
            {
                var committed = generic.Store.ReadCurrent().Generic ?? throw new StateException("INCOMPLETE_DURABLE_PUBLISH", "No authoritative Generic companion.");
                var requirements = committed.Provenance is { Kind: "construction_planning" } provenance ?
                    ContractJson.Read<PlanningHistory>(provenance.Payload, _ => { }).Steps.Where(s => s.Plan is not null).Select(s => s.Plan!.Response.Requirements).ToArray() : null;
                Model = ModelViews.Generic(committed.State, generic.Store.WorkingPath, generic.Status == ReopenStatus.Editable, requirements);
                if (generic.Status != ReopenStatus.Editable) Model = Model with { Phase = "committed_quarantined" };
            }
            else Model = new("managed", "owned_uncommitted", true, false, null, null, generic.Store.WorkingPath, "", false,
                Array.Empty<ModelFeature>(), Array.Empty<ModelEdge>());
        }
        Status = Status with { PartOpen = external is not null || generic is not null,
            Healthy = external is not null ? external.Status == ReopenStatus.Editable : generic is null || generic.Status == ReopenStatus.Editable, Revision = Model?.Revision };
    }
    public IConstructionPlanningRuntime ConstructionRuntime()
    {
        if (external is not null || context is not null) throw new StateException("REQUEST_MODE_MISMATCH", "Construction requires a new or owned Generic model.");
        return generic is null ? SolidWorksConstructionPlanningRuntime.ForNewModel() : SolidWorksConstructionPlanningRuntime.ForManagedSession(generic.Context);
    }
    public ModernOutcome ExecuteConstruction(ConstructionPlanPreview preview, string originalResponse)
    {
        _ = App;
        try
        {
            ConstructionPlanPreview accepted = preview;
            if (generic is null)
            {
                RequireAvailable();
                generic = GenericPartSession.Create(connection!, NewRoot(), connection!.ResolvePartTemplate(template));
                Status = Status with { PartOpen = true, Healthy = false, PartsCreated = Status.PartsCreated + 1 };
                // Revalidate the exact original response in the newly owned native context, not expanded IR or a repaired response.
                accepted = ConstructionReplay.Rebind(preview, SolidWorksConstructionPlanningRuntime.ForManagedSession(generic.Context), originalResponse);
            }
            var transaction = generic.ExecuteConstructionPlan(accepted.Approve(accepted.Sha256));
            return new(transaction, new { root = generic.Store.Root, workingPath = generic.Store.WorkingPath });
        }
        finally { RefreshModern(); }
    }
    public QueryPlanningResult PreviewBatch(BatchPreviewRequest request)
    {
        var s = RequireExternal(request.Revision);
        return s.PreviewEditSet(new("0.3", 1, s.CurrentExternal.Observation.Selection, request.Changes));
    }
    public ModernOutcome ExecuteBatch(ExternalQueryPreview preview, ModernConfirmation confirmation)
    {
        var s = RequireExternal(confirmation.Revision!.Value);
        try { var result = s.Edit(preview.Approve(confirmation.Sha256, confirmation.Revision.Value, confirmation.Targets)); return new(result.Transaction, result); }
        finally { RefreshModern(); }
    }
    public object PreviewScalar(ScalarPreviewRequest request)
    {
        var s = RequireExternal(request.Revision); s.VerifyLive(s.CurrentExternal);
        var edit = Scalar(request, s); NativeQualificationCandidates.RequireExecutable(s.CurrentExternal.Observation, request.Target, request.Parameter);
        return ExternalEditPlanning.Prepare(s.CurrentExternal, edit);
    }
    private static ScalarEditRequest Scalar(ScalarPreviewRequest r, ExternalPartSession s) => new("0.3", RequestMode.ExternalScalarEdit, ModelOrigin.External,
        s.CurrentExternal.Observation.Selection, new(r.Target, r.Parameter, r.Unit, r.ExpectedOldValue, r.Value));
    public ModernOutcome ExecuteScalar(ScalarPreviewRequest request)
    {
        var s = RequireExternal(request.Revision);
        try { var result = s.Edit(Scalar(request, s)); return new(result.Transaction, result); } finally { RefreshModern(); }
    }
    private ExternalPartSession RequireExternal(long revision)
    {
        var s = external ?? throw new StateException("REQUEST_MODE_MISMATCH", "External owned editing session required.");
        if (s.Status != ReopenStatus.Editable || s.CurrentExternal.Observation.Selection.ExpectedRevision != revision)
            throw new StateException("STALE_REVISION", "External session is quarantined or revision changed.");
        return s;
    }
    private NativeStatus CloseModern()
    {
        // Retain ownership fields if disposal cannot prove safe release.
        if (external is not null) { external.Dispose(); external = null; }
        if (generic is not null) { generic.Dispose(); generic = null; }
        Status = Status with { PartsClosed = Status.PartsClosed + (Status.PartOpen ? 1 : 0), PartOpen = false, Revision = null, Healthy = true };
        Model = null; return Status;
    }
}
