using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace NovelSpeaker.App.Shared.Theming;

/// <summary>
/// Adapts semantic MenuItem state to Wpf.Ui 4.3.0's internal resource hooks.
/// Each item owns its brushes: a Style resource would share a Freezable between
/// items and cannot reliably resolve a per-item inheritance context.
/// </summary>
internal static class MenuItemStateResources
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(MenuItemStateResources),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not MenuItem item)
        {
            return;
        }

        if (!(bool)args.NewValue)
        {
            item.Resources.Remove("MenuBarItemBackgroundSelected");
            item.Resources.Remove("MenuBarItemBackgroundPressed");
            item.Resources.Remove("MenuBarItemTextForegroundPressed");
            item.Resources.Remove("TextFillColorDisabledBrush");
            item.Resources.Remove("CheckBoxBackground");
            item.Resources.Remove("CheckBoxBorderBrush");
            return;
        }

        // Both background hooks follow the same effective state. In some Provider
        // roles IsHighlighted is evaluated after IsPressed; it must not restore
        // a hover background under the pressed foreground.
        Project("MenuBarItemBackgroundSelected", nameof(Control.Background));
        Project("MenuBarItemBackgroundPressed", nameof(Control.Background));
        Project("MenuBarItemTextForegroundPressed", nameof(Control.Foreground));
        // The shortcut label otherwise uses the Provider's disabled text brush even
        // on enabled items; the check glyph must share the item's surface pair.
        Project("TextFillColorDisabledBrush", nameof(Control.Foreground));
        Project("CheckBoxBackground", nameof(Control.Background));
        Project("CheckBoxBorderBrush", nameof(Control.Foreground));

        void Project(string key, string property)
        {
            var brush = new SolidColorBrush();
            BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty,
                new Binding($"{property}.Color") { Source = item, Mode = BindingMode.OneWay });
            item.Resources[key] = brush;
        }
    }
}
