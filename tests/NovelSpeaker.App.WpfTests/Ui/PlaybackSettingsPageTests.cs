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
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class PlaybackSettingsPageTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    public void Playback_settings_rows_and_errors_remain_usable_at_supported_widths_and_dpi(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<PlaybackSettingsPage>();
                page.ViewModel.DefaultSpeakSpeedErrorText =
                    "请输入允许范围内的整数；当前输入不会保存，也不会改变正在播放的内容。";
                page.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = new WpfControlHost(page);

                var rows = new[]
                {
                    Assert.IsType<AppSettingsRow>(page.FindName("DefaultSpeakSpeedRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("PrefetchCountRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("ReadChapterTitleRow"))
                };
                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Equal("播放设置", AutomationProperties.GetName(settingsList));
                Assert.Equal(3, settingsList.Items.Count);
                Assert.Same(rows[0], settingsList.Items[0]);
                Assert.Same(rows[1], settingsList.Items[1]);
                Assert.Same(rows[2], settingsList.Items[2]);
                Assert.Empty(VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(page));

                var speedInput = Assert.IsType<TextBox>(page.FindName("DefaultSpeakSpeedTextBox"));
                var prefetchInput = Assert.IsType<TextBox>(page.FindName("PrefetchCountTextBox"));
                AssertTextBinding(speedInput, nameof(PlaybackSettingsViewModel.DefaultSpeakSpeedText));
                AssertTextBinding(prefetchInput, nameof(PlaybackSettingsViewModel.PrefetchCountText));
                var toggle = Assert.IsType<ToggleSwitch>(page.FindName("ReadChapterTitleToggleSwitch"));
                var checkedBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(toggle, ToggleSwitch.IsCheckedProperty));
                Assert.Equal(nameof(PlaybackSettingsViewModel.ReadChapterTitle), checkedBinding.Path.Path);
                Assert.Equal(BindingMode.TwoWay, checkedBinding.Mode);
                var values = new FrameworkElement[]
                {
                    Assert.IsType<TextBox>(page.FindName("DefaultSpeakSpeedTextBox")).Parent as FrameworkElement
                        ?? throw new InvalidOperationException("Speed input has no value container."),
                    Assert.IsType<TextBox>(page.FindName("PrefetchCountTextBox")).Parent as FrameworkElement
                        ?? throw new InvalidOperationException("Prefetch input has no value container."),
                    Assert.IsType<ToggleSwitch>(page.FindName("ReadChapterTitleToggleSwitch"))
                };

                host.MeasureArrange(new Size(520, 900));
                for (var index = 0; index < rows.Length; index++)
                {
                    Assert.True(rows[index].IsNarrowLayout);
                    Assert.True(rows[index].ActualHeight >= 60);
                    AssertValueBelowTitle(rows[index], values[index]);
                }

                var validation = FindValidationText(page, nameof(PlaybackSettingsViewModel.DefaultSpeakSpeedErrorText));
                Assert.Equal(Visibility.Visible, validation.Visibility);
                Assert.True(validation.ActualWidth > 0);
                Assert.True(validation.ActualHeight > 0);
                Assert.Same(page.FindResource("App.Feedback.ValidationText"), validation.Style.BasedOn);

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
    public void Playback_settings_page_constructs_after_runtime_theme_switch(ApplicationTheme theme)
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
                var page = provider.GetRequiredService<PlaybackSettingsPage>();
                using var host = new WpfControlHost(page);
                host.MeasureArrange(new Size(1200, 900));
                var header = Assert.IsType<AppPageHeader>(page.FindName("PageHeader"));
                Assert.Equal("播放设置", header.Title);
                Assert.Equal(
                    nameof(PlaybackSettingsViewModel.BackCommand),
                    Assert.IsType<Binding>(BindingOperations.GetBinding(
                        header,
                        AppPageHeader.BackCommandProperty)).Path.Path);
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

    private static TextBlock FindValidationText(FrameworkElement page, string propertyName) =>
        Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(page),
            textBlock => BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path == propertyName);

    private static void AssertTextBinding(TextBox textBox, string propertyName)
    {
        var binding = Assert.IsType<Binding>(BindingOperations.GetBinding(textBox, TextBox.TextProperty));
        Assert.Equal(propertyName, binding.Path.Path);
        Assert.Equal(UpdateSourceTrigger.PropertyChanged, binding.UpdateSourceTrigger);
    }

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
}
