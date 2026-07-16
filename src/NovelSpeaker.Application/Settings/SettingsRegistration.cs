using Microsoft.Extensions.DependencyInjection;

namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Defines the composition boundary for settings application use cases.
/// </summary>
public static class SettingsRegistration
{
    public static IServiceCollection AddNovelSpeakerSettingsApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
