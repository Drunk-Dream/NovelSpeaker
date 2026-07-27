using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Execution;

/// <summary>Coordinates transport retries and response validation without owning HTTP or audio technology.</summary>
public sealed class TtsExecutionService : IHttpTtsClient
{
    private readonly ITtsHttpTransport _transport;
    private readonly ITtsRetryPolicy _retryPolicy;
    private readonly ITtsResponseValidator _responseValidator;

    public TtsExecutionService(
        ITtsHttpTransport transport,
        ITtsRetryPolicy retryPolicy,
        ITtsResponseValidator responseValidator)
    {
        _transport = transport;
        _retryPolicy = retryPolicy;
        _responseValidator = responseValidator;
    }

    public async Task<TtsHttpExecutionResult> ExecuteAsync(
        ParsedTtsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Headers.Keys.Any(static key => key.Equals("Cookie", StringComparison.OrdinalIgnoreCase)))
        {
            return Failure(TtsErrorKind.InvalidRule, "当前版本不支持 Cookie/LoginInfo 规则依赖。");
        }

        var completedRetries = 0;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Failure(TtsErrorKind.Cancelled, "已取消当前 HTTP TTS 请求。");
            }

            TtsTransportResult transportResult;
            try
            {
                transportResult = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failure(TtsErrorKind.Cancelled, "已取消当前 HTTP TTS 请求。");
            }
            catch (Exception)
            {
                return Failure(TtsErrorKind.Unknown, "HTTP TTS 执行失败，请稍后重试。");
            }

            if (!transportResult.IsSuccess)
            {
                if (_retryPolicy.ShouldRetry(completedRetries, transportResult.FailureKind, null))
                {
                    completedRetries++;
                    continue;
                }

                return transportResult.FailureKind switch
                {
                    TtsTransportFailureKind.Timeout => Failure(TtsErrorKind.Timeout, "请求超时，请稍后重试。"),
                    TtsTransportFailureKind.Network => Failure(TtsErrorKind.Network, "网络请求失败，请检查网络连接后重试。"),
                    _ => Failure(TtsErrorKind.Unknown, "HTTP TTS 执行失败，请稍后重试。")
                };
            }

            var response = transportResult.Response!;
            try
            {
                await using (response.ConfigureAwait(false))
                {
                    if (_retryPolicy.ShouldRetry(completedRetries, null, response.StatusCode))
                    {
                        completedRetries++;
                        continue;
                    }

                    return await _responseValidator.ValidateAsync(request, response, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return Failure(TtsErrorKind.Cancelled, "已取消当前 HTTP TTS 请求。");
            }
            catch (OperationCanceledException) when (response.IsReadTimedOut)
            {
                return Failure(TtsErrorKind.Timeout, "请求超时，请稍后重试。");
            }
            catch (Exception)
            {
                return Failure(TtsErrorKind.Unknown, "HTTP TTS 执行失败，请稍后重试。");
            }
        }
    }

    private static TtsHttpExecutionResult Failure(TtsErrorKind kind, string message) =>
        new(null, new TtsExecutionFailure(kind, message, null, null, null, null));
}
