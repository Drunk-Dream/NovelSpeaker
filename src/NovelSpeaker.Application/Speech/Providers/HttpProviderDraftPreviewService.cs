using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Synthesizes an unsaved HTTP draft with the current global speech speed.</summary>
public sealed class HttpProviderDraftPreviewService(
    IEnumerable<IProviderRuntime> runtimes,
    IAppSettingsService settings)
{
    public const string PreviewText = "君不见黄河之水天上来，奔流到海不复回。";

    public Task<ProviderSynthesisResult> PreviewAsync(
        SpeechProviderInstance draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var runtime = runtimes.FirstOrDefault(candidate => candidate.Type == SpeechProviderType.Http);
        if (runtime is null || draft.Type != SpeechProviderType.Http)
        {
            return Task.FromResult(new ProviderSynthesisResult(null, null,
                new ProviderSynthesisFailure(ProviderSynthesisFailureKind.ProviderUnavailable,
                    "HTTP Provider 运行时不可用。")));
        }

        return runtime.SynthesizeAsync(draft,
            new ProviderSynthesisRequest(PreviewText,
                AppSettings.NormalizeSpeakSpeed(settings.Current.DefaultSpeakSpeed)),
            cancellationToken);
    }
}
