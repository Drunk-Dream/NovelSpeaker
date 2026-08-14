using System.Windows;
using System.Windows.Controls;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Common;

public sealed class AppSectionSurface : ContentControl
{
    static AppSectionSurface()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppSectionSurface),
            new FrameworkPropertyMetadata(typeof(AppSectionSurface)));
    }

    public AppSectionSurface()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(
            nameof(Header),
            typeof(string),
            typeof(AppSectionSurface),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(AppSectionSurface),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty FooterProperty =
        DependencyProperty.Register(
            nameof(Footer),
            typeof(object),
            typeof(AppSectionSurface),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = FooterProperty;

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

    public object? Actions
    {
        get => Footer;
        set => Footer = value;
    }
}
