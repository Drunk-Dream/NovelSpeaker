using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using Wpf.Ui.Appearance;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class PlaybackSettingsPageTests
{
    [Fact]
    public void Playback_settings_page_uses_formal_headerless_settings_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<PlaybackSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));

                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Same(page.FindResource(typeof(AppPageHeader)), header.Style);
                Assert.Equal("播放设置", header.Title);
                var backBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(header, AppPageHeader.BackCommandProperty));
                Assert.Equal(nameof(PlaybackSettingsViewModel.BackCommand), backBinding.Path.Path);

                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Same(page.FindResource(typeof(AppSettingsList)), settingsList.Style);
                Assert.Equal("播放设置", AutomationProperties.GetName(settingsList));
                Assert.Equal(3, settingsList.Items.Count);
                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(page));
                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(page));

                var speedRow = AssertRow(page, "DefaultSpeakSpeedRow", "默认语速");
                var prefetchRow = AssertRow(page, "PrefetchCountRow", "播放预取数量");
                var titleRow = AssertRow(page, "ReadChapterTitleRow", "朗读章节标题");
                Assert.Same(speedRow, settingsList.Items[0]);
                Assert.Same(prefetchRow, settingsList.Items[1]);
                Assert.Same(titleRow, settingsList.Items[2]);

                var speedInput = Assert.IsType<TextBox>(page.FindName("DefaultSpeakSpeedTextBox"));
                Assert.Same(page.FindResource("App.Input.TextBox.Standard"), speedInput.Style);
                Assert.Equal("默认语速", AutomationProperties.GetName(speedInput));
                AssertTextBinding(speedInput, nameof(PlaybackSettingsViewModel.DefaultSpeakSpeedText));

                var prefetchInput = Assert.IsType<TextBox>(page.FindName("PrefetchCountTextBox"));
                Assert.Same(page.FindResource("App.Input.TextBox.Standard"), prefetchInput.Style);
                Assert.Equal("播放预取数量", AutomationProperties.GetName(prefetchInput));
                AssertTextBinding(prefetchInput, nameof(PlaybackSettingsViewModel.PrefetchCountText));

                var toggle = Assert.IsType<ToggleSwitch>(page.FindName("ReadChapterTitleToggleSwitch"));
                Assert.Same(page.FindResource("App.Input.ToggleSwitch.Standard"), toggle.Style);
                Assert.Equal("朗读标题", AutomationProperties.GetName(toggle));
                var checkedBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(toggle, ToggleSwitch.IsCheckedProperty));
                Assert.Equal(nameof(PlaybackSettingsViewModel.ReadChapterTitle), checkedBinding.Path.Path);
                Assert.Equal(BindingMode.TwoWay, checkedBinding.Mode);

                AssertValidationTextUsesFeedbackStyle(page, nameof(PlaybackSettingsViewModel.DefaultSpeakSpeedErrorText));
                AssertValidationTextUsesFeedbackStyle(page, nameof(PlaybackSettingsViewModel.PrefetchCountErrorText));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Playback_settings_page_is_transparent_and_has_no_legacy_or_group_resources()
    {
        var xamlPath = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "PlaybackSettings",
            "PlaybackSettingsPage.xaml");
        var source = File.ReadAllText(xamlPath);
        var pageElement = XDocument.Load(xamlPath).Root!;

        Assert.Equal("Transparent", pageElement.Attribute("Background")?.Value);
        Assert.DoesNotContain("AppSettingsGroup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsNavigationRow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"", source, StringComparison.Ordinal);
        Assert.Contains("AppSettingsList", source, StringComparison.Ordinal);
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
                     "SettingsRowControlWidth",
                     "ErrorTextBlockStyle",
                     "SettingsNavigationRowButtonStyle"
                 })
        {
            Assert.DoesNotContain(legacyKey, source, StringComparison.Ordinal);
        }

        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<PlaybackSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));
                Assert.Equal(Brushes.Transparent, page.Background);
                var root = Assert.IsType<Grid>(page.Content);
                Assert.Equal(new Thickness(24), root.Margin);
                Assert.Null(root.Background);
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
    public void Playback_settings_rows_and_errors_remain_usable_at_supported_widths_and_dpi(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<PlaybackSettingsPage>();
                page.ViewModel.DefaultSpeakSpeedErrorText =
                    "请输入允许范围内的整数；当前输入不会保存，也不会改变正在播放的内容。";
                page.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = new WpfControlHost(page);

                var rows = new[]
                {
                    Assert.IsType<AppSettingsRow>(page.FindName("DefaultSpeakSpeedRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("PrefetchCountRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("ReadChapterTitleRow"))
                };
                var values = new FrameworkElement[]
                {
                    Assert.IsType<TextBox>(page.FindName("DefaultSpeakSpeedTextBox")).Parent as FrameworkElement
                        ?? throw new InvalidOperationException("Speed input has no value container."),
                    Assert.IsType<TextBox>(page.FindName("PrefetchCountTextBox")).Parent as FrameworkElement
                        ?? throw new InvalidOperationException("Prefetch input has no value container."),
                    Assert.IsType<ToggleSwitch>(page.FindName("ReadChapterTitleToggleSwitch"))
                };

                host.MeasureArrange(new Size(520, 900));
                for (var index = 0; index < rows.Length; index++)
                {
                    Assert.True(rows[index].IsNarrowLayout);
                    Assert.True(rows[index].ActualHeight >= 60);
                    AssertValueBelowTitle(rows[index], values[index]);
                }

                var validation = FindValidationText(page, nameof(PlaybackSettingsViewModel.DefaultSpeakSpeedErrorText));
                Assert.Equal(Visibility.Visible, validation.Visibility);
                Assert.True(validation.ActualWidth > 0);
                Assert.True(validation.ActualHeight > 0);

                host.MeasureArrange(new Size(1200, 900));
                for (var index = 0; index < rows.Length; index++)
                {
                    Assert.False(rows[index].IsNarrowLayout);
                    AssertValueRightOfTitle(rows[index], values[index]);
                }

                var bitmap = host.Render(new Size(1200, 900), 96 * scale);
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
    public void Playback_settings_page_constructs_after_runtime_theme_switch(ApplicationTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new WpfUiThemeRuntime();
            if (theme == ApplicationTheme.Dark)
            {
                runtime.ApplyDarkTheme();
            }
            else
            {
                runtime.ApplyLightTheme();
            }

            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<PlaybackSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));
                Assert.True(page.ActualWidth > 0);
                Assert.True(page.ActualHeight > 0);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                runtime.ApplyLightTheme();
            }
        });
    }

    [Fact]
    public void Playback_settings_visual_review_generates_stable_page_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var scenarios = new[]
            {
                new PageVisualReviewScenario("default", 1d),
                new PageVisualReviewScenario("default", 1.5d),
                new PageVisualReviewScenario(
                    "long-description",
                    1.5d,
                    page => ((AppSettingsRow)page.FindName("DefaultSpeakSpeedRow")!).Description =
                        "用于后续朗读，并在存在当前播放会话时按既有刷新规则安全应用；已经开始的缓存任务继续使用创建时快照。"),
                new PageVisualReviewScenario(
                    "validation-error",
                    1d,
                    page => ((PlaybackSettingsPage)page).ViewModel.DefaultSpeakSpeedErrorText =
                        "请输入允许范围内的整数；当前输入不会保存。"),
                new PageVisualReviewScenario(
                    "validation-error",
                    1.5d,
                    page => ((PlaybackSettingsPage)page).ViewModel.DefaultSpeakSpeedErrorText =
                        "请输入允许范围内的整数；当前输入不会保存。")
            };

            PageVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "playback-settings",
                scenarios,
                CreateVisualReviewPage);
        });
    }

    private static AppSettingsRow AssertRow(FrameworkElement page, string name, string title)
    {
        var row = Assert.IsType<AppSettingsRow>(page.FindName(name));
        Assert.Same(page.FindResource(typeof(AppSettingsRow)), row.Style);
        Assert.Equal(title, row.Title);
        Assert.False(row.Focusable);
        Assert.False(row.IsTabStop);
        return row;
    }

    private static void AssertTextBinding(TextBox textBox, string propertyName)
    {
        var binding = Assert.IsType<Binding>(BindingOperations.GetBinding(textBox, TextBox.TextProperty));
        Assert.Equal(propertyName, binding.Path.Path);
        Assert.Equal(UpdateSourceTrigger.PropertyChanged, binding.UpdateSourceTrigger);
    }

    private static void AssertValidationTextUsesFeedbackStyle(FrameworkElement page, string propertyName)
    {
        var validation = FindValidationText(page, propertyName);
        Assert.Same(page.FindResource("App.Feedback.ValidationText"), validation.Style.BasedOn);
        Assert.Equal(Visibility.Collapsed, validation.Visibility);
    }

    private static TextBlock FindValidationText(FrameworkElement page, string propertyName) =>
        Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(page),
            textBlock => BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path == propertyName);

    private static void AssertValueBelowTitle(AppSettingsRow row, FrameworkElement value)
    {
        var title = Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(row),
            textBlock => textBlock.Text == row.Title);
        Assert.True(GetBounds(title, row).Bottom <= GetBounds(value, row).Top);
    }

    private static void AssertValueRightOfTitle(AppSettingsRow row, FrameworkElement value)
    {
        var title = Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(row),
            textBlock => textBlock.Text == row.Title);
        Assert.True(GetBounds(title, row).Right <= GetBounds(value, row).Left);
    }

    private static Rect GetBounds(FrameworkElement element, Visual ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(), element.RenderSize));

    private static PageVisualReviewPage CreateVisualReviewPage()
    {
        var provider = WpfTestHost.BuildServiceProvider();
        return new PageVisualReviewPage(
            provider.GetRequiredService<PlaybackSettingsPage>(),
            () => provider.DisposeAsync().AsTask().GetAwaiter().GetResult());
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
}
