using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryListComponentScene
{
    private static readonly Lazy<ResourceDictionary> ComponentResources = new(
        () => new ResourceDictionary
        {
            Source = new Uri(
                "/NovelSpeaker.StyleGallery;component/Resources/ComponentStyles.xaml",
                UriKind.RelativeOrAbsolute)
        });

    private static readonly string[] ComponentStates =
    [
        "default",
        "hover",
        "focus",
        "selected",
        "playing",
        "selected-playing",
        "selected-hover",
        "playing-hover",
        "disabled"
    ];

    public static FrameworkElement Create()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        AutomationProperties.SetAutomationId(scrollViewer, "list-components-scroll-viewer");

        var content = new StackPanel();
        content.Children.Add(CreateIntro());
        content.Children.Add(CreateComponentGallery());
        content.Children.Add(CreateVirtualizedSelectionFixture());
        scrollViewer.Content = content;
        return scrollViewer;
    }

    private static FrameworkElement CreateIntro()
    {
        var surface = new Border
        {
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16),
            Child = new StackPanel
            {
                Children =
                {
                    CreateText(
                        "List and card components",
                        "FontSizeSectionTitle",
                        "FontWeightSemiBold",
                        "PrimaryTextBrush"),
                    CreateText(
                        "每个组件拥有自己的内部结构、最小尺寸和可访问语义；选中、当前播放、Hover、Focus 与 Disabled 是独立状态。列表容器只负责虚拟化。",
                        "FontSizeSecondary",
                        "FontWeightRegular",
                        "SecondaryTextBrush")
                }
            }
        };
        surface.SetResourceReference(Border.BackgroundProperty, "PrimarySurfaceBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "SubtleBorderBrush");
        surface.BorderThickness = new Thickness(1);
        surface.SetResourceReference(Border.CornerRadiusProperty, "CornerRadiusMedium");
        AutomationProperties.SetAutomationId(surface, "list-components-intro");
        return surface;
    }

    private static FrameworkElement CreateComponentGallery()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddComponentColumn(grid, 0, "BookCard", CreateBookCard, "book-card");
        AddComponentColumn(grid, 1, "ListRow / SelectableRow", CreateListRowColumn, "list-row");
        AddComponentColumn(grid, 2, "SettingsRow / RuleListItem", CreateSettingsAndRuleColumn, "settings-row");

        var surface = CreateSurface("list-components-gallery");
        surface.Padding = new Thickness(16);
        var content = new StackPanel();
        content.Children.Add(grid);

        var emptyStates = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        emptyStates.Children.Add(CreateText("EmptyState", "FontSizeItemTitle", "FontWeightSemiBold", "PrimaryTextBrush"));
        foreach (var state in ComponentStates)
        {
            emptyStates.Children.Add(CreateEmptyState(state, $"empty-state-{state}"));
        }

        content.Children.Add(emptyStates);
        surface.Child = content;
        return surface;
    }

    private static void AddComponentColumn(
        Grid parent,
        int column,
        string title,
        Func<string, string, FrameworkElement> factory,
        string idPrefix)
    {
        var columnPanel = new StackPanel
        {
            Margin = new Thickness(column == 0 ? 0 : 8, 0, column == 2 ? 0 : 8, 0)
        };
        columnPanel.Children.Add(CreateText(title, "FontSizeItemTitle", "FontWeightSemiBold", "PrimaryTextBrush"));
        foreach (var state in ComponentStates)
        {
            columnPanel.Children.Add(factory(state, $"{idPrefix}-{state}"));
        }

        Grid.SetColumn(columnPanel, column);
        parent.Children.Add(columnPanel);
    }

    private static BookCard CreateBookCard(string state, string automationId) =>
        Configure(
            new BookCard(),
            "App.Component.BookCard",
            automationId,
            "BookCard · 长标题、作者和当前章节",
            state);

    private static ListRow CreateListRow(string state, string automationId) =>
        Configure(
            new ListRow(),
            "App.Component.ListRow",
            automationId,
            "ListRow · 章节目录项",
            state);

    private static SelectableRow CreateSelectableRow(string state, string automationId) =>
        Configure(
            new SelectableRow(),
            "App.Component.SelectableRow",
            automationId,
            "SelectableRow · 文件管理器式选择",
            state);

    private static SettingsRow CreateSettingsRow(string state, string automationId) =>
        Configure(
            new SettingsRow(),
            "App.Component.SettingsRow",
            automationId,
            "SettingsRow · 朗读章节标题",
            state);

    private static RuleListItem CreateRuleListItem(string state, string automationId) =>
        Configure(
            new RuleListItem(),
            "App.Component.RuleListItem",
            automationId,
            "RuleListItem · 章节规则",
            state);

    private static EmptyState CreateEmptyState(string state, string automationId) =>
        Configure(
            new EmptyState(),
            "App.Component.EmptyState",
            automationId,
            "EmptyState · 没有匹配内容",
            state);

    private static T Configure<T>(T component, string styleKey, string automationId, string name, string state)
        where T : GalleryComponentBase
    {
        component.Style = FindResource(styleKey) as Style
            ?? throw new InvalidOperationException($"Gallery component style '{styleKey}' was not found.");
        component.Margin = new Thickness(0, 8, 0, 0);
        AutomationProperties.SetAutomationId(component, automationId);
        AutomationProperties.SetName(component, name);
        component.ToolTip = name;
        component.IsSelected = state is "selected" or "selected-playing" or "selected-hover";
        component.IsCurrentPlayback = state is "playing" or "selected-playing" or "playing-hover";
        component.IsHoverPreview = state is "hover" or "selected-hover" or "playing-hover";
        component.IsFocusPreview = state == "focus";
        component.IsEnabled = state != "disabled";
        return component;
    }

    private static object? FindResource(string key) =>
        Application.Current?.TryFindResource(key) ?? ComponentResources.Value[key];

    private static FrameworkElement CreateListRowColumn(string state, string automationId)
    {
        var selectable = CreateSelectableRow(state, automationId.Replace("list-row", "selectable-row", StringComparison.Ordinal));
        var listRow = CreateListRow(state, automationId);
        var panel = new StackPanel { Children = { listRow, selectable } };
        AutomationProperties.SetAutomationId(panel, $"{automationId}-pair");
        return panel;
    }

    private static FrameworkElement CreateSettingsAndRuleColumn(string state, string automationId)
    {
        var settings = CreateSettingsRow(state, automationId);
        var rule = CreateRuleListItem(state, automationId.Replace("settings-row", "rule-list-item", StringComparison.Ordinal));
        var panel = new StackPanel { Children = { settings, rule } };
        AutomationProperties.SetAutomationId(panel, $"{automationId}-pair");
        return panel;
    }

    private static FrameworkElement CreateVirtualizedSelectionFixture()
    {
        var list = new ItemsControl
        {
            Height = 230,
            Margin = new Thickness(0, 16, 0, 16)
        };
        list.ItemsPanel = new ItemsPanelTemplate(
            new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        ScrollViewer.SetCanContentScroll(list, true);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        AutomationProperties.SetAutomationId(list, "list-components-virtualized-host");
        AutomationProperties.SetName(list, "Virtualized chapter selection list");

        for (var index = 0; index < 12; index++)
        {
            var row = Configure(
                new SelectableRow
                (
                    "Virtualized row",
                    $"第 {index + 1:00} 章 · 可回收容器 fixture"),
                "App.Component.SelectableRow",
                $"virtualized-selectable-row-{index + 1:00}",
                $"Virtualized row {index + 1}",
                index == 2 ? "selected" : "default");
            list.Items.Add(row);
        }

        var surface = CreateSurface("list-components-virtualized-surface");
        surface.Padding = new Thickness(16);
        surface.Child = new StackPanel
        {
            Children =
            {
                CreateText("Virtualized selection host", "FontSizeItemTitle", "FontWeightSemiBold", "PrimaryTextBrush"),
                CreateText(
                    "ItemsControl 只提供虚拟化；下面第 03 行仍保留自身 IsSelected 状态，容器不拥有选择模型。",
                    "FontSizeSecondary",
                    "FontWeightRegular",
                    "SecondaryTextBrush"),
                list
            }
        };
        return surface;
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

    private static WpfTextBlock CreateText(string text, string fontSizeKey, string fontWeightKey, string foregroundKey)
    {
        var block = new WpfTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };
        block.SetResourceReference(WpfTextBlock.FontFamilyProperty, "FontFamilyUi");
        block.SetResourceReference(WpfTextBlock.FontSizeProperty, fontSizeKey);
        block.SetResourceReference(WpfTextBlock.FontWeightProperty, fontWeightKey);
        block.SetResourceReference(WpfTextBlock.ForegroundProperty, foregroundKey);
        return block;
    }
}
