using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;

namespace NovelSpeaker.Application.DependencyInjection;

/// <summary>
/// Registers process-level application services and composes the feature registration modules.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddNovelSpeakerBooksApplication();
        services.AddNovelSpeakerSpeechApplication();
        services.AddNovelSpeakerPlaybackApplication();
        services.AddNovelSpeakerSettingsApplication();

        return services;
    }
}
