using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using Environment=System.Environment;

internal static partial class Program
{
    private static void ExternalFormal(FreezeRecord freeze,Slot slot,SolidWorksConnection connection,Lifecycle lifecycle,Action<string,object> journal)
    {
        var protocol=freeze.FormalExternal.Single(p=>p.Initial.Id==slot.Task);
        ValidateExternalProtocol(protocol);var root=Path.Combine(Output,"packages",slot.Task);var original=protocol.Initial;
        if(slot.Mode=="external-cold")
        {
            var priorSlot=freeze.Slots.Single(s=>s.Id==slot.Pair);var prior=Read<JsonElement>(Path.Combine(Output,"slots",priorSlot.Id,"native-result.json"));
            Check(prior.GetProperty("status").GetString()=="PASS"&&prior.GetProperty("controller").GetInt32()!=Environment.ProcessId&&
                prior.GetProperty("package").GetString()==root&&ManagedRevisionStore.Hash(Path.Combine(root,"working/CADHarnessManagedPart.SLDPRT"))==prior.GetProperty("nativeSha").GetString()&&
                ManagedRevisionStore.Hash(Path.Combine(root,"current.json"))==prior.GetProperty("manifestSha").GetString(),"External cold prior authority/controller mismatch");
            var open=ExternalPartSession.Open(connection,root,Path.Combine(root,"working/CADHarnessManagedPart.SLDPRT"),lifecycle);
            journal("external-cold-admission",new{open.Status,open.FailureCode,open.Message});Check(open.Session is not null,"External cold admission failed");using var cold=open.Session!;
            NativeEditOracle.Read((IModelDoc2)connection.Application.GetOpenDocumentByName(cold.Store.WorkingPath),protocol.Expected[^1],m=>journal("external-cold-oracle",m));
            Check(cold.Store.ReadCurrent().Manifest.Revision==protocol.Actions.Length&&!File.Exists(cold.Store.RecoveryPath),"External cold authority mismatch");
            VerifyIdentity(original.Source);return;
        }
        var armed=false;
        var result=ExternalPartSession.CreateCopy(connection,root,original.Source.Path,original.Configuration,lifecycle,editFault:p=>
            {if(armed&&p==ExternalEditFault.FirstEdit)throw new StateException("M20_CONTROLLED_FIRST_EDIT_FAILURE","Real first Setter completed before failure");});
        journal("external-admission",new{result.Status,result.FailureCode,result.Message,result.Observation});Check(result.Session is not null,"External task unqualified");using var current=result.Session!;
        current.EvidenceSink=t=>journal("external-api",t);
        void Validate(OracleInput spec)
        {
            NativeEditOracle.Read((IModelDoc2)connection.Application.GetOpenDocumentByName(current.Store.WorkingPath),spec,m=>journal("external-oracle",m));
            var revision=current.Store.ReadCurrent();Check(revision.Manifest.Revision==revision.State.Revision&&ManagedRevisionStore.Hash(current.Store.WorkingPath)==revision.Manifest.NativePart.Sha256&&!File.Exists(current.Store.RecoveryPath),"External Native/State/Manifest disagreement");
        }
        ScalarEdit Edit(ExternalValue v)
        {
            var f=current.CurrentExternal.Observation.Features.Single(f=>f.NativeReference?.Base64==original.References[v.Label]);
            Check(Math.Abs(f.Parameters.Single(p=>p.Key==v.Parameter).Value-v.OldValue)<1e-8,"Frozen external old value drift");
            return new(f.SemanticId,v.Parameter,ContractValidation.Unit(v.Parameter),v.OldValue,v.NewValue);
        }
        Validate(protocol.Expected[0]);
        for(var i=0;i<protocol.Actions.Length;i++)
        {
            var action=protocol.Actions[i];var selection=current.CurrentExternal.Observation.Selection;var edits=action.Edits.Select(Edit).ToArray();
            var edit=action.Kind=="scalar"?current.Edit(new ScalarEditRequest("0.3",RequestMode.ExternalScalarEdit,ModelOrigin.External,selection,edits.Single())):
                current.Edit(new EditSetRequest("0.3",RequestMode.EditSet,ModelOrigin.External,selection,edits));
            journal("external-frozen-action",new{index=i,action,edit});
            Check(edit.Transaction.Succeeded&&edit.Transaction.StateCommitted&&edit.Transaction.RebuildSucceeded&&edit.Checkpoints==1&&edit.Transaction.Revision==selection.ExpectedRevision+1,"Frozen scalar/batch commit failed");
            Validate(protocol.Expected[i+1]);
        }
        if(protocol.FirstEditFault)
        {
            var spec=protocol.Expected[^1];var pointer=ManagedRevisionStore.Hash(current.Store.PointerPath);var rev=current.CurrentExternal.Observation.Selection.ExpectedRevision;
            var b=spec.Holes.Single(h=>h.Label=="hole_b").Diameter;var c=spec.Holes.Single(h=>h.Label=="hole_c").Diameter;
            armed=true;var failed=current.Edit(new EditSetRequest("0.3",RequestMode.EditSet,ModelOrigin.External,current.CurrentExternal.Observation.Selection,
                new[]{Edit(new("hole_b",ParameterKey.HoleDiameter,b,b+.25)),Edit(new("hole_c",ParameterKey.HoleDiameter,c,c+.25))}));armed=false;
            journal("external-first-edit-fault",failed);
            Check(!failed.Transaction.Succeeded&&failed.Transaction.RollbackSucceeded&&!failed.Transaction.StateCommitted&&
                current.CurrentExternal.Observation.Selection.ExpectedRevision==rev&&ManagedRevisionStore.Hash(current.Store.PointerPath)==pointer,"First edit not restored atomically");Validate(spec);
        }
        Write(Path.Combine(root,"expected.json"),protocol.Expected[^1]);Write(Path.Combine(root,"controller.json"),new{controller=Environment.ProcessId,revision=current.Store.ReadCurrent().Manifest.Revision});
        VerifyIdentity(original.Source);
    }
}
