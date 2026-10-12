using System;
using System.Linq;
using SolidWorks.Interop.sldworks;
internal static partial class Program
{
    private static void BracketOracle(IModelDoc2 document,double height,bool fillet,Action<string,object> log)
    {
        // Frozen test geometry and independent native measurement; no production
        // material proof, selector, binding or expected state is used here.
        var body=Items<IBody2>(((IPartDoc)document).GetBodies2(0,false)).Single();var faces=Items<IFace2>(body.GetFaces());
        var cylinders=faces.Where(f=>((ISurface)f.GetSurface()).IsCylinder()).Select(f=>new { p=(double[])((ISurface)f.GetSurface()).CylinderParams,uv=(double[])f.GetUVBounds(),area=f.GetArea()*1e6,
            rings=Items<IEdge>(f.GetEdges()).Select(e=>(ICurve)e.GetCurve()).Where(c=>c.IsCircle()).Select(c=>(double[])c.CircleParams).ToArray() }).ToArray();
        var bores=new[] { new[]{30d,-10,0,2,0,8},new[]{30d,10,0,2,0,8},new[]{0d,0,30,0,0,8} };
        var holeMatch=bores.All(e=>cylinders.Count(c=>Math.Abs(c.p[6]*1000-3)<1e-6 && Math.Abs(Math.Abs(c.p[3+(int)e[3]])-1)<1e-8 &&
            Enumerable.Range(0,3).Where(i=>i!=(int)e[3]).All(i=>Math.Abs(c.p[i]*1000-e[i])<1e-6) && Math.Abs(c.uv[1]-c.uv[0]-2*Math.PI)<1e-8 && c.rings.Length==2 &&
            Math.Abs(c.rings.Min(r=>r[(int)e[3]])*1000-e[4])<1e-6 && Math.Abs(c.rings.Max(r=>r[(int)e[3]])*1000-e[5])<1e-6)==1);
        var cornerMatch=!fillet || cylinders.Count(c=>Math.Abs(c.p[6]*1000-1)<1e-6 && Math.Abs(c.p[0]*1000-1)<1e-6 && Math.Abs(c.p[2]*1000-(8+height-1))<1e-6 &&
            Math.Abs(Math.Abs(c.p[4])-1)<1e-8 && Math.Abs(c.uv[1]-c.uv[0]-Math.PI/2)<1e-7 && Math.Abs(c.area-20*Math.PI)<1e-4 && c.rings.Length==2 &&
            Math.Abs(c.rings.Min(r=>r[1])*1000+20)<1e-6 && Math.Abs(c.rings.Max(r=>r[1])*1000-20)<1e-6)==1;
        var lines=Items<IEdge>(body.GetEdges()).Where(e=>((ICurve)e.GetCurve()).IsLine()).Select(e=>new[]{(double[])((IVertex)e.GetStartVertex()).GetPoint(),(double[])((IVertex)e.GetEndVertex()).GetPoint()}).ToArray();
        var tangency=!fillet || new[]{new[]{0d,8+height-1},new[]{1d,8+height}}.All(v=>lines.Count(l=>l.All(p=>Math.Abs(p[0]*1000-v[0])<1e-6 && Math.Abs(p[2]*1000-v[1])<1e-6) && Math.Abs(l.Min(p=>p[1])*1000+20)<1e-6 && Math.Abs(l.Max(p=>p[1])*1000-20)<1e-6)==1);
        var normals=faces.Where(f=>((ISurface)f.GetSurface()).IsPlane()).Select(f=>new { normal=(double[])f.Normal,area=f.GetArea()*1e6 }).ToArray();
        var installation=normals.Count(f=>Math.Abs(f.normal[2]+1)<1e-8 && Math.Abs(f.area-(2400-18*Math.PI))<1e-4)==1 &&
            normals.Any(f=>Math.Abs(f.normal[0]+1)<1e-8 && Math.Abs(f.area-(40*(8+height)-(fillet?40:0)-9*Math.PI))<1e-4);
        var bounds=new double[6];for(var i=0;i<6;i++){var axis=i%3;var sign=i<3?-1:1;Program.Check(body.GetExtremePoint(axis==0?sign:0,axis==1?sign:0,axis==2?sign:0,out var x,out var y,out var z),"Oracle supports");bounds[i]=new[]{x,y,z}[axis]*1000;}
        var volume=((double[])body.GetMassProperties(1))[3]*1e9;var expected=60*40*8+8*40*height-216*Math.PI-(fillet?(1-Math.PI/4)*40:0);
        var passed=holeMatch && cornerMatch && tangency && installation && cylinders.Length==(fillet?4:3) && faces.All(f=>((ISurface)f.GetSurface()).IsPlane() || ((ISurface)f.GetSurface()).IsCylinder()) &&
            Math.Abs(volume-expected)<.01 && bounds.Zip(new[]{0d,-20,0,60,20,8+height}).All(p=>Math.Abs(p.First-p.Second)<.01);
        log("independent-oracle",new { passed,height,fillet,volume,expected,difference=volume-expected,bounds,cylinders,lines,normals,holeMatch,cornerMatch,tangency,installation });Program.Check(passed,"Independent cross-axis/fillet Oracle failed");
    }
}

