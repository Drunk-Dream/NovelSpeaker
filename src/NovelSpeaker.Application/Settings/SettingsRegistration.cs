using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Defines the composition boundary for the process-owned settings snapshot.
/// </summary>
public static class SettingsRegistration
{
    public static IServiceCollection AddNovelSpeakerSettingsApplication(
        this IServiceCollection services,
        AppSettings? startupSnapshot = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var normalizedStartupSnapshot = (startupSnapshot ?? AppSettings.Default).Normalize();
        services.TryAddSingleton<AppSettingsService>(provider =>
            new AppSettingsService(
                provider.GetRequiredService<IAppSettingsStore>(),
                normalizedStartupSnapshot));
        services.TryAddSingleton<IAppSettingsService>(provider => provider.GetRequiredService<AppSettingsService>());
        services.TryAddSingleton<IBookFileNameTemplateProvider>(provider => provider.GetRequiredService<AppSettingsService>());
        services.TryAddSingleton<ITextSegmentationOptionsProvider>(provider => provider.GetRequiredService<AppSettingsService>());
        return services;
    }
}
