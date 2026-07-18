using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Testing;
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
        services.TryAddSingleton<ITtsCompilationFailureReporter, TtsCompilationFailureReporter>();
        services.TryAddSingleton<ITtsRateLimiter, TtsRateLimiter>();
        services.TryAddSingleton<ITtsHttpTransport, HttpTtsClient>();
        services.TryAddSingleton<ITtsRetryPolicy, TtsRetryPolicy>();
        services.TryAddSingleton<ITtsResponseValidator, TtsResponseValidator>();
        services.TryAddSingleton<TemporaryAudioStore>();
        services.TryAddSingleton<AudioProbe>();
        services.TryAddSingleton<ITtsRuleTestFailureReporter, TtsRuleTestFailureReporter>();

        return services;
    }
}
