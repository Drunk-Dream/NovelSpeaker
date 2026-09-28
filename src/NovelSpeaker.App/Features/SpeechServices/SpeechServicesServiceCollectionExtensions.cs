using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NovelSpeaker.App.Features.SpeechServices;

public static class SpeechServicesServiceCollectionExtensions
{
    public static IServiceCollection AddSpeechServicesFeature(this IServiceCollection services)
    {
        services.TryAddTransient<SpeechServicesViewModel>();
        services.TryAddTransient<SpeechServicesPage>();
        return services;
    }
}
