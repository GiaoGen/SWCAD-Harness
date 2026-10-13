using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadHarness.Ir.V03;
using CadHarness.Planning;

internal sealed record CapsuleSlot(string Id,double X,double Y,double Width,double Length,double Angle,double Depth);
internal sealed record CapsuleGeometry(double Width,double Height,double Thickness,CapsuleSlot[] Slots);
internal sealed record ExternalAction(string Kind,ExternalValue[] Edits);
internal sealed record ExternalValue(string Label,ParameterKey Parameter,double OldValue,double NewValue);
internal sealed record ExternalProtocol(OracleInput Initial,ExternalAction[] Actions,OracleInput[] Expected,bool FirstEditFault);
internal static partial class Program
{
    private static void ValidateCapsuleTask(TaskDefinition task)
    {
        Check(ContractValidation.Id(task.Id)&&task.Family=="C4"&&task.Request.SemanticDraftVersion==2&&task.Program is null&&task.DraftJson=="","Live held-out task must not provide an answer program/draft");
        Check(task.Expected is {Length:3}&&task.Request.Input.Intent==task.Intent,"Three independent geometry phases and exact intent required");
        _=ConstructionRequirements.Normalize(task.Request);
        var edits=task.Request.EditabilityRequirements;
        Check(edits is {Count:1}&&edits[0].Parameter==ParameterKey.CutDepth&&edits[0].TestValue==task.NewValue,"Frozen cut-depth edit role required");
        foreach(var g in task.Expected!)
        {
            Check(new[]{g.Width,g.Height,g.Thickness}.All(v=>double.IsFinite(v)&&v>0)&&g.Slots.Length==3&&g.Slots.Select(s=>s.Id).Distinct().Count()==3,"Capsule descriptor domain");
            foreach(var s in g.Slots)
            {
                Check(new[]{s.X,s.Y,s.Width,s.Length,s.Angle,s.Depth}.All(double.IsFinite)&&s.Width>0&&s.Length>s.Width&&s.Depth>0&&s.Depth<g.Thickness,"Capsule dimensions/wall invalid");
                var a=s.Angle*Math.PI/180;var half=(s.Length-s.Width)/2;var r=s.Width/2;
                Check(Math.Abs(s.X)+Math.Abs(Math.Sin(a))*half+r<g.Width/2&&Math.Abs(s.Y)+Math.Abs(Math.Cos(a))*half+r<g.Height/2,"Capsule outside host");
            }
            for(var i=0;i<3;i++)for(var j=i+1;j<3;j++)Check(CapsuleSeparation(g.Slots[i],g.Slots[j])>0,"Capsules overlap/touch");
        }
        var initial=task.Expected![0];var edited=task.Expected[1];var cold=task.Expected[2];
        Check(initial.Slots.Count(s=>Math.Abs(s.Depth-task.OldValue)<1e-8)>0,"Initial edit value absent");
        var changed=initial.Slots.Zip(edited.Slots).Where(p=>p.First!=p.Second).ToArray();
        Check(changed.Length==1&&changed[0].Second==changed[0].First with{Depth=task.NewValue}&&changed[0].First.Depth==task.OldValue,
            "Specified edit must change exactly one depth");
        Check(initial.Width==edited.Width&&initial.Height==edited.Height&&initial.Thickness==edited.Thickness&&
            edited.Width==cold.Width&&edited.Height==cold.Height&&edited.Thickness==cold.Thickness&&
            Canonical(cold.Slots)==Canonical(edited.Slots.Select(s=>s.Id==changed[0].First.Id?s with{Depth=task.ColdValue}:s).ToArray()),"Cold expectation changes untouched geometry");
    }
    // Independent 2D segment distance, not the production material integrator.
    private static double CapsuleSeparation(CapsuleSlot a,CapsuleSlot b)
    {
        double[][] Ends(CapsuleSlot s){var h=(s.Length-s.Width)/2;var angle=s.Angle*Math.PI/180;return new[]{-1,1}.Select(n=>new[]{s.X-n*Math.Sin(angle)*h,s.Y+n*Math.Cos(angle)*h}).ToArray();}
        double Cross(double[] p,double[] q,double[] r)=>(q[0]-p[0])*(r[1]-p[1])-(q[1]-p[1])*(r[0]-p[0]);
        double Distance(double[] p,double[] x,double[] y){var dx=y[0]-x[0];var dy=y[1]-x[1];var t=Math.Clamp(((p[0]-x[0])*dx+(p[1]-x[1])*dy)/(dx*dx+dy*dy),0,1);return Math.Sqrt(Math.Pow(p[0]-x[0]-t*dx,2)+Math.Pow(p[1]-x[1]-t*dy,2));}
        var x=Ends(a);var y=Ends(b);
        if(Cross(x[0],x[1],y[0])*Cross(x[0],x[1],y[1])<=0&&Cross(y[0],y[1],x[0])*Cross(y[0],y[1],x[1])<=0&&
            Math.Max(x.Min(p=>p[0]),y.Min(p=>p[0]))<=Math.Min(x.Max(p=>p[0]),y.Max(p=>p[0]))&&Math.Max(x.Min(p=>p[1]),y.Min(p=>p[1]))<=Math.Min(x.Max(p=>p[1]),y.Max(p=>p[1])))return -1;
        return new[]{Distance(x[0],y[0],y[1]),Distance(x[1],y[0],y[1]),Distance(y[0],x[0],x[1]),Distance(y[1],x[0],x[1])}.Min()-(a.Width+b.Width)/2;
    }
    private static double ExternalValueOf(OracleInput s,ExternalValue e)=>e.Parameter switch
    {
        ParameterKey.ExtrusionDepth=>s.Depth,ParameterKey.PatternCount=>s.Pattern.Count,ParameterKey.PatternSpacing=>s.Pattern.Spacing,
        ParameterKey.HoleDiameter=>s.Holes.Single(h=>h.Label==e.Label).Diameter,_=>throw new InvalidDataException("Unqualified external parameter")
    };
    private static void ValidateExternalProtocol(ExternalProtocol p)
    {
        VerifyIdentity(p.Initial.Source);
        Check(p.Actions.Length>=2&&p.Actions.Length<=8&&p.Expected.Length==p.Actions.Length+1&&Canonical(p.Initial)==Canonical(p.Expected[0]),"External initial/phase count mismatch");
        Check(p.Actions.Any(a=>a.Kind=="scalar"&&a.Edits.Length==1)&&p.Actions.Any(a=>a.Kind=="batch"&&a.Edits.Length>=2),"Actual scalar and same-Part batch required");
        var expected=p.Initial;
        for(var i=0;i<p.Actions.Length;i++)
        {
            var a=p.Actions[i];Check(a.Kind is "scalar" or "batch"&&a.Edits.Length is >0 and <=4&&(a.Kind!="scalar"||a.Edits.Length==1)&&
                a.Edits.Select(e=>(e.Label,e.Parameter)).Distinct().Count()==a.Edits.Length,"External action bounds/duplicate");
            foreach(var e in a.Edits)
            {
                Check(p.Initial.References.ContainsKey(e.Label)&&double.IsFinite(e.NewValue)&&e.NewValue>0&&Math.Abs(ExternalValueOf(expected,e)-e.OldValue)<1e-8,"External identity/old value mismatch");
                Check(e.Parameter!=ParameterKey.PatternCount||e.NewValue==Math.Truncate(e.NewValue),"Pattern count not integral");
                expected=Changed(expected,e.Label,e.Parameter,e.NewValue);
            }
            Check(Canonical(expected)==Canonical(p.Expected[i+1]),"External Oracle altered an untouched target or driving parameter");
            ValidateExternalShape(expected);
        }
        ValidateExternalShape(p.Initial);
    }
    private static void ValidateExternalShape(OracleInput s)
    {
        Check(new[]{s.Width,s.Height,s.Depth,s.Pattern.Spacing}.All(v=>double.IsFinite(v)&&v>0)&&s.Pattern.Count is >=2 and <=128&&s.Holes.Count>=3,"External supported geometry domain");
        var seed=s.Holes.Single(h=>h.Label==s.Pattern.Seed);var circles=s.Holes.Concat(Enumerable.Range(1,s.Pattern.Count-1).Select(i=>seed with{X=seed.X+i*s.Pattern.Spacing})).ToArray();
        foreach(var h in circles)Check(double.IsFinite(h.Diameter)&&h.Diameter>0&&Math.Abs(h.X)+h.Diameter/2<s.Width/2&&Math.Abs(h.Y)+h.Diameter/2<s.Height/2,"External hole outside host");
        for(var i=0;i<circles.Length;i++)for(var j=i+1;j<circles.Length;j++)Check(Math.Sqrt(Math.Pow(circles[i].X-circles[j].X,2)+Math.Pow(circles[i].Y-circles[j].Y,2))>(circles[i].Diameter+circles[j].Diameter)/2,"External holes overlap");
    }
    private static ExternalProtocol RegressionProtocol(OracleInput original)
    {
        var spec=original;var actions=new List<ExternalAction>();var phases=new List<OracleInput>{original};
        void A(string kind,params ExternalValue[] edits){actions.Add(new(kind,edits));foreach(var e in edits)spec=Changed(spec,e.Label,e.Parameter,e.NewValue);phases.Add(spec);}
        A("scalar",new ExternalValue("host",ParameterKey.ExtrusionDepth,original.Depth,original.Depth+1.5));
        A("scalar",new ExternalValue("hole_b",ParameterKey.HoleDiameter,original.Holes.Single(h=>h.Label=="hole_b").Diameter,original.Holes.Single(h=>h.Label=="hole_b").Diameter+.5));
        A("scalar",new ExternalValue("pattern",ParameterKey.PatternCount,original.Pattern.Count,original.Id=="manual"?3:4));
        A("scalar",new ExternalValue("pattern",ParameterKey.PatternSpacing,original.Pattern.Spacing,original.Id=="manual"?24.5:22));
        A("batch",new("hole_b",ParameterKey.HoleDiameter,spec.Holes.Single(h=>h.Label=="hole_b").Diameter,spec.Holes.Single(h=>h.Label=="hole_b").Diameter+.75),
            new("hole_c",ParameterKey.HoleDiameter,spec.Holes.Single(h=>h.Label=="hole_c").Diameter,spec.Holes.Single(h=>h.Label=="hole_c").Diameter+1.25));
        return new(original,actions.ToArray(),phases.ToArray(),true);
    }
}
