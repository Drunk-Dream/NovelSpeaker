namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Prepares the local database for application startup.
/// </summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
