using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryFeedbackScene
{
    public static FrameworkElement Create()
    {
        var grid = new Grid { Margin = new Thickness(20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var popup = CreateSurface(
            "feedback-popup",
            "App.Feedback.PopupSurface",
            "Popup 内容",
            "短操作使用 raised surface 承载真实页面内容，不在 Style 中拥有宿主生命周期。");
        Grid.SetColumn(popup, 0);
        grid.Children.Add(popup);

        var inline = CreateSurface(
            "feedback-inline",
            "App.Feedback.InlineMessage",
            "页内提示",
            "InlineMessage 只提供内容区域的表面、间距和边界。");
        Grid.SetColumn(inline, 1);
        grid.Children.Add(inline);

        var validation = new TextBlock
        {
            Text = "请输入有效的播放速度。",
            Style = FindStyle("App.Feedback.ValidationText"),
            Margin = new Thickness(0, 16, 8, 0),
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetAutomationId(validation, "feedback-validation");
        AutomationProperties.SetName(validation, "播放速度校验错误");
        Grid.SetColumn(validation, 0);
        Grid.SetRow(validation, 1);
        grid.Children.Add(validation);

        var snackbar = CreateSurface(
            "feedback-snackbar",
            "App.Feedback.SnackbarBody",
            "轻量反馈",
            "SnackbarBody 只提供通知内容的排版和 raised surface，不接管 Snackbar 宿主。");
        Grid.SetColumn(snackbar, 1);
        Grid.SetRow(snackbar, 1);
        grid.Children.Add(snackbar);

        return grid;
    }

    private static Border CreateSurface(
        string automationId,
        string styleKey,
        string title,
        string body)
    {
        var surface = new Border
        {
            Margin = new Thickness(0, 0, 8, 8),
            Style = FindStyle(styleKey)
        };
        AutomationProperties.SetAutomationId(surface, automationId);
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = title,
            Style = FindStyle("App.Typography.ItemTitle")
        });
        content.Children.Add(new TextBlock
        {
            Text = body,
            Margin = new Thickness(0, 8, 0, 0),
            Style = FindStyle("App.Typography.Secondary"),
            TextWrapping = TextWrapping.Wrap
        });
        surface.Child = content;
        return surface;
    }

    private static Style FindStyle(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Style
        ?? throw new InvalidOperationException($"Gallery feedback resource '{key}' was not found.");
}
