using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovelSpeaker.StyleGallery;

internal static class GalleryInteractionScene
{
    private static readonly (string Id, string Label, string Background, string Foreground, string Border)[] States =
    [
        ("rest", "Rest", "App.Brush.Surface.Primary", "App.Brush.Text.Primary", "App.Brush.Border.Subtle"),
        ("hover", "Hover", "App.Brush.Interaction.Surface.Hover", "App.Brush.Interaction.Foreground.Hover", "App.Brush.Interaction.Border.Hover"),
        ("pressed", "Pressed", "App.Brush.Interaction.Surface.Pressed", "App.Brush.Interaction.Foreground.Pressed", "App.Brush.Interaction.Border.Pressed"),
        ("selected", "Selected / Current", "App.Brush.Accent.Subtle", "App.Brush.Interaction.Foreground.Selected", "App.Brush.Accent.Default"),
        ("selected-hover", "Selected + Hover", "App.Brush.Accent.Subtle.Hover", "App.Brush.Interaction.Foreground.Selected", "App.Brush.Accent.Hover"),
        ("keyboard-focus", "Keyboard Focus", "App.Brush.Surface.Primary", "App.Brush.Text.Primary", "App.Brush.Focus"),
        ("disabled", "Disabled", "App.Brush.Surface.Secondary", "App.Brush.Interaction.Foreground.Disabled", "App.Brush.Border.Subtle")
    ];

    public static FrameworkElement Create()
    {
        var root = new Grid
        {
            Width = GalleryRenderSettings.WindowWidth,
            Height = GalleryRenderSettings.WindowHeight,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        root.SetResourceReference(Panel.BackgroundProperty, "GalleryCanvasBackgroundBrush");
        AutomationProperties.SetAutomationId(root, "interaction-states");
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Margin = new Thickness(32, 28, 32, 20) };
        header.Children.Add(CreateText("Interaction Palette · states and motion", 24, FontWeights.SemiBold, "App.Brush.Text.Primary"));
        header.Children.Add(CreateText(
            "States keep distinct meanings and one owner: Disabled > Selected + Hover > Selected / Current > Hover > Rest. High Contrast projects these semantics to system colors.",
            13,
            FontWeights.Normal,
            "App.Brush.Text.Secondary",
            new Thickness(0, 6, 0, 0)));
        root.Children.Add(header);

        var columns = new Grid { Margin = new Thickness(32, 0, 32, 32) };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var stateSurface = CreateSurface("App.Brush.Surface.Primary");
        stateSurface.Margin = new Thickness(0, 0, 8, 0);
        var stateContent = new StackPanel();
        stateContent.Children.Add(CreateText("Interaction states", 16, FontWeights.SemiBold, "App.Brush.Text.Primary"));
        stateContent.Children.Add(CreateText(
            "Mouse Hover and Pressed never impersonate Keyboard Focus.",
            12,
            FontWeights.Normal,
            "App.Brush.Text.Secondary",
            new Thickness(0, 4, 0, 14)));
        foreach (var state in States)
        {
            stateContent.Children.Add(CreateStateRow(state));
        }
        stateSurface.Child = stateContent;
        Grid.SetColumn(stateSurface, 0);
        columns.Children.Add(stateSurface);

        var motionSurface = CreateSurface("App.Brush.Surface.Primary");
        motionSurface.Margin = new Thickness(8, 0, 0, 0);
        var motionContent = new StackPanel();
        motionContent.Children.Add(CreateText("Motion tokens", 16, FontWeights.SemiBold, "App.Brush.Text.Primary"));
        motionContent.Children.Add(CreateText(
            "Only Fast / Standard / Slow are used by shared interaction resources.",
            12,
            FontWeights.Normal,
            "App.Brush.Text.Secondary",
            new Thickness(0, 4, 0, 14)));
        foreach (var token in new[] { "App.Motion.Fast", "App.Motion.Standard", "App.Motion.Slow" })
        {
            motionContent.Children.Add(CreateMotionRow(token));
        }

        var note = CreateSurface("App.Brush.Surface.Secondary");
        note.Margin = new Thickness(0, 20, 0, 0);
        note.Child = CreateText(
            "Popup and Flyout transitions use Standard for entry with a 2–4 px restrained offset; exit uses Fast. Reduced motion keeps the final state without decorative movement.",
            13,
            FontWeights.Normal,
            "App.Brush.Text.Primary");
        motionContent.Children.Add(note);
        motionSurface.Child = motionContent;
        Grid.SetColumn(motionSurface, 1);
        columns.Children.Add(motionSurface);

        Grid.SetRow(columns, 1);
        root.Children.Add(columns);
        return root;
    }

    private static Border CreateStateRow((string Id, string Label, string Background, string Foreground, string Border) state)
    {
        var row = new Border
        {
            MinHeight = 40,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(state.Id == "keyboard-focus" ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            Child = CreateText(state.Label, 13, FontWeights.SemiBold, state.Foreground)
        };
        row.SetResourceReference(Border.BackgroundProperty, state.Background);
        row.SetResourceReference(Border.BorderBrushProperty, state.Border);
        if (state.Id == "disabled")
        {
            row.SetResourceReference(UIElement.OpacityProperty, "App.Opacity.Disabled");
        }

        AutomationProperties.SetAutomationId(row, $"interaction-state-{state.Id}");
        return row;
    }

    private static Border CreateMotionRow(string token)
    {
        var duration = (Duration)(global::System.Windows.Application.Current?.FindResource(token)
            ?? throw new InvalidOperationException($"Gallery motion token '{token}' was not found."));
        var row = CreateSurface("App.Brush.Surface.Secondary");
        row.Margin = new Thickness(0, 0, 0, 8);
        row.Padding = new Thickness(12, 10, 12, 10);
        row.Child = CreateText($"{token}  ·  {duration.TimeSpan.TotalMilliseconds:0} ms", 13, FontWeights.SemiBold, "App.Brush.Text.Primary");
        AutomationProperties.SetAutomationId(row, $"motion-{token.Replace('.', '-')}");
        return row;
    }

    private static Border CreateSurface(string backgroundKey)
    {
        var surface = new Border
        {
            Padding = new Thickness(20),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        surface.SetResourceReference(Border.BackgroundProperty, backgroundKey);
        surface.SetResourceReference(Border.BorderBrushProperty, "App.Brush.Border.Subtle");
        return surface;
    }

    private static TextBlock CreateText(
        string text,
        double fontSize,
        FontWeight fontWeight,
        string foregroundKey,
        Thickness? margin = null)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? new Thickness(0)
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, foregroundKey);
        return block;
    }
}
