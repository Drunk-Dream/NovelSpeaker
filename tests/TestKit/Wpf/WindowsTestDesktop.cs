using System.Runtime.InteropServices;

namespace NovelSpeaker.TestKit.Wpf;

internal sealed class WindowsTestDesktop : IDisposable
{
    private const uint DesktopAllAccess = 0x01FF;
    private static readonly object PendingCleanupGate = new();
    private static readonly HashSet<WindowsTestDesktop> PendingCleanup = [];
    private readonly IWindowsTestDesktopNativeApi _nativeApi;
    private readonly IntPtr _previousDesktop;
    private readonly IntPtr _isolatedDesktop;
    private bool _staInitialized;
    private bool _threadShutdownPrepared;
    private bool _desktopHandleReleased;

    private WindowsTestDesktop(
        WindowsTestDesktopInfo info,
        IWindowsTestDesktopNativeApi nativeApi,
        IntPtr previousDesktop,
        IntPtr isolatedDesktop)
    {
        Info = info;
        _nativeApi = nativeApi;
        _previousDesktop = previousDesktop;
        _isolatedDesktop = isolatedDesktop;
    }

    public WindowsTestDesktopInfo Info { get; }

    public void InitializeSta()
    {
        if (_staInitialized)
        {
            return;
        }

        var result = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded);
        if (result < 0)
        {
            throw WindowsTestDesktopInitializationException.For(
                "initialize the WPF test thread as STA",
                result);
        }

        _staInitialized = true;
    }

    public static WindowsTestDesktop Attach(
        bool allowVisibleWindows,
        IWindowsTestDesktopNativeApi? nativeApi = null)
    {
        nativeApi ??= new WindowsTestDesktopNativeApi();

        if (allowVisibleWindows)
        {
            return new WindowsTestDesktop(
                new WindowsTestDesktopInfo("interactive", IsIsolated: false),
                nativeApi,
                IntPtr.Zero,
                IntPtr.Zero);
        }

        var previousDesktop = nativeApi.GetCurrent();
        if (previousDesktop == IntPtr.Zero)
        {
            throw WindowsTestDesktopInitializationException.For(
                "read the current Windows Desktop",
                nativeApi.GetLastError());
        }

        var desktopName = $"NovelSpeaker.Test.{Guid.NewGuid():N}";
        var isolatedDesktop = nativeApi.Create(desktopName, DesktopAllAccess);
        if (isolatedDesktop == IntPtr.Zero)
        {
            throw WindowsTestDesktopInitializationException.For(
                "create the isolated Windows Desktop",
                nativeApi.GetLastError());
        }

        if (!nativeApi.Bind(isolatedDesktop))
        {
            var errorCode = nativeApi.GetLastError();
            var desktop = new WindowsTestDesktop(
                new WindowsTestDesktopInfo(desktopName, IsIsolated: true),
                nativeApi,
                previousDesktop,
                isolatedDesktop);
            try
            {
                desktop.ReleaseDesktopHandle();
            }
            catch (Exception cleanupException)
            {
                RegisterPendingCleanup(desktop);
                throw new AggregateException(
                    WindowsTestDesktopInitializationException.For(
                        "bind the WPF test thread to the isolated Windows Desktop",
                        errorCode),
                    cleanupException);
            }

            throw WindowsTestDesktopInitializationException.For(
                "bind the WPF test thread to the isolated Windows Desktop",
                errorCode);
        }

        return new WindowsTestDesktop(
            new WindowsTestDesktopInfo(desktopName, IsIsolated: true),
            nativeApi,
            previousDesktop,
            isolatedDesktop);
    }

    public void Dispose()
    {
        PrepareThreadShutdown();
        ReleaseDesktopHandle();
    }

    public void PrepareThreadShutdown()
    {
        if (_threadShutdownPrepared)
        {
            return;
        }

        _threadShutdownPrepared = true;
        if (_staInitialized)
        {
            CoUninitialize();
            _staInitialized = false;
        }

        if (_isolatedDesktop != IntPtr.Zero)
        {
            // This can fail when WPF still owns a native window. The thread is
            // terminating, so the owner thread closes the handle after exit.
            ThreadDesktopRestored = _nativeApi.Bind(_previousDesktop);
        }
    }

    public bool ThreadDesktopRestored { get; private set; }

    public bool IsDesktopHandleReleased => _desktopHandleReleased;

    internal static void RetryPendingCleanup()
    {
        WindowsTestDesktop[] pending;
        lock (PendingCleanupGate)
        {
            pending = PendingCleanup.ToArray();
        }

        Exception? cleanupException = null;
        foreach (var desktop in pending)
        {
            try
            {
                desktop.ReleaseDesktopHandle();
            }
            catch (Exception exception)
            {
                cleanupException = cleanupException is null
                    ? exception
                    : new AggregateException(cleanupException, exception);
            }
        }

        if (cleanupException is not null)
        {
            throw cleanupException;
        }
    }

    public void ReleaseDesktopHandle()
    {
        if (_desktopHandleReleased || _isolatedDesktop == IntPtr.Zero)
        {
            _desktopHandleReleased = true;
            return;
        }

        if (!_nativeApi.Close(_isolatedDesktop))
        {
            throw WindowsTestDesktopInitializationException.For(
                "release the isolated Windows Desktop",
                _nativeApi.GetLastError());
        }

        _desktopHandleReleased = true;
        lock (PendingCleanupGate)
        {
            PendingCleanup.Remove(this);
        }
    }

    private static void RegisterPendingCleanup(WindowsTestDesktop desktop)
    {
        lock (PendingCleanupGate)
        {
            PendingCleanup.Add(desktop);
        }
    }

    private const uint CoInitApartmentThreaded = 0x2;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();
}

internal sealed record WindowsTestDesktopInfo(string Name, bool IsIsolated);

internal sealed class WindowsTestDesktopInitializationException : InvalidOperationException
{
    private WindowsTestDesktopInitializationException(string message)
        : base(message)
    {
    }

    public static WindowsTestDesktopInitializationException For(
        string operation,
        int errorCode) =>
        new(
            $"WPF test host could not {operation} (Win32 error {errorCode}). " +
            "The test host fails closed and will not use the interactive Desktop.");
}

internal interface IWindowsTestDesktopNativeApi
{
    IntPtr GetCurrent();

    IntPtr Create(string name, uint desiredAccess);

    bool Bind(IntPtr desktop);

    bool Close(IntPtr desktop);

    int GetLastError();
}

internal sealed class WindowsTestDesktopNativeApi : IWindowsTestDesktopNativeApi
{
    public IntPtr GetCurrent() =>
        GetThreadDesktop(GetCurrentThreadId());

    public IntPtr Create(string name, uint desiredAccess) =>
        CreateDesktopNative(name, null, IntPtr.Zero, 0, desiredAccess, IntPtr.Zero);

    public bool Bind(IntPtr desktop) =>
        SetThreadDesktopNative(desktop);

    public bool Close(IntPtr desktop) =>
        CloseDesktopNative(desktop);

    public int GetLastError() =>
        Marshal.GetLastWin32Error();

    [DllImport("user32.dll", EntryPoint = "CreateDesktopW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopNative(
        string name,
        string? device,
        IntPtr devMode,
        uint flags,
        uint desiredAccess,
        IntPtr securityAttributes);

    [DllImport("user32.dll", EntryPoint = "SetThreadDesktop", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadDesktopNative(IntPtr desktop);

    [DllImport("user32.dll", EntryPoint = "CloseDesktop", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktopNative(IntPtr desktop);

    [DllImport("user32.dll", EntryPoint = "GetThreadDesktop", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

}
