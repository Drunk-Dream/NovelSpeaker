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
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CacheAndDataPageTests
{
    [Fact]
    public void Cache_and_data_page_uses_formal_headerless_settings_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<CacheAndDataPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));

                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Same(page.FindResource(typeof(AppPageHeader)), header.Style);
                Assert.Equal("缓存与数据", header.Title);
                var backBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(header, AppPageHeader.BackCommandProperty));
                Assert.Equal(nameof(CacheAndDataViewModel.BackCommand), backBinding.Path.Path);

                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Same(page.FindResource(typeof(AppSettingsList)), settingsList.Style);
                Assert.Equal("缓存与数据设置", AutomationProperties.GetName(settingsList));
                Assert.Equal(5, settingsList.Items.Count);
                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(page));

                var overviewRow = AssertRow(page, "CacheOverviewRow", "缓存总览");
                var limitRow = AssertRow(page, "CacheLimitRow", "缓存上限");
                var directoryRow = AssertRow(page, "AppDataDirectoryRow", "应用数据目录");
                var clearRow = AssertRow(page, "ClearAllCacheRow", "清理全部缓存");
                var managementRow = Assert.IsType<AppSettingsNavigationRow>(page.FindName("OpenCacheManagementRow"));

                Assert.Same(overviewRow, settingsList.Items[0]);
                Assert.Same(limitRow, settingsList.Items[1]);
                Assert.Same(directoryRow, settingsList.Items[2]);
                Assert.Same(clearRow, settingsList.Items[3]);
                Assert.Same(managementRow, settingsList.Items[4]);
                Assert.Contains("最近最少使用", limitRow.Description, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(page),
                    textBlock => textBlock.Text == "缓存策略");

                var valueInput = Assert.IsType<TextBox>(page.FindName("CacheLimitValueTextBox"));
                Assert.Same(page.FindResource("App.Input.TextBox.Standard"), valueInput.Style);
                var unitInput = Assert.IsType<ComboBox>(page.FindName("CacheLimitUnitComboBox"));
                Assert.Same(page.FindResource("App.Input.ComboBox.Standard"), unitInput.Style);
                var progress = Assert.Single(VisualTreeTestHelper.FindDescendants<ProgressBar>(overviewRow));
                Assert.Same(page.FindResource("App.Progress.Compact"), progress.Style);

                var directoryButton = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("OpenAppDataDirectoryButton"));
                Assert.Same(page.FindResource("App.Button.Icon"), directoryButton.Style);
                Assert.Equal(SymbolRegular.FolderOpen24, Assert.IsType<SymbolIcon>(directoryButton.Icon).Symbol);
                Assert.Equal("打开应用数据目录", directoryButton.ToolTip);
                Assert.Equal("打开应用数据目录", AutomationProperties.GetName(directoryButton));

                var clearButton = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("ClearAllCacheButton"));
                Assert.Same(page.FindResource("App.Button.DangerIcon"), clearButton.Style);
                Assert.Equal(SymbolRegular.Delete24, Assert.IsType<SymbolIcon>(clearButton.Icon).Symbol);
                Assert.Equal("清理全部缓存", clearButton.ToolTip);
                Assert.Equal("清理全部缓存", AutomationProperties.GetName(clearButton));

                Assert.Same(page.FindResource(typeof(AppSettingsNavigationRow)), managementRow.Style);
                Assert.Equal(SettingsNavigationIcon.CacheManagement, managementRow.Icon);
                Assert.Equal("缓存管理", managementRow.Title);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Cache_and_data_page_is_transparent_and_has_no_legacy_or_group_resources()
    {
        var xamlPath = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "Cache",
            "CacheAndDataPage.xaml");
        var source = File.ReadAllText(xamlPath);
        var pageElement = XDocument.Load(xamlPath).Root!;

        Assert.Equal("Transparent", pageElement.Attribute("Background")?.Value);
        Assert.DoesNotContain("AppSettingsGroup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("缓存策略", source, StringComparison.Ordinal);
        Assert.Contains("AppSettingsList", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            pageElement.Descendants(),
            element => element.Attribute("Background") is not null);

    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    public void Cache_and_data_rows_errors_and_actions_remain_usable_at_supported_widths_and_dpi(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<CacheAndDataPage>();
                page.ViewModel.CacheLimitErrorText =
                    "缓存上限不能低于 256 MB；当前输入不会保存。";
                page.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = new WpfControlHost(page);

                var rows = new[]
                {
                    Assert.IsType<AppSettingsRow>(page.FindName("CacheOverviewRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("CacheLimitRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("AppDataDirectoryRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("ClearAllCacheRow"))
                };

                host.MeasureArrange(new Size(520, 980));
                Assert.All(rows, row =>
                {
                    Assert.True(row.IsNarrowLayout);
                    Assert.True(row.ActualHeight >= 60);
                    Assert.True(row.ActualWidth > 0);
                });
                var validation = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(page),
                    textBlock => BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path ==
                                 nameof(CacheAndDataViewModel.CacheLimitErrorText));
                Assert.Equal(Visibility.Visible, validation.Visibility);
                Assert.True(validation.ActualWidth > 0);
                Assert.True(validation.ActualHeight > 0);

                foreach (var buttonName in new[] { "OpenAppDataDirectoryButton", "ClearAllCacheButton" })
                {
                    var button = Assert.IsAssignableFrom<Button>(page.FindName(buttonName));
                    var expectedTouchSize = (double)page.FindResource("App.Size.Icon.Touch");
                    Assert.True(button.ActualWidth >= expectedTouchSize);
                    Assert.True(button.ActualHeight >= expectedTouchSize);
                }

                host.MeasureArrange(new Size(1200, 900));
                Assert.All(rows, row => Assert.False(row.IsNarrowLayout));

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

    [Fact]
    public void Cache_and_data_visual_review_generates_stable_page_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var scenarios = new[]
            {
                new PageVisualReviewScenario("default", 1d, PopulateOverview),
                new PageVisualReviewScenario("default", 1.5d, PopulateOverview),
                new PageVisualReviewScenario(
                    "long-description",
                    1.5d,
                    page =>
                    {
                        PopulateOverview(page);
                        ((AppSettingsRow)page.FindName("CacheLimitRow")!).Description =
                            "最低为 256 MB；超出上限时按最近最少使用顺序清理，正在播放、正在生成或被活动任务保护的音频会暂时保留。";
                    }),
                new PageVisualReviewScenario(
                    "validation-error",
                    1d,
                    page =>
                    {
                        PopulateOverview(page);
                        ((CacheAndDataPage)page).ViewModel.CacheLimitErrorText =
                            "缓存上限不能低于 256 MB；当前输入不会保存。";
                    }),
                new PageVisualReviewScenario(
                    "load-error",
                    1.5d,
                    page =>
                    {
                        var viewModel = ((CacheAndDataPage)page).ViewModel;
                        viewModel.HasLoadError = true;
                        viewModel.LoadErrorMessage = "加载缓存总览失败，请重试。";
                    })
            };

            PageVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "cache-data",
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

    private static void PopulateOverview(FrameworkElement page)
    {
        var viewModel = ((CacheAndDataPage)page).ViewModel;
        viewModel.TotalCacheSizeText = "0 B";
        viewModel.CacheEntryCountText = "0 项缓存";
        viewModel.UsageText = "0 B / 2 GB · 0%";
        viewModel.UsagePercentage = 0;
        viewModel.IsOverviewLoaded = true;
    }

    private static PageVisualReviewPage CreateVisualReviewPage()
    {
        var provider = WpfTestHost.BuildServiceProvider();
        return new PageVisualReviewPage(
            provider.GetRequiredService<CacheAndDataPage>(),
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
