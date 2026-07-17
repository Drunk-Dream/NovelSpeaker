using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Defines the composition boundary for playback application use cases.
/// </summary>
public static class PlaybackRegistration
{
    public static IServiceCollection AddNovelSpeakerPlaybackApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IBookPlaybackContentService, BookPlaybackContentService>();
        return services;
    }
}
