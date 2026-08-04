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

namespace NovelSpeaker.App.WpfTests.Desktop;

[Collection("WpfDispatcher")]
public sealed class MiniPlayerWindowTests
{
    [Fact]
    public void Window_exposes_required_controls_and_accessibility_contract()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MiniPlayerWindow>();
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.True(window.AllowsTransparency);
                Assert.Equal(Brushes.Transparent, window.Background);
                Assert.Equal(ResizeMode.NoResize, window.ResizeMode);
                Assert.Equal(480d, window.Width);
                Assert.Equal(150d, window.Height);
                Assert.Equal(440d, window.MinWidth);
                Assert.Equal(130d, window.MinHeight);
                Assert.Equal(500d, window.MaxWidth);
                Assert.Equal(160d, window.MaxHeight);

                var surface = Assert.IsType<Border>(window.FindName("MiniPlayerSurface"));
                Assert.Same(window.FindResource("RaisedSurfaceBrush"), surface.Background);
                Assert.Equal(Brushes.Transparent, surface.BorderBrush);
                Assert.Equal(new Thickness(0), surface.BorderThickness);
                Assert.Equal(
                    Assert.IsType<CornerRadius>(window.FindResource("CornerRadiusLarge")),
                    surface.CornerRadius);
                Assert.Null(surface.Effect);

                var bookTitle = Assert.IsType<TextBlock>(window.FindName("MiniPlayerBookTitle"));
                Assert.NotNull(bookTitle.GetBindingExpression(TextBlock.TextProperty));
                Assert.Equal(13, bookTitle.FontSize);
                Assert.Equal(FontWeights.Normal, bookTitle.FontWeight);
                Assert.Equal(TextWrapping.NoWrap, bookTitle.TextWrapping);
                var chapterTitle = Assert.IsType<TextBlock>(window.FindName("MiniPlayerChapterTitle"));
                Assert.NotNull(chapterTitle.GetBindingExpression(TextBlock.TextProperty));
                Assert.Equal(16, chapterTitle.FontSize);
                Assert.Equal(FontWeights.SemiBold, chapterTitle.FontWeight);
                Assert.Equal(TextWrapping.NoWrap, chapterTitle.TextWrapping);
                Assert.NotNull(chapterTitle.GetBindingExpression(FrameworkElement.ToolTipProperty));
                var segmentCounter = Assert.IsType<TextBlock>(window.FindName("MiniPlayerSegmentCounterText"));
                Assert.NotNull(segmentCounter.GetBindingExpression(TextBlock.TextProperty));
                AssertControl<Button>(window, "MiniPlayerPreviousChapterButton", "上一章");
                AssertControl<Button>(window, "MiniPlayerPreviousSegmentButton", "上一段");
                AssertControl<Button>(window, "MiniPlayerPlaybackButton", "播放");
                var playbackButton = Assert.IsType<Button>(window.FindName("MiniPlayerPlaybackButton"));
                Assert.Equal(Colors.Transparent, Assert.IsType<SolidColorBrush>(playbackButton.Background).Color);
                Assert.Equal(new Thickness(0), playbackButton.BorderThickness);
                var playbackIcon = Assert.IsType<SymbolIcon>(playbackButton.Content);
                var playbackTrigger = Assert.Single(playbackIcon.Style!.Triggers.OfType<DataTrigger>());
                var playbackBinding = Assert.IsType<Binding>(playbackTrigger.Binding);
                Assert.Equal("PlaybackActionText", playbackBinding.Path.Path);
                AssertControl<Button>(window, "MiniPlayerNextSegmentButton", "下一段");
                AssertControl<Button>(window, "MiniPlayerNextChapterButton", "下一章");
                var volumeButton = Assert.IsType<Button>(window.FindName("MiniPlayerVolumeMenuButton"));
                Assert.Equal("播放音量", volumeButton.ToolTip);
                Assert.Equal("播放音量 100%", AutomationProperties.GetName(volumeButton));
                AssertControl<Button>(window, "MiniPlayerRestoreButton", "恢复主窗口");
                AssertControl<Button>(window, "MiniPlayerCloseButton", "退出应用");
                AssertControl<Button>(window, "MiniPlayerTopmostButton", "置顶");
                var topmostStateBorder = Assert.IsType<Border>(window.FindName("MiniPlayerTopmostStateBorder"));
                Assert.Equal(Brushes.Transparent, topmostStateBorder.Background);
                var topmostTrigger = Assert.Single(topmostStateBorder.Style!.Triggers.OfType<DataTrigger>());
                var topmostBinding = Assert.IsType<Binding>(topmostTrigger.Binding);
                Assert.Equal("IsTopmost", topmostBinding.Path.Path);
                Assert.Equal(
                    "播放进度",
                    AutomationProperties.GetName(
                        Assert.IsType<Slider>(window.FindName("MiniPlayerProgressSlider"))));
                var progressSlider = Assert.IsType<Slider>(window.FindName("MiniPlayerProgressSlider"));
                Assert.True(progressSlider.IsHitTestVisible);
                var progressToolTip = Assert.IsType<ToolTip>(progressSlider.ToolTip);
                Assert.True(progressToolTip.StaysOpen);
                Assert.False(ToolTipService.GetIsEnabled(progressSlider));
                Assert.Same(window.FindResource("App.Media.Slider"), progressSlider.Style);

                var volumePopup = Assert.IsType<Popup>(window.FindName("MiniPlayerVolumeMenuPopup"));
                var volumeSlider = Assert.IsType<Slider>(window.FindName("MiniPlayerVolumeSlider"));
                Assert.False(volumePopup.IsOpen);
                Assert.Equal(0d, volumeSlider.Minimum);
                Assert.Equal(1d, volumeSlider.Maximum);
                Assert.Equal("播放音量", AutomationProperties.GetName(volumeSlider));
                Assert.Same(window.FindResource("App.Media.Slider"), volumeSlider.Style);

                Assert.IsType<Grid>(window.FindName("MiniPlayerControlBar"));
                var mediaSurface = Assert.IsType<Border>(window.FindName("MiniPlayerMediaSurface"));
                Assert.Same(window.FindResource("SecondarySurfaceBrush"), mediaSurface.Background);
                Assert.Equal(
                    Assert.IsType<CornerRadius>(window.FindResource("CornerRadiusMedium")),
                    mediaSurface.CornerRadius);
                Assert.True(mediaSurface.ClipToBounds);
                var mediaControls = Assert.IsType<StackPanel>(window.FindName("MiniPlayerMediaControls"));
                Assert.Equal(1, Grid.GetColumn(mediaControls));
                Assert.Equal(HorizontalAlignment.Center, mediaControls.HorizontalAlignment);
                Assert.Equal(2, Grid.GetColumn(volumeButton));
                Assert.Equal(HorizontalAlignment.Right, volumeButton.HorizontalAlignment);
                Assert.Same(window.FindResource("App.Button.Icon"), playbackButton.Style);
                Assert.Same(window.FindResource("App.Button.Icon"),
                    Assert.IsType<Button>(window.FindName("MiniPlayerPreviousSegmentButton")).Style);
                Assert.Same(window.FindResource("App.Button.Icon"),
                    Assert.IsType<Button>(window.FindName("MiniPlayerPreviousChapterButton")).Style);
                Assert.Same(window.FindResource("App.Button.Icon"),
                    Assert.IsType<Button>(window.FindName("MiniPlayerRestoreButton")).Style);
                Assert.Same(window.FindResource("App.Button.Icon"), volumeButton.Style);
                Assert.Same(window.FindResource("App.Button.Icon"),
                    Assert.IsType<Button>(window.FindName("MiniPlayerTopmostButton")).Style);
                Assert.Same(window.FindResource("App.Button.DangerIcon"),
                    Assert.IsType<Button>(window.FindName("MiniPlayerCloseButton")).Style);
                Assert.Null(VisualTreeTestHelper.FindDescendant<TextBlock>(
                    volumePopup,
                    textBlock => textBlock.Text == "仅调整应用内播放音量，不改变系统音量。"));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Drag_policy_allows_blank_surface_but_excludes_interactive_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var blankSurface = new Border();
            var button = new Button();
            var slider = new Slider();
            var textBox = new TextBox();

            Assert.True(MiniPlayerWindowDragPolicy.CanStartDrag(blankSurface));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(button));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(slider));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(textBox));
        });
    }

    [Theory]
    [InlineData(double.NaN, 20)]
    [InlineData(10, double.PositiveInfinity)]
    [InlineData(-1, 20)]
    [InlineData(900, 20)]
    public void Invalid_or_offscreen_placement_uses_safe_fallback(double left, double top)
    {
        Assert.False(MiniPlayerPlacementValidator.TryValidate(
            left,
            top,
            200,
            100,
            [new MiniPlayerScreenBounds(0, 0, 1000, 800)],
            out _));
    }

    [Fact]
    public void Valid_placement_is_preserved()
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

    [Fact]
    public void User_close_requests_application_exit_instead_of_restoring_main_window()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MiniPlayerWindow>();
                var exitRequested = false;
                window.ExitRequested += (_, _) => exitRequested = true;
                window.Show();

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

    [Fact]
    public void Close_button_requests_application_exit_without_restoring_main_window()
    {
        WpfTestHost.RunInSta(() =>
        {
            var fixture = CreateWindow(PlaybackSnapshot.Idle);
            try
            {
                var exitRequested = false;
                fixture.Window.ExitRequested += (_, _) => exitRequested = true;
                fixture.Window.Show();

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

    [Fact]
    public void Playback_context_projection_keeps_window_actions_and_media_command_contract()
    {
        WpfTestHost.RunInSta(() =>
        {
            var snapshot = PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Paused,
                BookId = "book-1",
                BookTitle = "一部很长的测试小说名称，用于验证迷你播放器标题不会改变窗口结构",
                ChapterTitle = "第十二章 这是一个很长的章节标题，用于验证省略和窗口动作仍然可用",
                ChapterIndex = 11,
                SegmentIndex = 2,
                SegmentCount = 8
            };
            var fixture = CreateWindow(snapshot);
            try
            {
                fixture.Window.Show();
                fixture.Window.UpdateLayout();

                Assert.True(FindButton(fixture.Window, "MiniPlayerPreviousChapterButton").IsEnabled);
                Assert.True(FindButton(fixture.Window, "MiniPlayerNextChapterButton").IsEnabled);
                Assert.True(FindButton(fixture.Window, "MiniPlayerPlaybackButton").IsEnabled);
                Assert.Equal("一部很长的测试小说名称，用于验证迷你播放器标题不会改变窗口结构", fixture.ViewModel.BookTitle);
                Assert.Equal(
                    "第十二章 这是一个很长的章节标题，用于验证省略和窗口动作仍然可用",
                    fixture.ViewModel.ChapterTitle);

                var restoreRequested = false;
                fixture.Window.RestoreRequested += (_, _) => restoreRequested = true;
                FindButton(fixture.Window, "MiniPlayerTopmostButton").Command!.Execute(null);

                Assert.True(fixture.ViewModel.IsTopmost);
                Assert.True(fixture.Window.Topmost);
                Assert.Equal("取消置顶", FindButton(fixture.Window, "MiniPlayerTopmostButton").ToolTip);

                FindButton(fixture.Window, "MiniPlayerRestoreButton").Command!.Execute(null);
                Assert.True(restoreRequested);
            }
            finally
            {
                CloseFixture(fixture);
            }
        });
    }

    [Fact]
    public void Idle_projection_keeps_window_surface_and_media_controls_safe_without_playback_context()
    {
        WpfTestHost.RunInSta(() =>
        {
            var fixture = CreateWindow(PlaybackSnapshot.Idle);
            try
            {
                fixture.Window.Show();
                fixture.Window.UpdateLayout();

                Assert.False(fixture.ViewModel.HasPlaybackContext);
                Assert.False(FindButton(fixture.Window, "MiniPlayerPreviousChapterButton").IsEnabled);
                Assert.False(FindButton(fixture.Window, "MiniPlayerNextChapterButton").IsEnabled);
                Assert.False(FindButton(fixture.Window, "MiniPlayerPlaybackButton").IsEnabled);
                Assert.Equal("未打开书籍", fixture.ViewModel.BookTitle);
                Assert.Equal("尚未定位章节", fixture.ViewModel.ChapterTitle);
            }
            finally
            {
                CloseFixture(fixture);
            }
        });
    }

    [Fact]
    public void Default_layout_keeps_progress_and_control_bar_close_without_overlap()
    {
        WpfTestHost.RunInSta(() =>
        {
            var themeRuntime = new WpfUiThemeRuntime();
            var scenarios = new[]
            {
                (Name: "no-context", Snapshot: PlaybackSnapshot.Idle),
                (Name: "with-context", Snapshot: PlaybackSnapshot.Idle with
                {
                    State = PlaybackState.Paused,
                    BookId = "geometry-book",
                    BookTitle = "几何测试书名",
                    ChapterTitle = "几何测试章节",
                    ChapterIndex = 1,
                    SegmentIndex = 1,
                    SegmentCount = 4
                })
            };

            try
            {
                foreach (var (themeName, applyTheme) in new (string Name, Action Apply)[]
                         {
                             ("light", themeRuntime.ApplyLightTheme),
                             ("dark", themeRuntime.ApplyDarkTheme)
                         })
                {
                    applyTheme();
                    foreach (var (_, snapshot) in scenarios)
                    {
                        var fixture = CreateWindow(snapshot);
                        try
                        {
                            fixture.Window.Show();
                            fixture.Window.UpdateLayout();

                            var surface = Assert.IsType<Border>(fixture.Window.FindName("MiniPlayerSurface"));
                            var progress = Assert.IsType<Slider>(fixture.Window.FindName("MiniPlayerProgressSlider"));
                            var controlBar = Assert.IsType<Grid>(fixture.Window.FindName("MiniPlayerControlBar"));
                            var playbackButton = Assert.IsType<Button>(fixture.Window.FindName("MiniPlayerPlaybackButton"));
                            var progressBounds = GetBoundsIn(progress, surface);
                            var controlBarBounds = GetBoundsIn(controlBar, surface);
                            var gap = controlBarBounds.Top - progressBounds.Bottom;

                            Assert.True(
                                gap is >= 0.5 and <= 24,
                                $"Progress/control-bar gap was {gap:0.##} DIP in {themeName}.");
                            Assert.False(progressBounds.IntersectsWith(controlBarBounds));
                            Assert.True(progress.IsVisible);
                            Assert.True(progress.ActualWidth > 0);
                            Assert.True(progress.ActualHeight > 0);
                            Assert.True(controlBar.IsVisible);
                            Assert.True(controlBar.ActualWidth > 0);
                            Assert.True(controlBar.ActualHeight > 0);
                            Assert.Equal(48, playbackButton.ActualWidth);
                            Assert.Equal(48, playbackButton.ActualHeight);

                            foreach (var name in new[]
                                     {
                                         "MiniPlayerPreviousChapterButton",
                                         "MiniPlayerPreviousSegmentButton",
                                         "MiniPlayerPlaybackButton",
                                         "MiniPlayerNextSegmentButton",
                                         "MiniPlayerNextChapterButton",
                                         "MiniPlayerVolumeMenuButton"
                                     })
                            {
                                var button = Assert.IsType<Button>(fixture.Window.FindName(name));
                                Assert.True(button.IsVisible);
                                Assert.True(button.ActualWidth > 0);
                                Assert.True(button.ActualHeight > 0);
                                Assert.Equal(new Thickness(0), button.BorderThickness);
                                Assert.Equal(
                                    Colors.Transparent,
                                    Assert.IsType<SolidColorBrush>(button.Background).Color);
                            }
                        }
                        finally
                        {
                            CloseFixture(fixture);
                        }
                    }
                }
            }
            finally
            {
                themeRuntime.ApplyLightTheme();
            }
        });
    }

    [Fact]
    public void Saved_position_is_restored_and_a_user_move_is_persisted()
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
                fixture.Window.Show();
                fixture.Window.UpdateLayout();

                Assert.Equal(120, fixture.Window.Left);
                Assert.Equal(140, fixture.Window.Top);
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

    [Fact]
    public void Task10_visual_review_generates_light_dark_context_and_dpi_screenshots()
    {
        WpfTestHost.RunInSta(() =>
        {
            var outputDirectory = Path.Combine(
                LocateRepositoryRoot(),
                "artifacts",
                "visual-review",
                "10");
            Directory.CreateDirectory(outputDirectory);
            var expectedGitCommit = ReadGitCommit(LocateRepositoryRoot());
            GenerateVisualReview(outputDirectory, expectedGitCommit);
            var firstManifest = ReadVisualReviewManifest(outputDirectory);
            AssertManifestMatchesPngs(firstManifest, outputDirectory, expectedGitCommit);
            var firstSnapshot = CreateVisualReviewSnapshot(firstManifest);

            GenerateVisualReview(outputDirectory, expectedGitCommit);
            var secondManifest = ReadVisualReviewManifest(outputDirectory);
            AssertManifestMatchesPngs(secondManifest, outputDirectory, expectedGitCommit);
            Assert.Equal(firstSnapshot, CreateVisualReviewSnapshot(secondManifest));
        });
    }

    [Fact]
    public void Placement_in_gap_between_monitors_is_rejected()
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

    private static void AssertUsableLayout(MiniPlayerWindow window, double scale)
    {
        Assert.True(FindButton(window, "MiniPlayerTopmostButton").ActualWidth >= 32 * 0.9, $"Topmost button collapsed at {scale:0.##}x.");
        Assert.True(FindButton(window, "MiniPlayerRestoreButton").ActualWidth >= 32 * 0.9, $"Restore button collapsed at {scale:0.##}x.");
        Assert.True(FindButton(window, "MiniPlayerPlaybackButton").ActualWidth >= 44 * 0.9, $"Playback button collapsed at {scale:0.##}x.");
        Assert.True(FindButton(window, "MiniPlayerVolumeMenuButton").ActualWidth >= 32 * 0.9, $"Volume button collapsed at {scale:0.##}x.");
        Assert.True(Assert.IsType<TextBlock>(window.FindName("MiniPlayerBookTitle")).ActualWidth > 0);
        Assert.True(Assert.IsType<TextBlock>(window.FindName("MiniPlayerChapterTitle")).ActualWidth > 0);
    }

    private static void GenerateVisualReview(string outputDirectory, string gitCommit)
    {
        var entries = new List<MiniPlayerVisualReviewEntry>();
        var themeRuntime = new WpfUiThemeRuntime();
        var scenarios = new Dictionary<string, PlaybackSnapshot>(StringComparer.Ordinal)
        {
            ["no-context"] = PlaybackSnapshot.Idle,
            ["long-context"] = PlaybackSnapshot.Idle with
            {
                State = PlaybackState.Paused,
                BookId = "visual-review-book",
                BookTitle = "一部非常非常长的小说名称，用于迷你播放器视觉回归截图和文本省略场景",
                ChapterTitle = "第九十九章 这是一个非常非常长的章节名称，用于 Light Dark 和 DPI 回归",
                ChapterIndex = 98,
                SegmentIndex = 4,
                SegmentCount = 12
            }
        };

        try
        {
            foreach (var (themeName, applyTheme) in new (string Name, Action Apply)[]
                     {
                         ("light", themeRuntime.ApplyLightTheme),
                         ("dark", themeRuntime.ApplyDarkTheme)
                     })
            {
                applyTheme();
                foreach (var (scenarioName, snapshot) in scenarios)
                {
                    var fixture = CreateWindow(snapshot);
                    try
                    {
                        fixture.Window.Show();
                        fixture.Window.UpdateLayout();
                        var surface = Assert.IsType<Border>(fixture.Window.FindName("MiniPlayerSurface"));
                        Assert.True(surface.ActualWidth > 0);

                        foreach (var scale in new[] { 1d, 1.25d, 1.5d })
                        {
                            fixture.Window.UpdateLayout();
                            AssertUsableLayout(fixture.Window, scale);
                            var png = EncodePng(Render(fixture.Window, scale));
                            var fileName = $"mini-player.{themeName}.{scenarioName}.{scale * 100:0}.png";
                            var frame = DecodePng(png);
                            File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
                            entries.Add(new MiniPlayerVisualReviewEntry(
                                themeName,
                                scenarioName,
                                scale,
                                96 * scale,
                                frame.PixelWidth,
                                frame.PixelHeight,
                                fileName,
                                Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant()));
                        }
                    }
                    finally
                    {
                        CloseFixture(fixture);
                    }
                }
            }

            var manifest = new MiniPlayerVisualReviewManifest(
                "10",
                "NovelSpeaker.App.WpfTests",
                gitCommit,
                480,
                150,
                entries);
            File.WriteAllText(
                Path.Combine(outputDirectory, "manifest.json"),
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            themeRuntime.ApplyLightTheme();
        }
    }

    private static MiniPlayerVisualReviewManifest ReadVisualReviewManifest(string outputDirectory)
    {
        using var stream = File.OpenRead(Path.Combine(outputDirectory, "manifest.json"));
        return JsonSerializer.Deserialize<MiniPlayerVisualReviewManifest>(stream)
            ?? throw new InvalidDataException("Task 09 visual review manifest was empty.");
    }

    private static void AssertManifestMatchesPngs(
        MiniPlayerVisualReviewManifest manifest,
        string outputDirectory,
        string expectedGitCommit)
    {
        Assert.Equal("10", manifest.Task);
        Assert.Equal("NovelSpeaker.App.WpfTests", manifest.Tool);
        Assert.Equal(expectedGitCommit, manifest.GitCommit);
        Assert.True(IsValidGitCommit(manifest.GitCommit));
        Assert.Equal(480, manifest.WindowWidth);
        Assert.Equal(150, manifest.WindowHeight);
        Assert.Equal(12, manifest.Scenarios.Count);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Scenarios)
        {
            Assert.True(entry.Theme is "light" or "dark");
            Assert.True(entry.Scenario is "no-context" or "long-context");
            Assert.True(entry.Scale is 1d or 1.25d or 1.5d);
            Assert.Equal(96 * entry.Scale, entry.Dpi);
            Assert.True(keys.Add($"{entry.Theme}|{entry.Scenario}|{entry.Scale:R}"));
            Assert.Equal(32, Convert.FromHexString(entry.Sha256).Length);

            var pngPath = Path.Combine(outputDirectory, entry.Png);
            Assert.True(File.Exists(pngPath), $"Missing visual review PNG '{entry.Png}'.");
            var png = File.ReadAllBytes(pngPath);
            Assert.Equal(
                entry.Sha256,
                Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant());

            using var stream = new MemoryStream(png, writable: false);
            var frame = BitmapFrame.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            Assert.True(entry.Width > 0);
            Assert.True(entry.Height > 0);
            Assert.Equal(entry.Width, frame.PixelWidth);
            Assert.Equal(entry.Height, frame.PixelHeight);
            Assert.InRange(frame.DpiX, entry.Dpi - 0.1, entry.Dpi + 0.1);
            Assert.InRange(frame.DpiY, entry.Dpi - 0.1, entry.Dpi + 0.1);
        }

        Assert.Equal(12, keys.Count);
    }

    private static string[] CreateVisualReviewSnapshot(MiniPlayerVisualReviewManifest manifest) =>
        new[]
        {
            JsonSerializer.Serialize(new
            {
                manifest.Task,
                manifest.Tool,
                manifest.GitCommit,
                manifest.WindowWidth,
                manifest.WindowHeight
            })
        }
        .Concat(manifest.Scenarios
            .OrderBy(entry => entry.Theme, StringComparer.Ordinal)
            .ThenBy(entry => entry.Scenario, StringComparer.Ordinal)
            .ThenBy(entry => entry.Scale)
            .Select(entry => JsonSerializer.Serialize(entry)))
            .ToArray();

    private static BitmapSource Render(MiniPlayerWindow window, double scale)
    {
        var bitmap = new RenderTargetBitmap(
            Pixels(window.Width, scale),
            Pixels(window.Height, scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        bitmap.Freeze();
        return bitmap;
    }

    private static int Pixels(double dip, double scale) =>
        (int)Math.Round(dip * scale, MidpointRounding.AwayFromZero);

    private static Button FindButton(MiniPlayerWindow window, string name) =>
        Assert.IsType<Button>(window.FindName(name));

    private static Rect GetBoundsIn(FrameworkElement element, UIElement ancestor)
    {
        var topLeft = element.TranslatePoint(new Point(0, 0), ancestor);
        return new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapFrame DecodePng(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        return BitmapFrame.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static string ReadGitCommit(string repositoryRoot)
    {
        var gitDirectory = ResolveGitDirectory(repositoryRoot);
        var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
        var commit = head.StartsWith("ref: ", StringComparison.Ordinal)
            ? ReadGitReference(gitDirectory, head[5..].Trim())
            : head;

        if (!IsValidGitCommit(commit))
        {
            throw new InvalidDataException("Repository HEAD does not contain a valid Git commit.");
        }

        return commit!;
    }

    private static string? ReadGitReference(string gitDirectory, string referenceName)
    {
        var referencePath = Path.Combine(
            gitDirectory,
            referenceName.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(referencePath))
        {
            return File.ReadAllText(referencePath).Trim();
        }

        var packedRefsPath = Path.Combine(gitDirectory, "packed-refs");
        if (!File.Exists(packedRefsPath))
        {
            return null;
        }

        foreach (var line in File.ReadLines(packedRefsPath))
        {
            if (line.StartsWith('#') || line.StartsWith('^'))
            {
                continue;
            }

            var parts = line.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[1].Equals(referenceName, StringComparison.Ordinal))
            {
                return parts[0];
            }
        }

        return null;
    }

    private static string ResolveGitDirectory(string repositoryRoot)
    {
        var dotGitPath = Path.Combine(repositoryRoot, ".git");
        if (Directory.Exists(dotGitPath))
        {
            return dotGitPath;
        }

        if (File.Exists(dotGitPath))
        {
            var gitDirLine = File.ReadAllText(dotGitPath).Trim();
            const string prefix = "gitdir: ";
            if (gitDirLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var gitDirectory = gitDirLine[prefix.Length..].Trim();
                return Path.GetFullPath(
                    Path.IsPathRooted(gitDirectory)
                        ? gitDirectory
                        : Path.Combine(repositoryRoot, gitDirectory));
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository Git directory.");
    }

    private static bool IsValidGitCommit(string? commit) =>
        commit is not null &&
        (commit.Length == 40 || commit.Length == 64) &&
        commit.All(Uri.IsHexDigit);

    private sealed record MiniPlayerFixture(
        MiniPlayerWindow Window,
        MiniPlayerViewModel ViewModel,
        FakeAppSettingsService Settings);

    private sealed record MiniPlayerVisualReviewManifest(
        string Task,
        string Tool,
        string GitCommit,
        int WindowWidth,
        int WindowHeight,
        IReadOnlyList<MiniPlayerVisualReviewEntry> Scenarios);

    private sealed record MiniPlayerVisualReviewEntry(
        string Theme,
        string Scenario,
        double Scale,
        double Dpi,
        int Width,
        int Height,
        string Png,
        string Sha256);

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
