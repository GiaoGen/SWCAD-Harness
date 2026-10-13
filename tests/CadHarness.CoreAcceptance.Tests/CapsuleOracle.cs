using System;
using System.Linq;
using SolidWorks.Interop.sldworks;

internal sealed record MeasuredRing(double[] Parameters,double[] Start,double[] End);
internal sealed record MeasuredCylinder(double[] Parameters,double Sweep,MeasuredRing[] Rings);
internal sealed record MeasuredPlane(double[] Normal,double Area,double[] Bounds);
internal sealed record CapsuleMeasurement(double Volume,double[] Bounds,MeasuredCylinder[] Cylinders,double[][][] Lines,MeasuredPlane[] Planes,bool SupportedSurfaces);
internal static partial class Program
{
    private static CapsuleGeometry CoreCapsules(double value)=>new(100,60,8,Enumerable.Range(0,3).Select(i=>new CapsuleSlot("slot"+i,(i-1)*30,0,6,20,0,i==0?value:3)).ToArray());
    private static CapsuleMeasurement MeasureCapsules(IBody2 body)
    {
        var faces=Items<IFace2>(body.GetFaces());var bounds=new double[6];
        for(var i=0;i<6;i++){var k=i%3;var sign=i<3?-1:1;Check(body.GetExtremePoint(k==0?sign:0,k==1?sign:0,k==2?sign:0,out var x,out var y,out var z),"Capsule support missing");bounds[i]=new[]{x,y,z}[k]*1000;}
        var cylinders=faces.Where(f=>((ISurface)f.GetSurface()).IsCylinder()).Select(f=>new MeasuredCylinder((double[])((ISurface)f.GetSurface()).CylinderParams,
            ((double[])f.GetUVBounds())[1]-((double[])f.GetUVBounds())[0],Items<IEdge>(f.GetEdges()).Where(e=>((ICurve)e.GetCurve()).IsCircle()).Select(e=>
                new MeasuredRing((double[])((ICurve)e.GetCurve()).CircleParams,Vertex(e.GetStartVertex()),Vertex(e.GetEndVertex()))).ToArray())).ToArray();
        var planes=faces.Where(f=>((ISurface)f.GetSurface()).IsPlane()).Select(f=>new MeasuredPlane((double[])f.Normal,f.GetArea()*1e6,((double[])f.GetBox()).Select(v=>v*1000).ToArray())).ToArray();
        return new(((double[])body.GetMassProperties(1))[3]*1e9,bounds,cylinders,Lines(body),planes,faces.All(f=>((ISurface)f.GetSurface()).IsPlane()||((ISurface)f.GetSurface()).IsCylinder()));
    }
    internal static bool MatchCapsules(CapsuleGeometry g,CapsuleMeasurement m)
    {
        bool Near(double a,double b,double tolerance=.01)=>double.IsFinite(a)&&Math.Abs(a-b)<tolerance;
        bool Point(double[] p,double[] q)=>p.Length==3&&p.Zip(q).All(v=>Near(v.First,v.Second));
        var expected=Tasks.CapsuleExpected(g,1,0);
        if(!m.SupportedSurfaces||!Near(m.Volume,expected.Volume)||m.Bounds.Length!=6||!m.Bounds.Zip(expected.Bounds).All(p=>Near(p.First,p.Second))||
            m.Cylinders.Length!=6||m.Planes.Length!=15)return false;
        foreach(var s in g.Slots)
        {
            var angle=s.Angle*Math.PI/180;var u=new[]{Math.Cos(angle),Math.Sin(angle)};var v=new[]{-Math.Sin(angle),Math.Cos(angle)};
            var h=(s.Length-s.Width)/2;var r=s.Width/2;var bottom=g.Thickness-s.Depth;
            foreach(var end in new[]{-1,1})
            {
                var cx=s.X+end*h*v[0];var cy=s.Y+end*h*v[1];
                var matches=m.Cylinders.Where(c=>Near(c.Parameters[0]*1000,cx)&&Near(c.Parameters[1]*1000,cy)&&Near(c.Parameters[6]*1000,r)&&Near(Math.Abs(c.Parameters[5]),1,1e-8)&&Near(c.Sweep,Math.PI,1e-8)).ToArray();
                if(matches.Length!=1||matches[0].Rings.Length!=2)return false;
                foreach(var z in new[]{bottom,g.Thickness})
                {
                    var ring=matches[0].Rings.Where(t=>Near(t.Parameters[2]*1000,z)).ToArray();
                    if(ring.Length!=1||!Near(ring[0].Parameters[6]*1000,r)||!Near(Math.Abs(ring[0].Parameters[5]),1,1e-8))return false;
                    foreach(var sign in new[]{-1,1})if(new[]{ring[0].Start,ring[0].End}.Count(p=>Point(p,new[]{cx+sign*r*u[0],cy+sign*r*u[1],z}))!=1)return false;
                }
            }
            foreach(var side in new[]{-1,1})foreach(var z in new[]{bottom,g.Thickness})
            {
                var start=new[]{s.X+side*r*u[0]-h*v[0],s.Y+side*r*u[1]-h*v[1],z};var end=new[]{s.X+side*r*u[0]+h*v[0],s.Y+side*r*u[1]+h*v[1],z};
                if(m.Lines.Count(l=>l.Length==2&&(Point(l[0],start)&&Point(l[1],end)||Point(l[1],start)&&Point(l[0],end))&&Near(Math.Sqrt(l[0].Zip(l[1]).Sum(p=>Math.Pow(p.First-p.Second,2))),2*h))!=1)return false;
            }
            var area=s.Width*(s.Length-s.Width)+Math.PI*r*r;
            if(m.Planes.Count(f=>Near(Math.Abs(f.Normal[2]),1,1e-8)&&Near(f.Area,area)&&Near(f.Bounds[2],bottom)&&Near(f.Bounds[5],bottom)&&
                Near((f.Bounds[0]+f.Bounds[3])/2,s.X)&&Near((f.Bounds[1]+f.Bounds[4])/2,s.Y))!=1)return false;
        }
        return true;
    }
    private static void CapsuleOracle(IModelDoc2 doc,CapsuleGeometry expected,Action<string,object> journal)
    {
        var bodies=Items<IBody2>(((IPartDoc)doc).GetBodies2(0,false));Check(bodies.Length==1,"Capsule Oracle single body required");
        var measurement=MeasureCapsules(bodies[0]);var passed=MatchCapsules(expected,measurement);
        journal("independent-capsule-oracle",new{passed,expected,measurement,volumeDifference=measurement.Volume-Tasks.CapsuleExpected(expected,1,0).Volume});
        Check(passed,"Independent descriptor capsule shape mismatch");
    }
}
