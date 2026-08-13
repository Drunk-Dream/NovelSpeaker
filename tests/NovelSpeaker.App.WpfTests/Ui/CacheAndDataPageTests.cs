using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CacheAndDataPageTests
{
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
                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                var navigation = Assert.IsType<AppSettingsNavigationRow>(
                    page.FindName("OpenCacheManagementRow"));
                Assert.Equal("缓存与数据设置", AutomationProperties.GetName(settingsList));
                Assert.Equal(5, settingsList.Items.Count);
                Assert.Same(rows[0], settingsList.Items[0]);
                Assert.Same(rows[1], settingsList.Items[1]);
                Assert.Same(rows[2], settingsList.Items[2]);
                Assert.Same(rows[3], settingsList.Items[3]);
                Assert.Same(navigation, settingsList.Items[4]);

                var valueInput = Assert.IsType<TextBox>(page.FindName("CacheLimitValueTextBox"));
                var valueBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(valueInput, TextBox.TextProperty));
                Assert.Equal(nameof(CacheAndDataViewModel.CacheLimitValueText), valueBinding.Path.Path);
                Assert.Equal(UpdateSourceTrigger.PropertyChanged, valueBinding.UpdateSourceTrigger);
                var unitInput = Assert.IsType<ComboBox>(page.FindName("CacheLimitUnitComboBox"));
                Assert.Equal("缓存上限数值", AutomationProperties.GetName(valueInput));
                Assert.Equal("缓存上限单位", AutomationProperties.GetName(unitInput));
                Assert.Equal(
                    nameof(CacheAndDataViewModel.CacheLimitUnits),
                    Assert.IsType<Binding>(BindingOperations.GetBinding(
                        unitInput,
                        ItemsControl.ItemsSourceProperty)).Path.Path);
                Assert.Equal(
                    nameof(CacheAndDataViewModel.SelectedCacheLimitUnit),
                    Assert.IsType<Binding>(BindingOperations.GetBinding(
                        unitInput,
                        Selector.SelectedItemProperty)).Path.Path);

                foreach (var (name, commandPath, automationName) in new[]
                         {
                             ("OpenAppDataDirectoryButton", "OpenAppDataDirectoryCommand", "打开应用数据目录"),
                             ("ClearAllCacheButton", "ClearAllCommand", "清理全部缓存")
                         })
                {
                    var button = Assert.IsAssignableFrom<Button>(page.FindName(name));
                    Assert.Equal(automationName, AutomationProperties.GetName(button));
                    Assert.Equal(automationName, button.ToolTip);
                    Assert.Equal(
                        commandPath,
                        Assert.IsType<Binding>(BindingOperations.GetBinding(button, Button.CommandProperty))
                            .Path.Path);
                }

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

}
