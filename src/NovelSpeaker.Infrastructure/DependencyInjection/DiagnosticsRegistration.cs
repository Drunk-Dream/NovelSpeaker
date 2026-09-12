using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Infrastructure.Diagnostics;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class DiagnosticsRegistration
{
    public static IServiceCollection AddNovelSpeakerDiagnosticsAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<RollingFileLoggerProvider>();
        services.TryAddSingleton<ILoggerProvider>(
            static provider => new LoggerProviderRegistrationAdapter(
                provider.GetRequiredService<RollingFileLoggerProvider>()));
        return services;
    }
}
