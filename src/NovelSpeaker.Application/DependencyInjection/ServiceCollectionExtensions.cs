using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Desktop;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.DependencyInjection;

/// <summary>
/// Registers process-level application services and composes the feature registration modules.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerApplication(
        this IServiceCollection services,
        AppSettings? startupSettings = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddNovelSpeakerBooksApplication();
        services.AddNovelSpeakerSpeechApplication();
        services.AddNovelSpeakerPlaybackApplication();
        services.AddNovelSpeakerDesktopApplication();
        services.AddNovelSpeakerSettingsApplication(startupSettings);

        return services;
    }
}
