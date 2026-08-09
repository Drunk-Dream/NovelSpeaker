using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SettingsSubpageViewTests
{
    [Fact]
    public void Ordinary_settings_subpages_use_shared_left_label_right_control_rows()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                AssertRows(
                    provider.GetRequiredService<DiagnosticsAboutPage>(),
                    "AppNameItemBorder",
                    "AppVersionItemBorder",
                    "DescriptionItemBorder",
                    "DatabaseSchemaVersionItemBorder",
                    "AppDataDirectoryItemBorder",
                    "LogsDirectoryItemBorder",
                    "LogLevelItemBorder",
                    "DiagnosticsSummaryItemBorder",
                    "ThirdPartyNoticesItemBorder");
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Ordinary_settings_controls_expose_automation_names()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                AssertAutomationNames(
                    provider.GetRequiredService<PlaybackSettingsPage>(),
                    "默认语速",
                    "播放预取数量",
                    "朗读标题");
                AssertAutomationNames(
                    provider.GetRequiredService<ImportTextSettingsPage>(),
                    "文件名模板",
                    "拆分长段落",
                    "长段落阈值",
                    "正则替换");
                AssertAutomationNames(
                    provider.GetRequiredService<CacheAndDataPage>(),
                    "缓存上限数值",
                    "缓存上限单位",
                    "打开应用数据目录",
                    "缓存管理");
                AssertAutomationNames(
                    provider.GetRequiredService<AppearanceSettingsPage>(),
                    "应用主题");
                AssertAutomationNames(
                    provider.GetRequiredService<GeneralSettingsPage>(),
                    "关闭主窗口时",
                    "启动后最小化到托盘");
                AssertAutomationNames(
                    provider.GetRequiredService<DiagnosticsAboutPage>(),
                    "日志级别",
                    "打开日志目录",
                    "复制脱敏诊断摘要",
                    "打开第三方许可证");
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Low_frequency_settings_tools_use_accessible_icon_buttons()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                AssertIconTool(
                    provider.GetRequiredService<CacheAndDataPage>(),
                    "打开应用数据目录",
                    SymbolRegular.FolderOpen24,
                    "App.Button.Icon");
                var diagnosticsPage = provider.GetRequiredService<DiagnosticsAboutPage>();
                AssertIconTool(diagnosticsPage, "打开日志目录", SymbolRegular.FolderOpen24);
                AssertIconTool(diagnosticsPage, "复制脱敏诊断摘要", SymbolRegular.DocumentCopy24);
                AssertIconTool(diagnosticsPage, "打开第三方许可证", SymbolRegular.DocumentText24);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Diagnostics_title_and_value_rows_center_text_vertically()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<DiagnosticsAboutPage>();
                page.Measure(new Size(1200, 900));
                page.Arrange(new Rect(0, 0, 1200, 900));
                page.UpdateLayout();

                var row = Assert.IsType<Border>(page.FindName("AppNameItemBorder"));
                var textBlocks = VisualTreeTestHelper.FindDescendants<TextBlock>(row).ToArray();

                Assert.Equal(2, textBlocks.Length);
                Assert.All(textBlocks, textBlock => Assert.Equal(VerticalAlignment.Center, textBlock.VerticalAlignment));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Settings_pages_do_not_reimplement_shared_border_or_hover_visuals()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var relativePaths = new[]
        {
            Path.Combine("Features", "Settings", "SettingsPage.xaml"),
            Path.Combine("Features", "PlaybackSettings", "PlaybackSettingsPage.xaml"),
            Path.Combine("Features", "ImportTextSettings", "ImportTextSettingsPage.xaml"),
            Path.Combine("Features", "Cache", "CacheAndDataPage.xaml"),
            Path.Combine("Features", "Appearance", "AppearanceSettingsPage.xaml"),
            Path.Combine("Features", "GeneralSettings", "GeneralSettingsPage.xaml"),
            Path.Combine("Features", "Diagnostics", "DiagnosticsAboutPage.xaml")
        };

        foreach (var relativePath in relativePaths)
        {
            var document = XDocument.Load(Path.Combine(appRoot, relativePath));
            var localVisualSetters = document
                .Descendants()
                .Where(static element => element.Name.LocalName == "Setter")
                .Select(static element => (string?)element.Attribute("Property"))
                .Where(static property => property is "Background" or "BorderBrush" or "BorderThickness" or "CornerRadius")
                .ToArray();
            var localHoverTriggers = document
                .Descendants()
                .Where(static element => element.Name.LocalName is "Trigger" or "DataTrigger")
                .Where(static element => (string?)element.Attribute("Property") == "IsMouseOver")
                .ToArray();

            Assert.Empty(localVisualSetters);
            Assert.Empty(localHoverTriggers);
        }
    }

    private static void AssertRows(FrameworkElement page, params string[] rowNames)
    {
        page.Measure(new Size(1200, 900));
        page.Arrange(new Rect(0, 0, 1200, 900));
        page.UpdateLayout();

        var rowStyle = Assert.IsType<Style>(page.FindResource("SettingsRowBorderStyle"));
        var lastRowStyle = Assert.IsType<Style>(page.FindResource("SettingsLastRowBorderStyle"));

        foreach (var rowName in rowNames)
        {
            var border = Assert.IsType<Border>(page.FindName(rowName));
            Assert.True(
                ReferenceEquals(border.Style, rowStyle) || ReferenceEquals(border.Style, lastRowStyle),
                $"{page.GetType().Name}.{rowName} does not use a shared setting-row style.");

            var grid = Assert.IsType<Grid>(border.Child);
            Assert.Equal(2, grid.ColumnDefinitions.Count);
            Assert.Contains(
                grid.Children.Cast<UIElement>(),
                child => Grid.GetColumn(child) == 1);
        }
    }

    private static void AssertAutomationNames(FrameworkElement page, params string[] expectedNames)
    {
        page.Measure(new Size(1200, 900));
        page.Arrange(new Rect(0, 0, 1200, 900));
        page.UpdateLayout();

        var names = VisualTreeTestHelper.FindDescendants<FrameworkElement>(page)
            .Select(AutomationProperties.GetName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(expectedNames, name => Assert.Contains(name, names));
    }

    private static void AssertIconTool(
        FrameworkElement page,
        string accessibleName,
        SymbolRegular expectedSymbol,
        string styleKey = "SecondaryIconButtonStyle")
    {
        page.Measure(new Size(1200, 900));
        page.Arrange(new Rect(0, 0, 1200, 900));
        page.UpdateLayout();

        var button = Assert.Single(
            VisualTreeTestHelper.FindDescendants<Button>(page),
            candidate => AutomationProperties.GetName(candidate) == accessibleName);

        Assert.Equal(accessibleName, button.ToolTip);
        Assert.Equal(expectedSymbol, Assert.IsType<SymbolIcon>(button.Content).Symbol);
        Assert.Same(page.FindResource(styleKey), button.Style);
    }

    private static string GetRepositoryRoot()
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
