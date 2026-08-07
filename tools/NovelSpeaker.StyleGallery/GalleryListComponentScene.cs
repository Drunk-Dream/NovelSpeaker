using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryListComponentScene
{
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
        content.Children.Add(CreateSettingsFixture());
        content.Children.Add(CreateVirtualizedSelectionFixture());
        content.Children.Add(CreateStatusFixture());
        scrollViewer.Content = content;
        return scrollViewer;
    }

    private static FrameworkElement CreateIntro()
    {
        var intro = new AppSectionSurface
        {
            Header = "List and card fixtures",
            Description = "Gallery fixture 直接组合正式选择样式、Settings 控件和状态控件；页面或业务层拥有示例内容与状态。",
            Margin = new Thickness(0, 0, 0, 16),
            Content = CreateText(
                "卡片、列表行、可选行和空状态只展示正式资源族的组合方式，不向生产程序集引入固定文案或伪公共组件。",
                "App.Typography.Secondary")
        };
        AutomationProperties.SetAutomationId(intro, "list-components-intro");
        return intro;
    }

    private static FrameworkElement CreateComponentGallery()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        AddStateColumn(grid, 0, "Card fixture", CreateCard, "book-card");
        AddStateColumn(grid, 1, "List row fixture", CreateListRow, "list-row");
        AddStateColumn(grid, 2, "Selection fixture", CreateSelectionRow, "selectable-row");

        var surface = new Border
        {
            Padding = new Thickness(16),
            Child = grid,
            Style = FindStyle("App.Surface.Section")
        };
        AutomationProperties.SetAutomationId(surface, "list-components-gallery");
        return surface;
    }

    private static void AddStateColumn(
        Grid parent,
        int column,
        string title,
        Func<string, string, FrameworkElement> factory,
        string idPrefix)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(column == 0 ? 0 : 8, 0, column == 2 ? 0 : 8, 0)
        };
        panel.Children.Add(CreateText(title, "App.Typography.ItemTitle"));
        foreach (var state in ComponentStates)
        {
            panel.Children.Add(factory(state, $"{idPrefix}-{state}"));
        }

        Grid.SetColumn(panel, column);
        parent.Children.Add(panel);
    }

    private static Border CreateCard(string state, string automationId)
    {
        var card = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(12),
            Style = CreatePreviewStyle("App.Selection.CardItem"),
            DataContext = CreateState(state),
            IsEnabled = state != "disabled"
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cover = new Border
        {
            Width = 56,
            Height = 72,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = new WpfTextBlock
            {
                Text = "书\n封",
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Style = FindStyle("App.Typography.Secondary")
            }
        };
        cover.SetResourceReference(Border.BackgroundProperty, "App.Brush.Accent.Subtle");
        cover.SetResourceReference(Border.BorderBrushProperty, "App.Brush.Border.Subtle");
        Grid.SetColumn(cover, 0);
        grid.Children.Add(cover);

        var copy = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        var title = new WpfTextBlock
        {
            Text = "书名非常长的中文测试标题：在有限卡片宽度内自然省略",
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = "书名非常长的中文测试标题：在有限卡片宽度内自然省略",
            Style = FindStyle("App.Typography.ItemTitle")
        };
        AutomationProperties.SetAutomationId(title, "book-card-title");
        AutomationProperties.SetName(title, title.Text);
        copy.Children.Add(title);
        copy.Children.Add(CreateText("作者：固定脱敏 Gallery fixture", "App.Typography.Secondary"));
        copy.Children.Add(CreateText("当前章节 · 第 012 章", "App.Typography.Secondary"));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);

        var menu = new WpfButton
        {
            Content = new SymbolIcon { Symbol = SymbolRegular.MoreHorizontal24, Width = 18, Height = 18 },
            Style = FindStyle("App.Button.Icon"),
            ToolTip = "书籍更多操作"
        };
        AutomationProperties.SetName(menu, "BookCard more actions");
        AutomationProperties.SetAutomationId(menu, "book-card-more");
        Grid.SetColumn(menu, 2);
        grid.Children.Add(menu);

        card.Child = grid;
        SetAutomation(card, automationId, $"Card fixture {state}");
        return card;
    }

    private static Border CreateListRow(string state, string automationId)
    {
        var row = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Style = CreatePreviewStyle("App.Selection.CurrentItem"),
            DataContext = CreateState(state),
            IsEnabled = state != "disabled"
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon
        {
            Symbol = SymbolRegular.DocumentText24,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "App.Brush.Accent.Default");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);
        var copy = new StackPanel();
        copy.Children.Add(CreateText("章节目录 · 第 012 章", "App.Typography.ItemTitle"));
        copy.Children.Add(CreateText("长列表项目的辅助文案由场景 fixture 提供", "App.Typography.Secondary"));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var chevron = new SymbolIcon { Symbol = SymbolRegular.ChevronRight24, Width = 16, Height = 16 };
        chevron.SetResourceReference(SymbolIcon.ForegroundProperty, "App.Brush.Text.Tertiary");
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        row.Child = grid;
        SetAutomation(row, automationId, $"List row fixture {state}");
        return row;
    }

    private static Border CreateSelectionRow(string state, string automationId)
    {
        var row = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Style = CreatePreviewStyle("App.Selection.ListItem"),
            DataContext = new ListFixtureState
            {
                IsSelected = state is "selected" or "selected-playing" or "selected-hover",
                IsPreviewHover = state is "hover" or "selected-hover" or "playing-hover",
                IsPreviewFocus = state == "focus",
                Label = state
            },
            IsEnabled = state != "disabled"
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = SymbolRegular.TextBulletListSquare24, Width = 20, Height = 20 };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "App.Brush.Accent.Default");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);
        var copy = new StackPanel();
        copy.Children.Add(CreateText("章节标题识别", "App.Typography.ItemTitle"));
        copy.Children.Add(CreateText(@"正则：^\s*第\s*\d+章", "App.Typography.Secondary"));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var menu = new WpfButton
        {
            Content = new SymbolIcon { Symbol = SymbolRegular.MoreHorizontal24, Width = 18, Height = 18 },
            Style = FindStyle("App.Button.Icon"),
            ToolTip = "规则更多操作"
        };
        AutomationProperties.SetName(menu, "Rule fixture more actions");
        Grid.SetColumn(menu, 2);
        grid.Children.Add(menu);
        row.Child = grid;
        SetAutomation(row, automationId, $"Selection fixture {state}");
        return row;
    }

    private static FrameworkElement CreateSettingsFixture()
    {
        var group = new AppSettingsGroup
        {
            Header = "Formal settings controls",
            Description = "设置行、导航行和内容槽共享正式控件；示例开关、主题选择和按钮都由 Gallery fixture 创建。",
            Margin = new Thickness(0, 16, 0, 0)
        };
        SetAutomation(group, "list-components-settings-group", "Formal settings controls");
        group.Items.Add(new AppSettingsRow
        {
            Title = "朗读章节标题",
            Description = "开启后，每章正文前先朗读章节标题。",
            Content = new ToggleSwitch
            {
                IsChecked = true,
                Style = FindStyle("App.Input.ToggleSwitch.Compact")
            }
        });
        group.Items.Add(new AppSettingsRow
        {
            Title = "应用主题",
            Description = "选择跟随系统或固定主题。",
            Content = new ComboBox
            {
                ItemsSource = new[] { "跟随系统", "浅色", "深色" },
                SelectedIndex = 0,
                Style = FindStyle("App.Input.ComboBox.Standard")
            }
        });
        group.Items.Add(new AppSettingsNavigationRow
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
            Title = "更多设置",
            Description = "整行可点击，支持鼠标悬停和键盘焦点。",
            Command = new GalleryCommand()
        });
        return group;
    }

    private static FrameworkElement CreateVirtualizedSelectionFixture()
    {
        var list = new ItemsControl
        {
            Height = 230,
            Margin = new Thickness(0, 16, 0, 16),
            ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)))
        };
        ScrollViewer.SetCanContentScroll(list, true);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        SetAutomation(list, "list-components-virtualized-host", "Virtualized chapter selection list");

        for (var index = 0; index < 12; index++)
        {
            var item = new Border
            {
                Margin = new Thickness(0, 0, 0, 4),
                Style = FindStyle("App.Selection.ListItem"),
                DataContext = new ListFixtureState
                {
                    IsSelected = index == 2,
                    Label = $"第 {index + 1:00} 章"
                },
                Child = CreateText($"第 {index + 1:00} 章 · 可回收容器 fixture", "App.Typography.Body")
            };
            SetAutomation(item, $"virtualized-selectable-row-{index + 1:00}", $"Virtualized row {index + 1}");
            list.Items.Add(item);
        }

        var surface = new Border
        {
            Padding = new Thickness(16),
            Style = FindStyle("App.Surface.Section"),
            Child = new StackPanel
            {
                Children =
                {
                    CreateText("Virtualized selection host", "App.Typography.ItemTitle"),
                    CreateText("ItemsControl 只提供虚拟化；第 03 行的选择状态仍由 Gallery fixture 数据拥有。", "App.Typography.Secondary"),
                    list
                }
            }
        };
        SetAutomation(surface, "list-components-virtualized-surface", "Virtualized selection surface");
        return surface;
    }

    private static AppStatusView CreateStatusFixture()
    {
        var retry = new WpfButton
        {
            Content = "重试",
            Style = FindStyle("App.Button.Secondary")
        };
        SetAutomation(retry, "list-components-status-retry", "重试加载");
        var status = new AppStatusView
        {
            Status = AppStatusKind.NoResult,
            Icon = SymbolRegular.Search24,
            Title = "没有匹配的书籍",
            Description = "清空搜索或导入一本新书后，这里会显示内容。",
            PrimaryAction = retry,
            Margin = new Thickness(0, 16, 0, 0)
        };
        SetAutomation(status, "list-components-empty-state", "没有匹配的书籍");
        return status;
    }

    private static ListFixtureState CreateState(string state) =>
        new()
        {
            IsSelected = state is "selected" or "selected-playing" or "selected-hover",
            IsCurrent = state is "playing" or "selected-playing" or "playing-hover",
            IsPreviewHover = state is "hover" or "selected-hover" or "playing-hover",
            IsPreviewFocus = state == "focus",
            Label = state
        };

    private static Style CreatePreviewStyle(string baseStyleKey)
    {
        var style = new Style(typeof(Border), FindStyle(baseStyleKey));
        AddPreviewTrigger(
            style,
            nameof(ListFixtureState.IsCurrent),
            new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("App.Brush.Accent.Default")));
        AddPreviewTrigger(
            style,
            nameof(ListFixtureState.IsPreviewHover),
            new Setter(Border.BackgroundProperty, new DynamicResourceExtension("App.Brush.Surface.Secondary")));
        AddPreviewTrigger(
            style,
            nameof(ListFixtureState.IsPreviewFocus),
            new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("App.Brush.Focus")),
            new Setter(Border.BorderThicknessProperty, new Thickness(2)));
        return style;
    }

    private static void AddPreviewTrigger(Style style, string propertyName, params Setter[] setters)
    {
        var trigger = new DataTrigger
        {
            Binding = new Binding(propertyName),
            Value = true
        };
        foreach (var setter in setters)
        {
            trigger.Setters.Add(setter);
        }

        style.Triggers.Add(trigger);
    }

    private static WpfTextBlock CreateText(string text, string styleKey) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = FindStyle(styleKey),
            Margin = new Thickness(0, 0, 0, 4)
        };

    private static Style FindStyle(string key) =>
        System.Windows.Application.Current?.FindResource(key) as Style
        ?? throw new InvalidOperationException($"Gallery resource '{key}' was not found.");

    private static T SetAutomation<T>(T element, string automationId, string name)
        where T : FrameworkElement
    {
        AutomationProperties.SetAutomationId(element, automationId);
        AutomationProperties.SetName(element, name);
        return element;
    }

    private sealed class ListFixtureState
    {
        public bool IsSelected { get; init; }

        public bool IsCurrent { get; init; }

        public bool IsPreviewHover { get; init; }

        public bool IsPreviewFocus { get; init; }

        public string Label { get; init; } = string.Empty;
    }

    private sealed class GalleryCommand : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
