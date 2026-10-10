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
            if (args.Length == 2 && args[1] == "--pure") return PureTests.Run(args[0]);
            if (args.Length == 3 && args[1] == "--native") return NativeTests.Run(args[0], args[2]);
            if (args.Length == 4 && args[1] == "--native-copy") return NativeTests.Run(args[0], args[2], args[3]);
            if (args.Length == 3 && args[1] == "--recover-owned") return NativeTests.RecoverOwned(args[0], args[2]);
            Console.Error.WriteLine("Usage: <workspace> --pure | --native <unique-run-id>"); return 2;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    internal static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, value, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true);
    }
}
