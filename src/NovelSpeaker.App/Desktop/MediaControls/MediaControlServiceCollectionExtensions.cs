using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Desktop.MediaControls;

namespace NovelSpeaker.App.Desktop.MediaControls;

internal static class MediaControlServiceCollectionExtensions
{
    public static IServiceCollection AddMediaControls(this IServiceCollection services)
    {
        services.TryAddSingleton<IMediaControlPlatform, WindowsMediaControlAdapter>();
        services.TryAddSingleton<IMediaControlFailureReporter, MediaControlFailureReporter>();
        return services;
    }
}
