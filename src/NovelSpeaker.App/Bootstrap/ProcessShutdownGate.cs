using NovelSpeaker.App.Shell.Activation;

namespace NovelSpeaker.App.Bootstrap;

internal sealed class ProcessShutdownGate : IProcessShutdownGate, IDisposable
{
    private readonly object _syncRoot = new();
    private CancellationTokenSource _shutdownCancellation = new();
    private bool _shutdownRequested;
    private bool _disposed;

    public bool IsShutdownRequested
    {
        get
        {
            lock (_syncRoot)
            {
                return _shutdownRequested;
            }
        }
    }

    public CancellationToken ShutdownToken
    {
        get
        {
            lock (_syncRoot)
            {
                return _shutdownCancellation.Token;
            }
        }
    }

    public bool TryBeginShutdown()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return false;
            }

            if (_shutdownRequested)
            {
                return false;
            }

            _shutdownRequested = true;
            _shutdownCancellation.Cancel();
            return true;
        }
    }

    public void CancelShutdownRequest()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            if (!_shutdownRequested)
            {
                return;
            }

            _shutdownCancellation.Dispose();
            _shutdownCancellation = new CancellationTokenSource();
            _shutdownRequested = false;
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdownCancellation.Cancel();
            _shutdownCancellation.Dispose();
        }
    }
}
