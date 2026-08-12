using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shared.Presentation.Behaviors;

public static class WpfUiFlyoutPlacement
{
    public static readonly DependencyProperty PlacementTargetProperty = DependencyProperty.RegisterAttached(
        "PlacementTarget",
        typeof(UIElement),
        typeof(WpfUiFlyoutPlacement),
        new PropertyMetadata(null, OnPlacementTargetChanged));

    public static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.RegisterAttached(
        "HorizontalOffset",
        typeof(double),
        typeof(WpfUiFlyoutPlacement),
        new PropertyMetadata(0d, OnPlacementChanged));

    public static readonly DependencyProperty VerticalOffsetProperty = DependencyProperty.RegisterAttached(
        "VerticalOffset",
        typeof(double),
        typeof(WpfUiFlyoutPlacement),
        new PropertyMetadata(0d, OnPlacementChanged));

    public static void SetPlacementTarget(DependencyObject element, UIElement? value) =>
        element.SetValue(PlacementTargetProperty, value);

    public static UIElement? GetPlacementTarget(DependencyObject element) =>
        (UIElement?)element.GetValue(PlacementTargetProperty);

    public static void SetHorizontalOffset(DependencyObject element, double value) =>
        element.SetValue(HorizontalOffsetProperty, value);

    public static double GetHorizontalOffset(DependencyObject element) =>
        (double)element.GetValue(HorizontalOffsetProperty);

    public static void SetVerticalOffset(DependencyObject element, double value) =>
        element.SetValue(VerticalOffsetProperty, value);

    public static double GetVerticalOffset(DependencyObject element) =>
        (double)element.GetValue(VerticalOffsetProperty);

    private static void OnPlacementTargetChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not Flyout flyout)
        {
            throw new ArgumentException("WpfUiFlyoutPlacement can only be applied to Wpf.Ui Flyout controls.");
        }

        if (args.OldValue is not null)
        {
            flyout.Loaded -= OnFlyoutLoaded;
            flyout.Opened -= OnFlyoutOpened;
            flyout.Closed -= OnFlyoutClosed;
            flyout.PreviewKeyDown -= OnFlyoutPreviewKeyDown;
        }

        if (args.NewValue is null)
        {
            return;
        }

        flyout.Loaded += OnFlyoutLoaded;
        flyout.Opened += OnFlyoutOpened;
        flyout.Closed += OnFlyoutClosed;
        flyout.PreviewKeyDown += OnFlyoutPreviewKeyDown;
        ConfigurePopup(flyout);
    }

    private static void OnPlacementChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is Flyout flyout)
        {
            ConfigurePopup(flyout);
        }
    }

    private static void OnFlyoutLoaded(object sender, RoutedEventArgs args) => ConfigurePopup((Flyout)sender);

    private static void OnFlyoutOpened(Flyout sender, RoutedEventArgs args)
    {
        ConfigurePopup(sender);
        _ = sender.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                var focusScope = sender.Content as UIElement ?? sender;
                focusScope.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            });
    }

    private static void OnFlyoutClosed(Flyout sender, RoutedEventArgs args)
    {
        ScheduleFocusRestore(sender);
    }

    private static void ScheduleFocusRestore(Flyout sender)
    {
        var target = GetPlacementTarget(sender);
        if (target is null)
        {
            return;
        }

        _ = sender.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            () =>
            {
                if (CanReceiveFocus(target))
                {
                    FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target), target);
                    Keyboard.Focus(target);
                    return;
                }

                var window = Window.GetWindow(target) ?? Window.GetWindow(sender);
                window?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            });
    }

    private static bool CanReceiveFocus(UIElement target) =>
        target.IsVisible &&
        target.IsEnabled &&
        target.Focusable &&
        (target is not FrameworkElement frameworkElement || frameworkElement.IsLoaded);

    private static void OnFlyoutPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape)
        {
            return;
        }

        ((Flyout)sender).Hide();
        ScheduleFocusRestore((Flyout)sender);
        args.Handled = true;
    }

    private static void ConfigurePopup(Flyout flyout)
    {
        var target = GetPlacementTarget(flyout);
        if (target is null)
        {
            return;
        }

        flyout.ApplyTemplate();
        if (flyout.Template.FindName("PART_Popup", flyout) is not Popup popup)
        {
            return;
        }

        popup.PlacementTarget = target;
        popup.HorizontalOffset = GetHorizontalOffset(flyout);
        popup.VerticalOffset = GetVerticalOffset(flyout);
    }
}
