using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

// Acceptance measurements deliberately do not call Harness geometry or accessor helpers.
internal static class NativeEditOracle
{
    internal static IEnumerable<T> Items<T>(object? value)=>value is Array a?a.Cast<object>().OfType<T>():Array.Empty<T>();
    internal static IFeature Resolve(IModelDoc2 doc,string reference)
    {
        var feature=doc.Extension.GetObjectByPersistReference3(Convert.FromBase64String(reference),out var error) as IFeature;
        Program.Check(error==0&&feature is not null&&Convert.ToBase64String((byte[])doc.Extension.GetPersistReference3(feature))==reference,"Oracle persistent identity unresolved.");
        return feature!;
    }
    internal static double Value(IModelDoc2 doc,OracleInput input,string label,CadHarness.Ir.V03.ParameterKey key)
    {
        var f=Resolve(doc,input.References[label]);
        if(key==CadHarness.Ir.V03.ParameterKey.ExtrusionDepth)return ((IExtrudeFeatureData2)f.GetDefinition()).GetDepth(true)*1000;
        if(key==CadHarness.Ir.V03.ParameterKey.PatternCount)return ((ILinearPatternFeatureData)f.GetDefinition()).D1TotalInstances;
        if(key==CadHarness.Ir.V03.ParameterKey.PatternSpacing)return ((ILinearPatternFeatureData)f.GetDefinition()).D1Spacing*1000;
        var sketches=Items<IFeature>(f.GetParents()).Where(p=>p.GetTypeName2()=="ProfileFeature"&&p.GetSpecificFeature2() is ISketch).ToArray();
        Program.Check(sketches.Length==1,"Oracle circle profile ownership ambiguous.");
        var segments=Items<ISketchSegment>(((ISketch)sketches[0].GetSpecificFeature2()).GetSketchSegments()).Where(s=>!s.ConstructionGeometry).ToArray();
        Program.Check(segments.Length==1&&segments[0] is ISketchArc arc&&arc.IsCircle()==1,"Oracle circle profile unsupported.");
        return ((ISketchArc)segments[0]).GetRadius()*2000;
    }
    internal static object Read(IModelDoc2 doc,OracleInput spec)
    {
        Program.Check(doc.ConfigurationManager.ActiveConfiguration.Name==spec.Configuration&&doc.GetConfigurationCount()==1,"Oracle configuration mismatch.");
        Program.Check(doc.GetEquationMgr() is IEquationMgr equations&&equations.GetCount()==spec.EquationCount,"Oracle equation history differs.");
        var facts=new List<object>();
        foreach(var pair in spec.References.Where(p=>p.Key is "host" or "seed" or "pattern" or "hole_b" or "hole_c"))
        {
            var f=Resolve(doc,pair.Value);var error=f.GetErrorCode2(out var warning);
            Program.Check(error==0&&!warning&&!f.IsSuppressed(),"Oracle feature unhealthy: "+pair.Key);
            var expectedType=pair.Key=="host"?"Extrusion":pair.Key=="pattern"?"LPattern":"Cut";
            Program.Check(f.GetTypeName()==expectedType,"Oracle native definition type mismatch: "+pair.Key);
            var parents=Items<IFeature>(f.GetParents()).Select(p=>Convert.ToBase64String((byte[])doc.Extension.GetPersistReference3(p))).ToArray();
            if(pair.Key=="pattern")Program.Check(parents.Contains(spec.References[spec.Pattern.Seed]),"Oracle pattern seed parent differs.");
            if(pair.Key is "hole_b" or "hole_c")Program.Check(!parents.Contains(spec.References[pair.Key=="hole_b"?"hole_c":"hole_b"]),"Independent targets became dependent.");
            facts.Add(new{label=pair.Key,reference=pair.Value,name=f.Name,nativeType=f.GetTypeName2(),underlyingType=f.GetTypeName(),parents});
        }
        Near(Value(doc,spec,"host",CadHarness.Ir.V03.ParameterKey.ExtrusionDepth),spec.Depth,1e-6);
        foreach(var h in spec.Holes)
        {
            var data=(IExtrudeFeatureData2)Resolve(doc,spec.References[h.Label]).GetDefinition();
            Program.Check(data.GetEndCondition(true)==1&&!data.BothDirections,"Oracle cut must remain single direction through-all.");
            Near(Value(doc,spec,h.Label,CadHarness.Ir.V03.ParameterKey.HoleDiameter),h.Diameter,1e-6);
        }
        var pattern=(ILinearPatternFeatureData)Resolve(doc,spec.References[spec.Pattern.Label]).GetDefinition();
        Program.Check(pattern.D1TotalInstances==spec.Pattern.Count&&!pattern.IsDirection2Specified()&&!pattern.GeometryPattern&&pattern.GetSkippedItemCount()==0,"Oracle pattern definition differs.");
        Near(pattern.D1Spacing*1000,spec.Pattern.Spacing,1e-6);
        var bodies=Items<IBody2>(((IPartDoc)doc).GetBodies2((int)swBodyType_e.swAllBodies,false)).ToArray();
        Program.Check(bodies.Length==1&&bodies[0].GetType()==(int)swBodyType_e.swSolidBody,"Oracle single solid required.");
        var instances=spec.Holes.Select(h=>(h.X,h.Y,h.Diameter,Depth:spec.Depth)).ToList();
        var seed=spec.Holes.Single(h=>h.Label==spec.Pattern.Seed);
        for(var i=1;i<spec.Pattern.Count;i++)instances.Add((seed.X+i*spec.Pattern.Spacing,seed.Y,seed.Diameter,spec.Depth));
        if(spec.BlindDepth>0)instances.Add((spec.BlindX,spec.BlindY,spec.BlindDiameter,spec.BlindDepth));
        var expectedVolume=spec.Width*spec.Height*spec.Depth-instances.Sum(h=>Math.PI*h.Diameter*h.Diameter/4*h.Depth);
        var volume=((double[])bodies[0].GetMassProperties(1))[3]*1e9;Near(volume,expectedVolume,Math.Max(0.01,expectedVolume*1e-7));
        var bounds=(double[])bodies[0].GetBodyBox();var expectedBounds=new[]{-spec.Width/2,-spec.Height/2,0,spec.Width/2,spec.Height/2,spec.Depth};
        for(var i=0;i<6;i++)Near(bounds[i]*1000,expectedBounds[i],0.01);
        var faces=Items<IFace2>(bodies[0].GetFaces()).ToArray();
        Program.Check(faces.All(f=>f.GetSurface() is ISurface s&&(s.IsPlane()||s.IsCylinder())),"Oracle unexpected face surface.");
        var cylinders=faces.Where(f=>((ISurface)f.GetSurface()).IsCylinder()).ToArray();
        Program.Check(cylinders.Length==instances.Count,"Oracle full cylinder count differs.");
        var measurements=new List<object>();var used=new HashSet<int>();
        foreach(var expected in instances)
        {
            var matches=cylinders.Select((f,i)=>(f,i,p:(double[])((ISurface)f.GetSurface()).CylinderParams)).Where(c=>Math.Abs(c.p[0]*1000-expected.X)<0.001&&Math.Abs(c.p[1]*1000-expected.Y)<0.001).ToArray();
            Program.Check(matches.Length==1&&used.Add(matches[0].i),"Oracle missing/ambiguous cylinder center.");var c=matches[0];
            Near(c.p[6]*2000,expected.Diameter,0.001);Near(Math.Abs(c.p[5]),1,1e-8);
            var levels=Items<IEdge>(c.f.GetEdges()).Select(e=>(ICurve)e.GetCurve()).Where(e=>e.IsCircle()).Select(e=>((double[])e.CircleParams)[2]*1000).OrderBy(z=>z).ToArray();
            Program.Check(levels.Length==2,"Oracle circular boundaries missing.");Near(levels[0],0,0.001);Near(levels[1],expected.Depth,0.001);
            measurements.Add(new{x=expected.X,y=expected.Y,diameter=c.p[6]*2000,levels});
        }
        return new{kind="Independent M14 official API measurement; not Factory Reader or production analytic oracle",configuration=spec.Configuration,features=facts,volume,expectedVolume,boundsMm=bounds.Select(x=>x*1000),cylinders=measurements};
    }
    internal static double DrivingDiameter(IModelDoc2 doc,OracleInput input,string label)
    {
        var f=Resolve(doc,input.References[label]);var owner=Items<IFeature>(f.GetParents()).Single(p=>p.GetTypeName2()=="ProfileFeature");
        var dimensions=new List<(IDimension Dimension,int Type)>();var display=owner.GetFirstDisplayDimension() as IDisplayDimension;
        while(display is not null)
        {
            if(display.Type2 is (int)swDimensionType_e.swDiameterDimension or (int)swDimensionType_e.swRadialDimension)dimensions.Add(((IDimension)display.GetDimension2(0),display.Type2));
            Program.Check(dimensions.Count<=64,"Independent native dimension bound exceeded.");display=owner.GetNextDisplayDimension(display) as IDisplayDimension;
        }
        Program.Check(dimensions.Count==1&&dimensions[0].Dimension.DrivenState==(int)swDimensionDrivenState_e.swDimensionDriving,"Independent driving dimension not unique.");
        return dimensions[0].Dimension.SystemValue*(dimensions[0].Type==(int)swDimensionType_e.swDiameterDimension?1000:2000);
    }
    private static void Near(double actual,double expected,double tolerance)=>Program.Check(double.IsFinite(actual)&&Math.Abs(actual-expected)<=tolerance,$"Independent oracle mismatch {actual:R} != {expected:R}.");
}
