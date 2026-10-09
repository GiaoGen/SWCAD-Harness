using CadFixtureFactory;

namespace CadFixtureFactory.Reader;

// This oracle computes its expectations from the test specification, not the
// builder's manifest expectation or construction implementation.
public static class IndependentOracle
{
    public static void VerifyHostDimensionState(bool equationDriven,int nativeState,bool designTable)
    {
        // Official swDimensionDrivenState_e: driven=1, driving=2. A genuine
        // equation-driven dimension is driven; it must also pass equation binding.
        if(designTable||nativeState!=(equationDriven?1:2))throw new InvalidDataException("Host native dimension state does not match its independent driver specification.");
    }
    public static string EquationTarget(string equation,double expectedMm)
    {
        var match=System.Text.RegularExpressions.Regex.Match(equation,@"^\s*""([^""]+)""\s*=\s*([0-9]+(?:\.[0-9]+)?)\s*mm\s*$",System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if(!match.Success)throw new InvalidDataException("Equation negative must be a single literal-mm dimension assignment.");
        Near(double.Parse(match.Groups[2].Value,System.Globalization.CultureInfo.InvariantCulture),expectedMm,"Equation specification value");
        return match.Groups[1].Value;
    }
    public static IReadOnlyList<CylinderFact> Expected(FixtureSpec s)
    {
        var list=new List<CylinderFact>();
        foreach(var h in s.Holes)
        {
            var number=h.Label==s.Pattern.Seed?s.Pattern.Count:1;
            for(var i=0;i<number;i++)list.Add(new(h.XMm+i*s.Pattern.SpacingMm,h.YMm,h.DiameterMm/2,1,new[]{0.0,s.DepthMm}));
        }
        if(s.BlindCut is {} b)list.Add(new(b.XMm,b.YMm,b.DiameterMm/2,1,new[]{0.0,b.DepthMm}));
        return list;
    }
    public static double ExpectedVolume(FixtureSpec s)=>s.WidthMm*s.HeightMm*s.DepthMm-
        Expected(s).Sum(c=>Math.PI*c.RadiusMm*c.RadiusMm*(c.BoundaryZMm[1]-c.BoundaryZMm[0]));
    public static void Near(double actual,double expected,string label,double tolerance=0.0001)
    {if(!double.IsFinite(actual)||Math.Abs(actual-expected)>tolerance)throw new InvalidDataException($"{label}: actual {actual:R}; expected {expected:R}.");}
    public static void VerifyCylinders(FixtureSpec spec,IReadOnlyList<CylinderFact> actual)
    {
        var remaining=actual.ToList();
        foreach(var e in Expected(spec))
        {
            var matches=remaining.Where(a=>Math.Abs(a.XMm-e.XMm)<0.0001&&Math.Abs(a.YMm-e.YMm)<0.0001&&Math.Abs(a.RadiusMm-e.RadiusMm)<0.0001).ToArray();
            if(matches.Length!=1)throw new InvalidDataException("Missing/ambiguous independently measured cylinder.");
            var c=matches[0];Near(Math.Abs(c.AxisZ),1,"Cylinder world axis");
            if(c.BoundaryZMm.Count!=2)throw new InvalidDataException("Cylinder must have exactly two full-circle boundaries.");
            Near(c.BoundaryZMm.Min(),e.BoundaryZMm[0],"Cylinder lower boundary");Near(c.BoundaryZMm.Max(),e.BoundaryZMm[1],"Cylinder upper boundary");remaining.Remove(c);
        }
        if(remaining.Count!=0)throw new InvalidDataException("Unexpected cylindrical walls.");
    }
    public static bool HasPath(string from,string to,IReadOnlyDictionary<string,IReadOnlyList<string>> children)
    {
        var visited=new HashSet<string>();var queue=new Queue<string>();queue.Enqueue(from);
        while(queue.Count>0)
        {var key=queue.Dequeue();if(!visited.Add(key))continue;if(key==to)return true;if(visited.Count>4096)throw new InvalidDataException("Dependency traversal limit.");
         if(children.TryGetValue(key,out var next))foreach(var n in next)queue.Enqueue(n);}
        return false;
    }
}
