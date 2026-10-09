using System.Text.Json;
using System.Text.Json.Nodes;
using CadFixtureFactory;
using CadFixtureFactory.Reader;

var root=Path.GetFullPath(args[0]);var temp=Path.Combine(root,"artifacts","fixture-factory","pure-tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
var passed=0;
void Test(string name,Action action){action();passed++;Console.WriteLine("PASS "+name);}
void Reject(Action action){try{action();}catch(Exception e) when(e is InvalidDataException or JsonException or InvalidOperationException or ArgumentException){return;}throw new Exception("Expected refusal.");}
var specs=Directory.GetFiles(Path.Combine(root,"tools","fixture-specs"),"*.json").Order().Select(FixtureFiles.Read<FixtureSpec>).ToArray();
foreach(var s in specs)Test("valid "+s.FixtureId,()=>FixtureFiles.Validate(s));
var sample=specs.Single(s=>s.FixtureId=="dev_core");
Test("Held-out refused",()=>Reject(()=>FixtureFiles.Validate(sample with{Dataset=Dataset.HeldOut})));
Test("null holes",()=>Reject(()=>FixtureFiles.Validate(sample with{Holes=null!})));
Test("null hole member",()=>Reject(()=>FixtureFiles.Validate(sample with{Holes=new HoleSpec[]{null!,sample.Holes[1],sample.Holes[2]}})));
Test("null pattern",()=>Reject(()=>FixtureFiles.Validate(sample with{Pattern=null!})));
Test("nonfinite extent",()=>Reject(()=>FixtureFiles.Validate(sample with{WidthMm=double.NaN})));
Test("zero depth",()=>Reject(()=>FixtureFiles.Validate(sample with{DepthMm=0})));
Test("oversized extent",()=>Reject(()=>FixtureFiles.Validate(sample with{HeightMm=1001})));
Test("overlap",()=>Reject(()=>FixtureFiles.Validate(sample with{Holes=sample.Holes.Select(h=>h with{XMm=0,YMm=0}).ToArray()})));
Test("edge crossing",()=>Reject(()=>FixtureFiles.Validate(sample with{WidthMm=50})));
Test("negative spacing",()=>Reject(()=>FixtureFiles.Validate(sample with{Pattern=sample.Pattern with{SpacingMm=-1}})));
Test("excessive count",()=>Reject(()=>FixtureFiles.Validate(sample with{Pattern=sample.Pattern with{Count=9}})));
Test("missing seed",()=>Reject(()=>FixtureFiles.Validate(sample with{Pattern=sample.Pattern with{Seed="absent"}})));
Test("ambiguous labels",()=>Reject(()=>FixtureFiles.Validate(sample with{Holes=sample.Holes.Select(h=>h with{Label="same"}).ToArray()})));
Test("coincidence at wrong location",()=>Reject(()=>FixtureFiles.Validate(sample with{Holes=sample.Holes.Select(h=>h with{CoincidentOrigin=true}).ToArray()})));
Test("blind spec required",()=>Reject(()=>FixtureFiles.Validate(sample with{Negative=NegativeHistory.BlindCutDescendant})));
Test("unrequested blind cut",()=>Reject(()=>FixtureFiles.Validate(sample with{BlindCut=new(45,25,6,2)})));
var json=JsonSerializer.Serialize(sample,FixtureFiles.Json);
void BadJson(string value){var path=Path.Combine(temp,Guid.NewGuid().ToString("N")+".json");FixtureFiles.WriteNew(path,JsonDocument.Parse(value).RootElement);Reject(()=>FixtureFiles.Read<FixtureSpec>(path));}
Test("unknown JSON field",()=>{var node=JsonNode.Parse(json)!.AsObject();node["surprise"]=1;BadJson(node.ToJsonString());});
Test("missing JSON field",()=>{var node=JsonNode.Parse(json)!.AsObject();node.Remove("recipe");BadJson(node.ToJsonString());});
Test("nested missing field",()=>{var node=JsonNode.Parse(json)!.AsObject();node["pattern"]!.AsObject().Remove("count");BadJson(node.ToJsonString());});
Test("duplicate JSON field",()=>BadJson(json.Insert(json.IndexOf('{')+1,"\"recipe\":\"duplicate\",")));
Test("integer enum forbidden",()=>{var node=JsonNode.Parse(json)!.AsObject();node["dataset"]=0;BadJson(node.ToJsonString());});
Test("unknown enum forbidden",()=>{var node=JsonNode.Parse(json)!.AsObject();node["negative"]="magic";BadJson(node.ToJsonString());});
var identityFile=Path.Combine(temp,"immutable.json");FixtureFiles.WriteNew(identityFile,sample);var id=FixtureFiles.Identity(identityFile);
Test("fresh artifact verifies",()=>FixtureFiles.Verify(id));
Test("overwrite forbidden",()=>{try{FixtureFiles.WriteNew(identityFile,sample);}catch(IOException){return;}throw new Exception("Overwrite permitted.");});
Test("hash drift refused",()=>Reject(()=>FixtureFiles.Verify(id with{Sha256=new string('0',64)})));
foreach(var spec in specs)
{
    Test("independent cylinder oracle "+spec.FixtureId,()=>IndependentOracle.VerifyCylinders(spec,IndependentOracle.Expected(spec)));
    Test("volume positive "+spec.FixtureId,()=>{if(IndependentOracle.ExpectedVolume(spec)<=0)throw new Exception("Invalid volume.");});
}
var expected=IndependentOracle.Expected(sample);
Test("missing cylinder refused",()=>Reject(()=>IndependentOracle.VerifyCylinders(sample,expected.Skip(1).ToArray())));
Test("duplicate cylinder refused",()=>Reject(()=>IndependentOracle.VerifyCylinders(sample,expected.Concat(new[]{expected[0]}).ToArray())));
Test("wrong location refused",()=>Reject(()=>IndependentOracle.VerifyCylinders(sample,expected.Select(c=>c with{XMm=c.XMm+1}).ToArray())));
Test("blind mistaken for through refused",()=>Reject(()=>IndependentOracle.VerifyCylinders(sample,expected.Select(c=>c with{BoundaryZMm=new[]{0.0,2.0}}).ToArray())));
Test("split cylinder refused",()=>Reject(()=>IndependentOracle.VerifyCylinders(sample,expected.Select(c=>c with{BoundaryZMm=new[]{0.0,5.0,10.0}}).ToArray())));
Test("wrong axis refused",()=>Reject(()=>IndependentOracle.VerifyCylinders(sample,expected.Select(c=>c with{AxisZ=0}).ToArray())));
Test("nonfinite volume refused",()=>Reject(()=>IndependentOracle.Near(double.NaN,1,"volume")));
var graph=new Dictionary<string,IReadOnlyList<string>>{{"host",new[]{"a","b"}},{"a",new[]{"pattern","descendant"}},{"descendant",new[]{"a"}},{"b",Array.Empty<string>()}};
Test("dependency reachability",()=>{if(!IndependentOracle.HasPath("host","descendant",graph))throw new Exception("Path missing.");});
Test("independent targets",()=>{if(IndependentOracle.HasPath("a","b",graph)||IndependentOracle.HasPath("b","a",graph))throw new Exception("False path.");});
Test("cycle terminates",()=>{if(IndependentOracle.HasPath("a","missing",graph))throw new Exception("False path.");});
Test("literal equation target",()=>{if(IndependentOracle.EquationTarget("\"D1@native\" = 10mm",10)!="D1@native")throw new Exception("Wrong target.");});
Test("wrong equation value",()=>Reject(()=>IndependentOracle.EquationTarget("\"D1@native\" = 11mm",10)));
Test("nonliteral equation",()=>Reject(()=>IndependentOracle.EquationTarget("\"D1@native\" = 5mm + 5mm",10)));
Test("wrong equation unit",()=>Reject(()=>IndependentOracle.EquationTarget("\"D1@native\" = 10m",10)));
Test("ordinary host is driving",()=>IndependentOracle.VerifyHostDimensionState(false,2,false));
Test("equation host is driven",()=>IndependentOracle.VerifyHostDimensionState(true,1,false));
Test("ordinary host cannot be driven",()=>Reject(()=>IndependentOracle.VerifyHostDimensionState(false,1,false)));
Test("equation host cannot be ordinary driving",()=>Reject(()=>IndependentOracle.VerifyHostDimensionState(true,2,false)));
Test("design table driver refused",()=>Reject(()=>IndependentOracle.VerifyHostDimensionState(true,1,true)));
var authorization=new PreparationAuthorization("1.0",true,6,8,false,12,"original explicit human grant");
var grant=new AdditionalPreparationAuthorization("1.0",true,"development-fixture-preparation-only",id,6,8,12,16,false,false,"explicit additional human grant");
Test("additional budget is cumulative",()=>{var result=FixtureFiles.ApplyAdditionalAuthorization(authorization,grant);if(result.MaximumCreationAttempts!=12||result.MaximumOpenAttempts!=16||result.M14MaximumCumulativeOpens!=12)throw new Exception("Budget transfer/reset.");});
Test("budget cannot be reset",()=>Reject(()=>FixtureFiles.ApplyAdditionalAuthorization(authorization,grant with{MaximumOpenAttempts=8})));
Test("additional grant must be human authorized",()=>Reject(()=>FixtureFiles.ApplyAdditionalAuthorization(authorization,grant with{UserAuthorized=false})));
Test("additional grant cannot authorize M14",()=>Reject(()=>FixtureFiles.ApplyAdditionalAuthorization(authorization,grant with{M14AcceptanceAuthorized=true})));
Test("additional grant cannot authorize Held-out",()=>Reject(()=>FixtureFiles.ApplyAdditionalAuthorization(authorization,grant with{GenerateHeldOut=true})));
foreach(var project in Directory.GetFiles(Path.Combine(root,"tools"),"*.csproj",SearchOption.AllDirectories).Where(p=>p.Contains("CadFixtureFactory")))
    Test("dependency isolation "+Path.GetFileName(project),()=>{var content=System.Xml.Linq.XDocument.Load(project);var refs=content.Descendants("ProjectReference").Select(n=>(string?)n.Attribute("Include")??"");if(refs.Any(r=>r.Contains("CadHarness",StringComparison.OrdinalIgnoreCase)||Path.GetFileName(project).Contains("Reader")&&r.Contains("Builder")))throw new Exception("Isolation violation.");});
foreach(var deps in Directory.GetFiles(Path.Combine(root,"tools"),"*.deps.json",SearchOption.AllDirectories).Where(p=>p.Contains("CadFixtureFactory")))
    Test("binary isolation "+Path.GetFileName(deps),()=>{var libraries=JsonDocument.Parse(File.ReadAllText(deps)).RootElement.GetProperty("libraries").EnumerateObject().Select(x=>x.Name);if(libraries.Any(n=>n.StartsWith("CadHarness.",StringComparison.Ordinal)||deps.Contains("CadFixtureFactory.Reader")&&n.StartsWith("CadFixtureFactory.Builder",StringComparison.Ordinal)))throw new Exception("Binary dependency violation.");});
FixtureFiles.WriteNew(Path.Combine(temp,"result.json"),new{passed,nativeCalls=0,utc=DateTime.UtcNow});Console.WriteLine($"{passed}/{passed} pure tests passed; no native activation.");
