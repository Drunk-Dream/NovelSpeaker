using Microsoft.Extensions.DependencyInjection;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

/// <summary>
/// Composes the infrastructure adapter registration modules required at startup.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNovelSpeakerPersistence();
        services.AddNovelSpeakerFileStorage();
        services.AddNovelSpeakerBooksAdapters();
        services.AddNovelSpeakerSpeechAdapters();
        services.AddNovelSpeakerAudioAdapters();
        services.AddNovelSpeakerSettingsAdapters();
        services.AddNovelSpeakerDiagnosticsAdapters();

        return services;
    }
}
