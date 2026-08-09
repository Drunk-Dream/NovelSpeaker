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
using NovelSpeaker.App.Shared.Theming;
using Wpf.Ui.Appearance;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ImportTextSettingsPageTests
{
    [Fact]
    public void Import_text_page_uses_one_formal_settings_list_in_business_order()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<ImportTextSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));

                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Same(page.FindResource(typeof(AppPageHeader)), header.Style);
                Assert.Equal("导入与文本", header.Title);
                var backBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(header, AppPageHeader.BackCommandProperty));
                Assert.Equal(nameof(ImportTextSettingsViewModel.BackCommand), backBinding.Path.Path);

                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Same(page.FindResource(typeof(AppSettingsList)), settingsList.Style);
                Assert.Equal("导入与文本设置", AutomationProperties.GetName(settingsList));
                Assert.Equal(4, settingsList.Items.Count);
                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(page));

                var splitRow = AssertRow(page, "EnableLongParagraphSplittingRow", "拆分长段落");
                var thresholdRow = AssertRow(page, "LongParagraphThresholdRow", "长段落阈值");
                var templateRow = AssertRow(page, "BookFileNameTemplateRow", "文件名提取模板");
                var navigationRow = Assert.IsType<AppSettingsNavigationRow>(
                    page.FindName("OpenRegexReplacementRulesRow"));
                Assert.Same(page.FindResource(typeof(AppSettingsNavigationRow)), navigationRow.Style);
                Assert.Equal(SettingsNavigationIcon.RegexReplacement, navigationRow.Icon);
                Assert.Equal("正则替换", navigationRow.Title);
                Assert.Equal("正则替换", navigationRow.ToolTip);
                Assert.Equal("正则替换", AutomationProperties.GetName(navigationRow));
                Assert.True(navigationRow.Focusable);
                Assert.True(navigationRow.IsTabStop);
                var navigationBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(navigationRow, Button.CommandProperty));
                Assert.Equal(
                    nameof(ImportTextSettingsViewModel.OpenRegexReplacementRulesCommand),
                    navigationBinding.Path.Path);

                Assert.Same(splitRow, settingsList.Items[0]);
                Assert.Same(thresholdRow, settingsList.Items[1]);
                Assert.Same(templateRow, settingsList.Items[2]);
                Assert.Same(navigationRow, settingsList.Items[3]);

                var splitToggle = Assert.IsType<ToggleSwitch>(
                    page.FindName("EnableLongParagraphSplittingToggleSwitch"));
                Assert.Same(page.FindResource("App.Input.ToggleSwitch.Standard"), splitToggle.Style);
                AssertCheckedBinding(
                    splitToggle,
                    nameof(ImportTextSettingsViewModel.EnableLongParagraphSplitting));

                var thresholdInput = Assert.IsType<TextBox>(page.FindName("LongParagraphThresholdTextBox"));
                Assert.Same(page.FindResource("App.Input.TextBox.Standard"), thresholdInput.Style);
                AssertTextBinding(thresholdInput, nameof(ImportTextSettingsViewModel.LongParagraphThresholdText));

                var templateInput = Assert.IsType<TextBox>(page.FindName("BookFileNameTemplateTextBox"));
                Assert.Same(page.FindResource("App.Input.TextBox.Standard"), templateInput.Style);
                AssertTextBinding(templateInput, nameof(ImportTextSettingsViewModel.BookFileNameTemplateText));

                var validation = FindValidationText(page);
                Assert.Same(page.FindResource("App.Feedback.ValidationText"), validation.Style.BasedOn);
                Assert.Equal(Visibility.Collapsed, validation.Visibility);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Import_text_page_is_transparent_and_has_no_legacy_or_group_resources()
    {
        var xamlPath = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "ImportTextSettings",
            "ImportTextSettingsPage.xaml");
        var source = File.ReadAllText(xamlPath);
        var pageElement = XDocument.Load(xamlPath).Root!;

        Assert.Equal("Transparent", pageElement.Attribute("Background")?.Value);
        Assert.DoesNotContain("AppSettingsGroup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"", source, StringComparison.Ordinal);
        Assert.Contains("AppSettingsList", source, StringComparison.Ordinal);
        Assert.Contains("AppSettingsNavigationRow", source, StringComparison.Ordinal);
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
                var page = provider.GetRequiredService<ImportTextSettingsPage>();
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
    public void Import_text_fields_and_navigation_remain_usable_at_narrow_width_and_supported_dpi(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<ImportTextSettingsPage>();
                page.ViewModel.LongParagraphThresholdErrorText =
                    "请输入整数；当前输入不会保存，也不会影响后续导入。";
                page.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = new WpfControlHost(page);

                var rows = new[]
                {
                    Assert.IsType<AppSettingsRow>(page.FindName("EnableLongParagraphSplittingRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("LongParagraphThresholdRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("BookFileNameTemplateRow"))
                };
                var values = new FrameworkElement[]
                {
                    Assert.IsType<ToggleSwitch>(page.FindName("EnableLongParagraphSplittingToggleSwitch")),
                    Assert.IsType<TextBox>(page.FindName("LongParagraphThresholdTextBox")).Parent as FrameworkElement
                        ?? throw new InvalidOperationException("Threshold input has no value container."),
                    Assert.IsType<TextBox>(page.FindName("BookFileNameTemplateTextBox"))
                };
                var navigation = Assert.IsType<AppSettingsNavigationRow>(
                    page.FindName("OpenRegexReplacementRulesRow"));

                host.MeasureArrange(new Size(520, 900));
                for (var index = 0; index < rows.Length; index++)
                {
                    Assert.True(rows[index].IsNarrowLayout);
                    Assert.True(rows[index].ActualHeight >= 60);
                    AssertValueBelowTitle(rows[index], values[index]);
                }

                Assert.True(navigation.ActualWidth > 0);
                Assert.True(navigation.ActualHeight >= 60);
                Assert.True(navigation.IsHitTestVisible);
                Assert.True(FindValidationText(page).ActualHeight > 0);

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
    public void Import_text_page_constructs_after_runtime_theme_switch(ApplicationTheme theme)
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
                var page = provider.GetRequiredService<ImportTextSettingsPage>();
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
    public void Import_text_visual_review_generates_stable_page_screenshots()
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
                    page => ((AppSettingsRow)page.FindName("BookFileNameTemplateRow")!).Description =
                        "从文件名提取书名和作者；模板可以同时包含名称、作者和固定分隔文本，留空时完全保留导入内容中的元数据。"),
                new PageVisualReviewScenario(
                    "validation-error",
                    1d,
                    page => ((ImportTextSettingsPage)page).ViewModel.LongParagraphThresholdErrorText =
                        "请输入整数；当前输入不会保存。"),
                new PageVisualReviewScenario(
                    "validation-error",
                    1.5d,
                    page => ((ImportTextSettingsPage)page).ViewModel.LongParagraphThresholdErrorText =
                        "请输入整数；当前输入不会保存。")
            };

            PageVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "import-text-settings",
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

    private static void AssertCheckedBinding(ToggleSwitch toggle, string propertyName)
    {
        var binding = Assert.IsType<Binding>(
            BindingOperations.GetBinding(toggle, ToggleSwitch.IsCheckedProperty));
        Assert.Equal(propertyName, binding.Path.Path);
        Assert.Equal(BindingMode.TwoWay, binding.Mode);
    }

    private static TextBlock FindValidationText(FrameworkElement page) =>
        Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(page),
            textBlock => BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path ==
                         nameof(ImportTextSettingsViewModel.LongParagraphThresholdErrorText));

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
            provider.GetRequiredService<ImportTextSettingsPage>(),
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
