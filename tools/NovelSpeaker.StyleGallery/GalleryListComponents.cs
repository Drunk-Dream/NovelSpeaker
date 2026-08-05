using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Wpf.Ui.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.StyleGallery;

public abstract class GalleryComponentBase : ContentControl
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected),
            typeof(bool),
            typeof(GalleryComponentBase),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsCurrentPlaybackProperty =
        DependencyProperty.Register(
            nameof(IsCurrentPlayback),
            typeof(bool),
            typeof(GalleryComponentBase),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsHoverPreviewProperty =
        DependencyProperty.Register(
            nameof(IsHoverPreview),
            typeof(bool),
            typeof(GalleryComponentBase),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsFocusPreviewProperty =
        DependencyProperty.Register(
            nameof(IsFocusPreview),
            typeof(bool),
            typeof(GalleryComponentBase),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(GalleryComponentBase),
            new FrameworkPropertyMetadata(new CornerRadius(10)));

    protected GalleryComponentBase()
    {
        Focusable = true;
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsCurrentPlayback
    {
        get => (bool)GetValue(IsCurrentPlaybackProperty);
        set => SetValue(IsCurrentPlaybackProperty, value);
    }

    public bool IsHoverPreview
    {
        get => (bool)GetValue(IsHoverPreviewProperty);
        set => SetValue(IsHoverPreviewProperty, value);
    }

    public bool IsFocusPreview
    {
        get => (bool)GetValue(IsFocusPreviewProperty);
        set => SetValue(IsFocusPreviewProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }
}

public sealed class BookCard : GalleryComponentBase
{
    public BookCard()
    {
        Content = GalleryListComponentContent.CreateBookCard();
    }
}

public sealed class ListRow : GalleryComponentBase
{
    public ListRow()
        : this("ListRow", "章节目录 · 第 012 章")
    {
    }

    public ListRow(string title, string metadata)
    {
        Content = GalleryListComponentContent.CreateListRow(title, metadata);
    }
}

public sealed class SelectableRow : GalleryComponentBase
{
    public SelectableRow()
        : this("SelectableRow", "可多选的缓存章节")
    {
    }

    public SelectableRow(string title, string metadata)
    {
        Content = GalleryListComponentContent.CreateListRow(title, metadata);
    }
}

public sealed class SettingsRow : GalleryComponentBase
{
    public SettingsRow()
    {
        Content = GalleryListComponentContent.CreateSettingsRow();
    }
}

public sealed class RuleListItem : GalleryComponentBase
{
    public RuleListItem()
    {
        Content = GalleryListComponentContent.CreateRuleListItem();
    }
}

public sealed class EmptyState : GalleryComponentBase
{
    public EmptyState()
    {
        Content = GalleryListComponentContent.CreateEmptyState();
    }
}

internal static class GalleryListComponentContent
{
    public static Grid CreateBookCard()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cover = new Border
        {
            Width = 72,
            Height = 96,
            CornerRadius = new CornerRadius(8),
            Child = new WpfTextBlock
            {
                Text = "书\n封",
                FontSize = 18,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        cover.SetResourceReference(Border.BackgroundProperty, "AccentSubtleBrush");
        cover.SetResourceReference(Border.BorderBrushProperty, "SubtleBorderBrush");
        cover.BorderThickness = new Thickness(1);
        Grid.SetColumn(cover, 0);
        grid.Children.Add(cover);

        var title = CreateText(
            "书名非常长的中文测试标题：在有限卡片宽度内自然省略",
            "FontSizeItemTitle",
            "FontWeightSemiBold",
            "PrimaryTextBrush");
        title.TextWrapping = TextWrapping.NoWrap;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = title.Text;
        AutomationProperties.SetAutomationId(title, "book-card-title");
        AutomationProperties.SetName(title, title.Text);
        var copy = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        copy.Children.Add(title);
        copy.Children.Add(CreateText("作者：固定脱敏 fixture", "FontSizeSecondary", "FontWeightRegular", "SecondaryTextBrush"));
        copy.Children.Add(CreateText("当前章节 · 第 012 章", "FontSizeSecondary", "FontWeightRegular", "SecondaryTextBrush"));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);

        var menu = new WpfButton
        {
            Content = new SymbolIcon { Symbol = SymbolRegular.MoreHorizontal24, Width = 20, Height = 20 },
            Style = FindApplicationStyle("App.Button.Icon"),
            ToolTip = "书籍更多操作"
        };
        AutomationProperties.SetName(menu, "BookCard more actions");
        AutomationProperties.SetAutomationId(menu, "book-card-more");
        Grid.SetColumn(menu, 2);
        grid.Children.Add(menu);
        return grid;
    }

    public static Grid CreateListRow(string title, string metadata)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon
        {
            Symbol = SymbolRegular.DocumentText24,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center
        };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "AccentBrush");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);
        var copy = new StackPanel();
        var titleBlock = CreateText(title, "FontSizeItemTitle", "FontWeightSemiBold", "PrimaryTextBrush");
        titleBlock.TextWrapping = TextWrapping.NoWrap;
        titleBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        titleBlock.ToolTip = title;
        copy.Children.Add(titleBlock);
        copy.Children.Add(CreateText(metadata, "FontSizeSecondary", "FontWeightRegular", "SecondaryTextBrush"));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var chevron = new SymbolIcon { Symbol = SymbolRegular.ChevronRight24, Width = 16, Height = 16 };
        chevron.SetResourceReference(SymbolIcon.ForegroundProperty, "TertiaryTextBrush");
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        return grid;
    }

    public static Grid CreateSettingsRow()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(CreateText("朗读章节标题", "FontSizeItemTitle", "FontWeightSemiBold", "PrimaryTextBrush"));
        copy.Children.Add(CreateText("开启后每章正文前先朗读章节标题。", "FontSizeSecondary", "FontWeightRegular", "SecondaryTextBrush"));
        Grid.SetColumn(copy, 0);
        grid.Children.Add(copy);
        var toggle = new ToggleSwitch
        {
            IsChecked = true,
            Style = FindApplicationStyle("App.Input.ToggleSwitch.Compact"),
            ToolTip = "朗读章节标题"
        };
        AutomationProperties.SetName(toggle, "SettingsRow read chapter title");
        AutomationProperties.SetAutomationId(toggle, "settings-row-toggle");
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    public static Grid CreateRuleListItem()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = new SymbolIcon { Symbol = SymbolRegular.TextBulletListSquare24, Width = 20, Height = 20 };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "AccentBrush");
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);
        var copy = new StackPanel();
        copy.Children.Add(CreateText("章节标题识别", "FontSizeItemTitle", "FontWeightSemiBold", "PrimaryTextBrush"));
        copy.Children.Add(CreateText(@"正则：^\s*第\s*\d+章", "FontSizeSecondary", "FontWeightRegular", "SecondaryTextBrush"));
        Grid.SetColumn(copy, 1);
        grid.Children.Add(copy);
        var menu = new WpfButton
        {
            Content = new SymbolIcon { Symbol = SymbolRegular.MoreHorizontal24, Width = 18, Height = 18 },
            Style = FindApplicationStyle("App.Button.Icon"),
            ToolTip = "规则更多操作"
        };
        AutomationProperties.SetName(menu, "RuleListItem more actions");
        Grid.SetColumn(menu, 2);
        grid.Children.Add(menu);
        return grid;
    }

    public static StackPanel CreateEmptyState()
    {
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = new SymbolIcon { Symbol = SymbolRegular.Library24, Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, "TertiaryTextBrush");
        content.Children.Add(icon);
        content.Children.Add(CreateText("没有匹配的书籍", "FontSizeItemTitle", "FontWeightSemiBold", "PrimaryTextBrush"));
        content.Children.Add(CreateText("清空搜索或导入一本新书后，这里会显示内容。", "FontSizeSecondary", "FontWeightRegular", "SecondaryTextBrush"));
        var action = new WpfButton
        {
            Content = "清空搜索",
            Style = FindApplicationStyle("App.Button.Secondary"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            ToolTip = "清空当前搜索"
        };
        AutomationProperties.SetName(action, "EmptyState clear search");
        content.Children.Add(action);
        return content;
    }

    private static Style? FindApplicationStyle(string key) =>
        Application.Current?.TryFindResource(key) as Style;

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
