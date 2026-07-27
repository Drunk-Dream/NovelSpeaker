using System.Windows.Interop;
using NovelSpeaker.App.Shell;
using NovelSpeaker.Application.Desktop.MediaControls;
using Windows.Media;

namespace NovelSpeaker.App.Desktop.MediaControls;

internal sealed class WindowsMediaControlAdapter : IMediaControlPlatform
{
    private readonly MainWindow _mainWindow;
    private SystemMediaTransportControls? _controls;

    public WindowsMediaControlAdapter(MainWindow mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
    }

    public event EventHandler<MediaControlCommand>? CommandReceived;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_controls is not null)
        {
            return Task.CompletedTask;
        }

        var windowHandle = new WindowInteropHelper(_mainWindow).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("主窗口句柄尚未创建，无法注册系统媒体控制。");
        }

        var controls = SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
        _controls = controls;
        controls.IsPlayEnabled = true;
        controls.IsPauseEnabled = true;
        controls.IsPreviousEnabled = true;
        controls.IsNextEnabled = true;
        controls.IsStopEnabled = false;
        controls.ButtonPressed += OnButtonPressed;
        controls.IsEnabled = true;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        MediaControlMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();

        var controls = _controls
            ?? throw new InvalidOperationException("系统媒体控制尚未注册。");
        var updater = controls.DisplayUpdater;
        updater.Type = MediaPlaybackType.Music;
        updater.MusicProperties.Title = metadata.ChapterTitle;
        updater.MusicProperties.Artist = metadata.BookTitle;
        updater.MusicProperties.AlbumTitle = metadata.BookTitle;
        updater.Update();
        controls.PlaybackStatus = metadata.PlaybackStatus switch
        {
            MediaControlPlaybackStatus.Playing => MediaPlaybackStatus.Playing,
            MediaControlPlaybackStatus.Paused => MediaPlaybackStatus.Paused,
            _ => MediaPlaybackStatus.Stopped
        };
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        var controls = _controls;
        if (controls is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            controls.ButtonPressed -= OnButtonPressed;
            controls.IsEnabled = false;
            controls.DisplayUpdater.ClearAll();
        }
        finally
        {
            _controls = null;
        }

        return Task.CompletedTask;
    }

    private void OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        var command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => MediaControlCommand.Play,
            SystemMediaTransportControlsButton.Pause => MediaControlCommand.Pause,
            SystemMediaTransportControlsButton.Previous => MediaControlCommand.Previous,
            SystemMediaTransportControlsButton.Next => MediaControlCommand.Next,
            _ => (MediaControlCommand?)null
        };

        if (command is { } supportedCommand)
        {
            CommandReceived?.Invoke(this, supportedCommand);
        }
    }
}
