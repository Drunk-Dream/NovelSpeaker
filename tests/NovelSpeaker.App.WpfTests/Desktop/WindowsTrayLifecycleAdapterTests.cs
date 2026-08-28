using System.Reflection;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Desktop.Lifecycle;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Desktop;

[Collection("WpfDispatcher")]
public sealed class WindowsTrayLifecycleAdapterTests
{
    private async Task Start_requires_main_window_attachment()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>());

                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => adapter.StartAsync(CancellationToken.None));
            }
            finally
            {
                await provider.DisposeAsync();
            }
        });
    }

    private void Repeated_attachment_of_same_window_is_idempotent()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>());
                var window = provider.GetRequiredService<MainWindow>();

                adapter.AttachMainWindow(window);
                adapter.AttachMainWindow(window);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void Stop_cleanup_completes_without_dispatcher_continuation()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MainWindow>();
                var native = new FakeNativeApi { ExtractedIcon = new IntPtr(42) };
                var persistence = new GatedPlacementPersistence();
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    persistence,
                    native);
                adapter.AttachMainWindow(window);
                adapter.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

                var stopTask = adapter.StopAsync(CancellationToken.None);

                Assert.Equal(1, native.DeleteCount);
                Assert.False(stopTask.IsCompleted);
                Task.Run(persistence.Release).GetAwaiter().GetResult();
                stopTask.GetAwaiter().GetResult();
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private async Task Stop_closes_an_unshown_mini_player_window()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MainWindow>();
                var miniPlayer = provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>();
                var native = new FakeNativeApi { ExtractedIcon = new IntPtr(42) };
                var adapter = new WindowsTrayLifecycleAdapter(
                    miniPlayer,
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>(),
                    native);
                var closedCount = 0;
                miniPlayer.Closed += (_, _) => closedCount++;
                adapter.AttachMainWindow(window);

                await adapter.StartAsync(CancellationToken.None);
                Assert.False(miniPlayer.IsLoaded);

                await adapter.StopAsync(CancellationToken.None);

                Assert.Equal(1, closedCount);
            }
            finally
            {
                await provider.DisposeAsync();
            }
        });
    }

    private void Tray_menu_exposes_required_commands_and_enables_mini_player()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>());
                adapter.AttachMainWindow(provider.GetRequiredService<MainWindow>());
                var menu = Assert.IsType<ContextMenu>(
                    typeof(WindowsTrayLifecycleAdapter)
                        .GetField("_trayMenu", BindingFlags.Instance | BindingFlags.NonPublic)?
                        .GetValue(adapter));
                var items = menu.Items.OfType<MenuItem>().ToArray();

                Assert.Equal(
                    ["显示主窗口", "播放/暂停", "迷你播放器", "退出"],
                    items.Select(item => item.Header));
                Assert.True(items.Single(item => Equals(item.Header, "迷你播放器")).IsEnabled);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void Tray_menu_click_only_publishes_platform_command()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>());
                adapter.AttachMainWindow(provider.GetRequiredService<MainWindow>());
                DesktopLifecycleCommand? received = null;
                adapter.CommandReceived += (_, command) => received = command;
                var menu = Assert.IsType<ContextMenu>(
                    typeof(WindowsTrayLifecycleAdapter)
                        .GetField("_trayMenu", BindingFlags.Instance | BindingFlags.NonPublic)?
                        .GetValue(adapter));
                var showItem = menu.Items
                    .OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "显示主窗口"));

                showItem.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal(DesktopLifecycleCommand.ShowMainWindow, received);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void Mini_player_close_publishes_exit_application_command()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var miniPlayer = provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>();
                var adapter = new WindowsTrayLifecycleAdapter(
                    miniPlayer,
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>());
                adapter.AttachMainWindow(provider.GetRequiredService<MainWindow>());
                DesktopLifecycleCommand? received = null;
                adapter.CommandReceived += (_, command) => received = command;

                WpfWindowHost.Show(miniPlayer);
                miniPlayer.Close();

                Assert.Equal(DesktopLifecycleCommand.ExitApplication, received);
                Assert.True(miniPlayer.IsVisible);
                miniPlayer.CloseForShutdown();
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private async Task Owned_extracted_icon_is_destroyed_once_after_repeated_stop()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MainWindow>();
                var native = new FakeNativeApi { ExtractedIcon = new IntPtr(42) };
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>(),
                    native);
                adapter.AttachMainWindow(window);

                await adapter.StartAsync(CancellationToken.None);
                var firstStop = adapter.StopAsync(CancellationToken.None);

                Assert.True(firstStop.IsCompletedSuccessfully);
                Assert.Equal(1, native.DeleteCount);
                Assert.Equal([new IntPtr(42)], native.DestroyedIcons);
                await firstStop;
                await adapter.StopAsync(CancellationToken.None);

                Assert.Equal(1, native.AddCount);
                Assert.Equal(1, native.DeleteCount);
                Assert.Equal([new IntPtr(42)], native.DestroyedIcons);
                window.ConfigureDesktopLifecycle(_ => Task.CompletedTask, () => true);
                window.Close();
            }
            finally
            {
                await provider.DisposeAsync();
            }
        });
    }

    private async Task Tray_icon_uses_the_actual_current_process_path()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MainWindow>();
                var native = new FakeNativeApi { ExtractedIcon = new IntPtr(42) };
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>(),
                    native);
                adapter.AttachMainWindow(window);

                await adapter.StartAsync(CancellationToken.None);

                Assert.NotNull(Environment.ProcessPath);
                Assert.Equal(Environment.ProcessPath, native.ExtractedExecutablePath);

                await adapter.StopAsync(CancellationToken.None);
                window.ConfigureDesktopLifecycle(_ => Task.CompletedTask, () => true);
                window.Close();
            }
            finally
            {
                await provider.DisposeAsync();
            }
        });
    }

    private async Task Shared_fallback_icon_is_never_destroyed()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MainWindow>();
                var native = new FakeNativeApi
                {
                    ExtractedIcon = IntPtr.Zero,
                    SharedIcon = new IntPtr(7)
                };
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>(),
                    native);
                adapter.AttachMainWindow(window);

                await adapter.StartAsync(CancellationToken.None);
                await adapter.StopAsync(CancellationToken.None);

                Assert.Empty(native.DestroyedIcons);
                window.ConfigureDesktopLifecycle(_ => Task.CompletedTask, () => true);
                window.Close();
            }
            finally
            {
                await provider.DisposeAsync();
            }
        });
    }

    private async Task Failed_tray_registration_releases_owned_icon_and_repeated_stop_is_safe()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MainWindow>();
                var native = new FakeNativeApi
                {
                    ExtractedIcon = new IntPtr(99),
                    AddSucceeds = false
                };
                var adapter = new WindowsTrayLifecycleAdapter(
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.MiniPlayerWindow>(),
                    provider.GetRequiredService<NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence>(),
                    native);
                adapter.AttachMainWindow(window);

                await Assert.ThrowsAsync<InvalidOperationException>(
                    () => adapter.StartAsync(CancellationToken.None));
                await adapter.StopAsync(CancellationToken.None);

                Assert.Equal([new IntPtr(99)], native.DestroyedIcons);
                Assert.Equal(0, native.DeleteCount);
                window.ConfigureDesktopLifecycle(_ => Task.CompletedTask, () => true);
                window.Close();
            }
            finally
            {
                await provider.DisposeAsync();
            }
        });
    }

    [Fact]
    public async Task Windows_tray_lifecycle_contracts_cover_attachment_and_cleanup()
    {
        await Start_requires_main_window_attachment();
        Repeated_attachment_of_same_window_is_idempotent();
        Stop_cleanup_completes_without_dispatcher_continuation();
        await Stop_closes_an_unshown_mini_player_window();
    }

    [Fact]
    public void Windows_tray_command_contracts_cover_menu_actions_and_mini_player_exit()
    {
        Tray_menu_exposes_required_commands_and_enables_mini_player();
        Tray_menu_click_only_publishes_platform_command();
        Mini_player_close_publishes_exit_application_command();
    }

    [Fact]
    public async Task Windows_tray_icon_ownership_contracts_cover_owned_shared_and_failed_paths()
    {
        await Tray_icon_uses_the_actual_current_process_path();
        await Owned_extracted_icon_is_destroyed_once_after_repeated_stop();
        await Shared_fallback_icon_is_never_destroyed();
        await Failed_tray_registration_releases_owned_icon_and_repeated_stop_is_safe();
    }

    private sealed class FakeNativeApi : IWindowsTrayNativeApi
    {
        public IntPtr ExtractedIcon { get; set; }
        public IntPtr SharedIcon { get; set; } = new(1);
        public bool AddSucceeds { get; set; } = true;
        public int AddCount { get; private set; }
        public int DeleteCount { get; private set; }
        public List<IntPtr> DestroyedIcons { get; } = [];
        public string? ExtractedExecutablePath { get; private set; }

        public IntPtr ExtractLargeIcon(string executablePath)
        {
            ExtractedExecutablePath = executablePath;
            return ExtractedIcon;
        }

        public IntPtr LoadSharedApplicationIcon() => SharedIcon;

        public bool NotifyIcon(
            uint message,
            ref WindowsTrayLifecycleAdapter.NotifyIconData data)
        {
            if (message == 0)
            {
                AddCount++;
                return AddSucceeds;
            }

            if (message == 2)
            {
                DeleteCount++;
            }

            return true;
        }

        public bool DestroyIcon(IntPtr iconHandle)
        {
            DestroyedIcons.Add(iconHandle);
            return true;
        }

        public bool SetForegroundWindow(IntPtr windowHandle) => true;
    }

    private sealed class GatedPlacementPersistence :
        NovelSpeaker.App.Desktop.MiniPlayer.IMiniPlayerPlacementPersistence
    {
        private readonly TaskCompletionSource _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FlushPlacementAsync(CancellationToken cancellationToken) =>
            _completion.Task.WaitAsync(cancellationToken);

        public void Release() => _completion.TrySetResult();
    }
}
