using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.Playback;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class AudioRegistration
{
    public static IServiceCollection AddNovelSpeakerAudioAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IAudioPlayer, NaudioAudioPlayer>();
        services.TryAddSingleton<IAudioPlayerFactory, NaudioAudioPlayerFactory>();
        services.TryAddSingleton<IBookPlaybackContentFailureReporter, BookPlaybackContentFailureReporter>();
        return services;
    }
}
