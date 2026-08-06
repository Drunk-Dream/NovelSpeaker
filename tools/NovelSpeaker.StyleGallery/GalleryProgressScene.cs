using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryProgressScene
{
    public static FrameworkElement Create()
    {
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.Children.Add(CreateText(
            "任务进度",
            "App.Typography.SectionTitle",
            0));
        grid.Children.Add(CreateText(
            "ProgressBar 只表达完成度；Slider 保留可编辑位置和 Tooltip 行为。",
            "App.Typography.Secondary",
            1));

        var standard = new ProgressBar
        {
            Value = 64,
            Maximum = 100,
            Margin = new Thickness(0, 16, 0, 12),
            Style = FindStyle("App.Progress.Standard")
        };
        AutomationProperties.SetAutomationId(standard, "progress-standard");
        AutomationProperties.SetName(standard, "标准任务进度 64%");
        Grid.SetRow(standard, 2);
        grid.Children.Add(standard);

        var compact = new ProgressBar
        {
            Value = 38,
            Maximum = 100,
            Margin = new Thickness(0, 0, 0, 12),
            Style = FindStyle("App.Progress.Compact")
        };
        AutomationProperties.SetAutomationId(compact, "progress-compact");
        AutomationProperties.SetName(compact, "紧凑任务进度 38%");
        Grid.SetRow(compact, 3);
        grid.Children.Add(compact);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 140,
            Value = 58,
            Margin = new Thickness(0, 0, 0, 12),
            Style = FindStyle("App.Media.Slider")
        };
        AutomationProperties.SetAutomationId(slider, "progress-slider");
        AutomationProperties.SetName(slider, "可编辑段落位置");
        Grid.SetRow(slider, 4);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(slider);

        return grid;
    }

    private static TextBlock CreateText(string text, string styleKey, int row)
    {
        var block = new TextBlock
        {
            Text = text,
            Style = FindStyle(styleKey),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(block, row);
        return block;
    }

    private static Style FindStyle(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Style
        ?? throw new InvalidOperationException($"Gallery progress resource '{key}' was not found.");
}
