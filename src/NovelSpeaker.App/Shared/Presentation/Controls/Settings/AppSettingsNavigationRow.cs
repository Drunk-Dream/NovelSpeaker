using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Settings;

public sealed class AppSettingsNavigationRow : Button
{
    static AppSettingsNavigationRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppSettingsNavigationRow),
            new FrameworkPropertyMetadata(typeof(AppSettingsNavigationRow)));
    }

    public AppSettingsNavigationRow()
    {
        Focusable = true;
        IsTabStop = true;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        BindingOperations.SetBinding(
            this,
            AutomationProperties.NameProperty,
            new Binding(nameof(Title)) { Source = this });
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(object),
            typeof(AppSettingsNavigationRow),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(AppSettingsNavigationRow),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(AppSettingsNavigationRow),
            new FrameworkPropertyMetadata(string.Empty));

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

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
}
