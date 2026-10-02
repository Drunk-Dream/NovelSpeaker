namespace NovelSpeaker.Application.Books;

/// <summary>Exports complete local book text to one new UTF-8 file; returns false when unavailable.</summary>
public interface IBookTextExportService
{
    Task<bool> ExportAsync(string bookId, string destinationDirectory, CancellationToken cancellationToken);
}
