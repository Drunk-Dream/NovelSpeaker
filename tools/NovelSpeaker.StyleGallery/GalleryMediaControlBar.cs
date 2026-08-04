using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.StyleGallery;

/// <summary>
/// Gallery-only media control fixture. It owns no production command or playback state.
/// </summary>
public sealed class GalleryMediaControlBar : Border
{
    public GalleryMediaControlBar()
    {
        SliderProjection = new GalleryMediaSliderProjection(140, 58);
        SliderProjection.BeginDrag();

        Padding = new Thickness(20);
        BorderThickness = new Thickness(1);
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "PrimarySurfaceBrush");
        SetResourceReference(BorderBrushProperty, "SubtleBorderBrush");
        SetResourceReference(CornerRadiusProperty, "CornerRadiusMedium");
        AutomationProperties.SetAutomationId(this, "media-control-bar");

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var windowActions = CreateWindowActionRow();
        Grid.SetRow(windowActions, 0);
        content.Children.Add(windowActions);

        var progress = CreateProgressRow();
        Grid.SetRow(progress, 1);
        content.Children.Add(progress);

        var controls = CreatePlaybackRow();
        Grid.SetRow(controls, 2);
        content.Children.Add(controls);

        var statePreview = CreateStatePreview();
        Grid.SetRow(statePreview, 3);
        content.Children.Add(statePreview);

        SliderProjectionText = new TextBlock
        {
            Margin = new Thickness(0, 16, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = FormatProjection()
        };
        SliderProjectionText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        AutomationProperties.SetAutomationId(SliderProjectionText, "media-slider-projection");
        Grid.SetRow(SliderProjectionText, 4);
        content.Children.Add(SliderProjectionText);

        Child = content;
    }

    public GalleryMediaSliderProjection SliderProjection { get; }

    public Slider ProgressSlider { get; private set; } = null!;

    public Grid ProgressTrack { get; private set; } = null!;

    public Border PlayedTrack { get; private set; } = null!;

    public Border UnplayedTrack { get; private set; } = null!;

    public TextBlock SliderProjectionText { get; private set; } = null!;

    public TextBlock SliderPositionText { get; private set; } = null!;

    public Button PlayButton { get; private set; } = null!;

    public Button PauseButton { get; private set; } = null!;

    public Button VolumeButton { get; private set; } = null!;

    public Button PreviousSegmentButton { get; private set; } = null!;

    public Button NextSegmentButton { get; private set; } = null!;

    public Button PreviousChapterButton { get; private set; } = null!;

    public Button NextChapterButton { get; private set; } = null!;

    public Button PinButton { get; private set; } = null!;

    public Button RestoreButton { get; private set; } = null!;

    public Button CloseButton { get; private set; } = null!;

    public Button DisabledWindowActionButton { get; private set; } = null!;

    private Grid CreateWindowActionRow()
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "MediaControlBar · Style Gallery fixture",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        }.WithGalleryResource(TextBlock.ForegroundProperty, "PrimaryTextBrush"));
        title.Children.Add(new TextBlock
        {
            Text = "播放上下文固定为内存 fixture；窗口动作只展示激活、Focus 和 Disabled 状态。",
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        }.WithGalleryResource(TextBlock.ForegroundProperty, "SecondaryTextBrush"));
        Grid.SetColumn(title, 0);
        row.Children.Add(title);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        PinButton = CreateButton(
            "WindowAction",
            "media-window-pin",
            SymbolRegular.Pin24,
            "置顶已激活：固定窗口在其它窗口上方。此 Gallery fixture 不执行窗口命令。",
            "App.Media.WindowAction 置顶（已激活）",
            18);
        PinButton.SetResourceReference(Control.BackgroundProperty, "AccentSubtleBrush");
        PinButton.SetResourceReference(Control.BorderBrushProperty, "AccentBrush");

        RestoreButton = CreateButton(
            "WindowAction",
            "media-window-restore",
            SymbolRegular.WindowArrowUp24,
            "返回主窗口：此 Gallery fixture 只展示窗口动作 Tooltip，不执行导航。",
            "App.Media.WindowAction 返回主窗口",
            18);
        CloseButton = CreateButton(
            "WindowAction",
            "media-window-close",
            SymbolRegular.DismissSquare24,
            "关闭媒体窗口：此 Gallery fixture 只展示窗口动作 Tooltip，不关闭真实窗口。",
            "App.Media.WindowAction 关闭",
            18);
        DisabledWindowActionButton = CreateButton(
            "WindowAction",
            "media-window-disabled",
            SymbolRegular.Dismiss24,
            "Disabled 窗口动作：Tooltip 在禁用状态仍然可访问。",
            "App.Media.WindowAction Disabled",
            18);
        DisabledWindowActionButton.IsEnabled = false;

        foreach (var button in new[] { PinButton, RestoreButton, CloseButton, DisabledWindowActionButton })
        {
            button.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(button);
        }

        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    private Grid CreateProgressRow()
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = "段落进度",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        }.WithGalleryResource(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        ProgressTrack = new Grid
        {
            Height = 24,
            MinHeight = 24,
            MinWidth = 280,
            ClipToBounds = false
        };
        ProgressTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ProgressTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        PlayedTrack = new Border
        {
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(3)
        };
        PlayedTrack.SetResourceReference(Panel.BackgroundProperty, "AccentBrush");
        AutomationProperties.SetAutomationId(PlayedTrack, "media-slider-played-track");
        Grid.SetColumn(PlayedTrack, 0);
        ProgressTrack.Children.Add(PlayedTrack);

        UnplayedTrack = new Border
        {
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(3)
        };
        UnplayedTrack.SetResourceReference(Panel.BackgroundProperty, "SecondarySurfaceBrush");
        AutomationProperties.SetAutomationId(UnplayedTrack, "media-slider-unplayed-track");
        Grid.SetColumn(UnplayedTrack, 1);
        ProgressTrack.Children.Add(UnplayedTrack);

        ProgressSlider = new Slider
        {
            Style = FindStyle("Slider"),
            Minimum = 0,
            Maximum = SliderProjection.Maximum,
            Value = SliderProjection.Value,
            TickFrequency = 1,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            ToolTip = new ToolTip
            {
                Content = SliderProjection.TooltipText,
                Placement = PlacementMode.Top,
                PlacementTarget = null,
                StaysOpen = true
            }
        };
        ProgressSlider.ToolTip = CreateProgressToolTip(ProgressSlider);
        AutomationProperties.SetAutomationId(ProgressSlider, "media-slider");
        AutomationProperties.SetName(ProgressSlider, "App.Media.Slider 段落进度");
        ProgressSlider.ValueChanged += OnProgressValueChanged;
        ProgressSlider.Loaded += (_, _) =>
        {
            UpdateProgressTrack();
            if (ProgressSlider.ToolTip is ToolTip toolTip)
            {
                toolTip.PlacementTarget = ProgressSlider;
                toolTip.IsOpen = true;
            }
        };
        ProgressTrack.Children.Add(ProgressSlider);
        Grid.SetColumnSpan(ProgressSlider, 2);
        Grid.SetColumn(ProgressTrack, 1);
        row.Children.Add(ProgressTrack);

        var position = new TextBlock
        {
            Text = SliderProjection.TooltipText,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        }.WithGalleryResource(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        AutomationProperties.SetAutomationId(position, "media-slider-position");
        Grid.SetColumn(position, 2);
        row.Children.Add(position);
        SliderPositionText = position;
        ProgressTrack.SizeChanged += (_, _) => UpdateProgressTrack();
        UpdateProgressTrack();
        return row;

        ToolTip CreateProgressToolTip(Slider slider) => new()
        {
            Content = SliderProjection.TooltipText,
            Placement = PlacementMode.Top,
            PlacementTarget = slider,
            StaysOpen = true
        };
    }

    private StackPanel CreatePlaybackRow()
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        PreviousChapterButton = CreateButton(
            "Chapter",
            "media-chapter-previous",
            SymbolRegular.ChevronDoubleLeft20,
            "上一章：跳转到上一章的第一段。",
            "App.Media.Chapter 上一章",
            16);
        PreviousSegmentButton = CreateButton(
            "Secondary",
            "media-segment-previous",
            SymbolRegular.ChevronLeft20,
            "上一段：跳转到当前章节的上一段。",
            "App.Media.Secondary 上一段",
            18);
        PlayButton = CreateButton(
            "Primary",
            "media-primary-play",
            SymbolRegular.PlayCircle24,
            "播放：开始读取当前段。此 Gallery fixture 不执行播放命令。",
            "App.Media.Primary 播放",
            28);
        PauseButton = CreateButton(
            "Primary",
            "media-primary-pause",
            SymbolRegular.PauseCircle24,
            "暂停：暂停当前段。此 Gallery fixture 不执行播放命令。",
            "App.Media.Primary 暂停（Focus preview）",
            28);
        PauseButton.SetResourceReference(Control.BorderBrushProperty, "AccentFocusRingBrush");
        NextSegmentButton = CreateButton(
            "Secondary",
            "media-segment-next",
            SymbolRegular.ChevronRight20,
            "下一段：跳转到当前章节的下一段。",
            "App.Media.Secondary 下一段",
            18);
        NextChapterButton = CreateButton(
            "Chapter",
            "media-chapter-next",
            SymbolRegular.ChevronDoubleRight20,
            "下一章：跳转到下一章的第一段。",
            "App.Media.Chapter 下一章",
            16);
        VolumeButton = CreateButton(
            "Secondary",
            "media-volume",
            SymbolRegular.Speaker224,
            "音量：调整播放音量。此 Gallery fixture 不执行真实音量命令。",
            "App.Media.Secondary 音量",
            20);

        foreach (var button in new[]
                 {
                     PreviousChapterButton,
                     PreviousSegmentButton,
                     PlayButton,
                     PauseButton,
                     NextSegmentButton,
                     NextChapterButton,
                     VolumeButton
                 })
        {
            button.Margin = new Thickness(4, 0, 4, 0);
            row.Children.Add(button);
        }

        return row;
    }

    private Border CreateStatePreview()
    {
        var preview = new Border
        {
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "Focus preview · 长 Tooltip：置顶已激活会固定窗口在其它窗口上方；返回主窗口和关闭动作均有明确说明。",
                TextWrapping = TextWrapping.Wrap
            }.WithGalleryResource(TextBlock.ForegroundProperty, "PrimaryTextBrush")
        };
        preview.SetResourceReference(Border.BackgroundProperty, "AccentSubtleBrush");
        preview.SetResourceReference(Border.BorderBrushProperty, "AccentFocusRingBrush");
        AutomationProperties.SetAutomationId(preview, "media-tooltip-preview");
        return preview;
    }

    private Button CreateButton(
        string styleName,
        string automationId,
        SymbolRegular symbol,
        string toolTip,
        string automationName,
        double iconSize)
    {
        var foregroundKey = styleName switch
        {
            "Primary" => "AccentTextBrush",
            "Chapter" => "SecondaryTextBrush",
            _ => "PrimaryTextBrush"
        };
        var icon = new SymbolIcon
        {
            Symbol = symbol,
            Width = iconSize,
            Height = iconSize
        };
        icon.SetResourceReference(SymbolIcon.ForegroundProperty, foregroundKey);
        icon.SetResourceReference(TextElement.ForegroundProperty, foregroundKey);
        icon.Loaded += (_, _) => ApplyMediaIconGlyphForeground(icon, foregroundKey);
        var button = new Button
        {
            Style = FindStyle(styleName),
            Content = icon,
            ToolTip = toolTip,
            Focusable = true
        };
        ToolTipService.SetShowOnDisabled(button, true);
        AutomationProperties.SetAutomationId(button, automationId);
        AutomationProperties.SetName(button, automationName);
        return button;
    }

    private static void ApplyMediaIconGlyphForeground(SymbolIcon icon, string foregroundKey)
    {
        icon.ApplyTemplate();
        foreach (var glyph in FindVisualDescendants<TextBlock>(icon))
        {
            glyph.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        }
    }

    private static IReadOnlyList<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var matches = new List<T>();
        Visit(root, matches);
        return matches;

        static void Visit(DependencyObject current, ICollection<T> matches)
        {
            if (current is T match)
            {
                matches.Add(match);
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                Visit(VisualTreeHelper.GetChild(current, index), matches);
            }
        }
    }

    private void OnProgressValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        SliderProjection.Preview(args.NewValue);
        if (ProgressSlider.ToolTip is ToolTip toolTip)
        {
            toolTip.Content = SliderProjection.TooltipText;
        }

        SliderProjectionText.Text = FormatProjection();
        SliderPositionText.Text = SliderProjection.TooltipText;
        UpdateProgressTrack();
    }

    private void UpdateProgressTrack()
    {
        if (ProgressTrack is null || ProgressTrack.ActualWidth <= 0)
        {
            return;
        }

        var playedRatio = SliderProjection.Maximum == 0
            ? 0
            : (double)SliderProjection.Value / SliderProjection.Maximum;
        ProgressTrack.ColumnDefinitions[0].Width = new GridLength(playedRatio, GridUnitType.Star);
        ProgressTrack.ColumnDefinitions[1].Width = new GridLength(1 - playedRatio, GridUnitType.Star);
    }

    private string FormatProjection() =>
        $"拖动预览 · {SliderProjection.TooltipText} · 仅更新 Gallery projection，不触发真实播放命令。";

    private static Style FindStyle(string name) =>
        Application.Current?.FindResource($"App.Media.{name}") as Style
        ?? throw new InvalidOperationException($"Media style 'App.Media.{name}' was not found.");

}

internal static class GalleryMediaControlBarExtensions
{
    public static T WithGalleryResource<T>(this T element, DependencyProperty property, object resourceKey)
        where T : FrameworkElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}

public sealed class GalleryMediaSliderProjection
{
    public GalleryMediaSliderProjection(int maximum, int initialValue)
    {
        if (maximum <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximum));
        }

        Maximum = maximum;
        Value = Math.Clamp(initialValue, 0, maximum);
    }

    public int Maximum { get; }

    public int Value { get; private set; }

    public bool IsDragging { get; private set; }

    public string TooltipText => $"{Value} / {Maximum}";

    public void BeginDrag() => IsDragging = true;

    public void Preview(double value)
    {
        Value = Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, Maximum);
    }

    public void EndDrag() => IsDragging = false;
}
