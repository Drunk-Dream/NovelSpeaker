using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books;

/// <summary>
/// Imports TXT files directly and only interrupts when manual encoding selection is required.
/// </summary>
public sealed class DirectBookImportService : IDirectBookImportService
{
    private static readonly IReadOnlyList<string> SupportedEncodings = ["utf-8", "utf-16le", "utf-16be", "gb18030"];

    private readonly ITextFileAnalyzer _textFileAnalyzer;
    private readonly ITextNormalizer _textNormalizer;
    private readonly IContentHasher _contentHasher;
    private readonly IBookDuplicateDetector _duplicateDetector;
    private readonly IChapterRuleRepository _chapterRuleRepository;
    private readonly IChapterSplitter _chapterSplitter;
    private readonly IBookFileStore _bookFileStore;
    private readonly IBookImportRepository _bookImportRepository;
    private readonly IBookFileNameTemplateProvider _bookFileNameTemplateProvider;
    private readonly BookFileNameMetadataParser _bookFileNameMetadataParser;
    private readonly TimeProvider _timeProvider;

    public DirectBookImportService(
        ITextFileAnalyzer textFileAnalyzer,
        ITextNormalizer textNormalizer,
        IContentHasher contentHasher,
        IBookDuplicateDetector duplicateDetector,
        IChapterRuleRepository chapterRuleRepository,
        IChapterSplitter chapterSplitter,
        IBookFileStore bookFileStore,
        IBookImportRepository bookImportRepository,
        IBookFileNameTemplateProvider bookFileNameTemplateProvider,
        BookFileNameMetadataParser bookFileNameMetadataParser,
        TimeProvider? timeProvider = null)
    {
        _textFileAnalyzer = textFileAnalyzer;
        _textNormalizer = textNormalizer;
        _contentHasher = contentHasher;
        _duplicateDetector = duplicateDetector;
        _chapterRuleRepository = chapterRuleRepository;
        _chapterSplitter = chapterSplitter;
        _bookFileStore = bookFileStore;
        _bookImportRepository = bookImportRepository;
        _bookFileNameTemplateProvider = bookFileNameTemplateProvider;
        _bookFileNameMetadataParser = bookFileNameMetadataParser;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DirectBookImportResult> ImportAsync(
        DirectBookImportRequest request,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(request.FilePath);
        var fileNameTemplate = await _bookFileNameTemplateProvider
            .GetCurrentTemplateAsync(cancellationToken)
            .ConfigureAwait(false);
        var metadata = _bookFileNameMetadataParser.Parse(fileNameWithoutExtension, fileNameTemplate);

        try
        {
            var analyzedText = await _textFileAnalyzer.AnalyzeAsync(
                new BookImportRequest(request.FilePath, request.EncodingOverride),
                progress,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(request.EncodingOverride) && analyzedText.IsLowConfidence)
            {
                return new DirectBookImportResult(
                    DirectBookImportStatus.RequiresEncodingSelection,
                    EncodingSelectionPrompt: BuildPrompt(request.FilePath, analyzedText));
            }

            return await ImportDecodedTextAsync(request.FilePath, metadata, analyzedText, progress, cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            if (string.IsNullOrWhiteSpace(request.EncodingOverride))
            {
                return new DirectBookImportResult(
                    DirectBookImportStatus.RequiresEncodingSelection,
                    EncodingSelectionPrompt: BuildDetectionFailurePrompt(request.FilePath));
            }

            return new DirectBookImportResult(
                DirectBookImportStatus.Failed,
                FailureReason: BookImportFailureReason.UnsupportedEncoding);
        }
        catch (IOException)
        {
            return new DirectBookImportResult(
                DirectBookImportStatus.Failed,
                FailureReason: BookImportFailureReason.FileReadFailed);
        }
    }

    private async Task<DirectBookImportResult> ImportDecodedTextAsync(
        string filePath,
        BookFileNameMetadataParseResult metadata,
        TextFileAnalysis analyzedText,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedText = _textNormalizer.Normalize(analyzedText.RawText);
        var sourceHash = await _contentHasher.ComputeFileHashAsync(filePath, progress, cancellationToken);
        var existingBookId = await _duplicateDetector.FindExistingBookIdAsync(sourceHash, cancellationToken);
        if (existingBookId is not null)
        {
            return new DirectBookImportResult(
                DirectBookImportStatus.Failed,
                FailureReason: BookImportFailureReason.DuplicateBook);
        }

        progress?.Report(new BookImportProgress(
            BookImportPhase.SplittingChapters,
            0,
            0,
            true,
            "正在识别章节。"));

        var rules = await _chapterRuleRepository.GetEnabledAsync(cancellationToken);
        var chapters = _chapterSplitter.Split(normalizedText, rules);
        if (string.IsNullOrWhiteSpace(normalizedText) || chapters.Count == 0)
        {
            return new DirectBookImportResult(
                DirectBookImportStatus.Failed,
                FailureReason: BookImportFailureReason.NoValidChapters);
        }

        var bookId = Guid.NewGuid().ToString();
        var copyHandle = await _bookFileStore.StageNormalizedTextAsync(normalizedText, bookId, progress, cancellationToken);
        var now = _timeProvider.GetUtcNow();

        var book = new Book(
            bookId,
            metadata.SuggestedTitle,
            metadata.SuggestedAuthor,
            Path.GetFileName(filePath),
            copyHandle.FinalPath,
            sourceHash,
            analyzedText.DetectedEncoding,
            now,
            now,
            null,
            now);

        var chapterEntities = chapters
            .Select(chapter => new Chapter(
                Guid.NewGuid().ToString(),
                bookId,
                chapter.ChapterIndex,
                chapter.SortOrder,
                chapter.Title,
                chapter.StartOffset,
                chapter.Length))
            .ToArray();

        try
        {
            progress?.Report(new BookImportProgress(
                BookImportPhase.SavingBook,
                0,
                0,
                true,
                "正在写入书籍和章节数据。"));
            await _bookFileStore.FinalizeAsync(copyHandle, cancellationToken);
            await _bookImportRepository.SaveAsync(book, chapterEntities, cancellationToken);
            progress?.Report(new BookImportProgress(
                BookImportPhase.Completed,
                0,
                0,
                true,
                "导入完成。"));
        }
        catch
        {
            await _bookFileStore.CleanupAsync(copyHandle, includeFinalFile: true);
            throw;
        }

        return new DirectBookImportResult(
            DirectBookImportStatus.Imported,
            ImportedBook: new BookImportResult(bookId, book.Title, chapterEntities.Length));
    }

    private static EncodingSelectionPrompt BuildPrompt(string filePath, TextFileAnalysis analysis)
    {
        var reasonText = analysis.LowConfidenceReason switch
        {
            LowConfidenceReason.FallbackEncoding => $"已自动回退为 {analysis.DetectedEncoding}，请确认编码后继续导入。",
            LowConfidenceReason.SuspiciousCharacters => $"自动检测为 {analysis.DetectedEncoding}，但文本样本存在可疑字符，请确认编码。",
            _ => $"自动检测为 {analysis.DetectedEncoding}，请确认编码。"
        };

        return new EncodingSelectionPrompt(
            filePath,
            Path.GetFileName(filePath),
            reasonText,
            analysis.DetectedEncoding,
            SupportedEncodings);
    }

    private static EncodingSelectionPrompt BuildDetectionFailurePrompt(string filePath)
    {
        return new EncodingSelectionPrompt(
            filePath,
            Path.GetFileName(filePath),
            "无法识别编码，请手动选择后继续导入。",
            "utf-8",
            SupportedEncodings);
    }
}
