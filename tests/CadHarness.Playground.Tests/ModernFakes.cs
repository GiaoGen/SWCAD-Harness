using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.Playground;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;

namespace CadHarness.Playground.Tests;

internal sealed class ModernProvider : IProviderFactory
{
    internal string? Response;
    internal int Calls;
    internal TaskCompletionSource? Block;
    public IStructuredPlanSource Create(LlmSettings settings, string key) => new Source(this);
    private sealed class Source(ModernProvider p) : IStructuredPlanSource
    {
        public bool IsModelBacked => false;
        public async Task<StructuredPlanResponse> GenerateAsync(PlannerPrompt prompt, CancellationToken token)
        {
            p.Calls++; if (p.Block is not null) await p.Block.Task.WaitAsync(token);
            if(p.Response is not null)return new(p.Response);
            if(prompt.ResponseSchemaJson.Contains("\"nodes\"",StringComparison.Ordinal))
            {
                var node=new SemanticConstructionNode("body",ConstructionKind.CreateExtrude,new(SemanticPlane.Xy,"zero","zero","zero","frame_angle"),
                    new(SemanticProfileKind.Rectangle,new("zero","zero","width","height"),null,null,null),null,EndCondition.Blind,Direction.Forward,"depth",null,null);
                var v2=prompt.ResponseSchemaJson.Contains("externalRecesses",StringComparison.Ordinal);
                // The fixture source is deterministic, not a real model or production planner.
                return new(v2?ContractJson.Write(new SemanticConstructionDraftV2(2,PlanOutcome.Planned,new[]{new SemanticConstructionNodeV2(node.Id,node.Kind,node.Placement,new(SemanticProfileKind.Rectangle,node.Profile!.Rectangle,null,null,null),null,node.EndCondition,node.Direction,node.Depth,null,null)},Array.Empty<string>(),null),SemanticConstructionV2.ValidateDraft):
                    ContractJson.Write(new SemanticConstructionDraft(1,PlanOutcome.Planned,new[]{node},Array.Empty<string>(),null),SemanticConstructionCompiler.ValidateDraft));
            }
            var marker="\nImmutable requirements:\n";var facts=ContractJson.Read<RequirementRecord>(prompt.Instructions[(prompt.Instructions.LastIndexOf(marker,StringComparison.Ordinal)+marker.Length)..],ContractValidation.Requirements);
            var values=facts.Requirements.ToDictionary(r=>r.SemanticKey,r=>r.Value!.Value);
            var response=Cases.Response(Cases.Plate(),Cases.Program(Cases.Rectangle(values["width"],values["height"]),values["depth"]));
            return new(Cases.Wire(response with{Requirements=facts,Program=response.Program! with{RequirementRecordId=facts.RecordId}}));
        }
    }
}

// Mock transport only. Real pure production qualification/query/preflight, no COM connector.
internal sealed class ModernFake : INativeSession, IModernNativeSession
{
    public NativeStatus Status { get; private set; } = new();
    public CadState? State => null;
    public ModelView? Model { get; private set; }
    internal ExternalEditState External = Sample();
    internal int Dispatches, SetterCount, Checkpoints, StaCalls;
    internal bool Fail, Unsupported;
    internal ManualResetEventSlim? Block;
    private Guid session = Guid.NewGuid();
    private void Sta() { if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new Exception("Missing STA dispatch"); StaCalls++; }
    public NativeStatus Connect() { Sta(); return Status = Status with { Connected = true, ConnectionMode = "MOCK ONLY / NO CAD" }; }
    public ModelView OpenModel(ModelOpenRequest r)
    {
        Sta(); session = Guid.NewGuid(); External = Sample();
        if (Unsupported) External = External with { Observation = External.Observation with { Features = External.Observation.Features.Select(f => f.SemanticId == "hole_c" ? f with { EditSupport = EditSupport.ReadOnly } : f).ToArray() } };
        var owned = r.Mode != "inspect";
        Model = ModelViews.External(External.Observation, owned, owned, External.Geometry);
        Status = Status with { PartOpen = owned, Revision = Model.Revision, Healthy = true }; return Model;
    }
    public IConstructionPlanningRuntime ConstructionRuntime() { Sta(); return SolidWorksConstructionPlanningRuntime.ForNewModel(); }
    public QueryPlanningResult PreviewBatch(BatchPreviewRequest r)
    { Sta(); return ExternalQueryPlanning.Prepare(External, new("0.3", 1, External.Observation.Selection, r.Changes), session); }
    public ModernOutcome ExecuteBatch(ExternalQueryPreview p, ModernConfirmation c)
    {
        Sta(); var approval = p.Approve(c.Sha256, c.Revision!.Value, c.Targets).Consume(session);
        var edits = ExternalQueryPlanning.Revalidate(External, approval);
        return Commit(ExternalEditPlanning.Prepare(External, edits));
    }
    private ScalarEditRequest Scalar(ScalarPreviewRequest r) => new("0.3", RequestMode.ExternalScalarEdit, ModelOrigin.External,
        External.Observation.Selection, new(r.Target, r.Parameter, r.Unit, r.ExpectedOldValue, r.Value));
    public object PreviewScalar(ScalarPreviewRequest r)
    { Sta(); NativeQualificationCandidates.RequireExecutable(External.Observation, r.Target, r.Parameter); return ExternalEditPlanning.Prepare(External, Scalar(r)); }
    public ModernOutcome ExecuteScalar(ScalarPreviewRequest r) { Sta(); return Commit(ExternalEditPlanning.Prepare(External, Scalar(r))); }
    private ModernOutcome Commit(ExternalEditPreparation p)
    {
        Dispatches++; Block?.Wait(); Checkpoints++; SetterCount += p.Ordered.Count;
        if (!Fail) External = p.Proposed;
        Model = ModelViews.External(External.Observation, true, true, External.Geometry); Status = Status with { Revision = Model.Revision };
        return new(new(!Fail, Fail ? "MOCK_ROLLBACK" : null, "MOCK transaction / not native evidence", "commit", true, true, Fail, Fail, !Fail, false,
            Model.Revision, p.ChangeSet, null, null), new { mockOnly = true, checkpoints = 1 });
    }
    public ModernOutcome ExecuteConstruction(ConstructionPlanPreview p, string raw)
    {
        Sta(); var runtime = SolidWorksConstructionPlanningRuntime.ForNewModel(); var accepted = ConstructionReplay.Rebind(p, runtime, raw);
        var program = accepted.Approve(accepted.Sha256).Consume(runtime); Dispatches++; Checkpoints++;
        var features = program.Operations.Select((o, i) => new GenericNativeFeature(o.SemanticId, o.Kind, o.Kind.ToString(), new(Convert.ToBase64String(new byte[]{1,2,(byte)(i+1)})), Array.Empty<NativePersistentReference>())).ToArray();
        var sketch = program.Operations.First();
        var state = new GenericConstructionState(new(Guid.NewGuid(), Guid.NewGuid(), "Default", "D:\\mock\\working.SLDPRT"), 1, program,
            features, new[]{new GenericNativeSketch(sketch.SemanticId,features[0].Reference,new("AQID"),sketch.Placement!,sketch.Sketch!,Array.Empty<NativePersistentReference>())},
            Array.Empty<GenericNativeParameter>(), new[]{new DependencyEdge(sketch.SemanticId,program.Operations.Last().SemanticId,DependencyKind.NativeInput)}, null);
        Model = ModelViews.Generic(state, "D:\\mock\\working.SLDPRT", true); Status = Status with { PartOpen = true, Revision = 1, PartsCreated = Status.PartsCreated + 1 };
        return new(new(true,null,"Mock construction","commit",true,true,false,false,true,false,1,ChangeSet.Empty,null,null), new{mockOnly=true});
    }
    public NativeStatus Close() { Sta(); Model = null; return Status = Status with { PartOpen = false, Revision = null, PartsClosed = Status.PartsClosed + (Status.PartOpen ? 1 : 0) }; }
    public void Dispose() => Close();
    public IPlanningRuntime EditRuntime() => throw new NotSupportedException();
    public NativeOutcome Create(CadHarness.Ir.CadProgram program, bool autoClose) => throw new NotSupportedException();
    public NativeOutcome Edit(CadHarness.Ir.CadProgram program, long revision) => throw new NotSupportedException();
    internal static ExternalEditState Sample()
    {
        var evidence = new[]{new ObservationEvidence(EvidenceSource.NativeDefinition,"MOCK fact / no native qualification claim")};
        ObservedFeature Feature(string id, int n, NativeSubtype subtype, ParameterKey key, double value, NativeAccessor accessor) => new(id,id,n,ModelOrigin.External,
            subtype.ToString(),subtype,ObservationHealth.Healthy,new(Convert.ToBase64String(new byte[]{1,2,(byte)n})),EvidenceCompleteness.Known,EditSupport.Editable,
            "MOCK qualified descriptor",evidence,new[]{new ObservedParameter(id+".value",key,key==ParameterKey.PatternCount?ScalarUnit.Count:ScalarUnit.Millimeter,value,accessor,evidence)},Array.Empty<GeometryObservation>());
        var selection = new PartSelection(Guid.NewGuid(),Guid.NewGuid(),"Default",4,new("D:\\mock\\source.SLDPRT",new string('a',64),100),new("D:\\mock\\working.SLDPRT",new string('b',64),100));
        var features = new[]{Feature("base",1,NativeSubtype.StraightBlindBossExtrude,ParameterKey.ExtrusionDepth,10,NativeAccessor.ExtrudeDepthDirection1),
            Feature("hole_a",2,NativeSubtype.SingleCircleThroughAllCut,ParameterKey.HoleDiameter,4,NativeAccessor.SingleCircleRadius),
            Feature("hole_b",3,NativeSubtype.SingleCircleThroughAllCut,ParameterKey.HoleDiameter,10,NativeAccessor.SingleCircleRadius),
            Feature("hole_c",4,NativeSubtype.SingleCircleThroughAllCut,ParameterKey.HoleDiameter,6,NativeAccessor.SingleCircleRadius),
            Feature("pattern",5,NativeSubtype.SingleDirectionLinearPattern,ParameterKey.PatternCount,2,NativeAccessor.LinearPatternDirection1Count)};
        var deps = features.Skip(1).Select(f=>new ObservedDependency("base",f.SemanticId,NativeDependencyKind.Host,evidence)).Append(new("hole_a","pattern",NativeDependencyKind.PatternSeed,evidence)).ToArray();
        return new("0.3",new("0.3",ModelOrigin.External,selection,true,null,features,deps,Array.Empty<IdentityRemapping>()),
            new("base",-50,-30,50,30,0,10,new[]{new ExternalHoleContract("hole_a",-30,-15,4),new ExternalHoleContract("hole_b",-20,15,10),new ExternalHoleContract("hole_c",20,15,6)},new[]{new ExternalPatternContract("pattern","hole_a",2,20,1,0)}));
    }
}
