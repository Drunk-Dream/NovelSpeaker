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
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var dialog = CreateDialogContent();
        Grid.SetColumn(dialog, 0);
        grid.Children.Add(dialog);

        var flyout = CreateSurface(
            "feedback-flyout",
            "App.Feedback.PopupSurface",
            "Flyout 内容",
            "短操作使用 raised surface 承载真实页面内容，Wpf.Ui 继续拥有轻触关闭与宿主生命周期。");
        Grid.SetColumn(flyout, 1);
        grid.Children.Add(flyout);

        var inline = CreateSurface(
            "feedback-inline",
            "App.Feedback.InlineMessage",
            "页内提示",
            "InlineMessage 只提供内容区域的表面、间距和边界。");
        Grid.SetColumn(inline, 0);
        Grid.SetRow(inline, 1);
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
        Grid.SetColumn(validation, 1);
        Grid.SetRow(validation, 1);
        grid.Children.Add(validation);

        var snackbar = CreateSnackbarContent();
        Grid.SetColumnSpan(snackbar, 2);
        Grid.SetRow(snackbar, 2);
        grid.Children.Add(snackbar);

        return grid;
    }

    private static Border CreateDialogContent()
    {
        var host = new Border
        {
            Margin = new Thickness(0, 0, 8, 8),
            Style = FindStyle("App.Surface.Raised")
        };
        AutomationProperties.SetAutomationId(host, "feedback-dialog-host");

        var body = new Border
        {
            Style = FindStyle("App.Feedback.DialogBody")
        };
        AutomationProperties.SetAutomationId(body, "feedback-dialog-body");
        var content = new StackPanel();
        content.Children.Add(new TextBlock
        {
            Text = "删除所选书籍？",
            Style = FindStyle("App.Feedback.DialogTitle")
        });
        content.Children.Add(new TextBlock
        {
            Text = "此操作只删除应用内数据，不会修改外部源文件。",
            Style = FindStyle("App.Feedback.DialogMessage"),
            TextWrapping = TextWrapping.Wrap
        });
        body.Child = content;
        host.Child = body;
        return host;
    }

    private static Border CreateSnackbarContent()
    {
        var surface = new Border
        {
            Margin = new Thickness(0, 8, 8, 0),
            Style = FindStyle("App.Feedback.SnackbarBody")
        };
        AutomationProperties.SetAutomationId(surface, "feedback-snackbar");
        var content = new StackPanel();
        content.Children.Add(new ContentPresenter
        {
            Content = "缓存任务已开始",
            ContentTemplate = FindTemplate("App.Feedback.SnackbarTitleTemplate")
        });
        content.Children.Add(new ContentPresenter
        {
            Content = "可继续阅读，完成后会在此处显示结果。",
            ContentTemplate = FindTemplate("App.Feedback.SnackbarMessageTemplate"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        surface.Child = content;
        return surface;
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

    private static DataTemplate FindTemplate(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as DataTemplate
        ?? throw new InvalidOperationException($"Gallery feedback resource '{key}' was not found.");
}
