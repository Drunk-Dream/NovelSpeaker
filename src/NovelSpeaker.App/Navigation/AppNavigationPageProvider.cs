using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Abstractions;

namespace NovelSpeaker.App.Navigation;

/// <summary>
/// Resolves navigable pages from the desktop service provider for Wpf.Ui navigation.
/// </summary>
public sealed class AppNavigationPageProvider : INavigationViewPageProvider
{
    private readonly IServiceProvider _serviceProvider;

    public AppNavigationPageProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object GetPage(Type pageType)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        if (!typeof(System.Windows.Controls.Page).IsAssignableFrom(pageType))
        {
            throw new ArgumentException($"Navigation target must derive from {typeof(System.Windows.Controls.Page).FullName}.", nameof(pageType));
        }

        return _serviceProvider.GetRequiredService(pageType);
    }
}
