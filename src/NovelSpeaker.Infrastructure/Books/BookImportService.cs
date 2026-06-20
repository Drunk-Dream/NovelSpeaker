using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books;

/// <summary>
/// Runs the analyze-then-commit import workflow.
/// </summary>
public sealed class BookImportService : IBookImportService
{
    private readonly ITextFileAnalyzer _textFileAnalyzer;
    private readonly ITextNormalizer _textNormalizer;
    private readonly IContentHasher _contentHasher;
    private readonly IBookDuplicateDetector _duplicateDetector;
    private readonly IChapterRuleRepository _chapterRuleRepository;
    private readonly IChapterSplitter _chapterSplitter;
    private readonly IBookFileStore _bookFileStore;
    private readonly IBookImportRepository _bookImportRepository;

    public BookImportService(
        ITextFileAnalyzer textFileAnalyzer,
        ITextNormalizer textNormalizer,
        IContentHasher contentHasher,
        IBookDuplicateDetector duplicateDetector,
        IChapterRuleRepository chapterRuleRepository,
        IChapterSplitter chapterSplitter,
        IBookFileStore bookFileStore,
        IBookImportRepository bookImportRepository)
    {
        _textFileAnalyzer = textFileAnalyzer;
        _textNormalizer = textNormalizer;
        _contentHasher = contentHasher;
        _duplicateDetector = duplicateDetector;
        _chapterRuleRepository = chapterRuleRepository;
        _chapterSplitter = chapterSplitter;
        _bookFileStore = bookFileStore;
        _bookImportRepository = bookImportRepository;
    }

    public async Task<BookImportAnalysis> AnalyzeAsync(
        string filePath,
        string? encodingName,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzedText = await _textFileAnalyzer.AnalyzeAsync(filePath, encodingName, cancellationToken);
            var normalizedText = _textNormalizer.Normalize(analyzedText.RawText);
            var sourceHash = await _contentHasher.ComputeFileHashAsync(filePath, cancellationToken);
            var existingBookId = await _duplicateDetector.FindExistingBookIdAsync(sourceHash, cancellationToken);

            if (existingBookId is not null)
            {
                return new BookImportAnalysis(
                    BookImportAnalysisStatus.Failed,
                    filePath,
                    Path.GetFileName(filePath),
                    Path.GetFileNameWithoutExtension(filePath),
                    analyzedText.EncodingName,
                    analyzedText.PreviewText,
                    normalizedText,
                    sourceHash,
                    [],
                    BookImportFailureReason.DuplicateBook,
                    existingBookId);
            }

            var rules = await _chapterRuleRepository.GetEnabledAsync(cancellationToken);
            var chapters = _chapterSplitter.Split(normalizedText, rules);

            if (chapters.Count == 0)
            {
                return new BookImportAnalysis(
                    BookImportAnalysisStatus.Failed,
                    filePath,
                    Path.GetFileName(filePath),
                    Path.GetFileNameWithoutExtension(filePath),
                    analyzedText.EncodingName,
                    analyzedText.PreviewText,
                    normalizedText,
                    sourceHash,
                    [],
                    BookImportFailureReason.NoValidChapters,
                    null);
            }

            return new BookImportAnalysis(
                BookImportAnalysisStatus.ReadyToCommit,
                filePath,
                Path.GetFileName(filePath),
                Path.GetFileNameWithoutExtension(filePath),
                analyzedText.EncodingName,
                analyzedText.PreviewText,
                normalizedText,
                sourceHash,
                chapters,
                null,
                null);
        }
        catch (DecoderFallbackException)
        {
            return new BookImportAnalysis(
                BookImportAnalysisStatus.Failed,
                filePath,
                Path.GetFileName(filePath),
                Path.GetFileNameWithoutExtension(filePath),
                "unknown",
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                BookImportFailureReason.UnsupportedEncoding,
                null);
        }
        catch (IOException)
        {
            return new BookImportAnalysis(
                BookImportAnalysisStatus.Failed,
                filePath,
                Path.GetFileName(filePath),
                Path.GetFileNameWithoutExtension(filePath),
                "unknown",
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                BookImportFailureReason.FileReadFailed,
                null);
        }
    }

    public async Task<BookImportResult> CommitAsync(BookImportAnalysis analysis, CancellationToken cancellationToken)
    {
        if (analysis.Status != BookImportAnalysisStatus.ReadyToCommit)
        {
            throw new InvalidOperationException("Only ReadyToCommit analysis results can be committed.");
        }

        var bookId = Guid.NewGuid().ToString();
        var copyHandle = await _bookFileStore.PrepareCopyAsync(analysis.OriginalFilePath, bookId, cancellationToken);
        var now = DateTime.UtcNow.ToString("O");

        var book = new Book(
            bookId,
            analysis.SuggestedTitle,
            null,
            analysis.OriginalFileName,
            copyHandle.FinalPath,
            analysis.SourceHash,
            analysis.DetectedEncoding,
            now,
            now);

        var chapters = analysis.Chapters
            .Select(chapter => new Chapter(
                Guid.NewGuid().ToString(),
                bookId,
                chapter.ChapterIndex,
                chapter.Title,
                chapter.Content,
                chapter.StartOffset,
                chapter.Length))
            .ToArray();

        try
        {
            await _bookImportRepository.SaveAsync(book, chapters, cancellationToken);
            await _bookFileStore.FinalizeAsync(copyHandle, cancellationToken);
        }
        catch
        {
            await _bookFileStore.CleanupAsync(copyHandle);
            throw;
        }

        return new BookImportResult(bookId, book.Title, chapters.Length);
    }
}
