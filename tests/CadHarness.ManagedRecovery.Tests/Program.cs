using System;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[1] == "--pure") return PureTests.Run(args[0]);
        if (args.Length == 1 && args[0] == "--connection-probe")
        {
            using var connection = CadHarness.SolidWorks.SolidWorksConnection.Connect();
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { connection.StartedApplication,
                processId = connection.Application.GetProcessID(), revision = connection.Application.RevisionNumber(),
                documents = connection.Application.GetDocumentCount(), nativePartsCreated = 0, nativeOpenCycles = 0 }));
            return 0;
        }
        if (args.Length is 3 or 4 && args[1] == "--worker" && args[2] is "create" or "migrate" or "interrupt" or "recover-edit" or "readback")
            return NativeWorker.Run(args[0], args[2], args.Length == 4 ? args[3] : null);
        Console.WriteLine("Usage: <workspace> --pure | --worker create|interrupt|recover-edit|readback [template]"); return 2;
    }
}
