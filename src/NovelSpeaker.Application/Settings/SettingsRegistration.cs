using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Defines the composition boundary for settings application use cases.
/// </summary>
public static class SettingsRegistration
{
    public static IServiceCollection AddNovelSpeakerSettingsApplication(
        this IServiceCollection services,
        AppSettings? startupSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton((startupSnapshot ?? AppSettings.Default).Normalize());
        services.TryAddSingleton<AppSettingsService>();
        services.TryAddSingleton<IAppSettingsService>(provider => provider.GetRequiredService<AppSettingsService>());
        services.TryAddSingleton<IAudioCacheLimitProvider>(provider => provider.GetRequiredService<AppSettingsService>());
        services.TryAddSingleton<IBookFileNameTemplateProvider>(provider => provider.GetRequiredService<AppSettingsService>());
        services.TryAddSingleton<ITextSegmentationOptionsProvider>(provider => provider.GetRequiredService<AppSettingsService>());
        return services;
    }
}
