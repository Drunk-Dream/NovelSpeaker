using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using NovelSpeaker.App.Shared.Theming.Components;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using Wpf.Ui.Controls;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryNavigationFeedbackScene
{
    private static readonly Lazy<ResourceDictionary> FeedbackResources = new(
        () => new ResourceDictionary
        {
            Source = new Uri(
                "/NovelSpeaker.App;component/Shared/Theming/Resources/ControlThemes/NavigationFeedbackStyles.xaml",
                UriKind.RelativeOrAbsolute)
        });

    public static FrameworkElement Create()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        AutomationProperties.SetAutomationId(scrollViewer, "navigation-feedback-scroll-viewer");
        if (System.Windows.Application.Current?.TryFindResource("App.Feedback.SurfaceBase") is null)
        {
            scrollViewer.Resources.MergedDictionaries.Add(FeedbackResources.Value);
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var progress = CreateProgressSurface();
        Grid.SetColumn(progress, 0);
        grid.Children.Add(progress);

        var feedback = CreateFeedbackSurface();
        Grid.SetColumn(feedback, 1);
        grid.Children.Add(feedback);

        scrollViewer.Content = grid;
        return scrollViewer;
    }

    private static Border CreateProgressSurface()
    {
        var surface = CreateSurface("feedback-progress-surface");
        surface.Margin = new Thickness(0, 0, 8, 0);
        surface.Padding = new Thickness(16);
        var content = new StackPanel();
        content.Children.Add(CreateTitle("Progress 与 Slider"));
        content.Children.Add(CreateBody("ProgressBar 只表达任务完成度；Slider 仍由 App.Media.Slider 表达可编辑范围。"));

        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 64,
            Style = FindResource("App.Feedback.ProgressBar"),
            Margin = new Thickness(0, 14, 0, 18)
        };
        AutomationProperties.SetAutomationId(progress, "feedback-progress");
        AutomationProperties.SetName(progress, "导入进度 64%");
        content.Children.Add(progress);

        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = 42,
            Style = FindResource("App.Media.Slider")
        };
        AutomationProperties.SetAutomationId(slider, "feedback-slider");
        AutomationProperties.SetName(slider, "播放位置");
        content.Children.Add(slider);
        surface.Child = content;
        return surface;
    }

    private static Border CreateFeedbackSurface()
    {
        var surface = CreateSurface("feedback-components-surface");
        surface.Margin = new Thickness(8, 0, 0, 0);
        surface.Padding = new Thickness(12);
        var content = new StackPanel();
        content.Children.Add(CreateTitle("Flyout、Dialog、Snackbar 与状态"));
        content.Children.Add(CreateBody("Loading / Error / NoResult 共用状态结构；Dialog 只做一个决定，Snackbar 不阻塞页面。"));

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        var flyout = ApplyStyle(new FlyoutSurface(), "App.Feedback.FlyoutSurface", "feedback-flyout");
        flyout.Margin = new Thickness(0, 8, 8, 8);
        var dialog = ApplyStyle(new DialogShell(), "App.Feedback.DialogShell", "feedback-dialog");
        dialog.Margin = new Thickness(0, 8, 0, 8);
        row.Children.Add(flyout);
        row.Children.Add(dialog);
        content.Children.Add(row);

        var snackbar = ApplyStyle(new SnackbarContent(), "App.Feedback.SnackbarContent", "feedback-snackbar");
        snackbar.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(snackbar);

        var states = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (state, styleKey) in new (FeedbackStatusBase state, string styleKey)[]
                 {
                     (new LoadingState(), "App.Feedback.Loading"),
                     (new ErrorState(), "App.Feedback.Error"),
                     (new NoResultState(), "App.Feedback.NoResult")
                 })
        {
            var styled = ApplyStyle(state, styleKey, AutomationProperties.GetAutomationId(state.Content as DependencyObject));
            styled.Margin = new Thickness(0, 0, 8, 8);
            states.Children.Add(styled);
        }
        content.Children.Add(states);
        surface.Child = content;
        return surface;
    }

    private static T ApplyStyle<T>(T element, string styleKey, string? automationId)
        where T : FrameworkElement
    {
        element.Style = FindResource(styleKey);
        if (!string.IsNullOrWhiteSpace(automationId))
        {
            AutomationProperties.SetAutomationId(element, automationId);
        }

        return element;
    }

    private static Style FindResource(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Style
        ?? FeedbackResources.Value[key] as Style
        ?? throw new InvalidOperationException($"Gallery feedback resource '{key}' was not found.");

    private static Border CreateSurface(string automationId)
    {
        var surface = new Border { BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty, "App.Brush.Surface.Primary");
        surface.SetResourceReference(Border.BorderBrushProperty, "App.Brush.Border.Subtle");
        surface.SetResourceReference(Border.CornerRadiusProperty, "App.Radius.Medium");
        AutomationProperties.SetAutomationId(surface, automationId);
        return surface;
    }

    private static WpfTextBlock CreateTitle(string text) =>
        new WpfTextBlock
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithFeedbackResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Primary");

    private static WpfTextBlock CreateBody(string text) =>
        new WpfTextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithFeedbackResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Secondary");
}

internal static class GalleryFeedbackResourceExtensions
{
    public static T WithFeedbackResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
