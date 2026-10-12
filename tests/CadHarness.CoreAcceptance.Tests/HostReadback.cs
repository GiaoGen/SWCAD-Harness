using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using SolidWorks.Interop.sldworks;

internal static partial class Program
{
    private static int VerifyExistingPackage(string taskId,string package)
    {
        package=OwnedEvidencePath(package);var task=Definitions().Single(t=>t.Id==taskId);var step=0;
        void Log(string phase,object data)=>Write(Path.Combine(Output,(++step).ToString("D3")+"-"+phase+".json"),new{utc=DateTime.UtcNow,controller=System.Environment.ProcessId,data});
        using var budget=new NativeTestBudget(Ledger,"T5",int.MaxValue);using var connection=SolidWorksConnection.Connect(false);
        var before=Documents(connection.Application);var resource=NativeResourceGuard.Inspect(connection.Application,budget);Check(resource.Responding&&resource.GdiCount<7000&&budget.Snapshot.OpenTestOwnedTitles.Count==0,"Probe resource refusal");
        var bytes=Identity(Path.Combine(package,"working/CADHarnessManagedPart.SLDPRT"));var pointer=Identity(Path.Combine(package,"current.json"));
        Log("freeze",new{bytes,pointer,source=Directory.GetFiles(Path.Combine(Root,"tests/CadHarness.CoreAcceptance.Tests"),"*.cs").Select(Identity).ToArray(),binaries=Directory.GetFiles(AppContext.BaseDirectory,"*.dll").Select(Identity).ToArray(),resource,budget=budget.Snapshot});
        GenericPartSession? session=null;
        try
        {
            var opened=GenericPartSession.Open(connection,package,bytes.Path,lifecycle:new Lifecycle(budget,Log));Check(opened.Session is not null,"Probe public restoration failed");session=opened.Session!;
            ValidatePhase(session,task,2,Log);var state=session.CurrentGeneric.State;
            var features=state.Features.ToDictionary(f=>f.SemanticId,f=>(IFeature)session.Context.Document.Extension.GetObjectByPersistReference3(Convert.FromBase64String(f.Reference.Base64),out _));
            string Ref(IFeature feature)=>Convert.ToBase64String((byte[])session.Context.Document.Extension.GetPersistReference3(feature));
            IFeature[] Parents(IFeature f){var parents=Items<IFeature>(f.GetParents());Check(parents.Length==f.IGetParentCount(),"Independent native parent inventory incomplete");return parents;}
            bool Reach(IFeature source,IFeature target)
            {
                var wanted=Ref(target);var pending=new Stack<IFeature>(Parents(source));var seen=new HashSet<string>();
                while(pending.Count>0){var current=pending.Pop();var reference=Ref(current);if(reference==wanted)return true;if(!seen.Add(reference))continue;Check(seen.Count<=128,"Bounded parent graph exceeded");foreach(var p in Parents(current))pending.Push(p);}return false;
            }
            foreach(var op in state.Program.Operations.Where(o=>o.Profile is not null||o.EdgeTreatment is not null))
            {
                var feature=features[op.SemanticId];var parents=Parents(feature);var children=new List<IFeature>();var child=feature.GetFirstSubFeature() as IFeature;
                while(child is not null){Check(children.Count<128,"Subfeature inventory exceeded");children.Add(child);child=child.GetNextSubFeature() as IFeature;}
                bool consumed=true,host=true;
                if(op.Profile is not null){var sketch=features[op.Profile.SemanticId[..^8]];consumed=parents.Concat(children).Any(p=>Ref(p)==Ref(sketch));}
                if(op.Kind==ConstructionKind.CreateAdditiveBoss)host=Reach(feature,features[op.HostBody!.SemanticId[..^5]]);
                if(op.EdgeTreatment is not null)host=Reach(feature,features[op.EdgeTreatment.HostFeature.SemanticId]);
                Log("independent-ownership",new{op.SemanticId,reference=Ref(feature),nativeType=feature.GetTypeName2(),parents=parents.Select(p=>new{reference=Ref(p),nativeType=p.GetTypeName2()}),children=children.Select(Ref),consumed,host});
                Check(consumed&&host,"Independent profile/host native relationship failed");
            }
            Check(Identity(bytes.Path)==bytes&&Identity(pointer.Path)==pointer&&!File.Exists(session.Store.RecoveryPath),"Read-only probe changed authority/native");Log("result",new{passed=true,state.Revision,scalarValue=ResolveEditTarget(session.CurrentGeneric).Value,noSetter=true,noCheckpoint=true,package});
        }
        finally
        {
            session?.Dispose();var after=NativeResourceGuard.Inspect(connection.Application,budget);Log("cleanup",new{after,budget=budget.Snapshot,session?.OriginalActiveRestored});Check(budget.Snapshot.OpenTestOwnedTitles.Count==0&&after.Responding&&after.GdiCount<7000&&System.Text.Json.JsonSerializer.Serialize(before,Json)==System.Text.Json.JsonSerializer.Serialize(Documents(connection.Application),Json),"Probe cleanup failed");
        }
        return 0;
    }
}
