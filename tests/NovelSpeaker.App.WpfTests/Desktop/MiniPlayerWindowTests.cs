using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Desktop.MiniPlayer;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui.Appearance;
using Xunit;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using WpfUiButton = Wpf.Ui.Controls.Button;

namespace NovelSpeaker.App.WpfTests.Desktop;

[Collection("WpfDispatcher")]
public sealed class MiniPlayerWindowTests
{
    private void Window_exposes_required_controls_and_accessibility_contract()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MiniPlayerWindow>();
                Assert.NotNull(Assert.IsType<TextBlock>(window.FindName("MiniPlayerBookTitle"))
                    .GetBindingExpression(TextBlock.TextProperty));
                Assert.NotNull(Assert.IsType<TextBlock>(window.FindName("MiniPlayerChapterTitle"))
                    .GetBindingExpression(TextBlock.TextProperty));
                AssertControl<WpfUiButton>(window, "MiniPlayerPreviousChapterButton", "上一章");
                AssertControl<WpfUiButton>(window, "MiniPlayerPreviousSegmentButton", "上一段");
                AssertControl<WpfUiButton>(window, "MiniPlayerPlaybackButton", "播放");
                AssertControl<WpfUiButton>(window, "MiniPlayerNextSegmentButton", "下一段");
                AssertControl<WpfUiButton>(window, "MiniPlayerNextChapterButton", "下一章");
                AssertControl<WpfUiButton>(window, "MiniPlayerRestoreButton", "恢复主窗口");
                AssertControl<WpfUiButton>(window, "MiniPlayerCloseButton", "退出应用");
                AssertControl<WpfUiButton>(window, "MiniPlayerTopmostButton", "置顶");
                Assert.Equal("播放进度", AutomationProperties.GetName(
                    Assert.IsType<Slider>(window.FindName("MiniPlayerProgressSlider"))));
                Assert.Equal("播放音量", AutomationProperties.GetName(
                    Assert.IsType<Slider>(window.FindName("MiniPlayerVolumeSlider"))));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void Drag_policy_allows_blank_surface_but_excludes_interactive_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var blankSurface = new Border();
            var button = new Button();
            var resizeThumb = new Thumb();
            var slider = new Slider();
            var textBox = new TextBox();

            Assert.True(MiniPlayerWindowDragPolicy.CanStartDrag(blankSurface));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(button));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(resizeThumb));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(slider));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(textBox));
        });
    }

    private void Width_resize_thumb_changes_only_width_within_window_bounds()
    {
        WpfTestHost.RunInSta(() =>
        {
            var fixture = CreateWindow(PlaybackSnapshot.Idle);
            try
            {
                WpfWindowHost.Show(fixture.Window);
                fixture.Window.UpdateLayout();

                var resizeThumb = Assert.IsType<Thumb>(fixture.Window.FindName("MiniPlayerWidthResizeThumb"));
                Assert.Equal(0, resizeThumb.Opacity);
                var initialHeight = fixture.Window.ActualHeight;

                resizeThumb.RaiseEvent(new DragDeltaEventArgs(12, 40)
                {
                    RoutedEvent = Thumb.DragDeltaEvent
                });

                Assert.InRange(fixture.Window.Width, fixture.Window.MinWidth, fixture.Window.MaxWidth);
                Assert.Equal(initialHeight, fixture.Window.ActualHeight);

                resizeThumb.RaiseEvent(new DragDeltaEventArgs(100, 40)
                {
                    RoutedEvent = Thumb.DragDeltaEvent
                });

                Assert.Equal(fixture.Window.MaxWidth, fixture.Window.Width);
                Assert.Equal(initialHeight, fixture.Window.ActualHeight);
            }
            finally
            {
                CloseFixture(fixture);
            }
        });
    }

    private void Invalid_or_offscreen_placement_uses_safe_fallback()
    {
        foreach (var (left, top) in new[]
        {
            (double.NaN, 20d),
            (10d, double.PositiveInfinity),
            (-1d, 20d),
            (900d, 20d)
        })
        {
            Invalid_or_offscreen_placement_uses_safe_fallback_for_position(left, top);
        }
    }

    private void Invalid_or_offscreen_placement_uses_safe_fallback_for_position(double left, double top)
    {
        Assert.False(MiniPlayerPlacementValidator.TryValidate(
            left,
            top,
            200,
            100,
            [new MiniPlayerScreenBounds(0, 0, 1000, 800)],
            out _));
    }

    private void Valid_placement_is_preserved()
    {
        Assert.True(MiniPlayerPlacementValidator.TryValidate(
            100,
            120,
            200,
            100,
            [new MiniPlayerScreenBounds(0, 0, 1000, 800)],
            out var placement));
        Assert.Equal(new MiniPlayerPlacement(100, 120), placement);
    }

    private void User_close_requests_application_exit_instead_of_restoring_main_window()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MiniPlayerWindow>();
                var exitRequested = false;
                window.ExitRequested += (_, _) => exitRequested = true;
                WpfWindowHost.Show(window);

                window.Close();

                Assert.True(exitRequested);
                Assert.True(window.IsVisible);
                window.CloseForShutdown();
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void Close_button_requests_application_exit_without_restoring_main_window()
    {
        WpfTestHost.RunInSta(() =>
        {
            var fixture = CreateWindow(PlaybackSnapshot.Idle);
            try
            {
                var exitRequested = false;
                fixture.Window.ExitRequested += (_, _) => exitRequested = true;
                WpfWindowHost.Show(fixture.Window);

                FindButton(fixture.Window, "MiniPlayerCloseButton")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.True(exitRequested);
                Assert.True(fixture.Window.IsVisible);
            }
            finally
            {
                CloseFixture(fixture);
            }
        });
    }

    private void Saved_position_is_available_and_a_user_move_is_persisted()
    {
        WpfTestHost.RunInSta(() =>
        {
            var fixture = CreateWindow(
                PlaybackSnapshot.Idle,
                AppSettings.Default with
                {
                    MiniPlayerLeft = 120,
                    MiniPlayerTop = 140,
                    MiniPlayerTopmost = true
                },
                new FakeScreenBoundsProvider(new MiniPlayerScreenBounds(0, 0, 1200, 900)));
            try
            {
                Assert.Equal(120d, fixture.ViewModel.SavedLeft!.Value);
                Assert.Equal(140d, fixture.ViewModel.SavedTop!.Value);
                WpfWindowHost.Show(fixture.Window);
                fixture.Window.UpdateLayout();

                Assert.Equal(0d, fixture.Window.Left);
                Assert.Equal(0d, fixture.Window.Top);
                Assert.True(fixture.Window.Topmost);

                fixture.Window.Left = 260;
                fixture.Window.Top = 280;
                fixture.Window.UpdateLayout();
                fixture.ViewModel.FlushPlacementAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                Assert.Equal(260, fixture.Settings.Current.MiniPlayerLeft);
                Assert.Equal(280, fixture.Settings.Current.MiniPlayerTop);
                Assert.True(fixture.Settings.Current.MiniPlayerTopmost);
            }
            finally
            {
                CloseFixture(fixture);
            }
        });
    }

    private void Placement_in_gap_between_monitors_is_rejected()
    {
        Assert.False(MiniPlayerPlacementValidator.TryValidate(
            700,
            100,
            200,
            100,
            [
                new MiniPlayerScreenBounds(0, 0, 600, 800),
                new MiniPlayerScreenBounds(1000, 0, 600, 800)
            ],
            out _));
    }

    [Fact]
    public void Mini_player_surface_contracts_cover_controls_drag_and_layout()
    {
        Window_exposes_required_controls_and_accessibility_contract();
        Drag_policy_allows_blank_surface_but_excludes_interactive_controls();
        Width_resize_thumb_changes_only_width_within_window_bounds();
    }

    [Fact]
    public void Mini_player_placement_contracts_cover_valid_invalid_and_monitor_gap_positions()
    {
        Invalid_or_offscreen_placement_uses_safe_fallback();
        Valid_placement_is_preserved();
        Placement_in_gap_between_monitors_is_rejected();
    }

    [Fact]
    public void Mini_player_lifecycle_contracts_cover_close_and_persisted_position_commands()
    {
        User_close_requests_application_exit_instead_of_restoring_main_window();
        Close_button_requests_application_exit_without_restoring_main_window();
        Saved_position_is_available_and_a_user_move_is_persisted();
    }

    private static void AssertControl<T>(MiniPlayerWindow window, string name, string automationName)
        where T : FrameworkElement
    {
        var control = Assert.IsType<T>(window.FindName(name));
        Assert.Equal(automationName, AutomationProperties.GetName(control));
        Assert.Equal(automationName, control.ToolTip);
    }

    private static MiniPlayerFixture CreateWindow(
        PlaybackSnapshot snapshot,
        AppSettings? settings = null,
        IMiniPlayerScreenBoundsProvider? screenBoundsProvider = null)
    {
        var playback = new FakePlaybackSession(snapshot);
        var settingsService = new FakeAppSettingsService(settings ?? AppSettings.Default);
        var viewModel = new MiniPlayerViewModel(
            playback,
            settingsService,
            new InlineUiScheduler(),
            NullLogger<MiniPlayerViewModel>.Instance);
        var window = new MiniPlayerWindow(
            viewModel,
            screenBoundsProvider ?? new FakeScreenBoundsProvider(new MiniPlayerScreenBounds(0, 0, 1920, 1080)));
        return new MiniPlayerFixture(window, viewModel, settingsService);
    }

    private static void CloseFixture(MiniPlayerFixture fixture)
    {
        if (fixture.Window.IsVisible)
        {
            fixture.Window.CloseForShutdown();
        }

        fixture.ViewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static Button FindButton(MiniPlayerWindow window, string name) =>
        Assert.IsAssignableFrom<Button>(window.FindName(name));

    private sealed record MiniPlayerFixture(
        MiniPlayerWindow Window,
        MiniPlayerViewModel ViewModel,
        FakeAppSettingsService Settings);

    private sealed class FakeScreenBoundsProvider(MiniPlayerScreenBounds bounds) : IMiniPlayerScreenBoundsProvider
    {
        public IReadOnlyList<MiniPlayerScreenBounds> GetWorkAreas() => [bounds];
    }

    private sealed class InlineUiScheduler : IUiScheduler
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }
    }

    private sealed class FakeAppSettingsService(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = settings;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = Current;
            Current = Current with
            {
                MiniPlayerLeft = update.ClearMiniPlayerLeft ? null : update.MiniPlayerLeft ?? Current.MiniPlayerLeft,
                MiniPlayerTop = update.ClearMiniPlayerTop ? null : update.MiniPlayerTop ?? Current.MiniPlayerTop,
                MiniPlayerTopmost = update.MiniPlayerTopmost ?? Current.MiniPlayerTopmost
            };
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, Current));
            return Task.FromResult(Current);
        }
    }

    private sealed class FakePlaybackSession(PlaybackSnapshot snapshot) : IPlaybackSession
    {
        public PlaybackSnapshot CurrentSnapshot { get; private set; } = snapshot;

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public void Publish(PlaybackSnapshot nextSnapshot)
        {
            CurrentSnapshot = nextSnapshot;
            SnapshotChanged?.Invoke(this, nextSnapshot);
        }

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetVolume(double volume)
        {
        }
    }
}
