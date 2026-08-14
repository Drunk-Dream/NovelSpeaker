using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class GeneralSettingsPageTests
{
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
                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Equal(2, settingsList.Items.Count);
                Assert.Same(closeBehaviorRow, settingsList.Items[0]);
                Assert.Same(startMinimizedRow, settingsList.Items[1]);
                Assert.Equal("常规设置", AutomationProperties.GetName(settingsList));
                Assert.Equal("关闭主窗口时", AutomationProperties.GetName(comboBox));
                Assert.Equal(3, comboBox.Items.Count);

                var itemsSourceBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(comboBox, ItemsControl.ItemsSourceProperty));
                Assert.Equal(nameof(GeneralSettingsViewModel.CloseBehaviorOptions), itemsSourceBinding.Path.Path);
                Assert.Equal(BindingMode.OneWay, itemsSourceBinding.Mode);

                var selectedItemBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(comboBox, Selector.SelectedItemProperty));
                Assert.Equal(nameof(GeneralSettingsViewModel.SelectedCloseBehavior), selectedItemBinding.Path.Path);
                Assert.Equal(BindingMode.TwoWay, selectedItemBinding.Mode);
                Assert.Equal("DisplayName", comboBox.DisplayMemberPath);

                var isCheckedBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(toggleSwitch, ToggleSwitch.IsCheckedProperty));
                Assert.Equal(nameof(GeneralSettingsViewModel.StartMinimizedToTray), isCheckedBinding.Path.Path);
                Assert.Equal(BindingMode.TwoWay, isCheckedBinding.Mode);

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
}
