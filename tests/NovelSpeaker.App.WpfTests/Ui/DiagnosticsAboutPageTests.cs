using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class DiagnosticsAboutPageTests
{
    [Fact]
    public void Diagnostics_page_uses_formal_headerless_settings_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<DiagnosticsAboutPage>();
                PopulateLongValues(page);
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));

                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Same(page.FindResource(typeof(AppPageHeader)), header.Style);
                Assert.Equal("诊断与关于", header.Title);
                var backBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(header, AppPageHeader.BackCommandProperty));
                Assert.Equal(nameof(DiagnosticsAboutViewModel.BackCommand), backBinding.Path.Path);

                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Same(page.FindResource(typeof(AppSettingsList)), settingsList.Style);
                Assert.Equal(9, settingsList.Items.Count);
                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsGroup>(page));

                foreach (var name in new[]
                         {
                             "AppNameRow",
                             "AppVersionRow",
                             "DescriptionRow",
                             "DatabaseSchemaVersionRow",
                             "AppDataDirectoryRow",
                             "LogsDirectoryRow",
                             "LogLevelRow",
                             "DiagnosticsSummaryRow",
                             "ThirdPartyNoticesRow"
                         })
                {
                    var row = Assert.IsType<AppSettingsRow>(page.FindName(name));
                    Assert.Same(page.FindResource(typeof(AppSettingsRow)), row.Style);
                }

                Assert.Same(
                    page.FindResource("App.Input.ComboBox.Standard"),
                    Assert.IsType<ComboBox>(page.FindName("LogLevelComboBox")).Style);

                AssertIconButton(page, "OpenAppDataDirectoryButton", SymbolRegular.FolderOpen24);
                AssertIconButton(page, "OpenLogsDirectoryButton", SymbolRegular.FolderOpen24);
                AssertIconButton(page, "CopyRedactedSummaryButton", SymbolRegular.DocumentCopy24);
                AssertIconButton(page, "OpenThirdPartyNoticesButton", SymbolRegular.DocumentText24);

                foreach (var valueName in new[]
                         {
                             "AppNameValue",
                             "AppVersionValue",
                             "DescriptionValue",
                             "DatabaseSchemaVersionValue",
                             "AppDataDirectoryValue",
                             "LogsDirectoryValue"
                         })
                {
                    var value = Assert.IsType<TextBlock>(page.FindName(valueName));
                    Assert.Contains(value.Text, AutomationProperties.GetName(value), StringComparison.Ordinal);
                }
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Diagnostics_page_is_transparent_and_has_no_legacy_or_group_resources()
    {
        var xamlPath = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Features",
            "Diagnostics",
            "DiagnosticsAboutPage.xaml");
        var source = File.ReadAllText(xamlPath);
        var pageElement = XDocument.Load(xamlPath).Root!;

        Assert.Equal("Transparent", pageElement.Attribute("Background")?.Value);
        Assert.Contains("AppSettingsList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppSettingsGroup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"", source, StringComparison.Ordinal);
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
                     "SettingsRowValueTextBlockStyle",
                     "SettingsRowControlMargin",
                     "SettingsRowControlWidth",
                     "SecondaryIconButtonStyle"
                 })
        {
            Assert.DoesNotContain(legacyKey, source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    public void Diagnostics_values_and_actions_remain_usable_at_supported_widths_and_dpi(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<DiagnosticsAboutPage>();
                PopulateLongValues(page);
                using var host = new WpfControlHost(page);

                host.MeasureArrange(new Size(520, 900));
                var rows = VisualTreeTestHelper.FindDescendants<AppSettingsRow>(page).ToArray();
                Assert.Equal(9, rows.Length);
                Assert.All(rows, row => Assert.True(row.IsNarrowLayout));

                foreach (var valueName in new[]
                         {
                             "AppVersionValue",
                             "AppDataDirectoryValue",
                             "LogsDirectoryValue"
                         })
                {
                    var value = Assert.IsType<TextBlock>(page.FindName(valueName));
                    Assert.True(value.ActualWidth > 0);
                    Assert.True(value.ActualHeight > 0);
                    Assert.Equal(TextWrapping.Wrap, value.TextWrapping);
                    Assert.Contains(value.Text, AutomationProperties.GetName(value), StringComparison.Ordinal);
                }

                var appDataPath = Assert.IsType<TextBlock>(page.FindName("AppDataDirectoryValue"));
                var logsPath = Assert.IsType<TextBlock>(page.FindName("LogsDirectoryValue"));
                Assert.True(appDataPath.ActualHeight > appDataPath.FontSize * 2);
                Assert.True(logsPath.ActualHeight > logsPath.FontSize * 2);

                foreach (var buttonName in new[]
                         {
                             "OpenAppDataDirectoryButton",
                             "OpenLogsDirectoryButton",
                             "CopyRedactedSummaryButton",
                             "OpenThirdPartyNoticesButton"
                         })
                {
                    var button = Assert.IsType<Button>(page.FindName(buttonName));
                    var expectedTouchSize = (double)page.FindResource("App.Size.Icon.Touch");
                    Assert.True(button.ActualWidth >= expectedTouchSize);
                    Assert.True(button.ActualHeight >= expectedTouchSize);
                }

                var bitmap = host.Render(new Size(520, 900), 96 * scale);
                Assert.Equal((int)Math.Round(520 * scale), bitmap.PixelWidth);
                Assert.Equal((int)Math.Round(900 * scale), bitmap.PixelHeight);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Diagnostics_visual_review_generates_stable_page_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var scenarios = new[]
            {
                new PageVisualReviewScenario("default", 1d, PopulateValues),
                new PageVisualReviewScenario("long-values", 1.5d, PopulateLongValues)
            };

            PageVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "diagnostics-about",
                scenarios,
                CreateVisualReviewPage);
        });
    }

    private static void AssertIconButton(FrameworkElement page, string name, SymbolRegular symbol)
    {
        var button = Assert.IsType<Button>(page.FindName(name));
        Assert.Same(page.FindResource("App.Button.Icon"), button.Style);
        Assert.Equal(symbol, Assert.IsType<SymbolIcon>(button.Content).Symbol);
        Assert.Equal(button.ToolTip, AutomationProperties.GetName(button));
    }

    private static void PopulateValues(FrameworkElement element)
    {
        var viewModel = ((DiagnosticsAboutPage)element).ViewModel;
        viewModel.AppName = "NovelSpeaker";
        viewModel.AppVersion = "1.0.0";
        viewModel.Description = "Windows 桌面小说听书应用。";
        viewModel.DatabaseSchemaVersionText = "7";
        viewModel.AppDataDirectoryPath = @"C:\Users\Sample\AppData\Local\NovelSpeaker";
        viewModel.LogsDirectoryPath = @"C:\Users\Sample\AppData\Local\NovelSpeaker\Logs";
    }

    private static void PopulateLongValues(FrameworkElement element)
    {
        PopulateValues(element);
        var viewModel = ((DiagnosticsAboutPage)element).ViewModel;
        viewModel.AppVersion = "10.20.300-preview.12345+0123456789abcdef0123456789abcdef";
        viewModel.AppDataDirectoryPath = @"C:\Users\Sample\AppData\Local\NovelSpeaker\A-Very-Long-Application-Data-Directory\Profiles\Default";
        viewModel.LogsDirectoryPath = @"C:\Users\Sample\AppData\Local\NovelSpeaker\A-Very-Long-Application-Data-Directory\Logs";
    }

    private static PageVisualReviewPage CreateVisualReviewPage()
    {
        var provider = WpfTestHost.BuildServiceProvider();
        return new PageVisualReviewPage(
            provider.GetRequiredService<DiagnosticsAboutPage>(),
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
