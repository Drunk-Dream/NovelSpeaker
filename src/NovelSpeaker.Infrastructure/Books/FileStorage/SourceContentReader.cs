using System.Text;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Books.FileStorage;

/// <summary>
/// Reads chapter slices from one normalized content file and caches the most recent book text.
/// </summary>
public sealed class SourceContentReader : ISourceContentReader
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly Lock _cacheLock = new();
    private string? _cachedPath;
    private string? _cachedText;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SourceContentReader(IAppStoragePathResolver pathResolver, ISqliteConnectionFactory connectionFactory)
    {
        _pathResolver = pathResolver;
        _connectionFactory = connectionFactory;
    }

    public async Task<string> ReadChapterTextAsync(
        PlaybackChapterMetadata chapter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        if (chapter.SourceContext is not { } context || context.SourceId != chapter.SourceBindingId)
            throw new InvalidDataException("当前目录上下文无效。");
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.StoredContentPath, content.StartOffset, content.Length
            FROM Books b JOIN BookSourceBindings s ON s.BookId = b.Id AND s.Id = b.ActiveSourceBindingId
            JOIN LocalBookSourceBindings l ON l.BindingId = s.Id
            JOIN Chapters c ON c.BookId = b.Id AND c.SourceBindingId = s.Id
            JOIN LocalChapterContents content ON content.ChapterId = c.Id
            WHERE b.Id = $book AND s.Id = $source AND s.SourceType = 1 AND c.Id = $chapter
                AND c.ChapterIndex = $index
                AND (SELECT Id FROM Chapters WHERE BookId = b.Id ORDER BY ChapterIndex LIMIT 1) IS $version
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$book", chapter.BookId);
        command.Parameters.AddWithValue("$source", chapter.SourceBindingId);
        command.Parameters.AddWithValue("$chapter", chapter.ChapterId);
        command.Parameters.AddWithValue("$index", chapter.ChapterIndex);
        command.Parameters.AddWithValue("$version", (object?)context.CatalogVersion ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("来源章节已失效或正文不可用。");
        var storedFilePath = reader.GetString(0);
        var startOffset = reader.GetInt32(1);
        var length = reader.GetInt32(2);

        var resolvedPath = _pathResolver.ResolvePath(storedFilePath);
        var text = await GetBookTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
        if (startOffset > text.Length)
        {
            throw new InvalidDataException($"章节起始偏移 {startOffset} 超出正文长度 {text.Length}。");
        }

        if (length > text.Length - startOffset)
        {
            throw new InvalidDataException(
                $"章节范围 [{startOffset}, {startOffset + length}) 超出正文长度 {text.Length}。");
        }

        return text.Substring(startOffset, length);
    }

    public async Task<string> ReadBookTextAsync(string bookId, ActiveSourceContext expectedContext, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(expectedContext);
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.StoredContentPath FROM Books b
            JOIN BookSourceBindings s ON s.Id = b.ActiveSourceBindingId AND s.BookId = b.Id
            JOIN LocalBookSourceBindings l ON l.BindingId = s.Id
            WHERE b.Id = $book AND s.Id = $source AND s.SourceType = 1
                AND (SELECT Id FROM Chapters WHERE BookId = b.Id ORDER BY ChapterIndex LIMIT 1) IS $version
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$book", bookId);
        command.Parameters.AddWithValue("$source", expectedContext.SourceId);
        command.Parameters.AddWithValue("$version", (object?)expectedContext.CatalogVersion ?? DBNull.Value);
        var path = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string
            ?? throw new InvalidDataException("来源正文不可用。");
        return await GetBookTextAsync(_pathResolver.ResolvePath(path), cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetBookTextAsync(string storedFilePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_cacheLock)
        {
            if (string.Equals(_cachedPath, storedFilePath, StringComparison.Ordinal) && _cachedText is not null)
            {
                return _cachedText;
            }
        }

        if (!File.Exists(storedFilePath))
        {
            throw new FileNotFoundException("未找到已保存的正文文件。", storedFilePath);
        }

        var text = await File.ReadAllTextAsync(storedFilePath, Utf8, cancellationToken).ConfigureAwait(false);

        lock (_cacheLock)
        {
            _cachedPath = storedFilePath;
            _cachedText = text;
        }

        return text;
    }
}
