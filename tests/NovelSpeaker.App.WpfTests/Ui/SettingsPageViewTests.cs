using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell.Navigation;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SettingsPageViewTests
{
    [Fact]
    public void SettingsPage_uses_formal_header_groups_and_navigation_rows()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<SettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 800));

                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Same(page.FindResource(typeof(AppPageHeader)), header.Style);
                Assert.Equal("设置", header.Title);
                Assert.Null(header.BackCommand);

                var rootGrid = Assert.IsType<Grid>(page.Content);
                Assert.Equal(new Thickness(24), rootGrid.Margin);
                Assert.Equal(Brushes.Transparent, page.Background);

                var groups = VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(page).ToArray();
                Assert.Equal(3, groups.Length);
                Assert.Equal(["常用", "文本处理", "应用"], groups.Select(group => group.Header));
                Assert.Equal([3, 2, 3], groups.Select(group => group.Items.Count));
                Assert.All(groups, group =>
                {
                    Assert.Same(page.FindResource(typeof(AppSettingsGroup)), group.Style);
                    Assert.Equal(new Thickness(0, 0, 0, 16), group.Margin);
                });

                var expectedTitles = new[]
                {
                    "播放设置", "TTS 规则", "常规", "导入与文本", "章节规则", "缓存与数据", "外观", "诊断与关于"
                };
                var expectedIcons = new[]
                {
                    SymbolRegular.PlayCircle24,
                    SymbolRegular.Speaker124,
                    SymbolRegular.Settings24,
                    SymbolRegular.DocumentText24,
                    SymbolRegular.TextBulletListSquare24,
                    SymbolRegular.Database24,
                    SymbolRegular.DarkTheme24,
                    SymbolRegular.Info24
                };
                var rows = VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(page).ToArray();
                Assert.Equal(expectedTitles, rows.Select(row => row.Title));
                Assert.All(rows, row =>
                {
                    Assert.Same(page.FindResource(typeof(AppSettingsNavigationRow)), row.Style);
                    Assert.True(row.Focusable);
                    Assert.True(row.IsTabStop);
                    Assert.Equal(row.Title, AutomationProperties.GetName(row));
                    Assert.Equal(row.Title, row.ToolTip);
                    Assert.NotNull(row.Command);
                    Assert.True(row.Command!.CanExecute(null));
                });

                for (var index = 0; index < rows.Length; index++)
                {
                    var icons = VisualTreeTestHelper.FindDescendants<SymbolIcon>(rows[index]).ToArray();
                    Assert.Equal(2, icons.Length);
                    Assert.Equal(expectedIcons[index], icons[0].Symbol);
                    Assert.Equal(SymbolRegular.ChevronRight24, icons[1].Symbol);
                }

                Assert.Empty(VisualTreeTestHelper.FindDescendants<TextBox>(page));
                Assert.Empty(VisualTreeTestHelper.FindDescendants<ComboBox>(page));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    public void SettingsPage_navigation_rows_do_not_overlap_at_narrow_window_and_supported_dpi(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<SettingsPage>();
                page.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(520, 900));

                var rows = VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(page).ToArray();
                Assert.Equal(8, rows.Length);
                foreach (var row in rows)
                {
                    Assert.True(row.ActualWidth > 0);
                    Assert.True(row.ActualHeight >= 60);

                    var title = Assert.Single(
                        VisualTreeTestHelper.FindDescendants<TextBlock>(row),
                        textBlock => textBlock.Text == row.Title);
                    var chevron = Assert.Single(
                        VisualTreeTestHelper.FindDescendants<SymbolIcon>(row),
                        icon => icon.Symbol == SymbolRegular.ChevronRight24);
                    var titleBounds = title.TransformToAncestor(row)
                        .TransformBounds(new Rect(new Point(), title.RenderSize));
                    var chevronBounds = chevron.TransformToAncestor(row)
                        .TransformBounds(new Rect(new Point(), chevron.RenderSize));

                    Assert.True(
                        titleBounds.Right <= chevronBounds.Left,
                        $"{row.Title} overlaps its Chevron at {scale:0.##}x scale.");
                    Assert.True(titleBounds.Bottom <= row.ActualHeight);
                    Assert.True(chevronBounds.Right <= row.ActualWidth);
                }
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void SettingsPage_navigation_rows_activate_bound_routes_with_keyboard()
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigator = new RecordingNavigator();
            var page = new SettingsPage(new SettingsViewModel(navigator));
            using var host = WpfWindowHost.Show(new Window
            {
                Content = page,
                Width = 520,
                Height = 900,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var rows = VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(page).ToArray();
            var expectedRoutes = new[]
            {
                AppRoutes.PlaybackSettings,
                AppRoutes.TtsRules,
                AppRoutes.GeneralSettings,
                AppRoutes.ImportTextSettings,
                AppRoutes.ChapterRules,
                AppRoutes.CacheAndData,
                AppRoutes.AppearanceSettings,
                AppRoutes.DiagnosticsAbout
            };

            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                Assert.True(row.Focus());
                Assert.True(row.IsKeyboardFocused);

                var source = PresentationSource.FromVisual(row);
                Assert.NotNull(source);
                var keyDown = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    source!,
                    Environment.TickCount,
                    Key.Space)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                };
                row.RaiseEvent(keyDown);

                var keyUp = new KeyEventArgs(
                    Keyboard.PrimaryDevice,
                    source!,
                    Environment.TickCount,
                    Key.Space)
                {
                    RoutedEvent = Keyboard.KeyUpEvent
                };
                row.RaiseEvent(keyUp);

                Assert.Same(expectedRoutes[index], navigator.LastRoute);
            }
        });
    }

    [Fact]
    public void SettingsPage_does_not_reference_legacy_page_or_navigation_resources()
    {
        var source = File.ReadAllText(Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "Settings",
            "SettingsPage.xaml"));

        Assert.DoesNotContain("PagePadding", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SectionSpacing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PageTitleTextBlockStyle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SectionTitleTextBlockStyle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsGroupBorderStyle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsNavigationRowButtonStyle", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_home_visual_review_generates_stable_page_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var outputDirectory = Path.Combine(
                LocateRepositoryRoot(),
                "artifacts",
                "visual-review",
                "pages",
                "settings-home");
            Directory.CreateDirectory(outputDirectory);
            var repositoryRoot = LocateRepositoryRoot();
            var expectedGitCommit = ReadGitCommit(repositoryRoot);
            GenerateVisualReview(outputDirectory, expectedGitCommit);
            var firstManifest = ReadManifest(outputDirectory);
            AssertManifestMatchesPngs(firstManifest, outputDirectory, expectedGitCommit);
            var firstSnapshot = CreateSnapshot(firstManifest);

            GenerateVisualReview(outputDirectory, expectedGitCommit);
            var secondManifest = ReadManifest(outputDirectory);
            AssertManifestMatchesPngs(secondManifest, outputDirectory, expectedGitCommit);
            Assert.Equal(firstSnapshot, CreateSnapshot(secondManifest));
        });
    }

    private static void GenerateVisualReview(string outputDirectory, string gitCommit)
    {
        var entries = new List<SettingsVisualReviewEntry>();
        var themeRuntime = new WpfUiThemeRuntime();

        try
        {
            foreach (var (themeName, applyTheme) in new (string Name, Action Apply)[]
                     {
                         ("light", themeRuntime.ApplyLightTheme),
                         ("dark", themeRuntime.ApplyDarkTheme)
                     })
            {
                applyTheme();
                var provider = WpfTestHost.BuildServiceProvider();
                try
                {
                    var page = provider.GetRequiredService<SettingsPage>();
                    var size = new Size(960, 640);
                    using var host = new WpfControlHost(page);
                    host.MeasureArrange(size);
                    Assert.True(page.ActualWidth > 0);
                    Assert.True(page.ActualHeight > 0);

                    foreach (var scale in new[] { 1d, 1.25d, 1.5d })
                    {
                        var png = EncodePng(RenderWithShellCanvas(host.Render(size, 96 * scale), size, 96 * scale));
                        var frame = DecodePng(png);
                        var fileName = $"settings-home.{themeName}.{scale * 100:0}.png";
                        File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
                        entries.Add(new SettingsVisualReviewEntry(
                            themeName,
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
                    provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }

            var manifest = new SettingsVisualReviewManifest(
                "settings-home",
                "NovelSpeaker.App.WpfTests",
                gitCommit,
                960,
                640,
                entries);
            File.WriteAllText(
                Path.Combine(outputDirectory, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            themeRuntime.ApplyLightTheme();
        }
    }

    private static BitmapSource RenderWithShellCanvas(BitmapSource page, Size size, double dpi)
    {
        var shell = new Border
        {
            Width = size.Width,
            Height = size.Height,
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)global::System.Windows.Application.Current!.FindResource("App.Radius.Large")
        };
        shell.SetResourceReference(Border.BackgroundProperty, "NavigationViewContentBackground");
        shell.SetResourceReference(Border.BorderBrushProperty, "NavigationViewContentGridBorderBrush");
        shell.Measure(size);
        shell.Arrange(new Rect(new Point(), size));
        shell.UpdateLayout();

        var pixels = (int)Math.Round(size.Width * dpi / 96d);
        var shellBitmap = new RenderTargetBitmap(
            pixels,
            (int)Math.Round(size.Height * dpi / 96d),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        shellBitmap.Render(shell);

        var compositeVisual = new DrawingVisual();
        using (var drawingContext = compositeVisual.RenderOpen())
        {
            var bounds = new Rect(new Point(), size);
            drawingContext.DrawImage(shellBitmap, bounds);
            drawingContext.DrawImage(page, bounds);
        }

        var composite = new RenderTargetBitmap(
            pixels,
            (int)Math.Round(size.Height * dpi / 96d),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        composite.Render(compositeVisual);
        composite.Freeze();
        return composite;
    }

    private static SettingsVisualReviewManifest ReadManifest(string outputDirectory)
    {
        using var stream = File.OpenRead(Path.Combine(outputDirectory, "manifest.json"));
        return JsonSerializer.Deserialize<SettingsVisualReviewManifest>(stream)
            ?? throw new InvalidDataException("Settings home visual review manifest was empty.");
    }

    private static void AssertManifestMatchesPngs(
        SettingsVisualReviewManifest manifest,
        string outputDirectory,
        string expectedGitCommit)
    {
        Assert.Equal("settings-home", manifest.ArtifactId);
        Assert.Equal("NovelSpeaker.App.WpfTests", manifest.Tool);
        Assert.Equal(expectedGitCommit, manifest.GitCommit);
        Assert.Equal(960, manifest.WindowWidth);
        Assert.Equal(640, manifest.WindowHeight);
        Assert.Equal(6, manifest.Scenes.Count);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in manifest.Scenes)
        {
            Assert.True(entry.Theme is "light" or "dark");
            Assert.True(entry.Scale is 1d or 1.25d or 1.5d);
            Assert.Equal(96 * entry.Scale, entry.Dpi);
            Assert.True(keys.Add($"{entry.Theme}|{entry.Scale:R}"));
            Assert.Equal(32, Convert.FromHexString(entry.Sha256).Length);

            var pngPath = Path.Combine(outputDirectory, entry.Png);
            Assert.True(File.Exists(pngPath), $"Missing visual review PNG '{entry.Png}'.");
            var png = File.ReadAllBytes(pngPath);
            Assert.Equal(
                entry.Sha256,
                Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant());

            var frame = DecodePng(png);
            Assert.Equal(entry.Width, frame.PixelWidth);
            Assert.Equal(entry.Height, frame.PixelHeight);
            Assert.InRange(frame.DpiX, entry.Dpi - 0.1, entry.Dpi + 0.1);
            Assert.InRange(frame.DpiY, entry.Dpi - 0.1, entry.Dpi + 0.1);
        }

        Assert.Equal(6, keys.Count);
    }

    private static string[] CreateSnapshot(SettingsVisualReviewManifest manifest) =>
        new[]
        {
            JsonSerializer.Serialize(new
            {
                manifest.ArtifactId,
                manifest.Tool,
                manifest.GitCommit,
                manifest.WindowWidth,
                manifest.WindowHeight
            })
        }
        .Concat(manifest.Scenes
            .OrderBy(entry => entry.Theme, StringComparer.Ordinal)
            .ThenBy(entry => entry.Scale)
            .Select(entry => JsonSerializer.Serialize(entry)))
        .ToArray();

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
                Directory.Exists(Path.Combine(current.FullName, "docs")) &&
                File.Exists(Path.Combine(current.FullName, "NovelSpeaker.slnx")))
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

        if (commit is null ||
            (commit.Length != 40 && commit.Length != 64) ||
            !commit.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("Repository HEAD does not contain a valid Git commit.");
        }

        return commit;
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

    private sealed record SettingsVisualReviewManifest(
        string ArtifactId,
        string Tool,
        string GitCommit,
        int WindowWidth,
        int WindowHeight,
        IReadOnlyList<SettingsVisualReviewEntry> Scenes);

    private sealed record SettingsVisualReviewEntry(
        string Theme,
        double Scale,
        double Dpi,
        int Width,
        int Height,
        string Png,
        string Sha256);

    private sealed class RecordingNavigator : IAppNavigator
    {
        public AppRoute? LastRoute { get; private set; }

        public Task<bool> NavigateAsync(
            AppRoute route,
            CancellationToken cancellationToken,
            bool bypassGuard = false)
        {
            LastRoute = route;
            return Task.FromResult(true);
        }

        public Task<bool> GoBackAsync(
            CancellationToken cancellationToken,
            bool bypassGuard = false) =>
            Task.FromResult(false);
    }
}
