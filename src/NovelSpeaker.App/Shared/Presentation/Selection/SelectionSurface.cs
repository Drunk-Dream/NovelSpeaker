using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovelSpeaker.App.Shared.Presentation.Selection;

/// <summary>Draws the current marker independently of selection and focus chrome.</summary>
public sealed class SelectionSurface : Border
{
    public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.Register(
        nameof(IsCurrent), typeof(bool), typeof(SelectionSurface),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentRailBrushProperty = DependencyProperty.Register(
        nameof(CurrentRailBrush), typeof(Brush), typeof(SelectionSurface),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsCurrent
    {
        get => (bool)GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    public Brush? CurrentRailBrush
    {
        get => (Brush?)GetValue(CurrentRailBrushProperty);
        set => SetValue(CurrentRailBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (IsCurrent && CurrentRailBrush is not null && ActualHeight > 0)
        {
            drawingContext.DrawRectangle(CurrentRailBrush, null, new Rect(0, 0, 3, ActualHeight));
        }
    }
}
