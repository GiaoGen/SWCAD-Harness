using System;
using System.IO;
using System.Reflection;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length != 1 || Environment.Version.Major != 8 || !Environment.Is64BitProcess)
        { Console.WriteLine("FAIL: .NET 8 x64 runtime and configured interop directory are required."); return 1; }
        Console.WriteLine("PASS .NET 8 x64 runtime");
        try
        {
            foreach (var name in new[] { "SolidWorks.Interop.sldworks", "SolidWorks.Interop.swconst" })
                if (AssemblyName.GetAssemblyName(Path.Combine(args[0], name + ".dll")).Name != name)
                    throw new InvalidDataException("Interop identity differs.");
            Console.WriteLine("PASS installed interop assembly metadata (no COM activation)");
            Console.WriteLine("2/2 bootstrap pure checks passed; native Parts created=0, closed=0.");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL " + error.Message); return 1; }
    }
}
