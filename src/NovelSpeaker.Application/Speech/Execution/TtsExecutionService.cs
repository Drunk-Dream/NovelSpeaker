using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Execution;

/// <summary>Coordinates transport retries and response validation without owning HTTP or audio technology.</summary>
public sealed class TtsExecutionService : IHttpTtsClient
{
    private readonly ITtsHttpTransport _transport;
    private readonly ITtsRetryPolicy _retryPolicy;
    private readonly ITtsResponseValidator _responseValidator;
    private readonly IObservability _observability;

    public TtsExecutionService(
        ITtsHttpTransport transport,
        ITtsRetryPolicy retryPolicy,
        ITtsResponseValidator responseValidator,
        IObservability? observability = null)
    {
        _transport = transport;
        _retryPolicy = retryPolicy;
        _responseValidator = responseValidator;
        _observability = observability ?? new ObservabilityHub(new ObservabilityContextAccessor());
    }

    public async Task<TtsHttpExecutionResult> ExecuteAsync(
        ParsedTtsRequest request,
        CancellationToken cancellationToken)
    {
        using var operation = _observability.StartOperation(OperationCatalog.TtsRequest);
        try
        {
            var result = await ExecuteCoreAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                operation.Complete(OperationResult.Succeeded());
            }
            else if (result.Failure?.Kind == TtsErrorKind.Cancelled)
            {
                operation.Complete(OperationResult.Cancelled());
            }
            else
            {
                operation.Complete(OperationResult.Failed("tts-failed"));
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            operation.Complete(OperationResult.Failed("tts-failed"));
            throw;
        }
    }

    private async Task<TtsHttpExecutionResult> ExecuteCoreAsync(
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
                    using var retryOperation = _observability.StartOperation(OperationCatalog.TtsRetry);
                    retryOperation.Complete(OperationResult.Succeeded());
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
                        using var retryOperation = _observability.StartOperation(OperationCatalog.TtsRetry);
                        retryOperation.Complete(OperationResult.Succeeded());
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
