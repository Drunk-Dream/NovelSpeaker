using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
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
    public void Diagnostics_values_and_actions_remain_usable_at_supported_widths_and_dpi()
    {
        foreach (var scale in new[] { 1d, 1.25d, 1.5d })
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
                    var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                    Assert.Equal("诊断与关于", AutomationProperties.GetName(settingsList));
                    Assert.Equal(9, settingsList.Items.Count);
                    foreach (var (index, name, title) in new[]
                             {
                             (0, "AppNameRow", "应用名称"),
                             (1, "AppVersionRow", "应用版本"),
                             (2, "DescriptionRow", "项目说明"),
                             (3, "DatabaseSchemaVersionRow", "数据库版本"),
                             (4, "AppDataDirectoryRow", "应用数据目录"),
                             (5, "LogsDirectoryRow", "日志目录"),
                             (6, "LogLevelRow", "日志级别设置"),
                             (7, "DiagnosticsSummaryRow", "脱敏诊断摘要"),
                             (8, "ThirdPartyNoticesRow", "第三方许可证")
                         })
                    {
                        var row = Assert.IsType<AppSettingsRow>(page.FindName(name));
                        Assert.Same(row, settingsList.Items[index]);
                        Assert.Equal(title, AutomationProperties.GetName(row));
                    }

                    var logLevel = Assert.IsType<ComboBox>(page.FindName("LogLevelComboBox"));
                    Assert.Equal("日志级别", AutomationProperties.GetName(logLevel));
                    Assert.Equal(
                        nameof(DiagnosticsAboutViewModel.AvailableLogLevels),
                        Assert.IsType<Binding>(BindingOperations.GetBinding(
                            logLevel,
                            ItemsControl.ItemsSourceProperty)).Path.Path);
                    Assert.Equal(
                        nameof(DiagnosticsAboutViewModel.SelectedLogLevel),
                        Assert.IsType<Binding>(BindingOperations.GetBinding(
                            logLevel,
                            Selector.SelectedItemProperty)).Path.Path);

                    foreach (var (valueName, propertyName, expectedText, automationPrefix) in new[]
                             {
                             ("AppNameValue", nameof(DiagnosticsAboutViewModel.AppName), "NovelSpeaker", "应用名称："),
                             ("AppVersionValue", nameof(DiagnosticsAboutViewModel.AppVersion), "10.20.300-preview.12345+0123456789abcdef0123456789abcdef", "应用版本："),
                             ("DescriptionValue", nameof(DiagnosticsAboutViewModel.Description), "Windows 桌面小说听书应用。", "项目说明："),
                             ("DatabaseSchemaVersionValue", nameof(DiagnosticsAboutViewModel.DatabaseSchemaVersionText), "7", "数据库版本："),
                             ("AppDataDirectoryValue", nameof(DiagnosticsAboutViewModel.AppDataDirectoryPath), @"C:\Users\Sample\AppData\Local\NovelSpeaker\A-Very-Long-Application-Data-Directory\Profiles\Default", "应用数据目录："),
                             ("LogsDirectoryValue", nameof(DiagnosticsAboutViewModel.LogsDirectoryPath), @"C:\Users\Sample\AppData\Local\NovelSpeaker\A-Very-Long-Application-Data-Directory\Logs", "日志目录：")
                         })
                    {
                        var value = Assert.IsType<TextBlock>(page.FindName(valueName));
                        var binding = Assert.IsType<Binding>(
                            BindingOperations.GetBinding(value, TextBlock.TextProperty));
                        Assert.Equal(propertyName, binding.Path.Path);
                        Assert.Equal(expectedText, value.Text);
                        Assert.False(string.IsNullOrWhiteSpace(value.Text));
                        Assert.True(value.ActualWidth > 0);
                        Assert.True(value.ActualHeight > 0);
                        Assert.Equal(TextWrapping.Wrap, value.TextWrapping);
                        Assert.Contains(automationPrefix + expectedText, AutomationProperties.GetName(value), StringComparison.Ordinal);
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
                        var button = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName(buttonName));
                        var expectedTouchSize = (double)page.FindResource("App.Size.Icon.Touch");
                        Assert.True(button.ActualWidth >= expectedTouchSize);
                        Assert.True(button.ActualHeight >= expectedTouchSize);
                    }

                    foreach (var (name, automationName, commandPath, symbol) in new[]
                             {
                             ("OpenAppDataDirectoryButton", "打开应用数据目录", "OpenAppDataDirectoryCommand", SymbolRegular.FolderOpen24),
                             ("OpenLogsDirectoryButton", "打开日志目录", "OpenLogsDirectoryCommand", SymbolRegular.FolderOpen24),
                             ("CopyRedactedSummaryButton", "复制脱敏诊断摘要", "CopyRedactedSummaryCommand", SymbolRegular.DocumentCopy24),
                             ("OpenThirdPartyNoticesButton", "打开第三方许可证", "OpenThirdPartyNoticesCommand", SymbolRegular.DocumentText24)
                         })
                    {
                        var button = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName(name));
                        Assert.Equal(automationName, AutomationProperties.GetName(button));
                        Assert.Equal(automationName, button.ToolTip);
                        Assert.Equal(symbol, Assert.IsType<SymbolIcon>(button.Icon).Symbol);
                        Assert.Equal(
                            commandPath,
                            Assert.IsType<Binding>(BindingOperations.GetBinding(button, Button.CommandProperty))
                                .Path.Path);
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
}
