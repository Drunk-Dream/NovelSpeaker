using NovelSpeaker.Application.Cache.Audio;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Infrastructure.Speech.Scripting;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

public static class SpeechRegistration
{
    public static IServiceCollection AddNovelSpeakerSpeechAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<TemporarySpeechFileLease>(provider => new TemporarySpeechFileLease(
            provider.GetRequiredService<IAppDataDirectoryProvider>(),
            provider.GetRequiredService<IAppStoragePathResolver>()));
        services.TryAddSingleton<ITemplateEvaluator, JintTemplateEvaluator>();
        services.TryAddSingleton<IProviderRequestLimiter, ProviderRequestLimiter>();
        services.TryAddSingleton<IProviderPreviewAudioPlayer, ProviderPreviewAudioPlayer>();
        services.TryAddSingleton<ITtsHttpTransport, HttpTtsClient>();
        services.TryAddSingleton<ITtsRetryPolicy, TtsRetryPolicy>();
        services.TryAddSingleton<ITtsResponseValidator, TtsResponseValidator>();
        services.TryAddSingleton<TemporaryAudioStore>(provider => new TemporaryAudioStore(
            provider.GetRequiredService<IAppDataDirectoryProvider>(),
            new TemporaryAudioFileOperations(),
            provider.GetRequiredService<IAppStoragePathResolver>(),
            provider.GetRequiredService<TemporarySpeechFileLease>()));
        services.TryAddSingleton<IGeneratedAudioFileStore>(provider => provider.GetRequiredService<TemporaryAudioStore>());
        services.TryAddSingleton<AudioProbe>();
        services.TryAddSingleton<IEdgeSpeechTransport, NovelSpeaker.Infrastructure.Speech.Edge.EdgeSpeechTransport>();

        return services;
    }
}
