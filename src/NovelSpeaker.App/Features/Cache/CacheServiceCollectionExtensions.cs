using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.Cache;

public static class CacheServiceCollectionExtensions
{
    public static IServiceCollection AddCacheFeature(this IServiceCollection services)
    {
        services.TryAddTransient<CacheAndDataViewModel>();
        services.TryAddTransient<CacheManagementViewModel>();
        services.TryAddTransient<CacheAndDataPage>();
        services.TryAddTransient<CacheManagementPage>();
        return services;
    }
}
