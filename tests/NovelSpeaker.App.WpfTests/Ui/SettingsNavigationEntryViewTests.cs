using System.Windows;
using System.Windows.Automation;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
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
                    provider.GetRequiredService<CacheAndDataPage>(),
                    "OpenCacheManagementRow",
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

        var row = Assert.IsType<AppSettingsNavigationRow>(page.FindName(buttonName));
        Assert.Same(page.FindResource(typeof(AppSettingsNavigationRow)), row.Style);
        Assert.Equal(title, row.Title);
        Assert.Equal(title, row.ToolTip);
        Assert.Equal(title, AutomationProperties.GetName(row));
    }
}
