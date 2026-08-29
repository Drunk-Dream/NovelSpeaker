using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using Wpf.Ui.Controls;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryNavigationScene
{
    private static readonly DependencyProperty GalleryNavigationVisualStateProperty =
        DependencyProperty.RegisterAttached(
            "GalleryNavigationVisualState",
            typeof(string),
            typeof(GalleryNavigationScene),
            new PropertyMetadata("Default"));

    public static FrameworkElement Create()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        AutomationProperties.SetAutomationId(scrollViewer, "navigation-scroll-viewer");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var primary = CreatePrimaryNavigationSurface();
        Grid.SetColumn(primary, 0);
        grid.Children.Add(primary);

        var settings = CreateSettingsNavigationSurface();
        Grid.SetColumn(settings, 1);
        grid.Children.Add(settings);

        scrollViewer.Content = grid;
        return scrollViewer;
    }

    private static Border CreatePrimaryNavigationSurface()
    {
        var surface = CreateSurface("navigation-primary-surface");
        surface.Margin = new Thickness(0, 0, 8, 0);
        surface.Padding = new Thickness(12);
        var content = new StackPanel();
        content.Children.Add(CreateTitle("App.Navigation.Entry"));
        content.Children.Add(CreateBody("显式扩展 Provider.NavigationViewItem；选中、Hover、键盘 Focus 与 Disabled 状态保持可见。"));

        var navigation = new NavigationView
        {
            Height = 210,
            IsPaneOpen = true,
            IsPaneToggleVisible = true,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            OpenPaneLength = 218,
            CompactPaneLength = 218,
            Margin = new Thickness(0, 10, 0, 0)
        };
        AutomationProperties.SetAutomationId(navigation, "navigation-primary-view");
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

    private static Border CreateSettingsNavigationSurface()
    {
        var surface = CreateSurface("navigation-settings-surface");
        surface.Margin = new Thickness(8, 0, 0, 0);
        surface.Padding = new Thickness(12);
        var content = new StackPanel();
        content.Children.Add(CreateTitle("App.Navigation.SettingsEntry"));
        content.Children.Add(CreateBody("设置导航行使用图标 + 标题 + Chevron 的整行入口；Hover、Pressed、键盘 Focus 与 Disabled 状态由 Provider Button 模板与样式触发共同表达。"));

        foreach (var (label, symbol, state, automationId) in new[]
                 {
                     ("常规设置", SymbolRegular.Settings24, "Default", "settings-entry-general"),
                     ("正则替换", SymbolRegular.DocumentText24, "Default", "settings-entry-regex-replacement"),
                     ("播放设置", SymbolRegular.PlayCircle24, "Hover", "settings-entry-playback"),
                     ("外观", SymbolRegular.DarkTheme24, "Focus", "settings-entry-appearance"),
                     ("诊断与关于", SymbolRegular.Info24, "Default", "settings-entry-diagnostics"),
                     ("暂不可用", SymbolRegular.Warning24, "Disabled", "settings-entry-disabled")
                 })
        {
            var entry = CreateSettingsEntry(label, symbol, state, automationId);
            content.Children.Add(entry);
        }

        surface.Child = content;
        return surface;
    }

    private static WpfButton CreateSettingsEntry(
        string label,
        SymbolRegular symbol,
        string state,
        string automationId)
    {
        var entry = new WpfButton
        {
            Style = FindResource("App.Navigation.SettingsEntry"),
            Margin = new Thickness(0, 0, 0, 4),
            ToolTip = label,
            Content = CreateEntryContent(label, symbol)
        };
        AutomationProperties.SetAutomationId(entry, automationId);
        AutomationProperties.SetName(entry, label);
        entry.SetValue(GalleryNavigationVisualStateProperty, state);

        var baseStyle = entry.Style
            ?? throw new InvalidOperationException($"Settings entry style was not resolved for '{label}'.");
        var previewStyle = new Style(baseStyle.TargetType, baseStyle);
        var trigger = new Trigger
        {
            Property = GalleryNavigationVisualStateProperty,
            Value = state
        };
        if (state == "Hover")
        {
            trigger.Setters.Add(new Setter(
                Control.BackgroundProperty,
                new DynamicResourceExtension("App.Brush.Interaction.Surface.Hover")));
        }
        else if (state == "Focus")
        {
            trigger.Setters.Add(new Setter(
                Control.BorderBrushProperty,
                new DynamicResourceExtension("App.Brush.Focus")));
        }

        if (state == "Disabled")
        {
            entry.IsEnabled = false;
        }

        if (state is not ("Default" or "Disabled"))
        {
            previewStyle.Triggers.Add(trigger);
        }

        entry.Style = previewStyle;
        return entry;
    }

    private static Grid CreateEntryContent(string label, SymbolRegular symbol)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new SymbolIcon { Symbol = symbol, Width = 20, Height = 20, Margin = new Thickness(0, 0, 14, 0) };
        icon.SetResourceReference(TextElement.ForegroundProperty, "App.Brush.Text.Primary");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var title = new WpfTextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        title.SetResourceReference(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Primary");
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var chevron = new SymbolIcon
        {
            Symbol = SymbolRegular.ChevronRight24,
            Width = 16,
            Height = 16,
            Margin = new Thickness(16, 0, 0, 0)
        };
        chevron.SetResourceReference(TextElement.ForegroundProperty, "App.Brush.Text.Secondary");
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        return grid;
    }

    private static Style FindResource(string key) =>
        System.Windows.Application.Current?.FindResource(key) as Style
        ?? throw new InvalidOperationException($"Gallery navigation resource '{key}' was not found.");

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
        }.WithNavigationResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Primary");

    private static WpfTextBlock CreateBody(string text) =>
        new WpfTextBlock()
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithNavigationResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Secondary");
}

internal static class GalleryNavigationResourceExtensions
{
    public static T WithNavigationResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
