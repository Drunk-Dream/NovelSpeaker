namespace NovelSpeaker.Application.Books;

/// <summary>Serializes durable import/removal operations across SQLite and content files.</summary>
public sealed class BookMutationGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await operation().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public void Dispose() => _gate.Dispose();
}
