using System.Windows;
using System.Windows.Controls;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Forms;

public sealed class AppFormField : ContentControl
{
    static AppFormField()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppFormField),
            new FrameworkPropertyMetadata(typeof(AppFormField)));
    }

    public AppFormField()
    {
        Focusable = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Top;
    }

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label),
            typeof(string),
            typeof(AppFormField),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(AppFormField),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty ErrorProperty =
        DependencyProperty.Register(
            nameof(Error),
            typeof(string),
            typeof(AppFormField),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty RequiredProperty =
        DependencyProperty.Register(
            nameof(Required),
            typeof(bool),
            typeof(AppFormField),
            new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsRequiredProperty = RequiredProperty;

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public string Error
    {
        get => (string)GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    public bool Required
    {
        get => (bool)GetValue(RequiredProperty);
        set => SetValue(RequiredProperty, value);
    }

    public bool IsRequired
    {
        get => Required;
        set => Required = value;
    }
}
