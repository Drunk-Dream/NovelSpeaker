using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Rules;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Defines the composition boundary for speech application use cases.
/// </summary>
public static class SpeechRegistration
{
    public static IServiceCollection AddNovelSpeakerSpeechApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ITtsRuleNormalizer, TtsRuleNormalizer>();
        services.TryAddSingleton<ITtsRuleQueries, TtsRuleQueries>();
        services.TryAddSingleton<ITtsRuleSelectionUseCase, TtsRuleSelectionUseCase>();
        services.TryAddSingleton<ITtsRuleEditorUseCase, TtsRuleEditorUseCase>();
        services.TryAddSingleton<ITtsRuleImportUseCase, TtsRuleImportUseCase>();
        return services;
    }
}
