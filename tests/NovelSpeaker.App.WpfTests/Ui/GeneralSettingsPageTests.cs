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
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class GeneralSettingsPageTests
{
    [Fact]
    public void General_settings_page_uses_formal_header_and_flat_setting_rows_without_groups()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<GeneralSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));

                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Same(page.FindResource(typeof(AppPageHeader)), header.Style);
                Assert.Equal("常规", header.Title);
                var backCommandBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(header, AppPageHeader.BackCommandProperty));
                Assert.Equal(nameof(GeneralSettingsViewModel.BackCommand), backCommandBinding.Path.Path);

                var backButton = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Button>(header),
                    candidate => AutomationProperties.GetName(candidate) == "返回");
                Assert.Equal("返回", backButton.ToolTip);
                Assert.Same(page.FindResource("App.Button.Icon"), backButton.Style);

                var pageTitle = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(header),
                    textBlock => textBlock.Text == "常规");
                Assert.Same(page.FindResource("App.Typography.PageTitle"), pageTitle.Style);

                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(page));
                Assert.DoesNotContain(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(page),
                    textBlock => ReferenceEquals(textBlock.Style, page.FindResource("App.Typography.GroupTitle")));

                var closeBehaviorRow = Assert.IsType<AppSettingsRow>(page.FindName("CloseBehaviorRow"));
                Assert.Same(page.FindResource(typeof(AppSettingsRow)), closeBehaviorRow.Style);
                Assert.Equal("关闭行为", closeBehaviorRow.Title);
                Assert.Equal("选择隐藏到托盘、退出应用或每次询问。", closeBehaviorRow.Description);
                Assert.Equal("关闭主窗口时", AutomationProperties.GetName(closeBehaviorRow));
                Assert.False(closeBehaviorRow.Focusable);
                Assert.False(closeBehaviorRow.IsTabStop);

                var comboBox = Assert.IsType<ComboBox>(page.FindName("CloseBehaviorComboBox"));
                Assert.Same(page.FindResource("App.Input.ComboBox.Standard"), comboBox.Style);
                Assert.Equal("关闭主窗口时", AutomationProperties.GetName(comboBox));
                Assert.Equal(3, comboBox.Items.Count);
                Assert.Same(comboBox, closeBehaviorRow.Value);

                var itemsSourceBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(comboBox, ItemsControl.ItemsSourceProperty));
                Assert.Equal(nameof(GeneralSettingsViewModel.CloseBehaviorOptions), itemsSourceBinding.Path.Path);
                Assert.Equal(BindingMode.OneWay, itemsSourceBinding.Mode);

                var selectedItemBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(comboBox, Selector.SelectedItemProperty));
                Assert.Equal(nameof(GeneralSettingsViewModel.SelectedCloseBehavior), selectedItemBinding.Path.Path);
                Assert.Equal(BindingMode.TwoWay, selectedItemBinding.Mode);
                Assert.Equal("DisplayName", comboBox.DisplayMemberPath);

                var startMinimizedRow = Assert.IsType<AppSettingsRow>(page.FindName("StartMinimizedRow"));
                Assert.Same(page.FindResource(typeof(AppSettingsRow)), startMinimizedRow.Style);
                Assert.Equal("启动后最小化到托盘", startMinimizedRow.Title);
                Assert.Equal("应用仍会完成初始化和后台任务启动。", startMinimizedRow.Description);
                Assert.Equal("启动后最小化到托盘", AutomationProperties.GetName(startMinimizedRow));
                Assert.False(startMinimizedRow.Focusable);
                Assert.False(startMinimizedRow.IsTabStop);

                var toggleSwitch = Assert.IsType<ToggleSwitch>(page.FindName("StartMinimizedToggleSwitch"));
                Assert.Same(page.FindResource("App.Input.ToggleSwitch.Standard"), toggleSwitch.Style);
                Assert.Equal("启动后最小化到托盘", AutomationProperties.GetName(toggleSwitch));
                Assert.Same(toggleSwitch, startMinimizedRow.Value);

                var isCheckedBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(toggleSwitch, ToggleSwitch.IsCheckedProperty));
                Assert.Equal(nameof(GeneralSettingsViewModel.StartMinimizedToTray), isCheckedBinding.Path.Path);
                Assert.Equal(BindingMode.TwoWay, isCheckedBinding.Mode);

                var flatList = Assert.IsType<StackPanel>(closeBehaviorRow.Parent);
                Assert.Equal(2, flatList.Children.Count);
                Assert.Same(closeBehaviorRow, flatList.Children[0]);
                Assert.Same(startMinimizedRow, flatList.Children[1]);
                Assert.Equal(new Thickness(0, 16, 0, 0), startMinimizedRow.Margin);

                var closeRowText = VisualTreeTestHelper.FindDescendants<TextBlock>(closeBehaviorRow).ToArray();
                Assert.Contains(closeRowText, textBlock =>
                    textBlock.Text == "关闭行为" &&
                    ReferenceEquals(textBlock.Style, page.FindResource("App.Typography.ItemTitle")));
                Assert.Contains(closeRowText, textBlock =>
                    textBlock.Text == "选择隐藏到托盘、退出应用或每次询问。" &&
                    ReferenceEquals(textBlock.Style, page.FindResource("App.Typography.Secondary")));

                var startupRowText = VisualTreeTestHelper.FindDescendants<TextBlock>(startMinimizedRow).ToArray();
                Assert.Contains(startupRowText, textBlock =>
                    textBlock.Text == "启动后最小化到托盘" &&
                    ReferenceEquals(textBlock.Style, page.FindResource("App.Typography.ItemTitle")));
                Assert.Contains(startupRowText, textBlock =>
                    textBlock.Text == "应用仍会完成初始化和后台任务启动。" &&
                    ReferenceEquals(textBlock.Style, page.FindResource("App.Typography.Secondary")));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void General_settings_page_owns_canvas_background_without_window_shell_ring_and_legacy_references()
    {
        var xamlPath = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "GeneralSettings",
            "GeneralSettingsPage.xaml");
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
        Assert.DoesNotContain(
            source,
            "Header=\"",
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            pageElement.Descendants(),
            element => element.Attribute("Background") is not null);

        foreach (var legacyKey in new[]
                 {
                     "PagePadding",
                     "SectionSpacing",
                     "BackIconButtonStyle",
                     "PageTitleTextBlockStyle",
                     "SettingsRowsGroupBorderStyle",
                     "SettingsRowBorderStyle",
                     "SettingsLastRowBorderStyle",
                     "SettingsRowTitleTextBlockStyle",
                     "SettingsRowDescriptionTextBlockStyle",
                     "SettingsRowControlMargin",
                     "SettingsRowControlWidth"
                 })
        {
            Assert.DoesNotContain(legacyKey, source, StringComparison.Ordinal);
        }

        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<GeneralSettingsPage>();
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
    public void General_settings_rows_do_not_overlap_at_narrow_and_wide_widths()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<GeneralSettingsPage>();
                using var host = new WpfControlHost(page);

                var closeBehaviorRow = Assert.IsType<AppSettingsRow>(page.FindName("CloseBehaviorRow"));
                var startMinimizedRow = Assert.IsType<AppSettingsRow>(page.FindName("StartMinimizedRow"));
                var comboBox = Assert.IsType<ComboBox>(page.FindName("CloseBehaviorComboBox"));
                var toggleSwitch = Assert.IsType<ToggleSwitch>(page.FindName("StartMinimizedToggleSwitch"));

                host.MeasureArrange(new Size(520, 900));
                Assert.True(closeBehaviorRow.IsNarrowLayout);
                Assert.True(startMinimizedRow.IsNarrowLayout);
                Assert.True(closeBehaviorRow.ActualWidth > 0);
                Assert.True(closeBehaviorRow.ActualHeight >= 60);
                Assert.True(comboBox.ActualWidth >= 180);
                Assert.True(toggleSwitch.ActualWidth > 0);

                AssertControlBelowTitle(closeBehaviorRow, "关闭行为", comboBox);
                AssertControlBelowTitle(startMinimizedRow, "启动后最小化到托盘", toggleSwitch);

                host.MeasureArrange(new Size(1200, 900));
                Assert.False(closeBehaviorRow.IsNarrowLayout);
                Assert.False(startMinimizedRow.IsNarrowLayout);
                Assert.True(closeBehaviorRow.ActualWidth > 0);
                Assert.True(comboBox.ActualWidth >= 180);

                AssertControlRightOfTitle(closeBehaviorRow, "关闭行为", comboBox);
                AssertControlRightOfTitle(startMinimizedRow, "启动后最小化到托盘", toggleSwitch);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    public void General_settings_rows_do_not_overlap_at_supported_dpi(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<GeneralSettingsPage>();
                page.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(520, 900));

                var closeBehaviorRow = Assert.IsType<AppSettingsRow>(page.FindName("CloseBehaviorRow"));
                var startMinimizedRow = Assert.IsType<AppSettingsRow>(page.FindName("StartMinimizedRow"));
                var comboBox = Assert.IsType<ComboBox>(page.FindName("CloseBehaviorComboBox"));
                var toggleSwitch = Assert.IsType<ToggleSwitch>(page.FindName("StartMinimizedToggleSwitch"));

                Assert.True(closeBehaviorRow.IsNarrowLayout);
                Assert.True(startMinimizedRow.IsNarrowLayout);
                Assert.True(closeBehaviorRow.ActualWidth > 0);
                Assert.True(closeBehaviorRow.ActualHeight >= 60);
                Assert.True(comboBox.ActualWidth >= 180);
                Assert.True(toggleSwitch.ActualWidth > 0);
                AssertControlBelowTitle(closeBehaviorRow, "关闭行为", comboBox);
                AssertControlBelowTitle(startMinimizedRow, "启动后最小化到托盘", toggleSwitch);

                host.MeasureArrange(new Size(1200, 900));
                Assert.False(closeBehaviorRow.IsNarrowLayout);
                Assert.False(startMinimizedRow.IsNarrowLayout);
                AssertControlRightOfTitle(closeBehaviorRow, "关闭行为", comboBox);
                AssertControlRightOfTitle(startMinimizedRow, "启动后最小化到托盘", toggleSwitch);

                var bitmap = new RenderTargetBitmap(
                    (int)Math.Round(1200 * 96 * scale / 96d),
                    (int)Math.Round(900 * 96 * scale / 96d),
                    96 * scale,
                    96 * scale,
                    PixelFormats.Pbgra32);
                bitmap.Render(page);
                Assert.True(bitmap.PixelWidth > 0);
                Assert.True(bitmap.PixelHeight > 0);
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
    public void General_settings_page_constructs_after_runtime_theme_switch(ApplicationTheme theme)
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
                var page = provider.GetRequiredService<GeneralSettingsPage>();
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
    public void General_settings_visual_review_generates_stable_page_screenshots()
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
                "general-settings");
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

    private static void AssertControlBelowTitle(
        AppSettingsRow row,
        string title,
        FrameworkElement control)
    {
        var titleBlock = Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(row),
            textBlock => textBlock.Text == title);
        var titleBounds = titleBlock.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), titleBlock.RenderSize));
        var valueBounds = control.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), control.RenderSize));
        Assert.True(titleBounds.Bottom <= valueBounds.Top);
        Assert.True(titleBounds.Left >= 0);
        Assert.True(valueBounds.Right <= row.ActualWidth);
    }

    private static void AssertControlRightOfTitle(
        AppSettingsRow row,
        string title,
        FrameworkElement control)
    {
        var titleBlock = Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(row),
            textBlock => textBlock.Text == title);
        var titleBounds = titleBlock.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), titleBlock.RenderSize));
        var valueBounds = control.TransformToAncestor(row)
            .TransformBounds(new Rect(new Point(), control.RenderSize));
        Assert.True(titleBounds.Right <= valueBounds.Left);
        Assert.True(valueBounds.Right <= row.ActualWidth);
    }

    private static void GenerateVisualReview(string outputDirectory, string gitCommit)
    {
        var entries = new List<GeneralSettingsVisualReviewEntry>();
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
                    var page = provider.GetRequiredService<GeneralSettingsPage>();
                    var size = new Size(960, 640);
                    using var host = new WpfControlHost(page);
                    host.MeasureArrange(size);
                    Assert.True(page.ActualWidth > 0);
                    Assert.True(page.ActualHeight > 0);

                    foreach (var scale in new[] { 1d, 1.25d, 1.5d })
                    {
                        var png = EncodePng(RenderWithShellCanvas(host.Render(size, 96 * scale), size, 96 * scale));
                        var frame = DecodePng(png);
                        var fileName = $"general-settings.{themeName}.{scale * 100:0}.png";
                        File.WriteAllBytes(Path.Combine(outputDirectory, fileName), png);
                        entries.Add(new GeneralSettingsVisualReviewEntry(
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

            var manifest = new GeneralSettingsVisualReviewManifest(
                "general-settings",
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

    private static GeneralSettingsVisualReviewManifest ReadManifest(string outputDirectory)
    {
        using var stream = File.OpenRead(Path.Combine(outputDirectory, "manifest.json"));
        return JsonSerializer.Deserialize<GeneralSettingsVisualReviewManifest>(stream)
            ?? throw new InvalidDataException("General settings visual review manifest was empty.");
    }

    private static void AssertManifestMatchesPngs(
        GeneralSettingsVisualReviewManifest manifest,
        string outputDirectory,
        string expectedGitCommit)
    {
        Assert.Equal("general-settings", manifest.ArtifactId);
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

    private static string[] CreateSnapshot(GeneralSettingsVisualReviewManifest manifest) =>
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

    private sealed record GeneralSettingsVisualReviewManifest(
        string ArtifactId,
        string Tool,
        string GitCommit,
        int WindowWidth,
        int WindowHeight,
        IReadOnlyList<GeneralSettingsVisualReviewEntry> Scenes);

    private sealed record GeneralSettingsVisualReviewEntry(
        string Theme,
        double Scale,
        double Dpi,
        int Width,
        int Height,
        string Png,
        string Sha256);
}
