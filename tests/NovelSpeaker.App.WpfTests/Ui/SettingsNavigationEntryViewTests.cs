using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SettingsNavigationEntryViewTests
{
    [Fact]
    public void Subpages_use_shared_icon_title_chevron_rows_for_navigation()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                AssertNavigationEntry(
                    provider.GetRequiredService<ImportTextSettingsPage>(),
                    "OpenRegexReplacementRulesButton",
                    "正则替换");
                AssertNavigationEntry(
                    provider.GetRequiredService<CacheAndDataPage>(),
                    "OpenCacheManagementButton",
                    "缓存管理");
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static void AssertNavigationEntry(
        FrameworkElement page,
        string buttonName,
        string title)
    {
        page.Measure(new Size(1200, 800));
        page.Arrange(new Rect(0, 0, 1200, 800));
        page.UpdateLayout();

        var button = Assert.IsType<Button>(page.FindName(buttonName));
        var expectedStyle = Assert.IsType<Style>(page.FindResource("SettingsNavigationRowButtonStyle"));

        Assert.Same(expectedStyle, button.Style);
        Assert.Equal(title, button.Content);
        Assert.Equal(title, AutomationProperties.GetName(button));
        Assert.Equal(title, button.ToolTip);

        var icons = VisualTreeTestHelper.FindDescendants<SymbolIcon>(button).ToArray();
        Assert.Equal(2, icons.Length);
        Assert.Equal(SymbolRegular.ChevronRight24, icons[1].Symbol);

        Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(button),
            textBlock => textBlock.Text == title);
    }
}
