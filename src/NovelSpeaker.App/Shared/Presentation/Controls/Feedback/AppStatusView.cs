using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Feedback;

public enum AppStatusKind
{
    Loading,
    Empty,
    NoResult,
    Error,
    Success
}

public sealed class AppStatusView : ContentControl
{
    static AppStatusView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppStatusView),
            new FrameworkPropertyMetadata(typeof(AppStatusView)));
    }

    public AppStatusView()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(
            nameof(Status),
            typeof(AppStatusKind),
            typeof(AppStatusView),
            new FrameworkPropertyMetadata(AppStatusKind.Empty));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(AppStatusView),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(
            nameof(Description),
            typeof(string),
            typeof(AppStatusView),
            new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(SymbolRegular),
            typeof(AppStatusView),
            new FrameworkPropertyMetadata(SymbolRegular.Info24));

    public static readonly DependencyProperty PrimaryActionProperty =
        DependencyProperty.Register(
            nameof(PrimaryAction),
            typeof(object),
            typeof(AppStatusView),
            new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SecondaryActionProperty =
        DependencyProperty.Register(
            nameof(SecondaryAction),
            typeof(object),
            typeof(AppStatusView),
            new FrameworkPropertyMetadata(null));

    public AppStatusKind Status
    {
        get => (AppStatusKind)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
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

    public SymbolRegular Icon
    {
        get => (SymbolRegular)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public object? PrimaryAction
    {
        get => GetValue(PrimaryActionProperty);
        set => SetValue(PrimaryActionProperty, value);
    }

    public object? SecondaryAction
    {
        get => GetValue(SecondaryActionProperty);
        set => SetValue(SecondaryActionProperty, value);
    }
}
