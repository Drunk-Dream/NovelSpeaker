using System.Reflection;
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
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        columns.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var inlineMenu = CreateInlineMenuSurface();
        Grid.SetRow(inlineMenu, 0);
        Grid.SetColumnSpan(inlineMenu, 2);
        columns.Children.Add(inlineMenu);

        var contextMenu = CreateContextMenuSurface();
        Grid.SetRow(contextMenu, 1);
        Grid.SetColumnSpan(contextMenu, 2);
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
                    "App.Menu.Surface 与 App.Menu.ContextSurface 使用 Raised 表面和中等抬升；普通、Hover、Pressed、Checked、Disabled 与 Danger 状态由同一菜单项样式拥有。"),
                CreateBody(
                    "所有菜单项通过 Provider.MenuItem 继承 WPF MenuItem 基础模板；分隔线由独立 App.Menu.Separator 绘制，并与文字列对齐。")
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
        content.Children.Add(CreateBody("内联 Menu 使用 App.Menu.Surface；状态示例覆盖普通、Hover、Pressed、Checked、Disabled、Danger，以及多组独立分隔线。"));

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
        content.Children.Add(CreateBody("右键菜单使用 App.Menu.ContextSurface；危险删除独立于中性操作分组，分隔线不受相邻项禁用状态影响。"));

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
        content.Children.Add(CreateSeparatorPreview());
        surface.Child = content;
        return surface;
    }

    private static StackPanel CreateSeparatorPreview()
    {
        var preview = new StackPanel
        {
            Margin = new Thickness(0, 10, 0, 0)
        };
        preview.Children.Add(CreateBody("独立 Separator 与相邻 Disabled/Danger 项保持相同长度和不透明度："));
        preview.Children.Add(new TextBlock
        {
            Text = "普通操作",
            Margin = new Thickness(12, 0, 12, 0)
        }.WithMenusResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Primary"));
        preview.Children.Add(CreatePreviewSeparator());
        preview.Children.Add(new TextBlock
        {
            Text = "危险操作",
            Margin = new Thickness(12, 0, 12, 0)
        }.WithMenusResource(WpfTextBlock.ForegroundProperty, "App.Brush.Danger"));
        preview.Children.Add(CreatePreviewSeparator());
        preview.Children.Add(new TextBlock
        {
            Text = "关闭",
            Margin = new Thickness(12, 0, 12, 0)
        }.WithMenusResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Primary"));
        return preview;
    }

    private static Separator CreatePreviewSeparator()
    {
        var separator = new Separator
        {
            Style = FindResource("App.Menu.Separator"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 200
        };
        return separator.WithMenusAutomationId("menus-preview-separator");
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

    private static IReadOnlyList<FrameworkElement> CreateMenuItems()
    {
        var highlighted = new MenuItem
        {
            Header = "Hover 预览",
            Style = FindResource("App.Menu.Item")
        };
        SetHighlightedValue(highlighted, true);

        var pressed = new MenuItem
        {
            Header = "Pressed 预览",
            Style = FindResource("App.Menu.Item")
        };
        SetPressedValue(pressed, true);

        var checkedItem = new MenuItem
        {
            Header = "Checked",
            IsCheckable = true,
            IsChecked = true,
            Style = FindResource("App.Menu.Item")
        };

        var checkedHighlighted = new MenuItem
        {
            Header = "Checked + Hover",
            IsCheckable = true,
            IsChecked = true,
            Style = FindResource("App.Menu.Item")
        };
        SetHighlightedValue(checkedHighlighted, true);

        var checkedPressed = new MenuItem
        {
            Header = "Checked + Pressed",
            IsCheckable = true,
            IsChecked = true,
            Style = FindResource("App.Menu.Item")
        };
        SetPressedValue(checkedPressed, true);
        SetHighlightedValue(checkedPressed, true);

        var disabled = new MenuItem
        {
            Header = "Disabled",
            IsEnabled = false,
            Style = FindResource("App.Menu.Item")
        };

        var danger = new MenuItem
        {
            Header = "删除书籍",
            Style = FindResource("App.Menu.DangerItem")
        };
        SetHighlightedValue(danger, true);

        return
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
            highlighted,
            pressed,
            checkedItem,
            checkedHighlighted,
            checkedPressed,
            disabled,
            new Separator { Style = FindResource("App.Menu.Separator") },
            danger,
            new Separator { Style = FindResource("App.Menu.Separator") },
            new MenuItem
            {
                Header = "Close",
                Style = FindResource("App.Menu.Item")
            }
        ];
    }

    private static void SetHighlightedValue(MenuItem item, bool value)
    {
        var property = typeof(MenuItem).GetProperty(
            nameof(MenuItem.IsHighlighted),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var setter = property?.GetSetMethod(nonPublic: true)
            ?? throw new InvalidOperationException("WPF MenuItem.IsHighlighted setter is unavailable.");
        setter.Invoke(item, [value]);
    }

    private static void SetPressedValue(MenuItem item, bool value)
    {
        var wpfVersion = typeof(MenuItem).Assembly.GetName().Version;
        if (wpfVersion?.Major < 10)
        {
            throw new InvalidOperationException(
                $"Pressed-state gallery adapter requires WPF 10+; actual version: {wpfVersion?.ToString() ?? "unknown"}.");
        }

        var keyField = typeof(MenuItem).GetField(
            "IsPressedPropertyKey",
            BindingFlags.Static | BindingFlags.NonPublic);
        var key = keyField?.GetValue(null) as DependencyPropertyKey
            ?? throw new InvalidOperationException("WPF MenuItem.IsPressedPropertyKey is unavailable.");
        item.SetValue(key, value);
    }

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
    public static T WithMenusAutomationId<T>(this T element, string automationId)
        where T : FrameworkElement
    {
        AutomationProperties.SetAutomationId(element, automationId);
        return element;
    }

    public static T WithMenusResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
