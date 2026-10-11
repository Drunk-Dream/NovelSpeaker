using System.Text;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache.Export;

namespace NovelSpeaker.Infrastructure.Books.FileStorage;

public sealed class BookTextExportService(
    IBookPlaybackMetadataQuery metadataQuery,
    ISourceContentReader contentReader,
    ExportFileNameSanitizer fileNameSanitizer) : IBookTextExportService
{
    public async Task<bool> ExportAsync(string bookId, string destinationDirectory, CancellationToken cancellationToken)
    {
        var book = await metadataQuery.GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (book is null || book.Chapters.Count == 0) return false;
        if (book.SourceContext is null) return false;
        string text;
        try
        {
            text = await contentReader.ReadBookTextAsync(bookId, book.SourceContext, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return false; }
        catch (InvalidDataException) { return false; }
        var current = await metadataQuery.GetBookHeaderAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (current?.SourceContext != book.SourceContext) return false;
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
