using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Application.Diagnostics;
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
        services.TryAddSingleton<LocalPerformanceTelemetryStore>();
        services.TryAddSingleton<IPerformanceTelemetryService>(
            static provider => provider.GetRequiredService<LocalPerformanceTelemetryStore>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IObservabilityConsumer, PerformanceTelemetryConsumer>());
        services.TryAddSingleton<SqliteDiagnosticSessionStore>();
        services.TryAddSingleton<IDiagnosticSessionService>(provider =>
            provider.GetRequiredService<SqliteDiagnosticSessionStore>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IObservabilityConsumer, DiagnosticSessionConsumer>());
        return services;
    }
}
