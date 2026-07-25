using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

[Collection("WpfDispatcher")]
public sealed class SettingsPageViewTests
{
    [Fact]
    public void SettingsPage_shows_grouped_entries_without_form_controls_or_save_button()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<SettingsPage>();
                page.Measure(new Size(1200, 800));
                page.Arrange(new Rect(0, 0, 1200, 800));
                page.UpdateLayout();

                var allText = VisualTreeTestHelper.FindDescendants<System.Windows.Controls.TextBlock>(page)
                    .Select(text => text.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Cast<string>()
                    .ToArray();
                var allButtons = VisualTreeTestHelper.FindDescendants<System.Windows.Controls.Button>(page).ToArray();
                var allIcons = VisualTreeTestHelper.FindDescendants<SymbolIcon>(page).ToArray();

                Assert.Contains("常用", allText);
                Assert.Contains("文本处理", allText);
                Assert.Contains("应用", allText);
                Assert.DoesNotContain("保存设置", allText);
                Assert.DoesNotContain(allButtons, button => string.Equals(button.Content?.ToString(), "保存设置", StringComparison.Ordinal));
                Assert.Empty(VisualTreeTestHelper.FindDescendants<System.Windows.Controls.TextBox>(page));
                Assert.Empty(VisualTreeTestHelper.FindDescendants<System.Windows.Controls.ComboBox>(page));
                Assert.Equal(14, allIcons.Length);
                Assert.Equal(7, allIcons.Count(icon => icon.Symbol == SymbolRegular.ChevronRight24));
                Assert.Equal(
                    3,
                    VisualTreeTestHelper.FindDescendants<System.Windows.Controls.Border>(page)
                        .Count(border => string.Equals(border.Tag?.ToString(), "SettingsGroupSeparator", StringComparison.Ordinal)));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

}
