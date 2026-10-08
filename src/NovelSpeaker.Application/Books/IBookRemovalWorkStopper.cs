namespace NovelSpeaker.Application.Books;

/// <summary>Lets a runtime owner release source work and file leases before durable removal.</summary>
public interface IBookRemovalWorkStopper
{
    Task StopForRemovalAsync(string bookId, string? sourceId, CancellationToken cancellationToken);
}
