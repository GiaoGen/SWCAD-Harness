using System;
using System.IO;
using System.Text.Json;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 3 && args[1] == "--t3-pure") return T3Tests.Pure(args[0], args[2]);
            if (args.Length is 5 or 6 && args[1] == "--t3") return T3Tests.Run(args[0],args[2],args[3],args[4],args.Length==6?args[5]:null);
            if (args.Length == 3 && args[1] == "--t2-pure") return T2Tests.Pure(args[0], args[2]);
            if (args.Length is 5 or 6 && args[1] == "--t2") return T2Tests.Run(args[0], args[2], args[3], args[4], args.Length == 6 ? args[5] : null);
            if (args.Length == 3 && args[1] == "--t1-pure") return T1Tests.Pure(args[0], args[2]);
            if (args.Length == 4 && args[1] == "--t1") return T1Tests.Run(args[0], args[2], args[3]);
            if (args.Length == 2 && args[1] == "--pure") return PureTests.Run(args[0]);
            if (args.Length is 4 or 5 && args[1] == "--native") return NativeTests.Run(args[0], args[2], args[3], args.Length == 5 ? args[4] : null);
            return 2;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    internal static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    internal static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(file, value, new JsonSerializerOptions { WriteIndented = true }); file.Flush(true);
    }
}
