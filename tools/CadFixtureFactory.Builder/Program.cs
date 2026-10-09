using CadFixtureFactory;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if(args.Length!=4){Console.Error.WriteLine("budgetRoot specPath packagePath evidenceNamespace required.");return 2;}
        var package=Path.GetFullPath(args[2]);
        try
        {
            var spec=FixtureFiles.Read<FixtureSpec>(args[1]);FixtureFiles.Validate(spec);
            if(Directory.Exists(package))throw new InvalidOperationException("Use a fresh fixture package; never overwrite a failed attempt.");
            var freeze=FixtureFiles.VerifyFreeze(Path.Combine(args[0],"runs",args[3],"source-freeze.json"),typeof(Program).Assembly.Location,args[1]);
            var templateIdentity=freeze.Files.Single(f=>Path.GetExtension(f.Path).Equals(".prtdot",StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(package);
            using var session=new OwnedNativeSession(args[0],args[3]+":"+spec.FixtureId+":build");
            var doc=session.Create(templateIdentity.Path);var features=new Dictionary<string,IFeature>();
            var input=session.Application.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
            session.Application.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate,false);
            try
            {
                var plane=FindXyPlane(doc);var sketch=BeginSketch(doc,plane);
                var rectangle=Objects<ISketchSegment>(doc.SketchManager.CreateCornerRectangle(-spec.WidthMm/2000,-spec.HeightMm/2000,0,spec.WidthMm/2000,spec.HeightMm/2000,0)).ToArray();
                Require(rectangle.Length==4,"Rectangle API did not return four native segments.");doc.ClearSelection2(true);
                foreach(var segment in rectangle)Require(segment.Select4(true,null),"Rectangle selection failed.");doc.SketchAddConstraints("sgFIXED");
                EndSketch(doc);SelectSketch(doc,sketch);
                var initialDepth=spec.DepthMm+(spec.Negative==NegativeHistory.EquationDrivenDepth?1:0);
                var host=doc.FeatureManager.FeatureExtrusion3(true,false,false,0,0,initialDepth/1000,0,false,false,false,false,0,0,false,false,false,false,true,true,true,0,0,false) as IFeature;
                Require(host is not null,"Official extrusion API failed.");host!.Name="FF_Host";features.Add("host",host);Rebuild(doc);
                session.Record("stage","host");
                var seed=spec.Holes.Single(h=>h.Label==spec.Pattern.Seed);
                var seedFeature=MakeHole(doc,plane,seed,through:true,depth:spec.DepthMm);features.Add(seed.Label,seedFeature);session.Record("stage",seed.Label);
                // Direction is a native straight host edge, selected with the official pattern mark.
                var edge=Objects<IFace2>(host.GetFaces()).SelectMany(f=>Objects<IEdge>(f.GetEdges())).FirstOrDefault(e=> {
                    if(e.GetCurve() is not ICurve c||!c.IsLine())return false;var a=(double[])c.LineParams;return Math.Abs(a[3]-1)<1e-8&&Math.Abs(a[4])<1e-8&&Math.Abs(a[5])<1e-8; });
                Require(edge is not null,"No positive world-X straight host edge.");doc.ClearSelection2(true);
                var selection=(ISelectionMgr)doc.SelectionManager;var axisData=(ISelectData)selection.CreateSelectData();axisData.Mark=1;
                Require(((IEntity)edge!).Select4(false,(SelectData)axisData),"Pattern direction selection failed.");Require(seedFeature.Select2(true,4),"Pattern seed selection failed.");
                var pattern=doc.FeatureManager.FeatureLinearPattern5(spec.Pattern.Count,spec.Pattern.SpacingMm/1000,1,0,false,false,"","",false,false,false,false,false,false,false,false,false,false,0,0,false,false) as IFeature;
                Require(pattern is not null,"Official linear pattern API failed.");pattern!.Name="FF_Pattern";features.Add(spec.Pattern.Label,pattern);Rebuild(doc);session.Record("stage","pattern");
                foreach(var h in spec.Holes.Where(h=>h.Label!=seed.Label))
                {features.Add(h.Label,MakeHole(doc,plane,h,true,spec.DepthMm));session.Record("stage",h.Label);}
                if(spec.BlindCut is {} b)
                {features.Add("blind_cut",MakeHole(doc,plane,new("blind_cut",b.XMm,b.YMm,b.DiameterMm,false),false,b.DepthMm));session.Record("stage","blind_cut");}
                if(spec.Negative==NegativeHistory.EquationDrivenDepth)
                {
                    var nativeDim=(IDimension)((IDisplayDimension)host.GetFirstDisplayDimension()).GetDimension2(0);
                    var dim=nativeDim.Name+"@"+host.Name;
                    var equations=(IEquationMgr)doc.GetEquationMgr();
                    Require(equations.Add2(-1,"\""+dim+"\" = "+spec.DepthMm.ToString(System.Globalization.CultureInfo.InvariantCulture)+"mm",true)==0&&!equations.get_GlobalVariable(0)&&!equations.get_Disabled(0),"Equation negative creation failed.");Rebuild(doc);
                    Require(Math.Abs(((IExtrudeFeatureData2)host.GetDefinition()).GetDepth(true)*1000-spec.DepthMm)<0.0001,"Equation did not change the initial host depth to its specified value.");
                    session.Record("stage","equation-depth: official active equation changed "+initialDepth+"mm to "+spec.DepthMm+"mm during preparation");
                }
                foreach(var f in features.Values)Require(f.GetErrorCode2(out var warning)==0&&!warning,"Generated native feature has an error/warning.");
                var path=Path.Combine(package,spec.FixtureId+".SLDPRT");var errors=0;var warnings=0;
                var saved=doc.Extension.SaveAs3(path,(int)swSaveAsVersion_e.swSaveAsCurrentVersion,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,null,null,ref errors,ref warnings);
                session.RefreshOwnedTitle();Require(saved&&errors==0&&warnings==0&&!doc.GetSaveFlag(),$"Native save failed/dirty: {errors}/{warnings}.");
                var all=new Dictionary<string,IFeature>(features);
                foreach(var pair in features)
                    foreach(var profile in Objects<IFeature>(pair.Value.GetParents()).Where(p=>p.GetTypeName2()=="ProfileFeature"))all.TryAdd(pair.Key+"_profile",profile);
                var facts=all.Select(pair=>Fact(doc,pair.Key,pair.Value)).ToArray();
                var area=spec.WidthMm*spec.HeightMm;
                foreach(var h in spec.Holes)area-=Math.PI*h.DiameterMm*h.DiameterMm/4*(h.Label==spec.Pattern.Seed?spec.Pattern.Count:1);
                var volume=area*spec.DepthMm-(spec.BlindCut is {} blind?Math.PI*blind.DiameterMm*blind.DiameterMm/4*blind.DepthMm:0);
                var manifest=new FixtureManifest("1.0",spec.FixtureId,Dataset.Development,"Independent official SOLIDWORKS API workflow; not manually authored; no Harness dependencies",
                    "fixture-factory-1",session.Application.RevisionNumber(),FixtureFiles.Identity(args[1]),FixtureFiles.Identity(typeof(Program).Assembly.Location),templateIdentity,
                    FixtureFiles.Identity(Path.Combine(args[0],"runs",args[3],"source-freeze.json")),FixtureFiles.Identity(path),doc.ConfigurationManager.ActiveConfiguration.Name,facts,
                    new(spec.WidthMm,spec.HeightMm,spec.DepthMm,1,spec.Pattern.Count+2,spec.BlindCut is null?0:1,volume),Path.Combine(Path.GetFullPath(args[0]),"preparation-budget.json"),"GENERATED_PENDING_INDEPENDENT_READER");
                session.Close();session.RestoreActive();FixtureFiles.Verify(manifest.NativePart);FixtureFiles.Verify(templateIdentity);
                FixtureFiles.WriteNew(Path.Combine(package,"manifest.json"),manifest);
                Console.WriteLine("Generated closed fixture: "+path);return 0;
            }
            finally {session.Application.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate,input);}
        }
        catch(Exception error)
        { if(Directory.Exists(package)&&!File.Exists(Path.Combine(package,"build-failure.json")))FixtureFiles.WriteNew(Path.Combine(package,"build-failure.json"),new { error=error.ToString(),utc=DateTime.UtcNow });Console.Error.WriteLine(error);return 1; }
    }
    private static IFeature FindXyPlane(IModelDoc2 doc)
    {
        var matches=new List<IFeature>();
        for(var f=doc.FirstFeature() as IFeature;f is not null;f=f.GetNextFeature() as IFeature)
            if(f.GetSpecificFeature2() is IRefPlane plane)
            {var a=(double[])plane.Transform.ArrayData;if(Math.Abs(a[8]-1)<1e-8&&Math.Abs(a[6])<1e-8&&Math.Abs(a[7])<1e-8&&Math.Abs(a[9])+Math.Abs(a[10])+Math.Abs(a[11])<1e-8)matches.Add(f);}
        Require(matches.Count==1,"Template must expose exactly one world-XY origin plane.");return matches[0];
    }
    private static ISketch BeginSketch(IModelDoc2 doc,IFeature plane)
    {doc.ClearSelection2(true);Require(plane.Select2(false,0),"Principal-plane selection failed.");doc.SketchManager.InsertSketch(false);return (ISketch)doc.SketchManager.ActiveSketch;}
    private static void EndSketch(IModelDoc2 doc){doc.SketchManager.InsertSketch(false);doc.ClearSelection2(true);}
    private static void SelectSketch(IModelDoc2 doc,ISketch sketch)
    {
        var matches=new List<IFeature>();
        for(var f=doc.FirstFeature() as IFeature;f is not null;f=f.GetNextFeature() as IFeature)
            if(f.GetTypeName2()=="ProfileFeature"&&Equals(f.GetSpecificFeature2(),sketch))matches.Add(f);
        Require(matches.Count==1&&matches[0].Select2(false,0),"Consuming profile selection failed.");
    }
    private static IFeature MakeHole(IModelDoc2 doc,IFeature plane,HoleSpec hole,bool through,double depth)
    {
        var sketch=BeginSketch(doc,plane);var circle=doc.SketchManager.CreateCircleByRadius(hole.XMm/1000,hole.YMm/1000,0,hole.DiameterMm/2000) as ISketchArc;
        Require(circle is not null,"Official circle API failed.");var center=(ISketchPoint)circle!.GetCenterPoint2();doc.ClearSelection2(true);Require(center.Select4(false,null),"Circle center selection failed.");
        if(hole.CoincidentOrigin)
        {
            IFeature? origin=null;for(var f=doc.FirstFeature() as IFeature;f is not null;f=f.GetNextFeature() as IFeature)if(f.GetTypeName2()=="OriginProfileFeature"){origin=f;break;}
            Require(origin?.GetSpecificFeature2() is ISketch,"Native origin sketch unavailable.");var points=Objects<ISketchPoint>(((ISketch)origin!.GetSpecificFeature2()).GetSketchPoints2()).ToArray();
            Require(points.Length==1&&points[0].Select4(true,null),"Native origin point is ambiguous.");doc.SketchAddConstraints("sgCOINCIDENT");
        }
        else doc.SketchAddConstraints("sgFIXED");
        doc.ClearSelection2(true);Require(((ISketchSegment)circle).Select4(false,null),"Circle dimension selection failed.");
        var display=doc.AddDiameterDimension2(hole.XMm/1000+hole.DiameterMm/1000,hole.YMm/1000+hole.DiameterMm/1000,0) as IDisplayDimension;
        Require(display is not null,"Circle driving diameter dimension API failed.");var dimension=(IDimension)display!.GetDimension2(0);
        Require(dimension.SetSystemValue3(hole.DiameterMm/1000,(int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration,null)==0,"Diameter driving value failed.");
        EndSketch(doc);SelectSketch(doc,sketch);
        // Both profile and body start at Z=0. Reverse the default cut direction into +Z.
        var cut=doc.FeatureManager.FeatureCut4(true,false,true,through?1:0,0,depth/1000,0,false,false,false,false,0,0,false,false,false,false,false,true,true,false,false,false,0,0,false,false) as IFeature;
        Require(cut is not null,"Official circle cut API failed.");cut!.Name="FF_"+hole.Label;Rebuild(doc);return cut;
    }
    private static FeatureFact Fact(IModelDoc2 doc,string label,IFeature f)
    {
        string Ref(IFeature native)=>doc.Extension.GetPersistReference3(native) is byte[] bytes?Convert.ToBase64String(bytes):throw new InvalidOperationException("Native feature reference missing.");
        DefinitionFact? def=null;
        if(f.GetDefinition() is IExtrudeFeatureData2 e)def=new("IExtrudeFeatureData2",e.IsThinFeature(),e.BothDirections,e.GetDraftWhileExtruding(true),e.GetEndCondition(true),e.GetDepth(true)*1000,null,null,null,null,null);
        if(f.GetDefinition() is ILinearPatternFeatureData p)def=new("ILinearPatternFeatureData",null,null,null,null,null,p.D1TotalInstances,p.D1Spacing*1000,p.IsDirection2Specified(),p.GeometryPattern,p.GetSkippedItemCount());
        var error=f.GetErrorCode2(out var warning);
        return new(label,f.Name,f.GetTypeName2(),f.GetTypeName(),Ref(f),Objects<IFeature>(f.GetParents()).Select(Ref).ToArray(),Objects<IFeature>(f.GetChildren()).Select(Ref).ToArray(),Dimensions(f).ToArray(),f.IsSuppressed(),error,warning,def);
    }
    private static IEnumerable<DimensionFact> Dimensions(IFeature f)
    {
        var count=0;for(var dd=f.GetFirstDisplayDimension() as IDisplayDimension;dd is not null;dd=f.GetNextDisplayDimension(dd) as IDisplayDimension)
        {Require(++count<=64,"Dimension limit.");var d=(IDimension)dd.GetDimension2(0);yield return new(d.FullName,d.SystemValue,d.DrivenState,d.ReadOnly,d.IsDesignTableDimension());}
    }
    private static void Rebuild(IModelDoc2 doc){Require(doc.ForceRebuild3(false)&&doc.Extension.NeedsRebuild2==0,"Generated feature rebuild failed.");doc.ClearSelection2(true);}
    private static IEnumerable<T> Objects<T>(object? value)=>value is Array a?a.Cast<object>().OfType<T>():Array.Empty<T>();
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
