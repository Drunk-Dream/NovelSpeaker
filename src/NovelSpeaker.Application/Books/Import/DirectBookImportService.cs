using System.Text;
using System.Text.RegularExpressions;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books.Import;

/// <summary>
/// Coordinates one direct TXT import through file, persistence, and chapter-rule ports.
/// </summary>
public sealed class DirectBookImportService : IDirectBookImportService
{
    private static readonly IReadOnlyList<string> SupportedEncodings = ["utf-8", "utf-16le", "utf-16be", "gb18030"];

    private readonly ITextFileAnalyzer _textFileAnalyzer;
    private readonly ITextNormalizer _textNormalizer;
    private readonly IContentHasher _contentHasher;
    private readonly IChapterRuleRepository _chapterRuleRepository;
    private readonly IChapterSplitter _chapterSplitter;
    private readonly IBookFileStore _bookFileStore;
    private readonly IBookImportRepository _bookImportRepository;
    private readonly IBookOperationJournal _operationJournal;
    private readonly IFileNameMetadataRuleRepository _fileNameRules;
    private readonly ITextHeaderMetadataRuleRepository _headerRules;
    private readonly IAppSettingsService _settings;
    private readonly ImportMetadataExtractor _metadataExtractor;
    private readonly TimeProvider _timeProvider;
    private readonly IBookImportIdGenerator _idGenerator;
    private readonly BookMutationGate _mutations;
    private readonly BookSourceChanges _sourceChanges;

    public DirectBookImportService(
        ITextFileAnalyzer textFileAnalyzer,
        ITextNormalizer textNormalizer,
        IContentHasher contentHasher,
        IChapterRuleRepository chapterRuleRepository,
        IChapterSplitter chapterSplitter,
        IBookFileStore bookFileStore,
        IBookImportRepository bookImportRepository,
        IBookOperationJournal operationJournal,
        IFileNameMetadataRuleRepository fileNameRules,
        ITextHeaderMetadataRuleRepository headerRules,
        IAppSettingsService settings,
        ImportMetadataExtractor metadataExtractor,
        TimeProvider timeProvider,
        IBookImportIdGenerator idGenerator,
        BookMutationGate mutations,
        BookSourceChanges sourceChanges)
    {
        _textFileAnalyzer = textFileAnalyzer;
        _textNormalizer = textNormalizer;
        _contentHasher = contentHasher;
        _chapterRuleRepository = chapterRuleRepository;
        _chapterSplitter = chapterSplitter;
        _bookFileStore = bookFileStore;
        _bookImportRepository = bookImportRepository;
        _operationJournal = operationJournal;
        _fileNameRules = fileNameRules;
        _headerRules = headerRules;
        _settings = settings;
        _metadataExtractor = metadataExtractor;
        _timeProvider = timeProvider;
        _idGenerator = idGenerator;
        _mutations = mutations;
        _sourceChanges = sourceChanges;
    }

    public async Task<DirectBookImportResult> ImportAsync(
        DirectBookImportRequest request,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Parsing and catalog projection must not run unbounded on the WPF Dispatcher.
        return await Task.Run(() => ImportCoreAsync(request, progress, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<DirectBookImportResult> ImportCoreAsync(
        DirectBookImportRequest request, IProgress<BookImportProgress>? progress, CancellationToken cancellationToken)
    {
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

            return await ImportDecodedTextAsync(request, analyzedText, progress, cancellationToken);
        }
        catch (DecoderFallbackException)
        {
            if (string.IsNullOrWhiteSpace(request.EncodingOverride))
            {
                return new DirectBookImportResult(
                    DirectBookImportStatus.RequiresEncodingSelection,
                    EncodingSelectionPrompt: BuildDetectionFailurePrompt(request.FilePath, request.SourceFileName));
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
        catch (RegexMatchTimeoutException)
        {
            return new DirectBookImportResult(
                DirectBookImportStatus.Failed,
                FailureReason: BookImportFailureReason.ChapterRuleTimedOut);
        }
    }

    private async Task<DirectBookImportResult> ImportDecodedTextAsync(
        DirectBookImportRequest request,
        TextFileAnalysis analyzedText,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var normalizedText = _textNormalizer.Normalize(analyzedText.RawText);
        var sourceHash = await _contentHasher.ComputeFileHashAsync(request.FilePath, progress, cancellationToken);

        progress?.Report(new BookImportProgress(
            BookImportPhase.SplittingChapters,
            0,
            0,
            true,
            "正在识别章节。"));

        var rules = await _chapterRuleRepository.GetEnabledAsync(cancellationToken);
        var firstTitleOffset = _chapterSplitter.FindFirstExplicitTitleOffset(normalizedText, rules);
        var fileNameRules = await _fileNameRules.GetAllAsync(cancellationToken);
        var headerRules = await _headerRules.GetAllAsync(cancellationToken);
        var metadata = _metadataExtractor.Extract(
            analyzedText.SourceNameWithoutExtension,
            normalizedText,
            firstTitleOffset,
            fileNameRules,
            headerRules);
        var chapters = _chapterSplitter.Split(normalizedText, rules, _settings.Current.SplitChaptersOnBlankLines);
        if (string.IsNullOrWhiteSpace(normalizedText) || chapters.Count == 0)
        {
            return new DirectBookImportResult(
                DirectBookImportStatus.Failed,
                FailureReason: BookImportFailureReason.NoValidChapters);
        }

        if (request.CreateNewBook && request.TargetBookId is not null)
        {
            throw new ArgumentException("A target book and a new book cannot both be selected.", nameof(request));
        }

        // Preparation reads only external text and rule snapshots. Resolve the current target under the
        // mutation gate; staging stays here too because deletion can move the whole Book directory.
        return await _mutations.RunAsync(
            () => CommitPreparedTextAsync(request, analyzedText, normalizedText, sourceHash, metadata, chapters,
                progress, cancellationToken), cancellationToken);
    }

    private async Task<DirectBookImportResult> CommitPreparedTextAsync(
        DirectBookImportRequest request,
        TextFileAnalysis analyzedText,
        string normalizedText,
        string sourceHash,
        ImportMetadata metadata,
        IReadOnlyList<BookImportChapter> chapters,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        var targetBookId = request.TargetBookId;
        if (!request.CreateNewBook && targetBookId is null)
        {
            var candidates = await _bookImportRepository.FindCandidatesAsync(metadata.Title, metadata.Author, cancellationToken);
            if (candidates.Count > 1)
            {
                return new DirectBookImportResult(DirectBookImportStatus.RequiresBookSelection, BookCandidates: candidates);
            }

            targetBookId = candidates.SingleOrDefault()?.BookId;
        }

        var target = targetBookId is null
            ? null
            : await _bookImportRepository.GetTargetAsync(targetBookId, cancellationToken)
                ?? throw new InvalidOperationException("The selected book no longer exists.");
        var bookId = target?.Book.Id ?? _idGenerator.CreateBookId();
        var sourceId = target?.Source?.Id ?? _idGenerator.CreateSourceId();
        var now = _timeProvider.GetUtcNow();
        var chapterEntities = chapters.Select(chapter => new Chapter(
            _idGenerator.CreateChapterId(), sourceId, chapter.ChapterIndex, chapter.SortOrder, chapter.Title)).ToArray();
        var contents = chapters.Select((chapter, index) => new LocalChapterContent(
            chapterEntities[index].Id, chapter.StartOffset, chapter.Length)).ToArray();
        if (chapters.Where((chapter, index) => chapter.ChapterIndex != index || chapter.StartOffset < 0 ||
                chapter.Length <= 0 || chapter.StartOffset > normalizedText.Length - chapter.Length).Any())
        {
            throw new InvalidDataException("The prepared catalog does not match the normalized content.");
        }

        var copyHandle = await _bookFileStore.StageNormalizedTextAsync(normalizedText, bookId, progress, cancellationToken);
        var paths = new List<BookOperationPath>
        {
            new(copyHandle.FinalPath, copyHandle.TemporaryPath, IsDirectory: false)
        };
        if (target?.LocalSource is { } previous)
        {
            paths.Add(new BookOperationPath(previous.StoredContentPath, previous.StoredContentPath, IsDirectory: false));
        }

        var operation = new BookOperationRecord(
            _idGenerator.CreateOperationId(),
            BookOperationKind.Import,
            BookOperationPhase.Staged,
            bookId,
            paths,
            now);
        try
        {
            await _operationJournal.CreateAsync(operation, cancellationToken);
        }
        catch
        {
            // No durable recovery intent exists, so the staged file must be removed before propagating the failure.
            await _bookFileStore.CleanupAsync(copyHandle, includeFinalFile: true, CancellationToken.None);
            throw;
        }

        var book = target?.Book ?? new Book(bookId, metadata.Title, metadata.Author, sourceId, now, null, now, metadata.Description);
        var source = new BookSource(sourceId, bookId, SourceType.Local, metadata.Title, metadata.Author,
            metadata.Description, target?.Source?.CreatedAt ?? now, now);
        var localSource = new LocalBookSource(sourceId, analyzedText.SourceFileName, copyHandle.FinalPath, sourceHash,
            analyzedText.DetectedEncoding, target?.LocalSource?.ImportedAt ?? now, now);
        var snapshot = new LocalSourceImportSnapshot(book, source, localSource, chapterEntities, contents,
            target is null, target?.LocalSource?.StoredContentPath);

        try
        {
            progress?.Report(new BookImportProgress(
                BookImportPhase.SavingBook,
                0,
                0,
                true,
                "正在写入书籍和章节数据。"));
            // The new immutable file is ready before SQLite switches the source's content pointer.
            await _bookFileStore.FinalizeAsync(copyHandle, cancellationToken);
            await _bookImportRepository.SaveAsync(snapshot, operation.OperationId, cancellationToken);
        }
        catch
        {
            // A failed transaction leaves the old pointer intact. Cleanup must finish even after cancellation.
            await _bookFileStore.CleanupAsync(copyHandle, includeFinalFile: true, CancellationToken.None);
            await _operationJournal.SetPhaseAsync(operation.OperationId, BookOperationPhase.Completed, CancellationToken.None);

            throw;
        }

        // The mutation gate keeps this target authoritative through the durable commit.
        if (snapshot.IsNewBook || target?.Book.ActiveSourceId == sourceId)
        {
            _sourceChanges.Publish(new BookCommittedChange.MetadataCommitted(bookId));
            if (snapshot.IsNewBook)
            {
                _sourceChanges.Publish(new BookCommittedChange.ActiveSourceChanged(bookId, null, sourceId));
            }
            _sourceChanges.Publish(new BookCommittedChange.ActiveCatalogCommitted(bookId, sourceId, chapterEntities[0].Id));
        }

        try
        {
            if (target?.LocalSource is { } oldSource)
            {
                await _bookFileStore.CleanupAsync(new BookFileCopyHandle(oldSource.StoredContentPath,
                    oldSource.StoredContentPath), includeFinalFile: true, CancellationToken.None);
            }

            await _operationJournal.SetPhaseAsync(operation.OperationId, BookOperationPhase.Completed, CancellationToken.None);
        }
        catch (Exception)
        {
            // SQLite already owns the complete new snapshot. Startup recovery retries only obsolete-file cleanup.
        }

        progress?.Report(new BookImportProgress(BookImportPhase.Completed, 0, 0, true, "导入完成。"));

        return new DirectBookImportResult(
            DirectBookImportStatus.Imported,
            ImportedBook: new BookImportResult(bookId, source.Title, chapterEntities.Length));
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
            analysis.SourceFileName,
            reasonText,
            analysis.DetectedEncoding,
            SupportedEncodings);
    }

    private static EncodingSelectionPrompt BuildDetectionFailurePrompt(string filePath, string sourceFileName) =>
        new(filePath, sourceFileName, "无法识别编码，请手动选择后继续导入。", "utf-8", SupportedEncodings);
}
