using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Infrastructure.Speech.Legado;
using NovelSpeaker.Infrastructure.Speech.Rules;
using NovelSpeaker.Infrastructure.Speech.Scripting;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class SpeechRegistration
{
    public static IServiceCollection AddNovelSpeakerSpeechAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<LegadoRuleConverter>();
        services.TryAddSingleton<LegadoRuleSourceParser>();
        services.TryAddSingleton<ITtsRuleSourceAdapter, LegadoRuleSourceAdapter>();
        services.TryAddSingleton<ITemplateEvaluator, JintTemplateEvaluator>();
        services.TryAddSingleton<ITtsRequestCompiler, TtsRequestCompiler>();
        services.TryAddSingleton<ITtsRateLimiter, TtsRateLimiter>();
        services.TryAddSingleton<IHttpTtsClient, HttpTtsClient>();
        services.TryAddSingleton<ITtsRuleTestService, TtsRuleTestService>();

        return services;
    }
}
