using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.SolidWorks;
using CadHarness.SolidWorks.Tests;
using CadHarness.State;
using CadHarness.State.V03;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment=System.Environment;

[assembly: SupportedOSPlatform("windows")]
internal sealed record FrozenFile(string Path,string Sha256,long Bytes);
internal sealed record OracleHole(string Label,double X,double Y,double Diameter);
internal sealed record OraclePattern(string Label,string Seed,int Count,double Spacing);
internal sealed record OracleInput(string Id,string Provenance,FrozenFile Source,string Configuration,double Width,double Height,double Depth,
    IReadOnlyList<OracleHole> Holes,OraclePattern Pattern,IReadOnlyDictionary<string,string> References,string? FactoryReady,
    double BlindDepth,double BlindX,double BlindY,double BlindDiameter,int EquationCount);
internal sealed record Freeze(FrozenFile[] Files,OracleInput Source,string[] Schedule,int MaximumOpens,int MaximumParts);
internal static partial class Program
{
    internal static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,WriteIndented=true,Converters={new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower,false)}};
    internal static string Root="",Output="";
    private static string Ledger=>Path.Combine(Root,"artifacts/milestone18/native-budget.json");
    internal static void Check(bool pass,string message){if(!pass)throw new InvalidOperationException(message);}
    internal static void Write(string path,object data)
    {Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read);JsonSerializer.Serialize(f,data,Json);f.Flush(true);}
    internal static T Read<T>(string path)=>JsonSerializer.Deserialize<T>(File.ReadAllText(path),Json)!;
    internal static FrozenFile Identity(string path)=>new(Path.GetFullPath(path),ManagedRevisionStore.Hash(path),new FileInfo(path).Length);
    private static void Verify(FrozenFile file)=>Check(Identity(file.Path)==file,"Frozen identity drift: "+file.Path);
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Root=Path.GetFullPath(args[0]);Check(ContractValidation.Id(args[2]),"Invalid run ID");Output=Path.Combine(Root,"artifacts/milestone18",args[2]);
            if(args[1]=="--pure")return Pure();
            if(args[1]=="--prepare")return Prepare();
            var freeze=Read<Freeze>(Path.Combine(Output,"freeze.json"));Verify(Read<FrozenFile>(Path.Combine(Output,"freeze-seal.json")));
            foreach(var f in freeze.Files)Verify(f);
            Check(args[1] is "--native" or "--cold","Unknown phase");
            return Native(freeze,args[1]=="--cold");
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static OracleInput Source()
    {
        using var old=JsonDocument.Parse(File.ReadAllText(Path.Combine(Root,"artifacts/milestone20/acceptance_t5b_v2/freeze.json")));
        return old.RootElement.GetProperty("external").EnumerateArray().Single(f=>f.GetProperty("id").GetString()=="core").Deserialize<OracleInput>(Json)!;
    }
    private static int Prepare()
    {
        Check(!Directory.Exists(Output),"Never overwrite native evidence");var source=Source();Verify(source.Source);
        var files=new[]{"src","tests/CadHarness.QueryEditing.Tests"}.SelectMany(d=>Directory.GetFiles(Path.Combine(Root,d),"*.cs",SearchOption.AllDirectories))
            .Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(s=>s is "bin" or "obj"))
            .Concat(new[]{Path.Combine(Root,"tests/CadHarness.ExternalEditing.Tests/NativeEditOracle.cs"),Path.Combine(Root,"tests/CadHarness.SolidWorks.Tests/NativeTestBudget.cs"),
                Path.Combine(Root,"tests/CadHarness.SolidWorks.Tests/NativeResourceGuard.cs"),Path.Combine(Root,"tests/CadHarness.QueryEditing.Tests/CadHarness.QueryEditing.Tests.csproj"),
                Path.Combine(Root,"scripts/test-milestone18.ps1"),Path.Combine(Root,"artifacts/milestone20/acceptance_t5b_v2/freeze.json")})
            .Concat(Directory.GetFiles(Path.GetDirectoryName(typeof(Program).Assembly.Location)!)).Select(Identity)
            .Append(source.Source).Append(Identity(Path.Combine(Root,"SWCAD_Harness_v0.3_PRD.md"))).ToArray();
        var previous=Path.Combine(Root,"artifacts/milestone18/acceptance_v1/native-budget.json");
        if(!File.Exists(Ledger)&&File.Exists(previous))
        {
            var snapshot=Read<BudgetSnapshot>(previous);Check(snapshot.OpenTestOwnedTitles.Count==0,"Unresolved prior ownership");
            File.Copy(previous,Ledger,false);
            Write(Path.Combine(Output,"ledger-carry-forward.json"),new{previous=Identity(previous),initialCumulative=Identity(Ledger),snapshot,basis="Exact byte copy before new reservations; previous failed ledger retained"});
        }
        Write(Path.Combine(Output,"freeze.json"),new Freeze(files,source,new[]{"native: ambiguity/empty/value-membership drift refusal; independent batch; stale/replay rejection; dependent seed+spacing batch; first-setter fault rollback","cold: fresh controller complete Oracle, revision/native/state and cleanup"},6,0));
        Write(Path.Combine(Output,"freeze-seal.json"),Identity(Path.Combine(Output,"freeze.json")));
        Write(Path.Combine(Output,"execution-boundary.json"),new{milestone=18,newParts=0,plannedCumulativeOpens=6,
            authorization="授权无限制，但是保证不陷入死循环，在正确的时机及时退出",
            basis="Human-authorized increase beyond original PRD 5; retain first failed open, then only 5 scheduled remaining opens; no automatic retries",
            scope="No M19, no provider, no new native subtype or transaction coordinator"});
        return 0;
    }
    internal static ExternalTargetQuery Holes(IEnumerable<double> values,QueryCardinality cardinality=QueryCardinality.IntentionalBatch)=>
        new(NativeSubtype.SingleCircleThroughAllCut,null,ParameterKey.HoleDiameter,ScalarUnit.Millimeter,new(null,null,values.ToArray()),null,null,EditSupport.Editable,cardinality);
    internal static QueryEditSetRequest Query(ExternalEditState state,QueryParameterChange first,QueryParameterChange? second=null)=>
        new("0.3",1,state.Observation.Selection,second is null?new[]{first}:new[]{first,second});
    private static QueryEditApproval Approve(ExternalPartSession session,QueryEditSetRequest request,Action<string,object> log)
    {
        var result=session.PreviewEditSet(request);log("preview",new{result.Outcome,result.FailureCode,result.Message,result.Matches,preview=result.Preview?.Snapshot(),sha=result.Preview?.Sha256});
        Check(result.Preview is not null,"No executable preview");var data=result.Preview!.Snapshot();
        return result.Preview.Approve(result.Preview.Sha256,data.Expression.Selection.ExpectedRevision,data.OrderedEdits.Select(e=>e.Target).ToArray());
    }
    private static int Native(Freeze freeze,bool cold)
    {
        var phase=cold?"cold":"native";Write(Path.Combine(Output,phase+"-started.json"),new{controller=Environment.ProcessId,utc=DateTime.UtcNow});
        using var budget=new NativeTestBudget(Ledger,"M18",1);
        Check(budget.Snapshot.CreationAttempts==0&&budget.Snapshot.OpenTestOwnedTitles.Count==0,"Unresolved ownership/creation");
        using var connection=SolidWorksConnection.Connect(false);var app=connection.Application;
        var before=Documents(app);var resource=NativeResourceGuard.Inspect(app,budget);Check(before.Length==0&&resource.Responding&&resource.GdiCount<7000,"Resource/document preflight refusal; never close or adopt an existing document");
        var events=0;var watch=System.Diagnostics.Stopwatch.StartNew();
        void Log(string stage,object data)=>Write(Path.Combine(Output,"steps",phase,(++events).ToString("D4")+"-"+stage+".json"),new{utc=DateTime.UtcNow,controller=Environment.ProcessId,elapsedMs=watch.Elapsed.TotalMilliseconds,stage,data,budget=budget.Snapshot});
        var lifecycle=new Lifecycle(budget,Log,freeze.MaximumOpens);var root=Path.Combine(Output,"package");ExternalPartSession? session=null;string? failure=null;
        var spec=freeze.Source;var armed=false;var setters=0;
        try
        {
            if(cold)
            {
                var previous=Read<JsonElement>(Path.Combine(Output,"native-result.json"));Check(previous.GetProperty("passed").GetBoolean()&&previous.GetProperty("controller").GetInt32()!=Environment.ProcessId,"Independent successful controller required");
                Check(ManagedRevisionStore.Hash(Path.Combine(root,"current.json"))==previous.GetProperty("pointerSha").GetString()&&ManagedRevisionStore.Hash(Path.Combine(root,"working/CADHarnessManagedPart.SLDPRT"))==previous.GetProperty("nativeSha").GetString(),"Cold prior authority drift");
                spec=Read<OracleInput>(Path.Combine(Output,"expected.json"));
            }
            var opened=cold?ExternalPartSession.Open(connection,root,Path.Combine(root,"working/CADHarnessManagedPart.SLDPRT"),lifecycle):
                ExternalPartSession.CreateCopy(connection,root,spec.Source.Path,spec.Configuration,lifecycle,editFault:p=>{if(armed&&p==ExternalEditFault.FirstEdit)throw new StateException("M18_FIRST_EDIT_FAILURE","First real Setter completed, second not executed.");});
            Log("admission",new{opened.Status,opened.FailureCode,opened.Message});Check(opened.Session is not null,"External intake failed");session=opened.Session!;
            session.EvidenceSink=e=>{Log("native-api",e);if(e.Phase=="setter-start")setters++;};
            IModelDoc2 Doc()=>(IModelDoc2)app.GetOpenDocumentByName(session.Store.WorkingPath);
            void Oracle()
            {
                NativeEditOracle.Read(Doc(),spec,m=>Log("oracle",m));var current=session.Store.ReadCurrent();
                Check(current.Manifest.Revision==session.CurrentExternal.Observation.Selection.ExpectedRevision&&current.State.Revision==current.Manifest.Revision&&
                    ManagedRevisionStore.Hash(session.Store.WorkingPath)==current.Manifest.NativePart.Sha256&&!File.Exists(session.Store.RecoveryPath),"Native/state/manifest inconsistency");
            }
            Oracle();
            if(!cold)
            {
                void Refusal(string label,Action operation)
                {
                    var calls=setters;var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);var file=ManagedRevisionStore.Hash(session.Store.WorkingPath);
                    string? code=null;try{operation();}catch(Exception e)when(e is StateException or ContractException){code=e is ICadFailure f?f.Code:((ContractException)e).Code;}
                    Log("refusal",new{label,code,productionSetters=setters-calls,checkpointCreated=File.Exists(session.Store.RecoveryPath)});
                    Check(code is not null&&calls==setters&&pointer==ManagedRevisionStore.Hash(session.Store.PointerPath)&&file==ManagedRevisionStore.Hash(session.Store.WorkingPath)&&!File.Exists(session.Store.RecoveryPath),"Refusal crossed mutation boundary");
                }
                var initial=session.CurrentExternal;
                var allDiameters=spec.Holes.Select(h=>h.Diameter).ToArray();
                foreach(var kind in new[]{"ambiguous","empty"})
                {
                    var query=Query(initial,new(Holes(kind=="empty"?new[]{99d}:allDiameters,QueryCardinality.ExactlyOne),11));
                    var result=session.PreviewEditSet(query);Log("selection-refusal",new{kind,result.Outcome,result.FailureCode,result.Matches,setters,checkpointCreated=File.Exists(session.Store.RecoveryPath)});
                    Check(result.Preview is null&&result.Outcome==(kind=="empty"?QueryOutcome.Empty:QueryOutcome.Ambiguous)&&setters==0&&!File.Exists(session.Store.RecoveryPath),"Ambiguity/empty selection mutated");
                }
                var independent=Query(initial,new(Holes(new[]{spec.Holes.Single(h=>h.Label=="hole_b").Diameter,spec.Holes.Single(h=>h.Label=="hole_c").Diameter}),11));
                var drift=Approve(session,independent,Log);
                var hole=NativeEditOracle.Resolve(Doc(),spec.References["hole_b"]);var sketch=NativeEditOracle.Items<IFeature>(hole.GetParents()).Single(f=>f.GetTypeName2()=="ProfileFeature");
                var display=(IDisplayDimension)sketch.GetFirstDisplayDimension();var dimension=(IDimension)display.GetDimension2(0);var old=dimension.SystemValue;
                Check(display.Type2==(int)swDimensionType_e.swDiameterDimension,"Drift probe must use the verified diameter driver");
                try
                {
                    var status=dimension.SetSystemValue3(.009,(int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration,null!);Check(status==0&&session.RebuildNative(),"Owned drift probe failed");
                    var measured=NativeEditOracle.Value(Doc(),spec,"hole_b",ParameterKey.HoleDiameter);Log("owned-membership-drift",new{before=old*1000,measured,originalQueryValues=new[]{6d,10d},noLongerMatches=measured!=6&&measured!=10,probeSetterStatus=status});
                    Check(Math.Abs(measured-9)<1e-6,"Actual native membership did not change");Refusal("native-membership/value-drift",()=>session.Edit(drift));
                }
                finally{Check(dimension.SetSystemValue3(old,(int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration,null!)==0&&session.RebuildNative(),"Restore owned drift probe failed");}
                Oracle();
                var stale=Approve(session,independent,Log);var valid=Approve(session,independent,Log);
                var first=session.Edit(valid);Log("independent-batch",first);Check(first.Transaction.Succeeded&&first.Transaction.StateCommitted&&first.Transaction.Revision==1&&first.Checkpoints==1&&first.PartialNativeSteps.Count==2,"Independent batch aggregate semantics failed");
                spec=spec with{Holes=spec.Holes.Select(h=>h.Label is "hole_b" or "hole_c"?h with{Diameter=11}:h).ToArray()};Oracle();
                Refusal("approval-replay",()=>session.Edit(valid));Refusal("stale-revision/fingerprint",()=>session.Edit(stale));
                var current=session.CurrentExternal;var pattern=current.Geometry.Patterns.Single();
                var seedQuery=new ExternalTargetQuery(NativeSubtype.SingleCircleThroughAllCut,null,ParameterKey.HoleDiameter,ScalarUnit.Millimeter,null,current.Geometry.Root,new(QueryRelationKind.PatternSeedOf,pattern.Target),EditSupport.Editable,QueryCardinality.ExactlyOne);
                var spacingQuery=new ExternalTargetQuery(NativeSubtype.SingleDirectionLinearPattern,"LPattern",ParameterKey.PatternSpacing,ScalarUnit.Millimeter,null,current.Geometry.Root,new(QueryRelationKind.DirectChildOf,pattern.Seed),EditSupport.Editable,QueryCardinality.ExactlyOne);
                var dependent=Query(current,new(seedQuery,8.5),new(spacingQuery,24));
                var second=session.Edit(Approve(session,dependent,Log));Log("dependent-batch",second);Check(second.Transaction.Succeeded&&second.Transaction.Revision==2&&second.Checkpoints==1&&second.PartialNativeSteps[0].StartsWith(pattern.Seed+":",StringComparison.Ordinal),"Dependency-safe aggregate failed");
                spec=spec with{Holes=spec.Holes.Select(h=>h.Label=="seed"?h with{Diameter=8.5}:h).ToArray(),Pattern=spec.Pattern with{Spacing=24}};Oracle();
                var pointer=ManagedRevisionStore.Hash(session.Store.PointerPath);var calls=setters;
                var fault=Approve(session,Query(session.CurrentExternal,new(Holes(new[]{11d}),11.5)),Log);armed=true;
                var failed=session.Edit(fault);armed=false;Log("mid-batch-rollback",failed);
                Check(!failed.Transaction.Succeeded&&failed.Transaction.FailureCode=="M18_FIRST_EDIT_FAILURE"&&failed.Transaction.MutationStarted&&failed.Transaction.RollbackSucceeded&&!failed.Transaction.StateCommitted&&
                    failed.Checkpoints==1&&setters-calls==1&&failed.PartialNativeSteps.Count==1&&failed.Transaction.Revision==2&&pointer==ManagedRevisionStore.Hash(session.Store.PointerPath),"Mid-batch failed to restore the whole start state");Oracle();
                Write(Path.Combine(Output,"expected.json"),spec);
            }
            Check(session.CurrentExternal.Observation.Selection.ExpectedRevision==2,"Final authoritative revision differs");
        }
        catch(Exception error){failure=error.ToString();Log("failure",new{failure});}
        finally
        {
            var restored=false;try{session?.Dispose();restored=session is null||session.OriginalActiveRestored;}catch(Exception error){failure=(failure??"")+error;}
            var after=Documents(app);var resources=NativeResourceGuard.Inspect(app,budget);
            var cleanup=lifecycle.Owned==0&&budget.Snapshot.OpenTestOwnedTitles.Count==0&&JsonSerializer.Serialize(before,Json)==JsonSerializer.Serialize(after,Json)&&restored&&resources.Responding&&resources.GdiCount<7000;
            try{foreach(var file in freeze.Files)Verify(file);}catch(Exception error){failure=(failure??"")+error;}
            Check(!File.Exists(Path.Combine(root,"recovery.json")),"Unresolved recovery marker");
            var passed=failure is null&&cleanup&&budget.Snapshot.CreationAttempts==0;
            Write(Path.Combine(Output,phase+"-result.json"),new{passed,failure,cleanup,resources,controller=Environment.ProcessId,elapsedMs=watch.Elapsed.TotalMilliseconds,budget=budget.Snapshot,
                source=Identity(spec.Source.Path),setters,pointerSha=File.Exists(Path.Combine(root,"current.json"))?ManagedRevisionStore.Hash(Path.Combine(root,"current.json")):null,
                nativeSha=File.Exists(Path.Combine(root,"working/CADHarnessManagedPart.SLDPRT"))?ManagedRevisionStore.Hash(Path.Combine(root,"working/CADHarnessManagedPart.SLDPRT")):null,before,after});
            Write(Path.Combine(Output,phase+"-ledger-snapshot.json"),new{ledgerPath=Ledger,snapshot=budget.Snapshot,basis="Snapshot of the exclusively leased live ledger; hash after lease disposal"});
        }
        Check(Read<JsonElement>(Path.Combine(Output,phase+"-result.json")).GetProperty("passed").GetBoolean(),"Native result failed; retain evidence and stop");Console.WriteLine("M18 "+phase+": PASS");return 0;
    }
    private static object[] Documents(ISldWorks app)=>NativeEditOracle.Items<IModelDoc2>(app.GetDocuments()).Select(d=>(object)new{title=d.GetTitle(),path=d.GetPathName(),dirty=d.GetSaveFlag()}).OrderBy(d=>JsonSerializer.Serialize(d,Json)).ToArray();
    private sealed class Lifecycle(NativeTestBudget budget,Action<string,object> log,int maximum):IManagedDocumentLifecycle
    {
        internal int Owned;
        public void BeforeOpen(string path){budget.ReserveOpen(maximum);log("reserve-open",new{path});}
        public void Opened(string path,string title){budget.RegisterOpened(title);Owned++;log("opened",new{path,title});}
        public void Closed(string path,string title){budget.RegisterClosed(title);Owned--;log("closed",new{path,title});}
    }
}
