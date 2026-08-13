using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using ToggleSwitch = Wpf.Ui.Controls.ToggleSwitch;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ImportTextSettingsPageTests
{
    [Theory]
    [InlineData(1d)]
    [InlineData(1.25d)]
    [InlineData(1.5d)]
    public void Import_text_fields_and_navigation_remain_usable_at_narrow_width_and_supported_dpi(double scale)
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<ImportTextSettingsPage>();
                page.ViewModel.LongParagraphThresholdErrorText =
                    "请输入整数；当前输入不会保存，也不会影响后续导入。";
                page.LayoutTransform = new ScaleTransform(scale, scale);
                using var host = new WpfControlHost(page);

                var rows = new[]
                {
                    Assert.IsType<AppSettingsRow>(page.FindName("EnableLongParagraphSplittingRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("LongParagraphThresholdRow")),
                    Assert.IsType<AppSettingsRow>(page.FindName("BookFileNameTemplateRow"))
                };
                var values = new FrameworkElement[]
                {
                    Assert.IsType<ToggleSwitch>(page.FindName("EnableLongParagraphSplittingToggleSwitch")),
                    Assert.IsType<TextBox>(page.FindName("LongParagraphThresholdTextBox")).Parent as FrameworkElement
                        ?? throw new InvalidOperationException("Threshold input has no value container."),
                    Assert.IsType<TextBox>(page.FindName("BookFileNameTemplateTextBox"))
                };
                var navigation = Assert.IsType<AppSettingsNavigationRow>(
                    page.FindName("OpenRegexReplacementRulesRow"));
                var settingsList = Assert.IsType<AppSettingsList>(page.FindName("SettingsList"));
                Assert.Equal("导入与文本设置", AutomationProperties.GetName(settingsList));
                Assert.Equal(4, settingsList.Items.Count);
                Assert.Same(rows[0], settingsList.Items[0]);
                Assert.Same(rows[1], settingsList.Items[1]);
                Assert.Same(rows[2], settingsList.Items[2]);
                Assert.Same(navigation, settingsList.Items[3]);

                var splitToggle = Assert.IsType<ToggleSwitch>(
                    page.FindName("EnableLongParagraphSplittingToggleSwitch"));
                Assert.Equal("拆分长段落", AutomationProperties.GetName(splitToggle));
                AssertCheckedBinding(
                    splitToggle,
                    nameof(ImportTextSettingsViewModel.EnableLongParagraphSplitting));
                var thresholdInput = Assert.IsType<TextBox>(page.FindName("LongParagraphThresholdTextBox"));
                var templateInput = Assert.IsType<TextBox>(page.FindName("BookFileNameTemplateTextBox"));
                Assert.Equal("长段落阈值", AutomationProperties.GetName(thresholdInput));
                Assert.Equal("文件名模板", AutomationProperties.GetName(templateInput));
                AssertTextBinding(thresholdInput, nameof(ImportTextSettingsViewModel.LongParagraphThresholdText));
                AssertTextBinding(templateInput, nameof(ImportTextSettingsViewModel.BookFileNameTemplateText));

                host.MeasureArrange(new Size(520, 900));
                for (var index = 0; index < rows.Length; index++)
                {
                    Assert.True(rows[index].IsNarrowLayout);
                    Assert.True(rows[index].ActualHeight >= 60);
                    AssertValueBelowTitle(rows[index], values[index]);
                }

                Assert.True(navigation.ActualWidth > 0);
                Assert.True(navigation.ActualHeight >= 60);
                Assert.True(navigation.IsHitTestVisible);
                Assert.Equal("正则替换", navigation.Title);
                Assert.Equal("正则替换", AutomationProperties.GetName(navigation));
                Assert.Equal("正则替换", navigation.ToolTip);
                Assert.NotNull(navigation.Icon);
                Assert.True(navigation.Focusable);
                Assert.True(navigation.IsTabStop);
                var commandBinding = Assert.IsType<Binding>(
                    BindingOperations.GetBinding(navigation, Button.CommandProperty));
                Assert.Equal("OpenRegexReplacementRulesCommand", commandBinding.Path.Path);
                Assert.True(FindValidationText(page).ActualHeight > 0);

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

    private static TextBlock FindValidationText(FrameworkElement page) =>
        Assert.Single(
            VisualTreeTestHelper.FindDescendants<TextBlock>(page),
            textBlock => BindingOperations.GetBinding(textBlock, TextBlock.TextProperty)?.Path.Path ==
                         nameof(ImportTextSettingsViewModel.LongParagraphThresholdErrorText));

    private static void AssertCheckedBinding(ToggleSwitch toggle, string propertyName)
    {
        var binding = Assert.IsType<Binding>(
            BindingOperations.GetBinding(toggle, ToggleSwitch.IsCheckedProperty));
        Assert.Equal(propertyName, binding.Path.Path);
        Assert.Equal(BindingMode.TwoWay, binding.Mode);
    }

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
