namespace NovelSpeaker.Application.Settings;

/// <summary>
/// Provides the current file-name template used to derive imported book metadata.
/// </summary>
public interface IBookFileNameTemplateProvider
{
    Task<string> GetCurrentTemplateAsync(CancellationToken cancellationToken);
}
