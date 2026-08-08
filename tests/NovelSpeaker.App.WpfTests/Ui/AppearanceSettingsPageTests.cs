using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using Wpf.Ui.Appearance;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class AppearanceSettingsPageTests
{
    [Fact]
    public void Appearance_page_uses_headerless_settings_list_without_group()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<AppearanceSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));

                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Same(page.FindResource(typeof(AppPageHeader)), header.Style);
                Assert.Equal("外观", header.Title);
                var backCommandBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(header, AppPageHeader.BackCommandProperty));
                Assert.Equal(nameof(AppearanceSettingsViewModel.BackCommand), backCommandBinding.Path.Path);

                var backButton = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Button>(header),
                    candidate => AutomationProperties.GetName(candidate) == "返回");
                Assert.Equal("返回", backButton.ToolTip);
                Assert.Same(page.FindResource("App.Button.Icon"), backButton.Style);

                var pageTitle = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(header),
                    textBlock => textBlock.Text == "外观");
                Assert.Same(page.FindResource("App.Typography.PageTitle"), pageTitle.Style);

                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(page));
                Assert.DoesNotContain(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(page),
                    textBlock => ReferenceEquals(textBlock.Style, page.FindResource("App.Typography.GroupTitle")));

                var row = Assert.IsType<AppSettingsRow>(page.FindName("ThemeSettingRow"));
                Assert.Same(page.FindResource(typeof(AppSettingsRow)), row.Style);
                Assert.Equal("应用主题", row.Title);
                Assert.Equal("跟随系统，或固定使用浅色、深色主题。", row.Description);
                Assert.Equal("应用主题设置", AutomationProperties.GetName(row));
                Assert.False(row.Focusable);
                Assert.False(row.IsTabStop);
                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Same(page.FindResource(typeof(AppSettingsList)), settingsList.Style);
                Assert.Same(row, Assert.Single(settingsList.Items));
                Assert.Equal("外观设置", AutomationProperties.GetName(settingsList));

                var comboBox = Assert.IsType<ComboBox>(page.FindName("ThemeComboBox"));
                Assert.Same(page.FindResource("App.Input.ComboBox.Standard"), comboBox.Style);
                Assert.Equal("应用主题", AutomationProperties.GetName(comboBox));
                Assert.Equal(3, comboBox.Items.Count);
                Assert.Same(comboBox, row.Value);

                var itemsSourceBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(comboBox, ItemsControl.ItemsSourceProperty));
                Assert.Equal(nameof(AppearanceSettingsViewModel.AvailableThemes), itemsSourceBinding.Path.Path);
                Assert.Equal(BindingMode.OneWay, itemsSourceBinding.Mode);

                var selectedItemBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(comboBox, Selector.SelectedItemProperty));
                Assert.Equal(nameof(AppearanceSettingsViewModel.SelectedTheme), selectedItemBinding.Path.Path);
                Assert.Equal(BindingMode.TwoWay, selectedItemBinding.Mode);

                var rowText = VisualTreeTestHelper.FindDescendants<TextBlock>(row).ToArray();
                Assert.Contains(rowText, textBlock =>
                    textBlock.Text == "应用主题" &&
                    ReferenceEquals(textBlock.Style, page.FindResource("App.Typography.ItemTitle")));
                Assert.Contains(rowText, textBlock =>
                    textBlock.Text == "跟随系统，或固定使用浅色、深色主题。" &&
                    ReferenceEquals(textBlock.Style, page.FindResource("App.Typography.Secondary")));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Appearance_page_owns_canvas_background_without_window_shell_ring()
    {
        var xamlPath = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "Appearance",
            "AppearanceSettingsPage.xaml");
        var source = File.ReadAllText(xamlPath);
        var pageElement = XDocument.Load(xamlPath).Root!;

        Assert.Equal("Page", pageElement.Name.LocalName);
        Assert.Equal(
            "Transparent",
            pageElement.Attribute("Background")?.Value);
        Assert.DoesNotContain(
            source,
            "App.Brush.Window.Background",
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            source,
            "AppSettingsGroup",
            StringComparison.Ordinal);
        Assert.Contains(
            "AppSettingsList",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            pageElement.Descendants(),
            element => element.Attribute("Background") is not null);

        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<AppearanceSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));

                Assert.Equal(Brushes.Transparent, page.Background);

                var rootGrid = Assert.IsType<Grid>(page.Content);
                Assert.Equal(new Thickness(24), rootGrid.Margin);
                Assert.Null(rootGrid.Background);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Appearance_page_keeps_theme_row_non_overlapping_at_narrow_and_wide_widths()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<AppearanceSettingsPage>();
                using var host = new WpfControlHost(page);

                var row = Assert.IsType<AppSettingsRow>(page.FindName("ThemeSettingRow"));
                var comboBox = Assert.IsType<ComboBox>(page.FindName("ThemeComboBox"));

                host.MeasureArrange(new Size(520, 900));
                Assert.True(row.IsNarrowLayout);
                Assert.True(row.ActualWidth > 0);
                Assert.True(row.ActualHeight >= 60);
                Assert.True(comboBox.ActualWidth >= 180);

                var title = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(row),
                    textBlock => textBlock.Text == "应用主题");
                var titleBounds = title.TransformToAncestor(row)
                    .TransformBounds(new Rect(new Point(), title.RenderSize));
                var valueBounds = comboBox.TransformToAncestor(row)
                    .TransformBounds(new Rect(new Point(), comboBox.RenderSize));
                Assert.True(titleBounds.Bottom <= valueBounds.Top);

                host.MeasureArrange(new Size(1200, 900));
                Assert.False(row.IsNarrowLayout);
                Assert.True(row.ActualWidth > 0);
                Assert.True(comboBox.ActualWidth >= 180);

                var wideTitleBounds = title.TransformToAncestor(row)
                    .TransformBounds(new Rect(new Point(), title.RenderSize));
                var wideValueBounds = comboBox.TransformToAncestor(row)
                    .TransformBounds(new Rect(new Point(), comboBox.RenderSize));
                Assert.True(wideTitleBounds.Right <= wideValueBounds.Left);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Theory]
    [InlineData(ApplicationTheme.Dark)]
    [InlineData(ApplicationTheme.Light)]
    public void Appearance_page_constructs_after_runtime_theme_switch(ApplicationTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            var themeRuntime = new WpfUiThemeRuntime();
            if (theme == ApplicationTheme.Dark)
            {
                themeRuntime.ApplyDarkTheme();
            }
            else
            {
                themeRuntime.ApplyLightTheme();
            }
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<AppearanceSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));
                Assert.True(page.ActualWidth > 0);
                Assert.True(page.ActualHeight > 0);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                themeRuntime.ApplyLightTheme();
            }
        });
    }

    [Fact]
    public void Appearance_settings_visual_review_generates_stable_page_screenshots()
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
                "appearance-settings");
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
        var entries = new List<AppearanceVisualReviewEntry>();
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
                    var page = provider.GetRequiredService<AppearanceSettingsPage>();
                    var size = new Size(960, 640);
                    using var host = new WpfControlHost(page);
                    host.MeasureArrange(size);
                    Assert.True(page.ActualWidth > 0);
                    Assert.True(page.ActualHeight > 0);

                    foreach (var scale in new[] { 1d, 1.25d, 1.5d })
                    {
                        var png = EncodePng(RenderWithShellCanvas(host.Render(size, 96 * scale), size, 96 * scale));
                        var frame = DecodePng(png);
                        var fileName = $"appearance-settings.{themeName}.{scale * 100:0}.png";
                        File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
                        entries.Add(new AppearanceVisualReviewEntry(
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

            var manifest = new AppearanceVisualReviewManifest(
                "appearance-settings",
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

    private static AppearanceVisualReviewManifest ReadManifest(string outputDirectory)
    {
        using var stream = File.OpenRead(Path.Combine(outputDirectory, "manifest.json"));
        return JsonSerializer.Deserialize<AppearanceVisualReviewManifest>(stream)
            ?? throw new InvalidDataException("Appearance settings visual review manifest was empty.");
    }

    private static void AssertManifestMatchesPngs(
        AppearanceVisualReviewManifest manifest,
        string outputDirectory,
        string expectedGitCommit)
    {
        Assert.Equal("appearance-settings", manifest.ArtifactId);
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

    private static string[] CreateSnapshot(AppearanceVisualReviewManifest manifest) =>
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

    private sealed record AppearanceVisualReviewManifest(
        string ArtifactId,
        string Tool,
        string GitCommit,
        int WindowWidth,
        int WindowHeight,
        IReadOnlyList<AppearanceVisualReviewEntry> Scenes);

    private sealed record AppearanceVisualReviewEntry(
        string Theme,
        double Scale,
        double Dpi,
        int Width,
        int Height,
        string Png,
        string Sha256);
}
