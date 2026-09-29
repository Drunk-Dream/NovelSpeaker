using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Defines the composition boundary for speech application use cases.
/// </summary>
public static class SpeechRegistration
{
    public static IServiceCollection AddNovelSpeakerSpeechApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICurrentSpeechProvider, CurrentSpeechProvider>();
        services.TryAddSingleton<IHttpTtsClient, TtsExecutionService>();
        services.TryAddSingleton<IProviderRuntimeResolver>(provider => new ProviderRuntimeResolver(
            provider.GetRequiredService<IProviderStore>(),
            provider.GetServices<IProviderRuntime>().FirstOrDefault(runtime => runtime.Type == SpeechProviderType.Http),
            provider.GetServices<IProviderRuntime>().FirstOrDefault(runtime => runtime.Type == SpeechProviderType.MicrosoftEdge),
            provider.GetService<ExperimentalFeaturesService>()));
        services.TryAddSingleton<HttpProviderRequestCompiler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderRuntime, HttpProviderRuntime>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProviderRuntime, EdgeProviderRuntime>());
        services.TryAddSingleton<EdgeVoiceCatalog>();
        services.TryAddSingleton<ProviderDraftPreviewService>();
        services.TryAddSingleton<SpeechProviderWorkspace>();
        return services;
    }
}
