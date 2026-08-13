using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using Wpf.Ui.Appearance;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class AppearanceSettingsPageTests
{
    [Fact]
    public void Appearance_page_keeps_theme_row_non_overlapping_at_narrow_and_wide_widths()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<AppearanceSettingsPage>();
                using var host = new WpfControlHost(page);

                var row = Assert.IsType<AppSettingsRow>(page.FindName("ThemeSettingRow"));
                var comboBox = Assert.IsType<ComboBox>(page.FindName("ThemeComboBox"));
                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Same(row, Assert.Single(settingsList.Items));
                Assert.Equal("外观设置", AutomationProperties.GetName(settingsList));
                Assert.Equal("应用主题", AutomationProperties.GetName(comboBox));
                Assert.Equal(3, comboBox.Items.Count);
                Assert.Same(comboBox, row.Value);

                var itemsSourceBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(comboBox, ItemsControl.ItemsSourceProperty));
                Assert.Equal(nameof(AppearanceSettingsViewModel.AvailableThemes), itemsSourceBinding.Path.Path);
                Assert.Equal(BindingMode.OneWay, itemsSourceBinding.Mode);

                var selectedItemBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(comboBox, Selector.SelectedItemProperty));
                Assert.Equal(nameof(AppearanceSettingsViewModel.SelectedTheme), selectedItemBinding.Path.Path);
                Assert.Equal(BindingMode.TwoWay, selectedItemBinding.Mode);

                host.MeasureArrange(new Size(520, 900));
                Assert.True(row.IsNarrowLayout);
                Assert.True(row.ActualWidth > 0);
                Assert.True(row.ActualHeight >= 60);
                Assert.True(comboBox.ActualWidth >= 180);

                var title = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(row),
                    textBlock => textBlock.Text == "应用主题");
                var titleBounds = title.TransformToAncestor(row)
                    .TransformBounds(new Rect(new Point(), title.RenderSize));
                var valueBounds = comboBox.TransformToAncestor(row)
                    .TransformBounds(new Rect(new Point(), comboBox.RenderSize));
                Assert.True(titleBounds.Bottom <= valueBounds.Top);

                host.MeasureArrange(new Size(1200, 900));
                Assert.False(row.IsNarrowLayout);
                Assert.True(row.ActualWidth > 0);
                Assert.True(comboBox.ActualWidth >= 180);

                var wideTitleBounds = title.TransformToAncestor(row)
                    .TransformBounds(new Rect(new Point(), title.RenderSize));
                var wideValueBounds = comboBox.TransformToAncestor(row)
                    .TransformBounds(new Rect(new Point(), comboBox.RenderSize));
                Assert.True(wideTitleBounds.Right <= wideValueBounds.Left);
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
    public void Appearance_page_constructs_after_runtime_theme_switch(ApplicationTheme theme)
    {
        WpfTestHost.RunInSta(() =>
        {
            var themeRuntime = new WpfUiThemeRuntime();
            if (theme == ApplicationTheme.Dark)
            {
                themeRuntime.ApplyDarkTheme();
            }
            else
            {
                themeRuntime.ApplyLightTheme();
            }
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<AppearanceSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));
                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Equal("外观", header.Title);
                Assert.Equal(
                    nameof(AppearanceSettingsViewModel.BackCommand),
                    Assert.IsType<Binding>(BindingOperations.GetBinding(
                        header,
                        AppPageHeader.BackCommandProperty)).Path.Path);
                Assert.True(page.ActualWidth > 0);
                Assert.True(page.ActualHeight > 0);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                themeRuntime.ApplyLightTheme();
            }
        });
    }
}
