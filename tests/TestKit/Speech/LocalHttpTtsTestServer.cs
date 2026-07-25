using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class LocalHttpTtsTestServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _serverTask;
    private readonly byte[] _wavBytes;
    private readonly byte[] _mp3Bytes;
    private readonly Dictionary<string, int> _requestCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskCompletionSource _slowRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _slowRequestGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public LocalHttpTtsTestServer()
    {
        var port = GetAvailablePort();
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(BaseUri.ToString());
        _listener.Start();
        _wavBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Audio", "demo-tone.wav"));
        _mp3Bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Audio", "demo-tone.mp3"));
        _serverTask = Task.Run(() => RunAsync(_cts.Token));
    }

    public Uri BaseUri { get; }

    public Task SlowRequestStarted => _slowRequestStarted.Task;

    public string? LastJsonBody { get; private set; }
    public string? LastFormBody { get; private set; }

    public int GetRequestCount(string path)
    {
        lock (_requestCounts)
        {
            return _requestCounts.TryGetValue(path, out var count) ? count : 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
        try
        {
            await _serverTask;
        }
        catch (HttpListenerException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await HandleAsync(context, cancellationToken);
            }
            catch
            {
                // Timeout and client-cancel tests may abort the socket after the handler starts writing.
                // For the local test server, it is sufficient to swallow the transport failure.
            }
            finally
            {
                try
                {
                    context.Response.Close();
                }
                catch (ObjectDisposedException)
                {
                    // Timeout tests may dispose the listener response while the server is unwinding.
                }
                catch (HttpListenerException)
                {
                    // The transport may already be torn down after client cancellation.
                }
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        Increment(path);

        switch (path)
        {
            case "/audio":
                await WriteAudioAsync(context.Response, _wavBytes, "audio/wav", cancellationToken);
                return;
            case "/audio-json":
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    LastJsonBody = await reader.ReadToEndAsync(cancellationToken);
                }

                await WriteAudioAsync(context.Response, _mp3Bytes, "audio/mpeg", cancellationToken);
                return;
            case "/audio-form":
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    LastFormBody = await reader.ReadToEndAsync(cancellationToken);
                }

                await WriteAudioAsync(context.Response, _wavBytes, "audio/wav", cancellationToken);
                return;
            case "/error-json":
                context.Response.StatusCode = 400;
                await WriteTextAsync(context.Response, """{"token":"super-secret","message":"denied"}""", "application/json", cancellationToken);
                return;
            case "/error-text":
                context.Response.StatusCode = 400;
                await WriteTextAsync(context.Response, "token=super-secret denied", "text/plain", cancellationToken);
                return;
            case "/unauthorized":
                context.Response.StatusCode = 401;
                await WriteTextAsync(context.Response, "unauthorized", "text/plain", cancellationToken);
                return;
            case "/rate-limited":
                if (GetRequestCount(path) == 1)
                {
                    context.Response.StatusCode = 429;
                    context.Response.Headers["Retry-After"] = "0";
                    await WriteTextAsync(context.Response, "slow down", "text/plain", cancellationToken);
                    return;
                }

                await WriteAudioAsync(context.Response, _wavBytes, "audio/wav", cancellationToken);
                return;
            case "/server-error":
                if (GetRequestCount(path) <= 2)
                {
                    context.Response.StatusCode = 500;
                    await WriteTextAsync(context.Response, "server busy", "text/plain", cancellationToken);
                    return;
                }

                await WriteAudioAsync(context.Response, _wavBytes, "audio/wav", cancellationToken);
                return;
            case "/slow":
                _slowRequestStarted.TrySetResult();
                await _slowRequestGate.Task.WaitAsync(cancellationToken);
                await WriteAudioAsync(context.Response, _wavBytes, "audio/wav", cancellationToken);
                return;
            case "/empty":
                context.Response.StatusCode = 200;
                context.Response.ContentType = "audio/wav";
                return;
            case "/corrupt-audio":
                context.Response.StatusCode = 200;
                await WriteAudioAsync(context.Response, Encoding.UTF8.GetBytes("not-an-audio-file"), "audio/mpeg", cancellationToken);
                return;
            case "/cookie-required":
                if ((context.Request.Headers["Cookie"] ?? string.Empty).Contains("session=rule-cookie", StringComparison.Ordinal))
                {
                    await WriteAudioAsync(context.Response, _wavBytes, "audio/wav", cancellationToken);
                    return;
                }

                context.Response.StatusCode = 401;
                await WriteTextAsync(context.Response, "missing cookie", "text/plain", cancellationToken);
                return;
            default:
                context.Response.StatusCode = 404;
                await WriteTextAsync(context.Response, "not found", "text/plain", cancellationToken);
                return;
        }
    }

    private static async Task WriteAudioAsync(
        HttpListenerResponse response,
        byte[] bytes,
        string contentType,
        CancellationToken cancellationToken)
    {
        response.StatusCode = 200;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.LongLength;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private static async Task WriteTextAsync(
        HttpListenerResponse response,
        string text,
        string contentType,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        response.ContentType = contentType;
        response.ContentLength64 = bytes.LongLength;
        await response.OutputStream.WriteAsync(bytes, cancellationToken);
    }

    private void Increment(string path)
    {
        lock (_requestCounts)
        {
            _requestCounts[path] = _requestCounts.TryGetValue(path, out var count) ? count + 1 : 1;
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
