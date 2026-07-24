using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Diagnostics;

public static class DiagnosticsServiceCollectionExtensions
{
    public static IServiceCollection AddDiagnosticsFeature(this IServiceCollection services)
    {
        services.TryAddSingleton<IAppDiagnosticsService, AppDiagnosticsService>();
        services.TryAddSingleton<IClipboardService, WpfClipboardService>();
        services.TryAddTransient<DiagnosticsAboutViewModel>();
        services.TryAddTransient<DiagnosticsAboutPage>();
        return services;
    }
}
