using System.Runtime.InteropServices;

namespace NovelSpeaker.TestKit.Wpf;

internal sealed class WindowsTestDesktopThread : IDisposable
{
    private const uint WaitObject = 0;
    private const uint WaitTimeout = 0x00000102;
    private IntPtr _threadHandle;

    private WindowsTestDesktopThread(IntPtr threadHandle)
    {
        _threadHandle = threadHandle;
    }

    private static readonly NativeThreadStart EntryPoint = Run;

    public bool IsDisposed => _threadHandle == IntPtr.Zero;

    public static WindowsTestDesktopThread Start(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var state = new ThreadState(action);
        var stateHandle = GCHandle.Alloc(state);
        var thread = CreateThread(
            IntPtr.Zero,
            UIntPtr.Zero,
            EntryPoint,
            GCHandle.ToIntPtr(stateHandle),
            0,
            out _);
        if (thread == IntPtr.Zero)
        {
            stateHandle.Free();
            throw WindowsTestDesktopInitializationException.For(
                "create the WPF test thread",
                Marshal.GetLastWin32Error());
        }

        return new WindowsTestDesktopThread(thread);
    }

    public bool WaitForExit(TimeSpan timeout)
    {
        var milliseconds = timeout.TotalMilliseconds >= uint.MaxValue
            ? uint.MaxValue - 1
            : (uint)Math.Max(0d, timeout.TotalMilliseconds);
        var result = WaitForSingleObject(_threadHandle, milliseconds);
        if (result == WaitObject)
        {
            return true;
        }

        if (result == WaitTimeout)
        {
            return false;
        }

        throw WindowsTestDesktopInitializationException.For(
            "wait for the WPF test thread to exit",
            Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_threadHandle == IntPtr.Zero)
        {
            return;
        }

        if (!CloseHandle(_threadHandle))
        {
            throw WindowsTestDesktopInitializationException.For(
                "release the WPF test thread handle",
                Marshal.GetLastWin32Error());
        }

        _threadHandle = IntPtr.Zero;
    }

    private static uint Run(IntPtr statePointer)
    {
        var stateHandle = GCHandle.FromIntPtr(statePointer);
        try
        {
            ((ThreadState)stateHandle.Target!).Action();
        }
        finally
        {
            stateHandle.Free();
        }

        return 0;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint NativeThreadStart(IntPtr parameter);

    private sealed class ThreadState(Action action)
    {
        public Action Action { get; } = action;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateThread(
        IntPtr threadAttributes,
        UIntPtr stackSize,
        NativeThreadStart startAddress,
        IntPtr parameter,
        uint creationFlags,
        out uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
