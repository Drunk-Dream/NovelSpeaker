using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

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

                var allText = FindVisualChildren<System.Windows.Controls.TextBlock>(page)
                    .Select(text => text.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Cast<string>()
                    .ToArray();
                var allButtons = FindVisualChildren<System.Windows.Controls.Button>(page).ToArray();
                var allIcons = FindVisualChildren<SymbolIcon>(page).ToArray();

                Assert.Contains("常用", allText);
                Assert.Contains("文本处理", allText);
                Assert.Contains("应用", allText);
                Assert.DoesNotContain("保存设置", allText);
                Assert.DoesNotContain(allButtons, button => string.Equals(button.Content?.ToString(), "保存设置", StringComparison.Ordinal));
                Assert.Empty(FindVisualChildren<System.Windows.Controls.TextBox>(page));
                Assert.Empty(FindVisualChildren<System.Windows.Controls.ComboBox>(page));
                Assert.Equal(14, allIcons.Length);
                Assert.Equal(7, allIcons.Count(icon => icon.Symbol == SymbolRegular.ChevronRight24));
                Assert.Equal(
                    3,
                    FindVisualChildren<System.Windows.Controls.Border>(page)
                        .Count(border => string.Equals(border.Tag?.ToString(), "SettingsGroupSeparator", StringComparison.Ordinal)));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static IReadOnlyList<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var results = new List<T>();
        Visit(root, results);
        return results;
    }

    private static void Visit<T>(DependencyObject node, List<T> results)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); childIndex++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node, childIndex);
            if (child is T typedChild)
            {
                results.Add(typedChild);
            }

            Visit(child, results);
        }
    }
}
