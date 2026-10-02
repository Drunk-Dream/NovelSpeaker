using System.Text;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache.Export;

namespace NovelSpeaker.Infrastructure.Books.FileStorage;

public sealed class BookTextExportService(
    IBookPlaybackMetadataQuery metadataQuery,
    IAppStoragePathResolver pathResolver,
    ExportFileNameSanitizer fileNameSanitizer) : IBookTextExportService
{
    public async Task<bool> ExportAsync(string bookId, string destinationDirectory, CancellationToken cancellationToken)
    {
        var book = await metadataQuery.GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (book is null || book.Chapters.Count == 0) return false;
        var chapter = await metadataQuery.GetChapterAsync(bookId, book.Chapters[0].ChapterIndex, cancellationToken).ConfigureAwait(false);
        if (chapter is null || string.IsNullOrWhiteSpace(chapter.StoredFilePath)) return false;
        var source = pathResolver.ResolvePath(chapter.StoredFilePath);
        if (!File.Exists(source)) return false;
        var text = await File.ReadAllTextAsync(source, new UTF8Encoding(false, true), cancellationToken).ConfigureAwait(false);
        var name = fileNameSanitizer.Sanitize(book.Title, 100);
        var temporary = Path.Combine(destinationDirectory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            await Task.Run(() =>
            {
                for (var suffix = 0; ; suffix++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = suffix == 0 ? $"{name}.txt" : $"{name} ({suffix}).txt";
                    var target = Path.Combine(destinationDirectory, fileName);
                    try
                    {
                        File.Move(temporary, target, overwrite: false);
                        return;
                    }
                    catch (IOException) when (File.Exists(target) || Directory.Exists(target)) { }
                }
            }, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            await Task.Run(() => File.Delete(temporary)).ConfigureAwait(false);
        }
    }
}
