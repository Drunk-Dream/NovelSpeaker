using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Synthesizes an unsaved provider draft with the current global speech speed.</summary>
public sealed class ProviderDraftPreviewService(
    IEnumerable<IProviderRuntime> runtimes,
    IAppSettingsService settings,
    IProviderPreviewAudioPlayer player)
{
    public const string PreviewText = "君不见黄河之水天上来，奔流到海不复回。";

    public event EventHandler<ProviderPreviewPlaybackFailedEventArgs>? PlaybackFailed
    {
        add => player.PlaybackFailed += value;
        remove => player.PlaybackFailed -= value;
    }

    public Task StopAsync(CancellationToken cancellationToken) => player.StopAsync(cancellationToken);

    public Task<ProviderSynthesisResult> PreviewAsync(
        SpeechProviderInstance draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        cancellationToken.ThrowIfCancellationRequested();
        var runtime = runtimes.FirstOrDefault(candidate => candidate.Type == draft.Type);
        if (runtime is null)
        {
            return Task.FromResult(new ProviderSynthesisResult(null, null,
                new ProviderSynthesisFailure(ProviderSynthesisFailureKind.ProviderUnavailable,
                    "语音服务运行时不可用。")));
        }

        return runtime.SynthesizeAsync(draft,
            new ProviderSynthesisRequest(PreviewText,
                AppSettings.NormalizeSpeakSpeed(settings.Current.DefaultSpeakSpeed)),
            cancellationToken);
    }

    public async Task<ProviderSynthesisFailure?> PlayAsync(
        SpeechProviderInstance draft,
        CancellationToken cancellationToken)
    {
        var synthesis = await PreviewAsync(draft, cancellationToken).ConfigureAwait(false);
        if (!synthesis.IsSuccess)
        {
            return synthesis.Failure;
        }

        await using var audio = synthesis.Audio!;
        var playback = await player.PlayAsync(audio, synthesis.AudioFormat, cancellationToken).ConfigureAwait(false);
        return playback.IsSuccess ? null : new ProviderSynthesisFailure(
            ProviderSynthesisFailureKind.InvalidAudio,
            playback.FailureMessage ?? "试听音频播放失败。");
    }
}
