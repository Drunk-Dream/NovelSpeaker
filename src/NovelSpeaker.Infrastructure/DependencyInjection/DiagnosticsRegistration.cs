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

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, RollingFileLoggerProvider>());
        return services;
    }
}
