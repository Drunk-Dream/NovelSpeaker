using System.Runtime.InteropServices;

namespace NovelSpeaker.App.Desktop.Lifecycle;

internal sealed class WindowsTrayNativeApi : IWindowsTrayNativeApi
{
    public IntPtr ExtractLargeIcon(string executablePath)
    {
        var largeIcons = new IntPtr[1];
        return ExtractIconEx(executablePath, 0, largeIcons, null, 1) > 0
            ? largeIcons[0]
            : IntPtr.Zero;
    }

    public IntPtr LoadSharedApplicationIcon()
    {
        return LoadIcon(IntPtr.Zero, new IntPtr(32512));
    }

    public bool NotifyIcon(
        uint message,
        ref WindowsTrayLifecycleAdapter.NotifyIconData data)
    {
        return ShellNotifyIcon(message, ref data);
    }

    public bool DestroyIcon(IntPtr iconHandle)
    {
        return DestroyIconNative(iconHandle);
    }

    public bool SetForegroundWindow(IntPtr windowHandle)
    {
        return SetForegroundWindowNative(windowHandle);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "ExtractIconExW")]
    private static extern uint ExtractIconEx(
        string file,
        int iconIndex,
        [Out] IntPtr[]? largeIcons,
        [Out] IntPtr[]? smallIcons,
        uint iconCount);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(
        uint message,
        ref WindowsTrayLifecycleAdapter.NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [DllImport("user32.dll", EntryPoint = "DestroyIcon")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIconNative(IntPtr iconHandle);

    [DllImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindowNative(IntPtr windowHandle);
}
