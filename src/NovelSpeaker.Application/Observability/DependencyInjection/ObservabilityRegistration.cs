using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.Application.Observability.DependencyInjection;

/// <summary>
/// Registers the storage-free observability contract and its process context.
/// </summary>
public static class ObservabilityRegistration
{
    public static IServiceCollection AddNovelSpeakerObservability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ObservabilityContextAccessor>();
        services.TryAddSingleton<IObservabilityContextAccessor>(provider =>
            provider.GetRequiredService<ObservabilityContextAccessor>());
        services.TryAddSingleton<IObservability, ObservabilityHub>();
        return services;
    }
}
