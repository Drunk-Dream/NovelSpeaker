using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovelSpeaker.App.Shared.Presentation.Controls.Settings;

public class AppSettingsList : ItemsControl
{
    static AppSettingsList()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(AppSettingsList),
            new FrameworkPropertyMetadata(typeof(AppSettingsList)));
    }

    public AppSettingsList()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Loaded += (_, _) => UpdateContainerBoundaries();
    }

    public static readonly DependencyProperty IsLastItemProperty =
        DependencyProperty.RegisterAttached(
            "IsLastItem",
            typeof(bool),
            typeof(AppSettingsList),
            new FrameworkPropertyMetadata(false));

    public static bool GetIsLastItem(DependencyObject element) =>
        (bool)element.GetValue(IsLastItemProperty);

    public static void SetIsLastItem(DependencyObject element, bool value) =>
        element.SetValue(IsLastItemProperty, value);

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
