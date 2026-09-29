using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

public sealed class EdgeProviderRuntime(IEdgeSpeechTransport transport) : IProviderRuntime
{
    public SpeechProviderType Type => SpeechProviderType.MicrosoftEdge;

    public Task<ProviderSynthesisResult> SynthesizeAsync(SpeechProviderInstance provider,
        ProviderSynthesisRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (provider.Configuration is not EdgeSpeechProviderConfiguration { Voice: { } voice } ||
            !ProviderConfigurationValidator.Validate(provider).IsValid)
            return Task.FromResult(new ProviderSynthesisResult(null, null,
                new ProviderSynthesisFailure(ProviderSynthesisFailureKind.ProviderUnavailable, "Microsoft Edge 尚未配置 Voice。")));
        if (!AppSettings.IsValidSpeakSpeed(request.SpeakSpeed) || string.IsNullOrWhiteSpace(request.Text))
            return Task.FromResult(new ProviderSynthesisResult(null, null,
                new ProviderSynthesisFailure(ProviderSynthesisFailureKind.InvalidRequest, "语速或合成文本无效。")));
        return transport.SynthesizeAsync(voice, request.Text, request.SpeakSpeed * 2 - 100, cancellationToken);
    }
}
