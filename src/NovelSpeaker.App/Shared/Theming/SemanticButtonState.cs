using System.Windows;

namespace NovelSpeaker.App.Shared.Theming;

public static class SemanticButtonState
{
    public static readonly DependencyProperty IsAccentProperty =
        DependencyProperty.RegisterAttached(
            "IsAccent",
            typeof(bool),
            typeof(SemanticButtonState),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsDangerProperty =
        DependencyProperty.RegisterAttached(
            "IsDanger",
            typeof(bool),
            typeof(SemanticButtonState),
            new FrameworkPropertyMetadata(false));

    public static void SetIsAccent(DependencyObject element, bool value) =>
        element.SetValue(IsAccentProperty, value);

    public static bool GetIsAccent(DependencyObject element) =>
        (bool)element.GetValue(IsAccentProperty);

    public static void SetIsDanger(DependencyObject element, bool value) =>
        element.SetValue(IsDangerProperty, value);

    public static bool GetIsDanger(DependencyObject element) =>
        (bool)element.GetValue(IsDangerProperty);
}
