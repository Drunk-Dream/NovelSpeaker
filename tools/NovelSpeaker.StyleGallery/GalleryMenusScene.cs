using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MenuItem = System.Windows.Controls.MenuItem;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryMenusScene
{
    public static FrameworkElement Create()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        AutomationProperties.SetAutomationId(scrollViewer, "menus-scroll-viewer");

        var content = new StackPanel();
        content.Children.Add(CreateIntro());

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var inlineMenu = CreateInlineMenuSurface();
        Grid.SetColumn(inlineMenu, 0);
        columns.Children.Add(inlineMenu);

        var contextMenu = CreateContextMenuSurface();
        Grid.SetColumn(contextMenu, 1);
        columns.Children.Add(contextMenu);

        content.Children.Add(columns);
        scrollViewer.Content = content;
        return scrollViewer;
    }

    private static FrameworkElement CreateIntro()
    {
        var surface = CreateSurface("menus-intro");
        surface.Padding = new Thickness(16);
        surface.Child = new StackPanel
        {
            Children =
            {
                CreateTitle("Menu / ContextMenu 表面与菜单项"),
                CreateBody(
                    "App.Menu.Surface 与 App.Menu.ContextSurface 使用 Raised 表面和中等抬升；普通项保持中性，危险操作独立分组，Close 保持默认中性语义。"),
                CreateBody(
                    "所有菜单项通过 Provider.MenuItem 继承 WPF MenuItem 基础模板，NovelSpeaker 只提供表面、间距和语义状态。")
            }
        };
        return surface;
    }

    private static Border CreateInlineMenuSurface()
    {
        var surface = CreateSurface("menus-inline-surface");
        surface.Margin = new Thickness(0, 0, 8, 0);
        surface.Padding = new Thickness(12);
        var content = new StackPanel();
        content.Children.Add(CreateTitle("Menu"));
        content.Children.Add(CreateBody("内联 Menu 使用 App.Menu.Surface；分组标题不可交互，Danger 项与普通项保持不同语义。"));

        var menu = new Menu
        {
            Style = FindResource("App.Menu.Surface"),
            Margin = new Thickness(0, 10, 0, 0)
        };
        AutomationProperties.SetAutomationId(menu, "menus-inline-menu");
        foreach (var item in CreateMenuItems())
        {
            menu.Items.Add(item);
        }

        content.Children.Add(menu);
        surface.Child = content;
        return surface;
    }

    private static Border CreateContextMenuSurface()
    {
        var surface = CreateSurface("menus-context-surface");
        surface.Margin = new Thickness(8, 0, 0, 0);
        surface.Padding = new Thickness(12);
        var content = new StackPanel();
        content.Children.Add(CreateTitle("ContextMenu"));
        content.Children.Add(CreateBody("右键菜单使用 App.Menu.ContextSurface；危险删除独立于中性操作分组。"));

        var anchor = new WpfButton
        {
            Content = "右键打开 ContextMenu",
            Style = FindResource("App.Button.Secondary"),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0)
        };
        AutomationProperties.SetAutomationId(anchor, "menus-context-anchor");
        AutomationProperties.SetName(anchor, "打开示例 ContextMenu");
        anchor.ContextMenu = CreateContextMenu();
        content.Children.Add(anchor);
        surface.Child = content;
        return surface;
    }

    private static ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            Style = FindResource("App.Menu.ContextSurface")
        };
        AutomationProperties.SetAutomationId(menu, "menus-context-menu");
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

    private static Style FindResource(string key) =>
        System.Windows.Application.Current?.FindResource(key) as Style
        ?? throw new InvalidOperationException($"Gallery menu resource '{key}' was not found.");

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
        new WpfTextBlock()
        {
            Text = text,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithMenusResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Primary");

    private static WpfTextBlock CreateBody(string text) =>
        new WpfTextBlock()
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithMenusResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Secondary");
}

internal static class GalleryMenusResourceExtensions
{
    public static T WithMenusResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
