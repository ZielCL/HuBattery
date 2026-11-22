using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

internal sealed class HidReportChannel : IDisposable
{
    private const int Pending = 997;
    private const int WriteTimeoutMs = 120;
    private readonly SafeFileHandle handle;
    private readonly ManualResetEvent completed = new ManualResetEvent(false);

    internal HidReportChannel(SafeFileHandle handle)
    {
        this.handle = handle;
    }

    internal byte[] Exchange(byte[] request, int length, Func<byte[], bool> accepts, int timeoutMs = 220)
    {
        return Send(request) ? Receive(length, accepts, timeoutMs) : null;
    }

    internal bool Send(byte[] request)
    {
        return Transfer(request, true, WriteTimeoutMs);
    }

    internal byte[] Receive(int length, Func<byte[], bool> accepts, int timeoutMs = 220)
    {
        var clock = Stopwatch.StartNew();
        var reply = new byte[length];
        int remaining;
        while ((remaining = timeoutMs - (int)clock.ElapsedMilliseconds) > 0)
        {
            if (!Transfer(reply, false, remaining))
                break;
            if (accepts(reply))
                return reply;
        }
        return null;
    }

    private bool Transfer(byte[] bytes, bool write, int timeoutMs)
    {
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        IntPtr operation = IntPtr.Zero;
        try
        {
            completed.Reset();
            var state = new NativeOverlapped { EventHandle = completed.SafeWaitHandle.DangerousGetHandle() };
            operation = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
            Marshal.StructureToPtr(state, operation, false);

            uint transferred;
            bool done = write
                ? WriteFile(handle, pin.AddrOfPinnedObject(), (uint)bytes.Length, out transferred, operation)
                : ReadFile(handle, pin.AddrOfPinnedObject(), (uint)bytes.Length, out transferred, operation);
            if (!done && Marshal.GetLastWin32Error() != Pending)
                return false;

            if (GetOverlappedResultEx(handle, operation, out transferred, (uint)timeoutMs, false))
                return transferred == bytes.Length;

            // The native request can still own both buffers after a timeout.
            CancelIoEx(handle, operation);
            GetOverlappedResult(handle, operation, out transferred, true);
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(operation);
            pin.Free();
        }
    }

    public void Dispose()
    {
        completed.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, uint length, out uint bytesRead, IntPtr operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, uint length, out uint bytesWritten, IntPtr operation);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResultEx(SafeFileHandle handle, IntPtr operation, out uint bytesTransferred,
        uint timeoutMs, bool alertable);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr operation, out uint bytesTransferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr operation);
}
