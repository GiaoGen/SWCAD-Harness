using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadHarness.Ir;
using CadHarness.Ir.V03;
using CadHarness.State;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(args[0]);
            if (args.Contains("--pure")) return PureTests.Run(root);
            if (args.Contains("--native")) return NativeTests.Run(root, args[2], args[3], args[4], args.Length > 5 ? args[5] : null);
            throw new ArgumentException("Select --pure or an explicitly frozen --native slot.");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error); return 1;
        }
    }
    internal static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void WriteNew(string path, object value)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true);
    }
}
