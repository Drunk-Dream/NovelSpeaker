using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.StyleGallery;

internal static class GallerySelectionScene
{
    private static readonly DependencyProperty GallerySelectionVisualStateProperty =
        DependencyProperty.RegisterAttached(
            "GallerySelectionVisualState",
            typeof(string),
            typeof(GallerySelectionScene),
            new PropertyMetadata("Default"));

    private static readonly string[] SelectionStyleVariants =
    [
        "ListItem",
        "CardItem",
        "CurrentItem",
        "DropTarget",
        "MultiSelectItem"
    ];

    public static FrameworkElement Create()
    {
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false
        };
        AutomationProperties.SetAutomationId(scrollViewer, "selection-scroll-viewer");

        var content = new StackPanel();
        content.Children.Add(CreateIntro());
        content.Children.Add(CreateSelectionMatrix());
        content.Children.Add(CreateVirtualizedRecyclingHost());
        scrollViewer.Content = content;
        return scrollViewer;
    }

    private static FrameworkElement CreateIntro()
    {
        var surface = CreateSurface("selection-intro");
        surface.Padding = new Thickness(16);
        surface.Child = new StackPanel
        {
            Children =
            {
                CreateTitle("状态矩阵与数据事实"),
                CreateBody(
                    "App.Selection 样式只表达容器状态：默认、Hover、键盘 Focus、Disabled、Selected、Current、MultiSelect 与 DropTarget。状态事实来自数据项或列表选择模型，虚拟化回收容器不会保存业务事实。"),
                CreateBody(
                    "Hover 使用背景状态层，Selected/Current/MultiSelect/DropTarget 使用强调色背景与边框；组合状态下边框保持状态可见，Hover 背景叠加其上。")
            }
        };
        return surface;
    }

    private static FrameworkElement CreateSelectionMatrix()
    {
        var surface = CreateSurface("selection-state-matrix");
        surface.Padding = new Thickness(16);

        var table = new Grid();
        AutomationProperties.SetAutomationId(table, "selection-state-matrix-grid");
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        foreach (var _ in SelectionPreviewStates)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var _ in SelectionPreviewStates)
        {
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddHeader(table, "Variant", 0);
        for (var index = 0; index < SelectionPreviewStates.Length; index++)
        {
            AddHeader(table, SelectionPreviewStates[index], index + 1);
        }

        for (var variantIndex = 0; variantIndex < SelectionStyleVariants.Length; variantIndex++)
        {
            var variant = SelectionStyleVariants[variantIndex];
            var label = CreateText($"{variant}", 13, FontWeights.SemiBold);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(0, 8, 12, 8);
            Grid.SetRow(label, variantIndex + 1);
            Grid.SetColumn(label, 0);
            table.Children.Add(label);

            for (var stateIndex = 0; stateIndex < SelectionPreviewStates.Length; stateIndex++)
            {
                var state = SelectionPreviewStates[stateIndex];
                var row = CreateSelectionPreview(variant, state);
                Grid.SetRow(row, variantIndex + 1);
                Grid.SetColumn(row, stateIndex + 1);
                table.Children.Add(row);
            }
        }

        surface.Child = table;
        return surface;
    }

    private static FrameworkElement CreateVirtualizedRecyclingHost()
    {
        var items = new List<SelectionFixtureItem>();
        for (var index = 0; index < 12; index++)
        {
            var item = new SelectionFixtureItem(
                $"第 {index + 1:00} 章",
                $"缓存条目 {index + 1:00} · 可回收容器 fixture",
                $"selection-virtualized-row-{index + 1:00}",
                $"Virtualized row {index + 1}");
            if (index == 2)
            {
                item.IsSelected = true;
                item.IsCurrent = true;
            }

            if (index == 7)
            {
                item.IsDropTarget = true;
            }

            items.Add(item);
        }

        var list = new ListBox
        {
            ItemsSource = items,
            Height = 240,
            Background = null,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 16, 0, 0)
        };
        ScrollViewer.SetCanContentScroll(list, true);
        VirtualizingPanel.SetIsVirtualizing(list, true);
        VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
        AutomationProperties.SetAutomationId(list, "selection-virtualized-host");
        AutomationProperties.SetName(list, "Virtualized selection facts list");
        list.ItemContainerStyle = CreateRecyclingContainerStyle();
        list.ItemTemplate = CreateVirtualizedRowTemplate();

        var surface = CreateSurface("selection-virtualized-surface");
        surface.Padding = new Thickness(16);
        surface.Child = new StackPanel
        {
            Children =
            {
                CreateTitle("Virtualized recycling keeps data facts"),
                CreateBody(
                    "第 03 行同时选中与当前播放，第 08 行是 DropTarget；滚动回收容器后状态仍跟随数据项。"),
                list
            }
        };
        return surface;
    }

    private static Style CreateRecyclingContainerStyle()
    {
        var containerStyle = new Style(typeof(ListBoxItem));
        containerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        containerStyle.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0, 0, 0, 4)));
        containerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        containerStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        containerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        containerStyle.Setters.Add(new Setter(Control.TemplateProperty, CreateContentPresenterTemplate()));
        return containerStyle;
    }

    private static ControlTemplate CreateContentPresenterTemplate()
    {
        var template = new ControlTemplate(typeof(ListBoxItem));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        template.VisualTree = presenter;
        return template;
    }

    private static DataTemplate CreateVirtualizedRowTemplate()
    {
        var template = new DataTemplate(typeof(SelectionFixtureItem));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.StyleProperty, FindResource("App.Selection.ListItem"));
        border.SetValue(Border.MinWidthProperty, 160.0);
        border.SetBinding(AutomationProperties.AutomationIdProperty, new Binding(nameof(SelectionFixtureItem.RowId)));
        border.SetBinding(AutomationProperties.NameProperty, new Binding(nameof(SelectionFixtureItem.AutomationName)));

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(WpfTextBlock));
        title.SetBinding(WpfTextBlock.TextProperty, new Binding(nameof(SelectionFixtureItem.Title)));
        title.SetValue(WpfTextBlock.FontSizeProperty, 14.0);
        title.SetValue(WpfTextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(WpfTextBlock.TextWrappingProperty, TextWrapping.Wrap);
        title.SetValue(WpfTextBlock.MarginProperty, new Thickness(0, 0, 0, 2));
        title.SetResourceReference(WpfTextBlock.FontFamilyProperty, "App.Text.Family.Ui");
        title.SetValue(WpfTextBlock.StyleProperty, FindResource("App.Selection.Content.Primary"));
        stack.AppendChild(title);

        var metadata = new FrameworkElementFactory(typeof(WpfTextBlock));
        metadata.SetBinding(WpfTextBlock.TextProperty, new Binding(nameof(SelectionFixtureItem.Metadata)));
        metadata.SetValue(WpfTextBlock.FontSizeProperty, 12.0);
        metadata.SetValue(WpfTextBlock.TextWrappingProperty, TextWrapping.Wrap);
        metadata.SetResourceReference(WpfTextBlock.FontFamilyProperty, "App.Text.Family.Ui");
        metadata.SetValue(WpfTextBlock.StyleProperty, FindResource("App.Selection.Content.Secondary"));
        stack.AppendChild(metadata);

        border.AppendChild(stack);
        template.VisualTree = border;
        return template;
    }

    private static Border CreateSelectionPreview(string variant, string state)
    {
        var item = new SelectionFixtureItem(
            state,
            "容器状态 fixture",
            $"selection-{variant.ToLowerInvariant()}-{state.ToLowerInvariant()}",
            $"App.Selection.{variant} {state}");
        switch (state)
        {
            case "Selected":
                item.IsSelected = true;
                break;
            case "Current":
                item.IsCurrent = true;
                break;
            case "DropTarget":
                item.IsDropTarget = true;
                break;
        }

        var row = CreateRow(
            item,
            $"App.Selection.{variant}",
            $"selection-{variant.ToLowerInvariant()}-{state.ToLowerInvariant()}",
            $"App.Selection.{variant} {state}");
        row.SetValue(GallerySelectionVisualStateProperty, state);

        var baseStyle = row.Style
            ?? throw new InvalidOperationException($"Selection style was not resolved for '{variant}'.");
        var previewStyle = new Style(typeof(Border), baseStyle);
        var trigger = new Trigger
        {
            Property = GallerySelectionVisualStateProperty,
            Value = state
        };
        if (state == "Hover")
        {
            trigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                new DynamicResourceExtension("App.Brush.Interaction.Surface.Hover")));
        }
        else if (state == "Focus")
        {
            trigger.Setters.Add(new Setter(
                Border.BorderBrushProperty,
                new DynamicResourceExtension("App.Brush.Focus")));
        }
        else if (state == "MultiSelect" && variant == "MultiSelectItem")
        {
            trigger.Setters.Add(new Setter(
                Border.BackgroundProperty,
                new DynamicResourceExtension("App.Brush.Accent.Subtle")));
            trigger.Setters.Add(new Setter(
                Border.BorderBrushProperty,
                new DynamicResourceExtension("App.Brush.Accent.Default")));
        }

        if (state == "Disabled")
        {
            row.IsEnabled = false;
        }

        if (state is not ("Default" or "Disabled"))
        {
            previewStyle.Triggers.Add(trigger);
        }

        row.Style = previewStyle;
        return row;
    }

    private static Border CreateRow(
        SelectionFixtureItem item,
        string styleKey,
        string automationId,
        string name)
    {
        var border = new Border
        {
            Style = FindResource(styleKey),
            DataContext = item,
            MinWidth = 0,
            Margin = new Thickness(0, 0, 8, 8)
        };
        var content = new StackPanel();
        content.Children.Add(CreateSelectionText(item.Title, 14, FontWeights.SemiBold, "App.Selection.Content.Primary"));
        content.Children.Add(CreateSelectionText(item.Metadata, 12, FontWeights.Regular, "App.Selection.Content.Secondary"));
        border.Child = content;
        AutomationProperties.SetAutomationId(border, automationId);
        AutomationProperties.SetName(border, name);
        return border;
    }

    private static void AddHeader(Grid table, string text, int column)
    {
        var header = CreateText(text, 12, FontWeights.SemiBold);
        header.Margin = new Thickness(0, 0, 12, 8);
        Grid.SetColumn(header, column);
        table.Children.Add(header);
    }

    private static Style FindResource(string key) =>
        System.Windows.Application.Current?.FindResource(key) as Style
        ?? throw new InvalidOperationException($"Gallery selection resource '{key}' was not found.");

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
        }.WithSelectionResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Primary");

    private static WpfTextBlock CreateBody(string text) =>
        new WpfTextBlock()
        {
            Text = text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        }.WithSelectionResource(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Secondary");

    private static WpfTextBlock CreateText(string text, double fontSize, FontWeight fontWeight)
    {
        var block = new WpfTextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2)
        };
        block.SetResourceReference(WpfTextBlock.FontFamilyProperty, "App.Text.Family.Ui");
        block.SetResourceReference(WpfTextBlock.ForegroundProperty, "App.Brush.Text.Primary");
        return block;
    }

    private static WpfTextBlock CreateSelectionText(
        string text,
        double fontSize,
        FontWeight fontWeight,
        string styleKey)
    {
        var block = new WpfTextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 2),
            Style = FindResource(styleKey)
        };
        block.SetResourceReference(WpfTextBlock.FontFamilyProperty, "App.Text.Family.Ui");
        return block;
    }

    private static readonly string[] SelectionPreviewStates =
    [
        "Default",
        "Hover",
        "Selected",
        "Current",
        "DropTarget",
        "MultiSelect",
        "Focus",
        "Disabled"
    ];

    private sealed class SelectionFixtureItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private bool _isCurrent;
        private bool _isDropTarget;
        public SelectionFixtureItem(string title, string metadata, string rowId, string automationName)
        {
            Title = title;
            Metadata = metadata;
            RowId = rowId;
            AutomationName = automationName;
        }

        public string Title { get; }

        public string Metadata { get; }

        public string RowId { get; }

        public string AutomationName { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public bool IsCurrent
        {
            get => _isCurrent;
            set => SetField(ref _isCurrent, value);
        }

        public bool IsDropTarget
        {
            get => _isDropTarget;
            set => SetField(ref _isDropTarget, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetField(ref bool field, bool value, [CallerMemberName] string? name = null)
        {
            if (field == value)
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}

internal static class GallerySelectionResourceExtensions
{
    public static T WithSelectionResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
