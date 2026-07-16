using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Infrastructure.Settings;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class SettingsRegistration
{
    public static IServiceCollection AddNovelSpeakerSettingsAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<JsonAppSettingsStore>();
        services.TryAddSingleton<IAppSettingsStore>(provider => provider.GetRequiredService<JsonAppSettingsStore>());
        services.TryAddSingleton<AppSettingsService>();
        services.TryAddSingleton<IAppSettingsService>(provider => provider.GetRequiredService<AppSettingsService>());
        services.TryAddSingleton<IAudioCacheLimitProvider>(provider => provider.GetRequiredService<AppSettingsService>());
        services.TryAddSingleton<IBookFileNameTemplateProvider>(provider => provider.GetRequiredService<JsonAppSettingsStore>());
        services.TryAddSingleton<ITextSegmentationOptionsProvider>(provider => provider.GetRequiredService<JsonAppSettingsStore>());

        return services;
    }
}
