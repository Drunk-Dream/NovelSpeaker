namespace NovelSpeaker.Application.Books;

/// <summary>Excludes concurrent file/index mutations while a durable removal snapshots and stages storage.</summary>
public interface IBookRemovalStorageLeaseProvider
{
    Task<IDisposable> AcquireAsync(CancellationToken cancellationToken);
}
