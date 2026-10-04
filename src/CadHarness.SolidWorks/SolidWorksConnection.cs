using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Environment = System.Environment;

namespace CadHarness.SolidWorks;

public sealed class SolidWorksConnection : IDisposable
{
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private bool disposed;
    public ISldWorks Application { get; }
    public bool StartedApplication { get; }

    private SolidWorksConnection(ISldWorks application, bool started)
    { Application = application; StartedApplication = started; }

    public static SolidWorksConnection Connect()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("SOLIDWORKS requires a 64-bit Windows controller.");
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("SOLIDWORKS connection requires an STA thread.");
        Marshal.ThrowExceptionForHR(CLSIDFromProgID("SldWorks.Application", out var clsid));
        var status = GetActiveObject(ref clsid, IntPtr.Zero, out var running);
        if (status >= 0) return new((ISldWorks)running, false);
        // Only the normal 'not running' condition permits starting a COM server.
        if (status != unchecked((int)0x800401E3)) Marshal.ThrowExceptionForHR(status);
        var existingProcessIds = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("SLDWORKS"))
            using (process) existingProcessIds.Add(process.Id);
        var type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
        var application = (ISldWorks?)Activator.CreateInstance(type)
            ?? throw new COMException("SOLIDWORKS activation returned no application.");
        // Activation can reuse an existing process that has not registered in
        // the ROT. Do not claim ownership of that user's application session.
        return new(application, !existingProcessIds.Contains(application.GetProcessID()));
    }

    public string ResolvePartTemplate(string? configuredPath)
    {
        CheckThread();
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? Application.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart)
            : configuredPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".prtdot", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("Configure an existing SOLIDWORKS .prtdot template.", path);
        return Path.GetFullPath(path);
    }

    public SolidWorksExecutionContext CreatePart(string template)
    {
        CheckThread();
        if (!File.Exists(template) || !string.Equals(Path.GetExtension(template), ".prtdot", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("An existing .prtdot template is required.", nameof(template));
        var document = (IModelDoc2?)Application.NewDocument(template, 0, 0, 0)
            ?? throw new COMException("SOLIDWORKS NewDocument returned no Part.");
        return new(document);
    }

    private void CheckThread()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("The COM connection must stay on its owning STA thread.");
    }

    public void Dispose()
    {
        if (disposed) return;
        CheckThread();
        // An attached user session is never shut down. A session started here is
        // closed only when it contains no documents (including user-created ones).
        try
        {
            if (StartedApplication && Application.GetDocumentCount() == 0) Application.ExitApp();
        }
        finally
        {
            disposed = true;
            if (Marshal.IsComObject(Application)) Marshal.ReleaseComObject(Application);
        }
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);
    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}
