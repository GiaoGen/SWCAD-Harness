using CadFixtureFactory;
using CadFixtureFactory.Reader;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length is <3 or >4){Console.Error.WriteLine("budgetRoot originalPackage evidenceNamespace [freshProofPackage] required.");return 2;}
        var package=Path.GetFullPath(args[1]);var output=args.Length==4?Path.GetFullPath(args[3]):package;var proofPath=Path.Combine(output,"reader-proof.json");
        if(File.Exists(proofPath)||File.Exists(Path.Combine(output,"ready.json"))){Console.Error.WriteLine("Reader slot already recorded.");return 2;}
        var checks=new List<string>();var cylinders=new List<CylinderFact>();FixtureManifest? manifest=null;
        var volume=0.0;var bodies=0;var features=0;var equations=0;var restored=false;string? failure=null;OwnedNativeSession? session=null;
        try
        {
            manifest=FixtureFiles.Read<FixtureManifest>(Path.Combine(package,"manifest.json"));
            Require(manifest.Dataset==Dataset.Development&&manifest.Status=="GENERATED_PENDING_INDEPENDENT_READER","Not a development preparation package.");
            FixtureFiles.Verify(manifest.Specification);FixtureFiles.Verify(manifest.NativePart);FixtureFiles.Verify(manifest.Template);FixtureFiles.Verify(manifest.BuilderBinary);FixtureFiles.Verify(manifest.SourceFreeze);
            FixtureFiles.VerifyFreeze(manifest.SourceFreeze.Path,manifest.BuilderBinary.Path,manifest.Specification.Path);
            FixtureFiles.VerifyFreeze(Path.Combine(args[0],"runs",args[2],"source-freeze.json"),typeof(Program).Assembly.Location,manifest.Specification.Path);
            var spec=FixtureFiles.Read<FixtureSpec>(manifest.Specification.Path);FixtureFiles.Validate(spec);Require(spec.FixtureId==manifest.FixtureId,"Fixture identity mismatch.");checks.Add("Frozen source/spec/binaries/template/native SHA256 verified before independent open");
            using(session=new OwnedNativeSession(args[0],args[2]+":"+spec.FixtureId+":read"))
            {
                var doc=session.OpenReadOnly(manifest.NativePart.Path);
                Require(doc.ConfigurationManager.ActiveConfiguration.Name==manifest.Configuration,"Active configuration drift.");
                var configs=Objects<string>(doc.GetConfigurationNames()).ToArray();Require(configs.Length==1&&configs[0]==manifest.Configuration,"Expected exactly one recorded configuration.");
                Require(doc.Extension.NeedsRebuild2==0,"Saved fixture needs rebuild; reader never repairs it.");
                var eq=(IEquationMgr)doc.GetEquationMgr();equations=eq.GetCount();Require(!eq.LinkToFile&&equations==(spec.Negative==NegativeHistory.EquationDrivenDepth?1:0),"Equation history differs from independent spec.");
                var physical=new Dictionary<string,IFeature>();
                foreach(var fact in manifest.Features)
                {
                    var native=Resolve(doc,fact.PersistentReference);Require(native.Name==fact.Name&&native.GetTypeName2()==fact.NativeType&&native.GetTypeName()==fact.UnderlyingType,"Native history identity drift.");
                    Require(!native.IsSuppressed()&&native.GetErrorCode2(out var warning)==0&&!warning,"Unhealthy native history.");
                    Require(References(doc,native.GetParents()).Order().SequenceEqual(fact.Parents.Order())&&References(doc,native.GetChildren()).Order().SequenceEqual(fact.Children.Order()),"Native dependency drift.");
                    var dims=Dimensions(native).ToArray();Require(dims.Length==fact.Dimensions.Count,"Driving dimension count drift.");
                    foreach(var d in dims)
                    {var original=fact.Dimensions.Single(x=>x.Name==d.FullName);IndependentOracle.Near(d.SystemValue,original.SystemValue,"Frozen native dimension",1e-10);
                     Require(d.DrivenState==original.DrivenState&&d.ReadOnly==original.ReadOnly&&d.IsDesignTableDimension()==original.DesignTable,"Dimension driver flags drift.");}
                    if(fact.Definition is {} recorded)CheckDefinition(native,recorded);
                    if(!fact.Label.EndsWith("_profile",StringComparison.Ordinal))physical.Add(fact.Label,native);
                    features++;
                }
                Require(physical.Count==5+(spec.BlindCut is null?0:1)&&physical.ContainsKey("host")&&physical.ContainsKey(spec.Pattern.Label)&&spec.Holes.All(h=>physical.ContainsKey(h.Label)),"Declared history cardinality differs from spec.");
                var tree=AllFeatures(doc).ToArray();
                Require(tree.Count(f=>f.GetDefinition() is IExtrudeFeatureData2 or ILinearPatternFeatureData)==physical.Count,"Unexpected physical history.");
                var host=physical["host"];Require(host.GetDefinition() is IExtrudeFeatureData2,"Host native type.");var hostData=(IExtrudeFeatureData2)host.GetDefinition();
                Require(host.GetTypeName()=="Extrusion"&&!hostData.IsThinFeature()&&!hostData.BothDirections&&!hostData.GetDraftWhileExtruding(true)&&hostData.GetEndCondition(true)==0,"Host base extrusion subtype.");IndependentOracle.Near(hostData.GetDepth(true)*1000,spec.DepthMm,"Host depth");
                var hostDims=Dimensions(host).ToArray();Require(hostDims.Length==1,"Expected exactly one native host depth dimension.");IndependentOracle.Near(hostDims[0].SystemValue*1000,spec.DepthMm,"Host depth dimension");
                IndependentOracle.VerifyHostDimensionState(spec.Negative==NegativeHistory.EquationDrivenDepth,hostDims[0].DrivenState,hostDims[0].IsDesignTableDimension());
                if(spec.Negative==NegativeHistory.EquationDrivenDepth)
                {
                    var text=eq.get_Equation(0);Require(eq.Status==0,"Equation read failed.");
                    Require(!eq.get_GlobalVariable(0)&&!eq.get_Disabled(0)&&!eq.get_Suppression(0),"Equation is a global variable, disabled or suppressed, not an active dimension driver: "+text);
                    var target=IndependentOracle.EquationTarget(text,spec.DepthMm);
                    Require(doc.Parameter(target) is IDimension,"Equation target does not resolve to a native dimension: "+text);
                    var nativeDim=(IDimension)doc.Parameter(target);Require(hostDims.Length==1&&nativeDim.FullName==hostDims[0].FullName,"Equation drives a different dimension.");
                    var equationValue=eq.get_Value(0);Require(eq.Status==0&&double.IsFinite(equationValue),"Native equation value read failed.");
                    IndependentOracle.Near(nativeDim.SystemValue*1000,spec.DepthMm,"Resolved equation-driven depth");
                    checks.Add("Active non-global equation targets native host depth: "+text+"; raw equation value="+equationValue.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"; native dimension mm="+(nativeDim.SystemValue*1000).ToString("R",System.Globalization.CultureInfo.InvariantCulture));
                }
                var hostProfile=Profile(host);Require(hostProfile.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained,"Rectangle solver not fully constrained.");var lines=Objects<ISketchSegment>(hostProfile.GetSketchSegments()).ToArray();Require(lines.Length==4&&lines.All(s=>!s.ConstructionGeometry&&s.GetType()==(int)swSketchSegments_e.swSketchLINE),"Host profile is not a four-line rectangle.");
                var corners=lines.Cast<ISketchLine>().SelectMany(l=>new[]{(ISketchPoint)l.GetStartPoint2(),(ISketchPoint)l.GetEndPoint2()}).Select(p=>World(session.Application,hostProfile,p)).ToArray();
                foreach(var p in corners){IndependentOracle.Near(Math.Abs(p[0]),spec.WidthMm/2,"Rectangle X");IndependentOracle.Near(Math.Abs(p[1]),spec.HeightMm/2,"Rectangle Y");IndependentOracle.Near(p[2],0,"Rectangle plane");}
                foreach(var h in spec.Holes)CheckCircle(session.Application,physical[h.Label],h,spec.DepthMm,true);
                if(spec.BlindCut is {} b)CheckCircle(session.Application,physical["blind_cut"],new("blind_cut",b.XMm,b.YMm,b.DiameterMm,false),b.DepthMm,false);
                var pattern=physical[spec.Pattern.Label];Require(pattern.GetDefinition() is ILinearPatternFeatureData,"Pattern native subtype.");var pd=(ILinearPatternFeatureData)pattern.GetDefinition();
                Require(!pd.IsDirection2Specified()&&!pd.GeometryPattern&&!pd.VarySketch&&pd.GetSkippedItemCount()==0&&pd.D1TotalInstances==spec.Pattern.Count,"Pattern flags/count mismatch.");IndependentOracle.Near(pd.D1Spacing*1000,spec.Pattern.SpacingMm,"Pattern spacing");
                Require(pd.AccessSelections(doc,null),"Pattern read access failed.");
                try{var seeds=Objects<IFeature>(pd.PatternFeatureArray).ToArray();Require(seeds.Length==1&&Ref(doc,seeds[0])==Ref(doc,physical[spec.Pattern.Seed]),"Pattern does not point to the declared seed.");}finally{pd.ReleaseSelectionAccess();}
                var graph=tree.ToDictionary(f=>Ref(doc,f),f=>(IReadOnlyList<string>)References(doc,f.GetChildren()).ToArray());
                var independent=spec.Holes.Where(h=>h.Label!=spec.Pattern.Seed).Select(h=>Ref(doc,physical[h.Label])).ToArray();
                Require(!IndependentOracle.HasPath(independent[0],independent[1],graph)&&!IndependentOracle.HasPath(independent[1],independent[0],graph),"Independent holes depend on each other.");checks.Add("Persistent history, definitions, single configuration, drivers, native profiles and independent-hole dependency graph verified");
                var solids=Objects<IBody2>(((IPartDoc)doc).GetBodies2((int)swBodyType_e.swSolidBody,false)).ToArray();bodies=solids.Length;Require(bodies==1,"Fixture must contain one solid.");
                var box=(double[])solids[0].GetBodyBox();var expectedBox=new[]{-spec.WidthMm/2,-spec.HeightMm/2,0,spec.WidthMm/2,spec.HeightMm/2,spec.DepthMm};
                for(var i=0;i<6;i++)IndependentOracle.Near(box[i]*1000,expectedBox[i],"World bounding box",0.01);
                volume=((double[])solids[0].GetMassProperties(1.0))[3]*1e9;IndependentOracle.Near(volume,IndependentOracle.ExpectedVolume(spec),"Independent native volume",0.001);
                foreach(var face in Objects<IFace2>(solids[0].GetFaces()))
                {
                    var surface=(ISurface)face.GetSurface();Require(surface.IsPlane()||surface.IsCylinder(),"Unexpected non-planar/non-cylindrical surface.");if(!surface.IsCylinder())continue;
                    var c=(double[])surface.CylinderParams;IndependentOracle.Near(c[3],0,"Cylinder axis X");IndependentOracle.Near(c[4],0,"Cylinder axis Y");
                    var zs=new List<double>();var edges=Objects<IEdge>(face.GetEdges()).ToArray();Require(edges.Count(e=>((ICurve)e.GetCurve()).IsCircle())==2,"Cylinder must have two circular boundaries.");Require(edges.Length is >=2 and <=4,"Cylinder has unexpected boundaries.");
                    foreach(var edge in edges)
                    {
                        var curve=(ICurve)edge.GetCurve();var limits=(ICurveParamData)edge.GetCurveParams3();
                        if(!curve.IsCircle())
                        {
                            Require(curve.IsLine(),"Cylinder seam not straight.");var start=(double[])limits.StartPoint;var end=(double[])limits.EndPoint;
                            IndependentOracle.Near(start[0],end[0],"Seam X",1e-8);IndependentOracle.Near(start[1],end[1],"Seam Y",1e-8);
                            IndependentOracle.Near(Math.Sqrt(Math.Pow(start[0]-c[0],2)+Math.Pow(start[1]-c[1],2)),c[6],"Seam radius",1e-8);
                            var matching=IndependentOracle.Expected(spec).Single(x=>Math.Abs(x.XMm-c[0]*1000)<0.0001&&Math.Abs(x.YMm-c[1]*1000)<0.0001);
                            IndependentOracle.Near(Math.Min(start[2],end[2])*1000,matching.BoundaryZMm[0],"Seam lower Z");IndependentOracle.Near(Math.Max(start[2],end[2])*1000,matching.BoundaryZMm[1],"Seam upper Z");continue;
                        }
                        var circle=(double[])curve.CircleParams;
                        IndependentOracle.Near(curve.GetLength3(limits.UMinValue,limits.UMaxValue),2*Math.PI*c[6],"Full-circle boundary length",1e-8);
                        IndependentOracle.Near(circle[0],c[0],"Circle/cylinder center X",1e-8);IndependentOracle.Near(circle[1],c[1],"Circle/cylinder center Y",1e-8);IndependentOracle.Near(circle[6],c[6],"Circle/cylinder radius",1e-8);zs.Add(circle[2]*1000);
                    }
                    cylinders.Add(new(c[0]*1000,c[1]*1000,c[6]*1000,c[5],zs.Order().ToArray()));
                }
                IndependentOracle.VerifyCylinders(spec,cylinders);checks.Add("Independent complete-body envelope/volume/all circular boundaries and through/blind cylinders verified");
                IndependentOracle.Near(manifest.Expected.VolumeMm3,IndependentOracle.ExpectedVolume(spec),"Manifest/spec volume");
                Require(manifest.Expected.SolidBodies==bodies&&manifest.Expected.ThroughCylinders==spec.Pattern.Count+2&&manifest.Expected.BlindCylinders==(spec.BlindCut is null?0:1),"Manifest expectations mismatch.");
                Require(!doc.GetSaveFlag()&&doc.IsOpenedReadOnly(),"Reader changed native document.");session.Close();session.RestoreActive();restored=session.ActiveRestored;
            }
            FixtureFiles.Verify(manifest.NativePart);FixtureFiles.Verify(manifest.Specification);FixtureFiles.Verify(manifest.SourceFreeze);checks.Add("Read-only cold reopen closed; source hash unchanged; active engineer document restored");
        }
        catch(Exception e){restored=session?.ActiveRestored??false;failure=e.ToString();Console.Error.WriteLine(e);}
        if(manifest is null){FixtureFiles.WriteNew(Path.Combine(output,"reader-failure.json"),new{failure,utc=DateTime.UtcNow});return 1;}
        var proof=new ReaderProof("1.0",manifest.FixtureId,manifest.NativePart,FixtureFiles.Identity(typeof(Program).Assembly.Location),failure is null,failure,manifest.Configuration,features,bodies,cylinders.Count,volume,checks,cylinders,equations,Environment.ProcessId,DateTime.UtcNow,restored);
        FixtureFiles.WriteNew(proofPath,proof);
        if(failure is not null)return 1;
        FixtureFiles.WriteNew(Path.Combine(output,"ready.json"),new{schemaVersion="1.0",dataset="development",status="PREPARATION_SELF_CHECKED_NOT_M14_ACCEPTANCE",readerFreeze=FixtureFiles.Identity(Path.Combine(args[0],"runs",args[2],"source-freeze.json")),manifest=FixtureFiles.Identity(Path.Combine(package,"manifest.json")),proof=FixtureFiles.Identity(proofPath),nativePart=manifest.NativePart});
        Console.WriteLine("Preparation self-check passed: "+manifest.FixtureId);return 0;
    }
    private static void CheckDefinition(IFeature f,DefinitionFact r)
    {
        if(r.InterfaceName=="IExtrudeFeatureData2")
        {Require(f.GetDefinition() is IExtrudeFeatureData2,"Extrude definition missing.");var e=(IExtrudeFeatureData2)f.GetDefinition();Require(e.IsThinFeature()==r.Thin&&e.BothDirections==r.BothDirections&&e.GetDraftWhileExtruding(true)==r.Draft&&e.GetEndCondition(true)==r.EndConditionD1,"Extrude definition drift.");IndependentOracle.Near(e.GetDepth(true)*1000,r.DepthMm!.Value,"Extrude depth drift");}
        else if(r.InterfaceName=="ILinearPatternFeatureData")
        {Require(f.GetDefinition() is ILinearPatternFeatureData,"Pattern definition missing.");var p=(ILinearPatternFeatureData)f.GetDefinition();Require(p.D1TotalInstances==r.CountD1&&p.IsDirection2Specified()==r.Direction2&&p.GeometryPattern==r.GeometryPattern&&p.GetSkippedItemCount()==r.SkippedInstances,"Pattern definition drift.");IndependentOracle.Near(p.D1Spacing*1000,r.SpacingMm!.Value,"Pattern spacing drift");}
        else throw new InvalidDataException("Unknown definition record.");
    }
    private static void CheckCircle(ISldWorks app,IFeature f,HoleSpec h,double depth,bool through)
    {
        Require(f.GetDefinition() is IExtrudeFeatureData2,"Circle cut missing native definition.");var e=(IExtrudeFeatureData2)f.GetDefinition();Require(f.GetTypeName()=="Cut"&&!e.IsThinFeature()&&!e.BothDirections&&!e.GetDraftWhileExtruding(true)&&e.GetEndCondition(true)==(through?1:0),"Circle cut subtype.");
        if(!through)IndependentOracle.Near(e.GetDepth(true)*1000,depth,"Blind depth");
        var sketch=Profile(f);Require(sketch.GetConstrainedStatus()==(int)swConstrainedStatus_e.swFullyConstrained,"Circle solver not fully constrained.");var segments=Objects<ISketchSegment>(sketch.GetSketchSegments()).ToArray();Require(segments.Length==1&&!segments[0].ConstructionGeometry&&segments[0] is ISketchArc,"Profile must contain one nonconstruction circle.");var arc=(ISketchArc)segments[0];Require(arc.IsCircle()==1,"Profile is an arc, not full circle.");
        var center=World(app,sketch,(ISketchPoint)arc.GetCenterPoint2());IndependentOracle.Near(center[0],h.XMm,"Circle world X");IndependentOracle.Near(center[1],h.YMm,"Circle world Y");IndependentOracle.Near(center[2],0,"Circle world Z");IndependentOracle.Near(arc.GetRadius()*2000,h.DiameterMm,"Circle native diameter");
        var profile=Objects<IFeature>(f.GetParents()).Single(x=>x.GetTypeName2()=="ProfileFeature");var dims=Dimensions(profile).ToArray();Require(dims.Length==1&&dims[0].DrivenState==(int)swDimensionDrivenState_e.swDimensionDriving&&!dims[0].ReadOnly&&!dims[0].IsDesignTableDimension(),"Single driving diameter required.");IndependentOracle.Near(dims[0].SystemValue*1000,h.DiameterMm,"Driving diameter");
        if(h.CoincidentOrigin)
        {var relations=Objects<ISketchRelation>(sketch.RelationManager.GetRelations((int)swSketchRelationFilterType_e.swAll));Require(relations.Any(r=>r.GetRelationType()==(int)swConstraintType_e.swConstraintType_COINCIDENT&&Objects<ISketchPoint>(r.GetEntities()).Count()==2),"Native origin coincidence relation absent.");Require(Objects<IFeature>(profile.GetParents()).Any(p=>p.GetTypeName2()=="OriginProfileFeature"),"Origin dependency absent.");}
    }
    private static double[] World(ISldWorks app,ISketch sketch,ISketchPoint p)
    {var math=(IMathUtility)app.GetMathUtility();var point=(IMathPoint)math.CreatePoint(new[]{p.X,p.Y,p.Z});return ((double[])((IMathPoint)point.MultiplyTransform((IMathTransform)sketch.ModelToSketchTransform.Inverse())).ArrayData).Select(x=>x*1000).ToArray();}
    private static ISketch Profile(IFeature f)=>(ISketch)Objects<IFeature>(f.GetParents()).Single(p=>p.GetTypeName2()=="ProfileFeature").GetSpecificFeature2();
    private static IEnumerable<IFeature> AllFeatures(IModelDoc2 doc)
    {
        var seen=new HashSet<string>();var queue=new Queue<IFeature>();for(var f=doc.FirstFeature() as IFeature;f is not null;f=f.GetNextFeature() as IFeature)queue.Enqueue(f);
        while(queue.Count>0){var f=queue.Dequeue();if(!seen.Add(Ref(doc,f)))continue;Require(seen.Count<4096,"Native feature limit.");yield return f;foreach(var p in Objects<IFeature>(f.GetParents()))queue.Enqueue(p);foreach(var c in Objects<IFeature>(f.GetChildren()))queue.Enqueue(c);}
    }
    private static IFeature Resolve(IModelDoc2 d,string r){var f=d.Extension.GetObjectByPersistReference3(Convert.FromBase64String(r),out var status) as IFeature;Require(f is not null&&status==0,"Persistent feature no longer resolves.");return f!;}
    private static string Ref(IModelDoc2 d,IFeature f)=>Convert.ToBase64String((byte[])d.Extension.GetPersistReference3(f));
    private static IEnumerable<string> References(IModelDoc2 d,object? v)=>Objects<IFeature>(v).Select(f=>Ref(d,f));
    private static IEnumerable<IDimension> Dimensions(IFeature f){var n=0;for(var d=f.GetFirstDisplayDimension() as IDisplayDimension;d is not null;d=f.GetNextDisplayDimension(d) as IDisplayDimension){Require(++n<=64,"Dimension limit.");yield return (IDimension)d.GetDimension2(0);}}
    private static IEnumerable<T> Objects<T>(object? v)=>v is Array a?a.Cast<object>().OfType<T>():Array.Empty<T>();
    private static void Require(bool b,string text){if(!b)throw new InvalidDataException(text);}
}
