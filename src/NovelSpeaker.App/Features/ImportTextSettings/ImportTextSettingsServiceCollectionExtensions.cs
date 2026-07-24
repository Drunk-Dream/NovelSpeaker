using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.ImportTextSettings;

public static class ImportTextSettingsServiceCollectionExtensions
{
    public static IServiceCollection AddImportTextSettingsFeature(this IServiceCollection services)
    {
        services.TryAddTransient<ImportTextSettingsViewModel>();
        services.TryAddTransient<ImportTextSettingsPage>();
        return services;
    }
}
