using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovelSpeaker.App.Shell.Navigation;

/// <summary>
/// Keeps a page workspace constrained to its navigation frame so nested scroll regions receive a finite viewport.
/// </summary>
public static class NavigationViewportHeightBehavior
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(NavigationViewportHeightBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty RegistrationProperty = DependencyProperty.RegisterAttached(
        "Registration",
        typeof(ViewportRegistration),
        typeof(NavigationViewportHeightBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        if ((bool)args.NewValue)
        {
            Enable(element);
        }
        else
        {
            Disable(element);
        }
    }

    private static void Enable(FrameworkElement element)
    {
        if (element.GetValue(RegistrationProperty) is ViewportRegistration)
        {
            return;
        }

        var registration = new ViewportRegistration(element);
        element.SetValue(RegistrationProperty, registration);
        element.Loaded += Element_OnLoaded;
        element.Unloaded += Element_OnUnloaded;

        if (element.IsLoaded)
        {
            registration.Attach();
        }
    }

    private static void Disable(FrameworkElement element)
    {
        element.Loaded -= Element_OnLoaded;
        element.Unloaded -= Element_OnUnloaded;
        if (element.GetValue(RegistrationProperty) is ViewportRegistration registration)
        {
            registration.Detach();
            element.ClearValue(RegistrationProperty);
        }
    }

    private static void Element_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.GetValue(RegistrationProperty) is ViewportRegistration registration)
        {
            registration.Attach();
        }
    }

    private static void Element_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element
            && element.GetValue(RegistrationProperty) is ViewportRegistration registration)
        {
            registration.Detach();
        }
    }

    private sealed class ViewportRegistration(FrameworkElement target)
    {
        private FrameworkElement? _host;

        public void Attach()
        {
            var host = FindAncestor<Frame>(target) as FrameworkElement ?? Window.GetWindow(target);
            if (ReferenceEquals(_host, host))
            {
                UpdateHeight();
                return;
            }

            DetachHost();
            _host = host;
            if (_host is null)
            {
                target.Height = double.NaN;
                return;
            }

            _host.SizeChanged += Host_OnSizeChanged;
            UpdateHeight();
        }

        public void Detach()
        {
            DetachHost();
            target.Height = double.NaN;
        }

        private void DetachHost()
        {
            if (_host is null)
            {
                return;
            }

            _host.SizeChanged -= Host_OnSizeChanged;
            _host = null;
        }

        private void Host_OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateHeight();
        }

        private void UpdateHeight()
        {
            var height = _host?.ActualHeight ?? 0d;
            target.Height = height > 0d ? height : double.NaN;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? start)
        where T : DependencyObject
    {
        for (var current = GetParent(start); current is not null; current = GetParent(current))
        {
            if (current is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject? dependencyObject)
    {
        if (dependencyObject is null)
        {
            return null;
        }

        return dependencyObject switch
        {
            FrameworkElement frameworkElement => frameworkElement.Parent ?? VisualTreeHelper.GetParent(frameworkElement),
            FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
            _ => VisualTreeHelper.GetParent(dependencyObject)
        };
    }
}
