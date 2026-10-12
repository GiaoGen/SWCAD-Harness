using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.Planning;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

internal static partial class Program
{
    private static OracleInput Changed(OracleInput s,string label,ParameterKey key,double value)=>key switch
    {
        ParameterKey.ExtrusionDepth=>s with{Depth=value},ParameterKey.PatternCount=>s with{Pattern=s.Pattern with{Count=(int)value}},
        ParameterKey.PatternSpacing=>s with{Pattern=s.Pattern with{Spacing=value}},
        ParameterKey.HoleDiameter=>s with{Holes=s.Holes.Select(h=>h.Label==label?h with{Diameter=value}:h).ToArray()},
        _=>throw new InvalidOperationException("Unscheduled parameter")
    };
    private static void External(FreezeRecord freeze,Slot slot,SolidWorksConnection connection,Lifecycle lifecycle,Action<string,object> journal)
    {
        var id=slot.Task;var root=freeze.CompletedExternal?.Task==id?freeze.CompletedExternal.Package:Path.Combine(Output,"packages",id);
        if(slot.Mode=="readonly")
        {
            using var registry=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"artifacts/fixture-factory/audits/final-v1/fixture-registry.json")));
            var target=id=="equation"?"dev_equation_driver":"dev_unknown_descendant";
            var source=registry.RootElement.GetProperty("fixtures").EnumerateArray().Single(r=>r.GetProperty("fixtureId").GetString()==target).GetProperty("nativePart").GetProperty("path").GetString()!;
            var result=ExternalPartSession.CreateCopy(connection,root,source,lifecycle:lifecycle);
            journal("readonly-admission",new{result.Status,result.FailureCode,result.Message,result.Observation,source,sha256=ManagedRevisionStore.Hash(source),provenance="Factory development; not independently authored",setterCount=0,checkpointCount=0});
            using var session=result.Session;
            Check(session is null||session.CurrentExternal.Observation.Features.All(f=>f.EditSupport!=EditSupport.Editable),"Unsafe driver/downstream advertised editable");
            Check(result.Observation is not null,"Unsupported history lost its inspectable inventory");return;
        }
        var original=freeze.External.Single(i=>i.Id==id);var spec=original;
        if(slot.Mode=="external-cold")
        {
            spec=Read<OracleInput>(Path.Combine(root,"expected.json"));
            var result=ExternalPartSession.Open(connection,root,Path.Combine(root,"working","CADHarnessManagedPart.SLDPRT"),lifecycle);
            journal("external-cold-admission",new{result.Status,result.FailureCode,result.Message});
            Check(result.Session is not null,"External cold reopen failed");using var cold=result.Session!;
            NativeEditOracle.Read((IModelDoc2)connection.Application.GetOpenDocumentByName(cold.Store.WorkingPath),spec,m=>journal("external-cold-oracle",m));
            Check(!File.Exists(cold.Store.RecoveryPath),"Unresolved recovery after external cold reopen");
            journal("external-cold-state",new{revision=cold.CurrentExternal.Observation.Selection.ExpectedRevision,manifest=cold.Store.ReadCurrent().Manifest,priorController=Read<JsonElement>(Path.Combine(root,"controller.json")).GetProperty("controller").GetInt32(),controller=Environment.ProcessId});return;
        }
        var armed=false;
        var opened=ExternalPartSession.CreateCopy(connection,root,original.Source.Path,original.Configuration,lifecycle,editFault:p=>
        {if(armed&&p==ExternalEditFault.FirstEdit)throw new StateException("M20_CONTROLLED_FIRST_EDIT_FAILURE","First native Setter completed before controlled failure");});
        journal("external-admission",new{opened.Status,opened.FailureCode,opened.Message,opened.Observation});
        Check(opened.Session is not null,"External source unexpectedly unqualified");using var current=opened.Session!;
        current.EvidenceSink=t=>journal("external-api",t);
        IModelDoc2 Doc()=>(IModelDoc2)connection.Application.GetOpenDocumentByName(current.Store.WorkingPath);
        void Validate(){NativeEditOracle.Read(Doc(),spec,m=>journal("external-oracle",m));var revision=current.Store.ReadCurrent();Check(revision.Manifest.Revision==revision.State.Revision&&!File.Exists(current.Store.RecoveryPath)&&ManagedRevisionStore.Hash(current.Store.WorkingPath)==revision.Manifest.NativePart.Sha256,"External native/state/manifest differs");}
        ScalarEdit Edit(string label,ParameterKey parameter,double value)
        {
            var f=current.CurrentExternal.Observation.Features.Single(f=>f.NativeReference?.Base64==original.References[label]);
            return new(f.SemanticId,parameter,ContractValidation.Unit(parameter),f.Parameters.Single(p=>p.Key==parameter).Value,value);
        }
        void Scalar(string label,ParameterKey key,double value)
        {
            var old=current.CurrentExternal.Observation.Selection.ExpectedRevision;
            var result=current.Edit(new ScalarEditRequest("0.3",RequestMode.ExternalScalarEdit,ModelOrigin.External,current.CurrentExternal.Observation.Selection,Edit(label,key,value)));
            journal("external-scalar",new{label,key,value,result});Check(result.Transaction.Succeeded&&result.Transaction.StateCommitted&&result.Transaction.Revision==old+1&&result.Checkpoints==1,"External scalar transaction failed");
            spec=Changed(spec,label,key,value);Validate();
        }
        Validate();Scalar("host",ParameterKey.ExtrusionDepth,original.Depth+1.5);Scalar("hole_b",ParameterKey.HoleDiameter,original.Holes.Single(h=>h.Label=="hole_b").Diameter+.5);
        Scalar("pattern",ParameterKey.PatternCount,id=="manual"?3:4);Scalar("pattern",ParameterKey.PatternSpacing,id=="manual"?24.5:22);
        var selection=current.CurrentExternal.Observation.Selection;var revisionBefore=selection.ExpectedRevision;
        var b=spec.Holes.Single(h=>h.Label=="hole_b").Diameter+.75;var c=spec.Holes.Single(h=>h.Label=="hole_c").Diameter+1.25;
        var batch=current.Edit(new EditSetRequest("0.3",RequestMode.EditSet,ModelOrigin.External,selection,new[]{Edit("hole_b",ParameterKey.HoleDiameter,b),Edit("hole_c",ParameterKey.HoleDiameter,c)}));
        journal("external-batch",batch);Check(batch.Transaction.Succeeded&&batch.Checkpoints==1&&batch.Transaction.Revision==revisionBefore+1,"Batch failed aggregate checkpoint/revision");
        spec=Changed(Changed(spec,"hole_b",ParameterKey.HoleDiameter,b),"hole_c",ParameterKey.HoleDiameter,c);Validate();
        var before=current.Store.ReadCurrent();var pointer=ManagedRevisionStore.Hash(current.Store.PointerPath);armed=true;
        var failure=current.Edit(new EditSetRequest("0.3",RequestMode.EditSet,ModelOrigin.External,current.CurrentExternal.Observation.Selection,new[]{Edit("hole_b",ParameterKey.HoleDiameter,b+.25),Edit("hole_c",ParameterKey.HoleDiameter,c+.25)}));armed=false;
        journal("external-first-edit-fault",failure);
        Check(!failure.Transaction.Succeeded&&failure.Transaction.RollbackSucceeded&&!failure.Transaction.StateCommitted&&current.CurrentExternal.Observation.Selection.ExpectedRevision==before.Manifest.Revision&&pointer==ManagedRevisionStore.Hash(current.Store.PointerPath),"First-edit fault not atomically restored");Validate();
        var invalid=Edit("hole_b",ParameterKey.HoleDiameter,b+.5) with{ExpectedOldValue=b+100};
        var rejected=current.Edit(new ScalarEditRequest("0.3",RequestMode.ExternalScalarEdit,ModelOrigin.External,current.CurrentExternal.Observation.Selection,invalid));
        journal("external-precheck-refusal",rejected);Check(!rejected.Transaction.Succeeded&&rejected.Checkpoints==0,"Invalid old value reached checkpoint");Validate();
        Write(Path.Combine(root,"expected.json"),spec);Write(Path.Combine(root,"controller.json"),new{controller=Environment.ProcessId,revision=current.Store.ReadCurrent().Manifest.Revision});
        Check(Identity(original.Source.Path)==original.Source,"External original changed");
    }
    private static void G2(Slot slot,SolidWorksConnection connection,Lifecycle lifecycle,NativeTestBudget budget,Action<string,object> journal)
    {
        var root=Path.Combine(Output,"packages/G2");
        if(slot.Mode=="g2-cold")
        {
            var open=ManagedPartSession.Open(connection,root,Path.Combine(root,"working/CADHarnessManagedPart.SLDPRT"),lifecycle:lifecycle);
            journal("G2-cold-open",new{open.Status,open.FailureCode,open.Message});Check(open.Session is not null,"G2 cold restore failed");using var session=open.Session!;
            G2Oracle(session.Context,11.25,session.Store.Load(),journal);
            Check(session.PlanningRuntime().Capabilities.ParameterEdits.Any(e=>e.Target=="plate"&&e.Parameter==EditableParameter.ExtrusionDepth),"G2 restored edit binding missing");return;
        }
        var source=Path.Combine(Output,"G2-original");Directory.CreateDirectory(source);var part=Path.Combine(source,"G2.SLDPRT");
        var stateFile=Path.Combine(source,"state.json");var programFile=Path.Combine(source,"program.json");
        var parsed=new CadProgramJson().Parse(File.ReadAllText(Path.Combine(Root,"tests/CadHarness.Relations.Tests/Fixtures/rect2x2.json")));Check(parsed.IsValid,"G2 frozen construction invalid");
        {
            var scope=new TestPartScope(connection.Application,budget);
            try
            {
                var context=scope.Create(connection,connection.ResolvePartTemplate(Environment.GetEnvironmentVariable("CAD_HARNESS_PART_TEMPLATE")));
                var creation=new RelationBackend().Create(context,parsed.Program!);journal("G2-construction",creation);Check(creation.Succeeded,"G2 build failed");
                int errors=0,warnings=0;Check(context.Document.Extension.SaveAs(part,0,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,null!,ref errors,ref warnings)&&errors==0&&warnings==0,"G2 source save failed");scope.RefreshOwnedTitle();
                var state=context.CaptureBindingState();new AtomicStateStore(stateFile).Commit(state);
                File.WriteAllText(programFile,new CadProgramJson().Serialize(new DesignRelationEngine().Solve(parsed.Program!).Program));G2Oracle(context,8,state,journal);
            }
            finally{scope.Cleanup();Check(scope.Closed&&scope.OriginalActiveRestored&&scope.CleanupError is null,"G2 create cleanup failed");journal("G2-creation-ledger",budget.Snapshot);}
        }
        var hashes=new[]{Identity(part),Identity(stateFile),Identity(programFile)};Write(Path.Combine(source,"originals.json"),hashes);
        var migrated=ManagedPartSession.MigrateCopy(connection,root,part,stateFile,programFile,lifecycle);
        journal("G2-migration",new{migrated.Status,migrated.FailureCode,migrated.Message});Check(migrated.Session is not null,"G2 migration failed");using var active=migrated.Session!;
        var old=active.Store.Load();
        var operation=new OperationNode("depth_edit",OperationKind.EditParameter,null,new[]{new OperationInput("target",new[]{new SemanticReference("plate",SemanticType.FeatureRef)})},
            new Dictionary<string,OperationParameter>{["parameter"]=new ParameterNameParameter(EditableParameter.ExtrusionDepth),["value"]=new EditValueParameter(new LengthParameter(11.25))});
        var result=active.Edit(operation,old.Revision);journal("G2-edit",result);Check(result.Succeeded&&result.StateCommitted&&result.Revision==old.Revision+1,"G2 edit failed");
        G2Oracle(active.Context,11.25,active.Store.Load(),journal);Check(hashes.All(f=>Identity(f.Path)==f),"G2 originals changed");
    }
    private static void G2Oracle(SolidWorksExecutionContext context,double depth,CadState state,Action<string,object> journal)
    {
        var body=NativeEditOracle.Items<IBody2>(((IPartDoc)context.Document).GetBodies2(0,false)).Single();
        var volume=((double[])body.GetMassProperties(1))[3]*1e9;var expected=(6000-36*Math.PI)*depth;
        var cylinders=NativeEditOracle.Items<IFace2>(body.GetFaces()).Where(f=>((ISurface)f.GetSurface()).IsCylinder()).Select(f=>new{p=(double[])((ISurface)f.GetSurface()).CylinderParams,
            levels=NativeEditOracle.Items<IEdge>(f.GetEdges()).Select(e=>(ICurve)e.GetCurve()).Where(c=>c.IsCircle()).Select(c=>((double[])c.CircleParams)[2]*1000).OrderBy(z=>z).ToArray()}).ToArray();
        var expectedCenters=new[]{(-30d,-15d),(-30d,15d),(30d,-15d),(30d,15d)};
        var passed=state.Relations.Count==5&&state.Dependencies.Count>0&&Math.Abs(volume-expected)<.01&&cylinders.Length==4&&expectedCenters.All(e=>cylinders.Count(a=>Math.Abs(a.p[0]*1000-e.Item1)<.01&&Math.Abs(a.p[1]*1000-e.Item2)<.01&&Math.Abs(a.p[6]*1000-3)<.01&&a.levels.Length==2&&Math.Abs(a.levels[0])<.01&&Math.Abs(a.levels[1]-depth)<.01)==1);
        journal("G2-independent-oracle",new{passed,volume,expected,difference=volume-expected,cylinders,state.Revision,relations=state.Relations.Count,dependencies=state.Dependencies.Count});Check(passed,"G2 geometry/relation readback failed");
    }
}
