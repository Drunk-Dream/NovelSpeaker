using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Settings;

public sealed class AppSettingsGroup : ItemsControl
{
    static AppSettingsGroup()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppSettingsGroup),
            new FrameworkPropertyMetadata(typeof(AppSettingsGroup)));
    }

    public AppSettingsGroup()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Loaded += (_, _) => UpdateContainerBoundaries();
    }

    public static readonly DependencyProperty IsLastItemProperty =
        DependencyProperty.RegisterAttached(
            "IsLastItem",
            typeof(bool),
            typeof(AppSettingsGroup),
            new FrameworkPropertyMetadata(false));

    public static bool GetIsLastItem(DependencyObject element) =>
        (bool)element.GetValue(IsLastItemProperty);

    public static void SetIsLastItem(DependencyObject element, bool value) =>
        element.SetValue(IsLastItemProperty, value);

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

    protected override DependencyObject GetContainerForItemOverride() => new ContentControl();

    protected override bool IsItemItsOwnContainerOverride(object item) => false;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        UpdateContainerBoundaries();
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(UpdateContainerBoundaries));
    }

    private void UpdateContainerBoundaries()
    {
        var lastIndex = Items.Count - 1;
        for (var index = 0; index < Items.Count; index++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container)
            {
                SetIsLastItem(container, index == lastIndex);
            }
        }
    }
}
