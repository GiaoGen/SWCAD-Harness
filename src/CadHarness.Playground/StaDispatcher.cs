using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace CadHarness.Playground;

// Every COM call and disposal stays on one long-lived STA. HTTP requests never
// carry an RCW. Admission is handled before dispatch by PlaygroundSession.
public sealed class StaDispatcher : IDisposable
{
    private readonly BlockingCollection<Action> work = new();
    private readonly Thread thread;
    public StaDispatcher()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Playground CAD STA" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    public Task<T> Invoke<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        work.Add(() => { try { completion.SetResult(action()); } catch (Exception error) { completion.SetException(error); } });
        return completion.Task;
    }
    private void Run()
    {
        while (!work.IsCompleted)
        {
            if (work.TryTake(out var action, 25)) action();
            while (PeekMessage(out var message, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessage(ref message); }
        }
    }
    public void Dispose() { work.CompleteAdding(); thread.Join(); work.Dispose(); }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public IntPtr Hwnd; public uint Id; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X; public int Y; public uint Private; }
    [DllImport("user32.dll")] private static extern bool PeekMessage(out Message message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref Message message);
}
