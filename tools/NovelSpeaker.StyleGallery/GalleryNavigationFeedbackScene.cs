using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MenuItem = System.Windows.Controls.MenuItem;
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
                "/NovelSpeaker.App;component/Shared/Theming/Resources/NavigationFeedbackStyles.xaml",
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
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var navigation = CreateNavigationSurface();
        Grid.SetColumn(navigation, 0);
        Grid.SetRow(navigation, 0);
        grid.Children.Add(navigation);

        var menu = CreateMenuSurface();
        Grid.SetColumn(menu, 1);
        Grid.SetRow(menu, 0);
        grid.Children.Add(menu);

        var progress = CreateProgressSurface();
        Grid.SetColumn(progress, 0);
        Grid.SetRow(progress, 1);
        grid.Children.Add(progress);

        var feedback = CreateFeedbackSurface();
        Grid.SetColumn(feedback, 1);
        Grid.SetRow(feedback, 1);
        grid.Children.Add(feedback);

        scrollViewer.Content = grid;
        return scrollViewer;
    }

    private static Border CreateNavigationSurface()
    {
        var surface = CreateSurface("feedback-navigation-surface");
        surface.Margin = new Thickness(0, 0, 8, 8);
        surface.Padding = new Thickness(12);
        var content = new StackPanel();
        content.Children.Add(CreateTitle("Navigation Entry"));
        content.Children.Add(CreateBody("显式 App.Navigation.Entry 扩展 Provider.NavigationViewItem；选中、Hover、Focus 和 Disabled 保持可见。"));

        var navigation = new NavigationView
        {
            Height = 198,
            IsPaneOpen = true,
            IsPaneToggleVisible = true,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            OpenPaneLength = 218,
            CompactPaneLength = 218,
            Margin = new Thickness(0, 10, 0, 0)
        };
        AutomationProperties.SetAutomationId(navigation, "feedback-navigation-view");
        foreach (var (label, symbol, selected, enabled) in new[]
                 {
                     ("书库", SymbolRegular.Library24, true, true),
                     ("播放队列", SymbolRegular.PlayCircle24, false, true),
                     ("设置", SymbolRegular.Settings24, false, true),
                     ("暂不可用", SymbolRegular.Warning24, false, false)
                 })
        {
            var item = new NavigationViewItem
            {
                Content = label,
                Icon = new SymbolIcon { Symbol = symbol, Width = 20, Height = 20 },
                IsActive = selected,
                IsEnabled = enabled,
                Style = FindResource("App.Navigation.Entry")
            };
            AutomationProperties.SetAutomationId(item, $"navigation-entry-{label}");
            AutomationProperties.SetName(item, $"导航：{label}");
            navigation.MenuItems.Add(item);
        }
        var firstItem = navigation.MenuItems.OfType<NavigationViewItem>().First();
        SetSelectedItem(navigation, firstItem);
        navigation.Loaded += (_, _) =>
        {
            navigation.IsPaneOpen = true;
            SetSelectedItem(navigation, firstItem);
        };

        content.Children.Add(navigation);
        surface.Child = content;
        return surface;
    }

    private static Border CreateMenuSurface()
    {
        var surface = CreateSurface("feedback-menu-surface");
        surface.Margin = new Thickness(8, 0, 0, 8);
        surface.Padding = new Thickness(12);
        var content = new StackPanel();
        content.Children.Add(CreateTitle("ContextMenu / MenuItem"));
        content.Children.Add(CreateBody("Danger 操作独立分组；Close 保持默认中性菜单样式。"));

        var anchor = new WpfButton
        {
            Content = "右键打开 ContextMenu",
            Style = FindResource("App.Button.Secondary"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 10)
        };
        AutomationProperties.SetAutomationId(anchor, "feedback-context-anchor");
        AutomationProperties.SetName(anchor, "打开示例 ContextMenu");
        anchor.ContextMenu = CreateContextMenu();
        content.Children.Add(anchor);

        var visualMenu = new Menu
        {
            Style = FindResource("App.Menu.Surface")
        };
        AutomationProperties.SetAutomationId(visualMenu, "feedback-inline-menu");
        foreach (var item in CreateMenuItems())
        {
            visualMenu.Items.Add(item);
        }
        content.Children.Add(visualMenu);
        surface.Child = content;
        return surface;
    }

    private static ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            Style = FindResource("App.Menu.ContextSurface")
        };
        AutomationProperties.SetAutomationId(menu, "feedback-context-menu");
        foreach (var item in CreateMenuItems())
        {
            menu.Items.Add(item);
        }

        return menu;
    }

    private static IReadOnlyList<FrameworkElement> CreateMenuItems() =>
    [
        new MenuItem
        {
            Header = "书籍操作",
            Style = FindResource("App.Menu.GroupHeader")
        },
        new MenuItem
        {
            Header = "打开详情",
            Style = FindResource("App.Menu.Item")
        },
        new Separator(),
        new MenuItem
        {
            Header = "删除书籍",
            Style = FindResource("App.Menu.DangerItem")
        },
        new Separator(),
        new MenuItem
        {
            Header = "Close",
            Style = FindResource("App.Menu.Item")
        }
    ];

    private static Border CreateProgressSurface()
    {
        var surface = CreateSurface("feedback-progress-surface");
        surface.Margin = new Thickness(0, 8, 8, 8);
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
        surface.Margin = new Thickness(8, 8, 0, 8);
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

    private static void SetSelectedItem(NavigationView navigation, object item)
    {
        typeof(NavigationView)
            .GetProperty(nameof(NavigationView.SelectedItem))?
            .GetSetMethod(nonPublic: true)?
            .Invoke(navigation, [item]);
    }

    private static Border CreateSurface(string automationId)
    {
        var surface = new Border { BorderThickness = new Thickness(1) };
        surface.SetResourceReference(Border.BackgroundProperty, "PrimarySurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "SubtleBorderBrush");
        surface.SetResourceReference(Border.CornerRadiusProperty, "CornerRadiusMedium");
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
        }.WithFeedbackResource(WpfTextBlock.ForegroundProperty, "PrimaryTextBrush");

    private static WpfTextBlock CreateBody(string text) =>
        new WpfTextBlock
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithFeedbackResource(WpfTextBlock.ForegroundProperty, "SecondaryTextBrush");
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
