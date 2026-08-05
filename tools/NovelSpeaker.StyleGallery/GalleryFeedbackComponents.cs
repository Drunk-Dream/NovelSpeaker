using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.StyleGallery;

public abstract class GalleryFeedbackSurfaceBase : ContentControl
{
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(GalleryFeedbackSurfaceBase),
            new FrameworkPropertyMetadata(new CornerRadius(10)));

    protected GalleryFeedbackSurfaceBase()
    {
        Focusable = false;
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected static Style? ApplicationStyle(string key) =>
        Application.Current?.TryFindResource(key) as Style;
}

public abstract class GalleryFeedbackStatusBase : GalleryFeedbackSurfaceBase
{
    protected GalleryFeedbackStatusBase()
    {
        Padding = new Thickness(14);
    }
}

public sealed class FlyoutSurface : GalleryFeedbackSurfaceBase
{
    public FlyoutSurface()
    {
        Content = FeedbackContent.CreateFlyout();
    }
}

public sealed class DialogShell : GalleryFeedbackSurfaceBase
{
    private readonly WpfButton _confirmButton;
    private readonly WpfButton _cancelButton;

    public DialogShell()
    {
        FocusManager.SetIsFocusScope(this, true);
        KeyboardNavigation.SetTabNavigation(this, KeyboardNavigationMode.Cycle);
        PreviewKeyDown += OnPreviewKeyDown;
        Content = FeedbackContent.CreateDialog();

        if (Content is not StackPanel content ||
            content.Children.OfType<StackPanel>().SingleOrDefault() is not StackPanel buttonPanel ||
            buttonPanel.Children.OfType<WpfButton>().ToArray() is not [var cancelButton, var confirmButton])
        {
            throw new InvalidOperationException("Dialog shell content must own its cancel and confirm buttons.");
        }

        _cancelButton = cancelButton;
        _confirmButton = confirmButton;
        _confirmButton.Click += (_, _) =>
        {
            Complete(DialogOutcome.Confirmed);
        };
        _cancelButton.Click += (_, _) =>
        {
            Complete(DialogOutcome.Cancelled);
        };
        Loaded += (_, _) => _confirmButton.Focus();
    }

    public bool IsDismissed { get; private set; }

    public bool IsConfirmed { get; private set; }

    public bool IsCancelled { get; private set; }

    public event EventHandler? Dismissed;

    public event EventHandler? Confirmed;

    public event EventHandler? Cancelled;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        Complete(DialogOutcome.Cancelled);
        e.Handled = true;
    }

    private void Complete(DialogOutcome outcome)
    {
        if (IsDismissed)
        {
            return;
        }

        IsDismissed = true;
        if (outcome == DialogOutcome.Confirmed)
        {
            IsConfirmed = true;
            Confirmed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            IsCancelled = true;
            Cancelled?.Invoke(this, EventArgs.Empty);
        }

        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private enum DialogOutcome
    {
        Confirmed,
        Cancelled
    }
}

public sealed class SnackbarContent : GalleryFeedbackSurfaceBase
{
    public SnackbarContent()
    {
        Content = FeedbackContent.CreateSnackbar();
    }
}

public sealed class LoadingState : GalleryFeedbackStatusBase
{
    public LoadingState()
    {
        Content = FeedbackContent.CreateStatus(
            SymbolRegular.ArrowSync24,
            "正在加载",
            "请稍候，内容仍在准备中。",
            "loading-state");
    }
}

public sealed class ErrorState : GalleryFeedbackStatusBase
{
    public ErrorState()
    {
        Content = FeedbackContent.CreateStatus(
            SymbolRegular.Warning24,
            "加载失败",
            "请求未完成，可以重试或返回上一层。",
            "error-state");
    }
}

public sealed class NoResultState : GalleryFeedbackStatusBase
{
    public NoResultState()
    {
        Content = FeedbackContent.CreateStatus(
            SymbolRegular.Search24,
            "没有结果",
            "调整筛选条件后再次尝试。",
            "no-result-state");
    }
}

internal static class FeedbackContent
{
    public static StackPanel CreateFlyout()
    {
        var content = CreateStack("flyout-content");
        content.Children.Add(CreateTitle("后台处理"));
        content.Children.Add(CreateBody("导入任务正在后台运行；这个 raised surface 不阻塞页面操作。"));
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 68,
            Margin = new Thickness(0, 12, 0, 8)
        };
        AutomationProperties.SetAutomationId(progress, "flyout-progress");
        content.Children.Add(progress);
        var cancel = new WpfButton
        {
            Content = "取消任务",
            Style = ApplicationStyle("App.Button.Secondary"),
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 96
        };
        AutomationProperties.SetAutomationId(cancel, "flyout-cancel");
        AutomationProperties.SetName(cancel, "取消后台任务");
        content.Children.Add(cancel);
        return content;
    }

    public static StackPanel CreateDialog()
    {
        var content = CreateStack("dialog-content");
        content.Children.Add(CreateTitle("确认导入"));
        content.Children.Add(CreateBody("将把脱敏 fixture 文本加入书库。默认按钮在右侧，Escape 取消当前决定。"));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var cancel = new WpfButton
        {
            Content = "取消",
            Style = ApplicationStyle("App.Button.Secondary"),
            IsCancel = true,
            MinWidth = 84,
            Margin = new Thickness(0, 0, 8, 0)
        };
        AutomationProperties.SetAutomationId(cancel, "dialog-cancel");
        AutomationProperties.SetName(cancel, "取消对话框");
        buttons.Children.Add(cancel);
        var confirm = new WpfButton
        {
            Content = "确认",
            Style = ApplicationStyle("App.Button.Primary"),
            IsDefault = true,
            MinWidth = 84
        };
        AutomationProperties.SetAutomationId(confirm, "dialog-confirm");
        AutomationProperties.SetName(confirm, "确认导入");
        buttons.Children.Add(confirm);
        content.Children.Add(buttons);
        return content;
    }

    public static Grid CreateSnackbar()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = SymbolRegular.CheckmarkCircle24, Width = 22, Height = 22 };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "SuccessBrush");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);
        var message = CreateBody("导入完成 · 3 个章节已加入队列");
        message.Margin = new Thickness(12, 0, 12, 0);
        message.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetAutomationId(message, "snackbar-message");
        Grid.SetColumn(message, 1);
        grid.Children.Add(message);
        var close = new WpfButton
        {
            Content = new SymbolIcon { Symbol = SymbolRegular.Dismiss24, Width = 18, Height = 18 },
            Style = ApplicationStyle("App.Button.Icon"),
            ToolTip = "关闭通知"
        };
        AutomationProperties.SetAutomationId(close, "snackbar-close");
        AutomationProperties.SetName(close, "关闭通知");
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);
        return grid;
    }

    public static StackPanel CreateStatus(
        SymbolRegular symbol,
        string title,
        string description,
        string automationId)
    {
        var content = CreateStack(automationId);
        var icon = new SymbolIcon { Symbol = symbol, Width = 22, Height = 22 };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "AccentBrush");
        content.Children.Add(icon);
        content.Children.Add(CreateTitle(title));
        content.Children.Add(CreateBody(description));
        return content;
    }

    private static StackPanel CreateStack(string automationId)
    {
        var stack = new StackPanel();
        AutomationProperties.SetAutomationId(stack, automationId);
        return stack;
    }

    private static WpfTextBlock CreateTitle(string text) =>
        new WpfTextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithFeedbackResource(WpfTextBlock.ForegroundProperty, "PrimaryTextBrush");

    private static WpfTextBlock CreateBody(string text) =>
        new WpfTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            MaxWidth = 248,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithFeedbackResource(WpfTextBlock.ForegroundProperty, "SecondaryTextBrush");

    private static Style? ApplicationStyle(string key) =>
        Application.Current?.TryFindResource(key) as Style;
}

internal static class GalleryFeedbackElementExtensions
{
    public static T WithFeedbackResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
