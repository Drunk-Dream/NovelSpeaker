using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Desktop.MediaControls;

namespace NovelSpeaker.Application.Desktop;

internal static class DesktopRegistration
{
    public static IServiceCollection AddNovelSpeakerDesktopApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IMediaControlCoordinator, MediaControlCoordinator>();
        return services;
    }
}
