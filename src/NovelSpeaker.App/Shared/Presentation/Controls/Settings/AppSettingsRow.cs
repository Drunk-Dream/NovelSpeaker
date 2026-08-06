using System.Windows;
using System.Windows.Controls;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Settings;

public sealed class AppSettingsRow : ContentControl
{
    private const double NarrowLayoutThreshold = 560;

    static AppSettingsRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppSettingsRow),
            new FrameworkPropertyMetadata(typeof(AppSettingsRow)));
    }

    public AppSettingsRow()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
    }

    public static readonly DependencyProperty IsNarrowLayoutProperty =
        DependencyProperty.Register(
            nameof(IsNarrowLayout),
            typeof(bool),
            typeof(AppSettingsRow),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(AppSettingsRow),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(AppSettingsRow),
            new FrameworkPropertyMetadata(string.Empty));

    // Value is an intentional semantic alias for Content so callers can use
    // either a property element or the normal ContentControl content slot.
    public static readonly DependencyProperty ValueProperty = ContentProperty;

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsNarrowLayout
    {
        get => (bool)GetValue(IsNarrowLayoutProperty);
        private set => SetValue(IsNarrowLayoutProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        IsNarrowLayout = sizeInfo.NewSize.Width > 0 && sizeInfo.NewSize.Width <= NarrowLayoutThreshold;
    }
}
