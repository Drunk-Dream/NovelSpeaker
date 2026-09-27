using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Executes configured HTTP Providers through the shared HTTP transport and validator.</summary>
public sealed class HttpProviderRuntime(
    HttpProviderRequestCompiler compiler,
    IHttpTtsClient httpClient,
    IProviderRequestLimiter limiter) : IProviderRuntime
{
    public SpeechProviderType Type => SpeechProviderType.Http;

    public async Task<ProviderSynthesisResult> SynthesizeAsync(
        SpeechProviderInstance provider,
        ProviderSynthesisRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (provider.Configuration is not HttpSpeechProviderConfiguration configuration ||
            !ProviderConfigurationValidator.Validate(provider).IsValid)
        {
            return Failure(ProviderSynthesisFailureKind.ProviderUnavailable, "HTTP Provider 配置不可用。");
        }

        var compiled = await compiler.CompileAsync(configuration, request, cancellationToken).ConfigureAwait(false);
        if (!compiled.IsSuccess)
        {
            return Failure(MapFailure(compiled.Failure!.Kind), compiled.Failure.Message);
        }

        await using var lease = await limiter.AcquireAsync(
            provider.Id, configuration.RateLimit, request.Priority, cancellationToken).ConfigureAwait(false);
        var execution = await httpClient.ExecuteAsync(compiled.Request!, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            if (execution.Audio is not null)
            {
                await execution.Audio.DisposeAsync().ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        if (!execution.IsSuccess)
        {
            if (execution.Failure?.RetryAfter is { } retryAfter)
            {
                limiter.ApplyRetryAfter(provider.Id, retryAfter);
            }

            return Failure(MapFailure(execution.Failure?.Kind ?? TtsErrorKind.Unknown),
                execution.Failure?.Message ?? "HTTP Provider 合成失败。");
        }

        var audio = execution.Audio!;
        try
        {
            var stream = new FileStream(audio.FilePath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous);
            return new ProviderSynthesisResult(new ProviderAudioStream(stream, audio),
                audio.ResponseContentType, null);
        }
        catch
        {
            await audio.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static ProviderSynthesisFailureKind MapFailure(TtsErrorKind kind) => kind switch
    {
        TtsErrorKind.InvalidRule or TtsErrorKind.ScriptError => ProviderSynthesisFailureKind.InvalidRequest,
        TtsErrorKind.Network => ProviderSynthesisFailureKind.Network,
        TtsErrorKind.Timeout => ProviderSynthesisFailureKind.Timeout,
        TtsErrorKind.AudioDecode or TtsErrorKind.EmptyAudioResponse or TtsErrorKind.InvalidResponse =>
            ProviderSynthesisFailureKind.InvalidAudio,
        TtsErrorKind.Cancelled => ProviderSynthesisFailureKind.Cancelled,
        _ => ProviderSynthesisFailureKind.Unknown
    };

    private static ProviderSynthesisResult Failure(ProviderSynthesisFailureKind kind, string message) =>
        new(null, null, new ProviderSynthesisFailure(kind, message));

    private sealed class ProviderAudioStream(Stream stream, TtsAudioResponse audio) : Stream
    {
        private int _disposed;

        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => stream.CanSeek;
        public override bool CanWrite => false;
        public override long Length => stream.Length;
        public override long Position { get => stream.Position; set => stream.Position = value; }
        public override void Flush() => stream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            stream.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    stream.Dispose();
                }
                finally
                {
                    audio.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                try
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                finally
                {
                    await audio.DisposeAsync().ConfigureAwait(false);
                }
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
