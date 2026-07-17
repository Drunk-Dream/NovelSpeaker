using System.Text;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.FileStorage;

/// <summary>
/// Reads chapter slices from one normalized content file and caches the most recent book text.
/// </summary>
public sealed class BookContentReader : IBookContentReader
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private readonly Lock _cacheLock = new();
    private string? _cachedPath;
    private string? _cachedText;
    private readonly IAppStoragePathResolver _pathResolver;

    public BookContentReader(IAppStoragePathResolver pathResolver)
    {
        _pathResolver = pathResolver;
    }

    public async Task<string> ReadChapterTextAsync(
        string storedFilePath,
        int startOffset,
        int length,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storedFilePath);
        ArgumentOutOfRangeException.ThrowIfNegative(startOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        var resolvedPath = _pathResolver.ResolvePath(storedFilePath);
        var text = await GetBookTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
        if (startOffset > text.Length)
        {
            throw new InvalidDataException($"章节起始偏移 {startOffset} 超出正文长度 {text.Length}。");
        }

        if (startOffset + length > text.Length)
        {
            throw new InvalidDataException(
                $"章节范围 [{startOffset}, {startOffset + length}) 超出正文长度 {text.Length}。");
        }

        return text.Substring(startOffset, length);
    }

    private async Task<string> GetBookTextAsync(string storedFilePath, CancellationToken cancellationToken)
    {
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
