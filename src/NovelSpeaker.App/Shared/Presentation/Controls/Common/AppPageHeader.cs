using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Common;

public sealed class AppPageHeader : ContentControl
{
    static AppPageHeader()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppPageHeader),
            new FrameworkPropertyMetadata(typeof(AppPageHeader)));
    }

    public AppPageHeader()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(AppPageHeader),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(AppPageHeader),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty BackCommandProperty =
        DependencyProperty.Register(
            nameof(BackCommand),
            typeof(ICommand),
            typeof(AppPageHeader),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(
            nameof(Actions),
            typeof(object),
            typeof(AppPageHeader),
            new FrameworkPropertyMetadata(null));

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

    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
