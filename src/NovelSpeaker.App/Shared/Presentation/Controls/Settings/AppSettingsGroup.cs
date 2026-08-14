using System.Windows;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Settings;

public sealed class AppSettingsGroup : AppSettingsList
{
    static AppSettingsGroup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppSettingsGroup),
            new FrameworkPropertyMetadata(typeof(AppSettingsGroup)));
    }

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(
            nameof(Header),
            typeof(string),
            typeof(AppSettingsGroup),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(AppSettingsGroup),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty FooterProperty =
        DependencyProperty.Register(
            nameof(Footer),
            typeof(object),
            typeof(AppSettingsGroup),
            new FrameworkPropertyMetadata(null));

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }
}
