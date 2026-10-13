using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;
using CadHarness.State.V03;

internal static partial class Program
{
    private sealed class AuditOnly:IObservedRevisionNative
    {
        public ExternalEditState CurrentExternal=>throw new NotSupportedException();
        public void SaveNative()=>throw new NotSupportedException();
        public void VerifySavedExternal(ExternalEditState state)=>throw new NotSupportedException();
    }
    private static int Pure()
    {
        Check(!Directory.Exists(Output),"Never overwrite pure results");var tests=new List<object>();
        using var store=new ManagedRevisionStore(Path.Combine(Root,"artifacts/milestone20/acceptance_t5b_v2/packages/core"),new AuditOnly());
        var state=store.ReadCurrent().External!;var b=state.Geometry.Holes.Single(h=>h.Target!=state.Geometry.Patterns.Single().Seed&&h.DiameterMm>10);
        var c=state.Geometry.Holes.Single(h=>h.Target!=state.Geometry.Patterns.Single().Seed&&h.DiameterMm<10);
        var session=Guid.NewGuid();
        QueryEditSetRequest Request()=>Query(state,new(Holes(new[]{b.DiameterMm,c.DiameterMm}),12));
        ExternalQueryPreview Preview(QueryEditSetRequest? request=null)=>ExternalQueryPlanning.Prepare(state,request??Request(),session).Preview!;
        void Test(string name,Action action)
        {
            try{action();tests.Add(new{name,passed=true});}
            catch(Exception e){tests.Add(new{name,passed=false,error=e.ToString()});Write(Path.Combine(Output,"failed.json"),new{tests,nativeCalls=0});throw;}
        }
        void Refuse(Action action,string? code=null)
        {
            try{action();}catch(Exception e)when(e is StateException or ContractException)
            {var actual=e is ICadFailure f?f.Code:((ContractException)e).Code;Check(code is null||actual==code,"Wrong typed error: "+actual);return;}
            throw new InvalidOperationException("Expected typed refusal");
        }
        Test("exact bounded query produces two qualified native identities",()=>{var p=Preview().Snapshot();Check(p.OrderedEdits.Count==2&&p.Matches.Select(m=>m.NativeReference).Distinct().Count()==2&&p.FullNativeValidationRequired&&p.AffectedTargets.Count>=2,"Incomplete preview");});
        Test("query strict schema roundtrip",()=>{var json=ContractJson.Write(Request(),ExternalQueryPlanning.Validate);Check(ContractJson.Write(ContractJson.Read<QueryEditSetRequest>(json,ExternalQueryPlanning.Validate),ExternalQueryPlanning.Validate)==json,"Noncanonical query");});
        foreach(var variant in new[]{"unknown-field","missing-field","duplicate-field","unknown-enum"})
            Test("strict query rejects "+variant,()=>
            {
                var json=ContractJson.Write(Request(),ExternalQueryPlanning.Validate);
                json=variant switch{"unknown-field"=>json.Replace("\"queryVersion\": 1","\"queryVersion\": 1, \"nativeApi\": \"SetDiameter\""),
                    "missing-field"=>json.Replace("\"queryVersion\": 1,",""),"duplicate-field"=>json.Replace("\"queryVersion\": 1","\"queryVersion\": 1, \"queryVersion\": 1"),
                    _=>json.Replace("\"intentional_batch\"","\"edit_all\"")};Refuse(()=>ContractJson.Read<QueryEditSetRequest>(json,ExternalQueryPlanning.Validate));
            });
        Test("no matches has no fallback",()=>Check(ExternalQueryPlanning.Prepare(state,Query(state,new(Holes(new[]{999d}),12)),session) is {Outcome:QueryOutcome.Empty,Preview:null},"Empty fallback"));
        Test("genuine under-specified single-target selection is ambiguous",()=>{var result=ExternalQueryPlanning.Prepare(state,Query(state,new(Holes(new[]{b.DiameterMm,c.DiameterMm},QueryCardinality.ExactlyOne),12)),session);Check(result.Outcome==QueryOutcome.Ambiguous&&result.Matches.Count==2&&result.Preview is null,"Ambiguity confused with intentional batch");});
        Test("intentional batch too small rejects",()=>Check(ExternalQueryPlanning.Prepare(state,Query(state,new(Holes(new[]{b.DiameterMm}),12)),session).Preview is null,"One-item batch admitted"));
        Test("duplicate query target/parameter rejects",()=>{var change=Request().Changes[0];Refuse(()=>Preview(Query(state,change,change)));});
        Test("wrong units rejects",()=>Refuse(()=>Preview(Query(state,new(Holes(new[]{b.DiameterMm,c.DiameterMm}) with{Unit=ScalarUnit.Degree},12)))));
        Test("invalid new value rejects",()=>Refuse(()=>Preview(Query(state,new(Holes(new[]{b.DiameterMm,c.DiameterMm}),double.NaN)))));
        Test("reversed interval rejects",()=>Refuse(()=>Preview(Query(state,new(Holes(Array.Empty<double>()) with{Dimension=new(20,10,Array.Empty<double>())},12)))));
        Test("no arbitrary close-value guessing",()=>Check(ExternalQueryPlanning.Prepare(state,Query(state,new(Holes(new[]{b.DiameterMm+0.0001,c.DiameterMm+0.0001}),12)),session).Preview is null,"Approximate retargeting"));
        Test("native unit-conversion ULP representations match without geometry tolerance",()=>
        {
            var converted=state with{Geometry=state.Geometry with{Holes=state.Geometry.Holes.Select(h=>h.Target==b.Target?h with{DiameterMm=10.000000000000002}:h.Target==c.Target?h with{DiameterMm=6.0000000000000036}:h).ToArray()},
                Observation=state.Observation with{Features=state.Observation.Features.Select(f=>f.SemanticId==b.Target||f.SemanticId==c.Target?f with{Parameters=f.Parameters.Select(p=>p.Key==ParameterKey.HoleDiameter?p with{Value=f.SemanticId==b.Target?10.000000000000002:6.0000000000000036}:p).ToArray()}:f).ToArray()}};
            var request=Query(converted,new(Holes(new[]{10d,6d}),11));var result=ExternalQueryPlanning.Prepare(converted,request,session);
            Check(result.Preview is not null&&result.Matches.Count==2,"Meter conversion loses exact identities");
            var range=Holes(Array.Empty<double>()) with{Dimension=new(6d,10d,Array.Empty<double>())};
            Check(ExternalQueryPlanning.Prepare(converted,Query(converted,new(range,11)),session).Matches.Count==3,"Inclusive interval excludes numeric representation boundary");
        });
        Test("unresolved host rejects",()=>Refuse(()=>Preview(Query(state,new(Holes(new[]{b.DiameterMm,c.DiameterMm}) with{Host="missing_host"},12))),"QUERY_REFERENCE_UNRESOLVED"));
        Test("unresolved relation rejects",()=>Refuse(()=>Preview(Query(state,new(Holes(new[]{b.DiameterMm,c.DiameterMm}) with{Relation=new(QueryRelationKind.DownstreamOf,"missing")},12))),"QUERY_REFERENCE_UNRESOLVED"));
        Test("frozen preview unaffected by caller array mutation",()=>{var values=new[]{b.DiameterMm,c.DiameterMm};var p=Preview(Query(state,new(Holes(values),12)));var sha=p.Sha256;values[0]=999;Check(p.Sha256==sha&&p.Snapshot().Expression.Changes[0].Query.Dimension!.Values[0]==b.DiameterMm,"Aliased mutable preview");});
        Test("confirmation hash and exact ordered set required",()=>{var p=Preview();var data=p.Snapshot();Refuse(()=>p.Approve("wrong",state.Observation.Selection.ExpectedRevision,data.OrderedEdits.Select(e=>e.Target).ToArray()));Refuse(()=>p.Approve(p.Sha256,state.Observation.Selection.ExpectedRevision,data.OrderedEdits.Select(e=>e.Target).Reverse().ToArray()));});
        Test("confirmation revision required",()=>{var p=Preview();Refuse(()=>p.Approve(p.Sha256,999,p.Snapshot().OrderedEdits.Select(e=>e.Target).ToArray()));});
        Test("approval creation and dispatch each one-use",()=>{var p=Preview();var ids=p.Snapshot().OrderedEdits.Select(e=>e.Target).ToArray();var grant=p.Approve(p.Sha256,state.Observation.Selection.ExpectedRevision,ids);Refuse(()=>p.Approve(p.Sha256,state.Observation.Selection.ExpectedRevision,ids),"QUERY_APPROVAL_USED");_=grant.Consume(session);Refuse(()=>grant.Consume(session),"QUERY_APPROVAL_USED");});
        Test("cross-session Part approval forbidden",()=>{var p=Preview();var grant=p.Approve(p.Sha256,state.Observation.Selection.ExpectedRevision,p.Snapshot().OrderedEdits.Select(e=>e.Target).ToArray());Refuse(()=>grant.Consume(Guid.NewGuid()),"QUERY_SESSION_MISMATCH");});
        foreach(var kind in new[]{"revision","configuration","document","source","copy","membership","value","native-reference"})
            Test("dispatch invalidates "+kind,()=>
            {
                var frozen=Preview().Snapshot();var changed=state;
                if(kind is "membership" or "value" or "native-reference")
                {
                    changed=changed with{Observation=changed.Observation with{Features=changed.Observation.Features.Select(f=>f.SemanticId!=b.Target?f:
                        kind=="native-reference"?f with{NativeReference=new(Convert.ToBase64String(new byte[]{1,2,3,4}))}:f with{Parameters=f.Parameters.Select(p=>p.Key==ParameterKey.HoleDiameter?p with{Value=b.DiameterMm+1}:p).ToArray()}).ToArray()}};
                    if(kind!="native-reference")changed=changed with{Geometry=changed.Geometry with{Holes=changed.Geometry.Holes.Select(h=>h.Target==b.Target?h with{DiameterMm=b.DiameterMm+1}:h).ToArray()}};
                }
                else
                {
                    var s=changed.Observation.Selection;s=kind switch{"revision"=>s with{ExpectedRevision=s.ExpectedRevision+1},"configuration"=>s with{ConfigurationId=Guid.NewGuid()},
                        "document"=>s with{DocumentId=Guid.NewGuid()},"source"=>s with{Source=s.Source with{Sha256=new string('0',64)}},_=>s with{WorkingCopy=s.WorkingCopy with{Sha256=new string('0',64)}}};
                    changed=changed with{Observation=changed.Observation with{Selection=s}};
                }
                Refuse(()=>ExternalQueryPlanning.Revalidate(changed,frozen));
            });
        Test("readonly matches cannot gain qualification",()=>
        {
            var readonlyState=state with{Observation=state.Observation with{Features=state.Observation.Features.Select(f=>f.SemanticId==c.Target?f with{EditSupport=EditSupport.ReadOnly}:f).ToArray()}};
            var request=Query(readonlyState,new(Holes(new[]{b.DiameterMm,c.DiameterMm}) with{Capability=null},12));
            var result=ExternalQueryPlanning.Prepare(readonlyState,request,session);Check(result.Outcome==QueryOutcome.Unsupported&&result.Preview is null&&result.Matches.Count==2,"Silently omitted readonly match");
        });
        Test("unsupported parameter remains refused",()=>{var query=new ExternalTargetQuery(NativeSubtype.StraightBlindBossExtrude,null,ParameterKey.CutDepth,ScalarUnit.Millimeter,null,null,null,null,QueryCardinality.ExactlyOne);Check(ExternalQueryPlanning.Prepare(state,Query(state,new(query,1)),session).Preview is null,"Widened native subtype");});
        Test("unknown downstream dependency rejects",()=>
        {
            var evidence=new[]{new ObservationEvidence(EvidenceSource.NativeDependency,"Pure negative dependency, not native evidence")};
            var feature=new ObservedFeature("query_unknown","unknown",250,ModelOrigin.External,"Fillet",NativeSubtype.Unrecognized,ObservationHealth.Unknown,null,EvidenceCompleteness.Known,EditSupport.ReadOnly,"unsupported",evidence,Array.Empty<ObservedParameter>(),Array.Empty<GeometryObservation>());
            var changed=state with{Observation=state.Observation with{Features=state.Observation.Features.Append(feature).ToArray(),Dependencies=state.Observation.Dependencies.Append(new(b.Target,feature.SemanticId,NativeDependencyKind.ParentChild,evidence)).ToArray()}};
            Refuse(()=>ExternalQueryPlanning.Prepare(changed,Query(changed,new(Holes(new[]{b.DiameterMm,c.DiameterMm}),12)),session),"UNVERIFIABLE_DEPENDENCY");
        });
        Test("final invalid geometry uses M14 preflight",()=>Refuse(()=>Preview(Query(state,new(Holes(new[]{b.DiameterMm,c.DiameterMm}),100)))));
        Test("dependent seed and pattern use existing M14 order",()=>
        {
            var pattern=state.Geometry.Patterns.Single();var root=state.Geometry.Root;
            var q1=new ExternalTargetQuery(NativeSubtype.SingleCircleThroughAllCut,null,ParameterKey.HoleDiameter,ScalarUnit.Millimeter,null,root,new(QueryRelationKind.PatternSeedOf,pattern.Target),EditSupport.Editable,QueryCardinality.ExactlyOne);
            var q2=new ExternalTargetQuery(NativeSubtype.SingleDirectionLinearPattern,null,ParameterKey.PatternSpacing,ScalarUnit.Millimeter,null,root,new(QueryRelationKind.DirectChildOf,pattern.Seed),EditSupport.Editable,QueryCardinality.ExactlyOne);
            var p=Preview(Query(state,new(q2,24),new(q1,8.5))).Snapshot();Check(p.OrderedEdits[0].Target==pattern.Seed&&p.AffectedDependencies.Count>0,"Query invented execution order");
        });
        Test("native type, range and host filters select exact qualified set",()=>
        {
            var q=Holes(Array.Empty<double>()) with{NativeType=state.Observation.Features.Single(f=>f.SemanticId==b.Target).NativeType,Host=state.Geometry.Root,Dimension=new(c.DiameterMm,b.DiameterMm,Array.Empty<double>())};
            var result=ExternalQueryPlanning.Prepare(state,Query(state,new(q,12)),session);Check(result.Preview is not null&&result.Matches.Any(m=>m.Target==b.Target)&&result.Matches.Any(m=>m.Target==c.Target),"Filter dropped target");
        });
        Test("direct parent and downstream query relations use native graph",()=>
        {
            var pattern=state.Geometry.Patterns.Single();
            var q1=Holes(Array.Empty<double>(),QueryCardinality.ExactlyOne) with{Dimension=null,Relation=new(QueryRelationKind.DirectParentOf,pattern.Target)};
            var q2=new ExternalTargetQuery(NativeSubtype.SingleDirectionLinearPattern,null,ParameterKey.PatternSpacing,ScalarUnit.Millimeter,null,null,new(QueryRelationKind.DownstreamOf,pattern.Seed),EditSupport.Editable,QueryCardinality.ExactlyOne);
            Check(Preview(Query(state,new(q1,8.5),new(q2,24))).Snapshot().OrderedEdits.Count==2,"Native relation filters failed");
        });
        Test("same-size distinct identities admit confirmed intentional batch",()=>
        {
            var same=state with{Geometry=state.Geometry with{Holes=state.Geometry.Holes.Select(h=>h.Target==b.Target?h with{DiameterMm=c.DiameterMm}:h).ToArray()},
                Observation=state.Observation with{Features=state.Observation.Features.Select(f=>f.SemanticId==b.Target?f with{Parameters=f.Parameters.Select(p=>p.Key==ParameterKey.HoleDiameter?p with{Value=c.DiameterMm}:p).ToArray()}:f).ToArray()}};
            var result=ExternalQueryPlanning.Prepare(same,Query(same,new(Holes(new[]{c.DiameterMm}),12)),session);Check(result.Preview is not null&&result.Matches.Count==2&&result.Matches.Select(m=>m.NativeReference).Distinct().Count()==2,"Equal size is not ambiguous batch intent");
        });
        Test("query match overflow refuses rather than truncating",()=>
        {
            var change=new QueryParameterChange(Holes(state.Geometry.Holes.Select(h=>h.DiameterMm)),12);
            var request=new QueryEditSetRequest("0.3",1,state.Observation.Selection,Enumerable.Repeat(change,6).ToArray());
            var result=ExternalQueryPlanning.Prepare(state,request,session);Check(result.Preview is null&&result.FailureCode=="QUERY_LIMIT_EXCEEDED","Query silently truncated");
        });
        Test("query clause bound and version reject",()=>
        {Refuse(()=>ExternalQueryPlanning.Validate(Request() with{Changes=Enumerable.Repeat(Request().Changes[0],17).ToArray()}));Refuse(()=>ExternalQueryPlanning.Validate(Request() with{QueryVersion=2}));});
        Write(Path.Combine(Output,"query-schema.json"),JsonSerializer.Deserialize<JsonElement>(ContractJson.Schema<QueryEditSetRequest>()));
        Write(Path.Combine(Output,"result.json"),new{passed=true,assertions=tests.Count,tests,nativeCalls=0,providerCalls=0,kind="Pure query/preview; not native qualification"});
        Console.WriteLine("M18 pure: "+tests.Count+" PASS");return 0;
    }
}
