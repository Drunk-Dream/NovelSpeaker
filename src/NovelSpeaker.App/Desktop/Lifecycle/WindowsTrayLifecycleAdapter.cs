using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using NovelSpeaker.App.Shell;

namespace NovelSpeaker.App.Desktop.Lifecycle;

internal sealed class WindowsTrayLifecycleAdapter : IDesktopLifecyclePlatform
{
    private const int TrayIconId = 1;
    private const int TrayCallbackMessage = 0x8001;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int WmRightButtonUp = 0x0205;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;

    private readonly MainWindow _window;
    private readonly IWindowsTrayNativeApi _nativeApi;
    private readonly ContextMenu _trayMenu;
    private HwndSource? _windowSource;
    private TrayIconResource? _iconResource;
    private IntPtr _windowHandle;
    private bool _started;

    public WindowsTrayLifecycleAdapter(MainWindow window)
        : this(window, new WindowsTrayNativeApi())
    {
    }

    internal WindowsTrayLifecycleAdapter(
        MainWindow window,
        IWindowsTrayNativeApi nativeApi)
    {
        _window = window;
        _nativeApi = nativeApi;
        _trayMenu = CreateTrayMenu();
    }

    public event EventHandler<DesktopLifecycleCommand>? CommandReceived;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(
            () =>
            {
                if (_started)
                {
                    return;
                }

                _windowHandle = new WindowInteropHelper(_window).EnsureHandle();
                _windowSource = HwndSource.FromHwnd(_windowHandle)
                    ?? throw new InvalidOperationException("无法连接主窗口消息源。");
                _windowSource.AddHook(WindowMessageHook);

                try
                {
                    var icon = _nativeApi.ExtractLargeIcon(
                        Path.Combine(AppContext.BaseDirectory, "NovelSpeaker.App.exe"));
                    _iconResource = icon != IntPtr.Zero
                        ? TrayIconResource.Owned(icon, _nativeApi.DestroyIcon)
                        : CreateSharedIconResource();

                    var data = CreateNotifyIconData(_iconResource.Handle);
                    if (!_nativeApi.NotifyIcon(NimAdd, ref data))
                    {
                        throw new InvalidOperationException("无法创建系统托盘图标。");
                    }
                }
                catch
                {
                    CleanupPlatformResources();
                    throw;
                }

                _started = true;
            },
            cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(
            () =>
            {
                if (!_started)
                {
                    return;
                }

                try
                {
                    var data = CreateNotifyIconData(IntPtr.Zero);
                    _nativeApi.NotifyIcon(NimDelete, ref data);
                }
                finally
                {
                    CleanupPlatformResources();
                    _started = false;
                }
            },
            cancellationToken);
    }

    public Task ShowMainWindowAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(
            () =>
            {
                if (!_window.IsVisible)
                {
                    _window.Show();
                }

                if (_window.WindowState == WindowState.Minimized)
                {
                    _window.WindowState = WindowState.Normal;
                }

                _window.Activate();
            },
            cancellationToken);
    }

    public Task HideMainWindowAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(_window.Hide, cancellationToken);
    }

    public Task CloseMainWindowAsync(CancellationToken cancellationToken)
    {
        return InvokeAsync(_window.Close, cancellationToken);
    }

    public async Task<DesktopCloseChoice> PromptForCloseChoiceAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var operation = _window.Dispatcher.InvokeAsync(
            () =>
            {
                var result = MessageBox.Show(
                    _window,
                    "选择“是”将最小化到托盘；选择“否”将退出应用。",
                    "关闭 NovelSpeaker",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                return result switch
                {
                    MessageBoxResult.Yes => DesktopCloseChoice.HideToTray,
                    MessageBoxResult.No => DesktopCloseChoice.ExitApplication,
                    _ => DesktopCloseChoice.Cancel
                };
            },
            DispatcherPriority.Normal,
            cancellationToken);
        return await operation.Task.ConfigureAwait(false);
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(CreateMenuItem("显示主窗口", DesktopLifecycleCommand.ShowMainWindow));
        menu.Items.Add(CreateMenuItem("播放/暂停", DesktopLifecycleCommand.TogglePlayback));
        menu.Items.Add(new MenuItem
        {
            Header = "迷你播放器",
            IsEnabled = false,
            ToolTip = "将在迷你播放器功能完成后可用"
        });
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem("退出", DesktopLifecycleCommand.ExitApplication));
        return menu;
    }

    private MenuItem CreateMenuItem(string header, DesktopLifecycleCommand command)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => CommandReceived?.Invoke(this, command);
        return item;
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != TrayCallbackMessage)
        {
            return IntPtr.Zero;
        }

        switch (unchecked((int)lParam.ToInt64()))
        {
            case WmLeftButtonDoubleClick:
                CommandReceived?.Invoke(this, DesktopLifecycleCommand.ShowMainWindow);
                handled = true;
                break;
            case WmRightButtonUp:
                _nativeApi.SetForegroundWindow(_windowHandle);
                _trayMenu.IsOpen = true;
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private NotifyIconData CreateNotifyIconData(IntPtr icon)
    {
        return new NotifyIconData
        {
            Size = Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = TrayIconId,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = TrayCallbackMessage,
            IconHandle = icon,
            Tip = "NovelSpeaker"
        };
    }

    private TrayIconResource CreateSharedIconResource()
    {
        var icon = _nativeApi.LoadSharedApplicationIcon();
        if (icon == IntPtr.Zero)
        {
            CleanupPlatformResources();
            throw new InvalidOperationException("无法加载系统托盘图标。");
        }

        return TrayIconResource.Shared(icon);
    }

    private void CleanupPlatformResources()
    {
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        _iconResource?.Dispose();
        _iconResource = null;
    }

    private Task InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_window.Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        var operation = _window.Dispatcher.InvokeAsync(
            action,
            DispatcherPriority.Normal,
            cancellationToken);
        return operation.Task;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        public int Size;
        public IntPtr WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public IntPtr IconHandle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;

        public uint State;
        public uint StateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;

        public uint TimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;

        public uint InfoFlags;
        public Guid ItemGuid;
        public IntPtr BalloonIconHandle;
    }

}
