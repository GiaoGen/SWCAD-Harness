using System;
using System.Linq;
using SolidWorks.Interop.sldworks;

internal static partial class Program
{
    private static T[] Items<T>(object value)=>NativeEditOracle.Items<T>(value).ToArray();
    private static double[][][] Lines(IBody2 body)=>Items<IEdge>(body.GetEdges()).Where(e=>((ICurve)e.GetCurve()).IsLine())
        .Select(e=>new[]{((double[])((IVertex)e.GetStartVertex()).GetPoint()).Select(v=>v*1000).ToArray(),((double[])((IVertex)e.GetEndVertex()).GetPoint()).Select(v=>v*1000).ToArray()}).ToArray();
    internal static bool TangentLines(double[][][] lines,double x,double y,double bottom,double top,double radius=3,double halfSpan=7)
        =>new[]{bottom,top}.All(z=>new[]{x-radius,x+radius}.All(side=>lines.Count(e=>e.Length==2&&e.All(p=>p.Length==3&&Math.Abs(p[0]-side)<.01&&Math.Abs(p[2]-z)<.01)&&
            Math.Abs(e.Min(p=>p[1])-(y-halfSpan))<.01&&Math.Abs(e.Max(p=>p[1])-(y+halfSpan))<.01&&Math.Abs(Math.Sqrt(e[0].Zip(e[1]).Sum(p=>Math.Pow(p.First-p.Second,2)))-2*halfSpan)<.01)==1));
    private static void Oracle(IModelDoc2 doc,TaskDefinition task,GeometryPhase expected,Action<string,object> log)
    {
        if(task.Family=="C3"){BracketOracle(doc,expected.Value,true,log);return;}
        var bodies=Items<IBody2>(((IPartDoc)doc).GetBodies2(0,false));Check(bodies.Length==1,"Oracle requires one body");var body=bodies.Single();
        var volume=((double[])body.GetMassProperties(1))[3]*1e9;var bounds=new double[6];
        for(var i=0;i<6;i++){var k=i%3;var s=i<3?-1:1;Check(body.GetExtremePoint(k==0?s:0,k==1?s:0,k==2?s:0,out var x,out var y,out var z),"Oracle extreme missing");bounds[i]=new[]{x,y,z}[k]*1000;}
        var faces=Items<IFace2>(body.GetFaces());var cylinders=faces.Where(f=>((ISurface)f.GetSurface()).IsCylinder()).Select(f=>new{p=(double[])((ISurface)f.GetSurface()).CylinderParams,uv=(double[])f.GetUVBounds(),
            rings=Items<IEdge>(f.GetEdges()).Where(e=>((ICurve)e.GetCurve()).IsCircle()).Select(e=>new{p=(double[])((ICurve)e.GetCurve()).CircleParams,
                a=Vertex(e.GetStartVertex()),b=Vertex(e.GetEndVertex())}).ToArray()}).ToArray();
        var isSlot=task.Family=="C4";var axis=isSlot?2:0;var angle=isSlot?Math.PI:2*Math.PI;
        var match=cylinders.Length==expected.Cylinders.Length&&expected.Cylinders.All(e=>cylinders.Count(n=>Math.Abs(n.p[6]*1000-e[0])<.01&&
            Math.Abs(Math.Abs(n.p[3+axis])-1)<1e-8&&Math.Abs(n.uv[1]-n.uv[0]-angle)<1e-8&&n.rings.Length==2&&
            (isSlot?Math.Abs(n.p[0]*1000-e[1])<.01&&Math.Abs(n.p[1]*1000-e[2])<.01:Math.Abs(n.p[1])<1e-8&&Math.Abs(n.p[2])<1e-8)&&
            Math.Abs(n.rings.Min(r=>r.p[axis])*1000-e[isSlot?3:1])<.01&&Math.Abs(n.rings.Max(r=>r.p[axis])*1000-e[isSlot?4:2])<.01)==1);
        var lines=Lines(body);bool tangent=true,arcs=true,floors=true;
        if(isSlot)
        {
            tangent=Enumerable.Range(0,3).All(i=>TangentLines(lines,(i-1)*30,0,8-(i==0?expected.Value:3),8));
            arcs=cylinders.All(c=>c.rings.All(r=>Math.Abs(r.p[6]*1000-3)<.01&&Math.Abs(Math.Abs(r.p[5])-1)<1e-8&&
                new[]{-1,1}.All(sign=>new[]{r.a,r.b}.Count(p=>p.Length==3&&Math.Abs(p[0]-(c.p[0]*1000+sign*3))<.01&&Math.Abs(p[1]-c.p[1]*1000)<.01&&Math.Abs(p[2]-r.p[2]*1000)<.01)==1)));
            var floorFaces=faces.Where(f=>((ISurface)f.GetSurface()).IsPlane()).Select(f=>new{normal=(double[])f.Normal,area=f.GetArea()*1e6,box=(double[])f.GetBox()}).ToArray();
            floors=Enumerable.Range(0,3).All(i=>floorFaces.Count(f=>Math.Abs(Math.Abs(f.normal[2])-1)<1e-8&&Math.Abs(f.area-(84+9*Math.PI))<.01&&Math.Abs(f.box[2]*1000-(8-(i==0?expected.Value:3)))<.01&&Math.Abs((f.box[0]+f.box[3])*500-(i-1)*30)<.01)==1);
        }
        var surfaces=faces.All(f=>((ISurface)f.GetSurface()).IsPlane()||((ISurface)f.GetSurface()).IsCylinder());
        var passed=match&&tangent&&arcs&&floors&&surfaces&&Math.Abs(volume-expected.Volume)<.01&&bounds.Zip(expected.Bounds).All(p=>Math.Abs(p.First-p.Second)<.01);
        log("independent-oracle",new{passed,task.Id,expected,volume,difference=volume-expected.Volume,bounds,cylinders,lines,match,tangent,arcs,floors,surfaces});Check(passed,"Independent task geometry failed");
    }
    private static double[] Vertex(object? value)=>value is IVertex v?((double[])v.GetPoint()).Select(p=>p*1000).ToArray():Array.Empty<double>();
}
