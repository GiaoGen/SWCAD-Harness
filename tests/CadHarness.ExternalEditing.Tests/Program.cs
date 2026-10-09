using System;
using System.IO;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args[1] == "--pure") return PureTests.Run(args[0]);
            if (args[1] == "--prepare-acceptance") return AcceptancePreparation.Run(args[0],args[2]);
            if (args[1] == "--freeze-stage") return AcceptancePreparation.Stage(args[0],args[2],args[3]);
            if (args[1] == "--replan-acceptance") return AcceptancePreparation.Replan(args[0],args[2],args[3]);
            if (args[1] == "--resume-acceptance") return AcceptancePreparation.Replan(args[0],args[2],args[3],true);
            return NativeTests.Run(args[0], args[1], args[2], args[3]);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    internal static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void WriteNew(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true);
    }
}
