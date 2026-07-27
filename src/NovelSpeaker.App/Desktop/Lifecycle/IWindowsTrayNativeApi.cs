namespace NovelSpeaker.App.Desktop.Lifecycle;

internal interface IWindowsTrayNativeApi
{
    IntPtr ExtractLargeIcon(string executablePath);

    IntPtr LoadSharedApplicationIcon();

    bool NotifyIcon(uint message, ref WindowsTrayLifecycleAdapter.NotifyIconData data);

    bool DestroyIcon(IntPtr iconHandle);

    bool SetForegroundWindow(IntPtr windowHandle);
}
