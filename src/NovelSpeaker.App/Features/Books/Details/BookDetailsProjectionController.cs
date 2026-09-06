using System.Collections.ObjectModel;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.App.Features.Books.Shared;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Features.Books.Details;

/// <summary>
/// Owns the details feature's immutable chapter catalog and sparse current/cache decorations.
/// The view model consumes only page-facing state and targeted projection operations.
/// </summary>
internal sealed class BookDetailsProjectionController
{
    private const int CacheDecorationWindowSize = 32;
    private const int CatalogIndexingTaskThreshold = 512;
    private readonly ResettableObservableCollection<BookDetailsChapterProjection> _chapters = [];
    private readonly SparseCatalogDecoration<bool> _currentChapterDecoration = new();
    private readonly SparseCatalogDecoration<string> _cacheDecorations = new();
    private readonly HashSet<int> _cacheDecorationWindow = [];
    private readonly HashSet<int> _explicitCacheStatusRequests = [];
    private BookDetailsChapterCatalog _catalog = new([]);
    private BookReadingPosition? _readingPosition;
    private int? _currentChapterIndex;
    private BookDetailsChapterProjection? _currentChapterItem;

    public ObservableCollection<BookDetailsChapterProjection> Chapters => _chapters;

    public int CatalogCount => _catalog.Count;

    public bool ContainsChapter(int chapterIndex) => _catalog.Contains(chapterIndex);

    public BookDetailsChapterProjection? CurrentChapterItem => _currentChapterItem;

    public int? CurrentChapterPosition => _currentChapterIndex is int chapterIndex &&
                                           _catalog.TryGetPosition(chapterIndex, out var position)
        ? position
        : null;

    public bool IsCatalogReady { get; private set; }

    public void Reset()
    {
        _catalog = new BookDetailsChapterCatalog([]);
        _readingPosition = null;
        _currentChapterIndex = null;
        _currentChapterItem = null;
        IsCatalogReady = false;
        _currentChapterDecoration.Clear();
        _cacheDecorations.Clear();
        _cacheDecorationWindow.Clear();
        _explicitCacheStatusRequests.Clear();
        _chapters.Clear();
    }

    public async Task<EffectiveReadingProgress> ReplaceCatalogAsync(
        string bookId,
        IReadOnlyList<BookChapterSummary> catalog,
        BookReadingPosition? readingPosition,
        PlaybackSnapshot initialSnapshot,
        IUiScheduler uiScheduler,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(initialSnapshot);
        ArgumentNullException.ThrowIfNull(uiScheduler);

        var nextCatalog = catalog.Count >= CatalogIndexingTaskThreshold
            ? await Task.Run(
                () => new BookDetailsChapterCatalog(catalog),
                cancellationToken).ConfigureAwait(true)
            : new BookDetailsChapterCatalog(catalog);
        var progress = ProjectProgress(bookId, nextCatalog, readingPosition, initialSnapshot);
        _catalog = nextCatalog;
        _readingPosition = readingPosition;
        _currentChapterDecoration.Clear();
        _cacheDecorations.Clear();
        _cacheDecorationWindow.Clear();
        _explicitCacheStatusRequests.Clear();
        SetCurrentChapterDecoration(progress.CurrentChapterIndex);
        _currentChapterItem = null;
        IsCatalogReady = false;

        var currentDecoration = _currentChapterDecoration.Snapshot();
        var cacheDecoration = _cacheDecorations.Snapshot();
        await _chapters.ReplaceWithInBatchesAsync(
            nextCatalog.Items,
            chapter => CreateChapterItem(chapter, currentDecoration, cacheDecoration),
            uiScheduler,
            cancellationToken,
            notifyEachBatch: false).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();

        IsCatalogReady = true;
        var latestProgress = ProjectProgress(bookId, _catalog, _readingPosition, initialSnapshot);
        ApplyReadingProgress(latestProgress, notify: false);
        ApplyStoredDecorations();
        return latestProgress;
    }

    public EffectiveReadingProgress ApplyPlaybackSnapshot(
        string bookId,
        PlaybackSnapshot snapshot)
    {
        var progress = ProjectProgress(bookId, _catalog, _readingPosition, snapshot);
        ApplyReadingProgress(progress);
        return progress;
    }

    public void SetCacheDecorationWindow(IReadOnlyCollection<int> chapterIndices)
    {
        ArgumentNullException.ThrowIfNull(chapterIndices);
        _cacheDecorationWindow.Clear();
        _cacheDecorationWindow.UnionWith(chapterIndices);
    }

    public IReadOnlyList<int> GetCacheDecorationWindow(
        string bookId,
        PlaybackSnapshot playbackSnapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(playbackSnapshot);
        if (_catalog.Count == 0)
        {
            return [];
        }

        var currentChapterIndex = string.Equals(playbackSnapshot.BookId, bookId, StringComparison.Ordinal) &&
                                  playbackSnapshot.ChapterIndex >= 0
            ? playbackSnapshot.ChapterIndex
            : _readingPosition?.ChapterIndex;
        if (currentChapterIndex is null || !_catalog.TryGetPosition(currentChapterIndex.Value, out var currentPosition))
        {
            return _catalog.Slice(0, CacheDecorationWindowSize)
                .Select(static chapter => chapter.ChapterIndex)
                .ToArray();
        }

        var start = Math.Max(0, currentPosition - (CacheDecorationWindowSize / 4));
        return _catalog.Slice(start, CacheDecorationWindowSize)
            .Select(static chapter => chapter.ChapterIndex)
            .ToArray();
    }

    public IReadOnlyList<int> GetChapterIndices(int start, int count)
    {
        if (_catalog.Count == 0 || count <= 0)
        {
            return [];
        }

        var boundedStart = Math.Clamp(start, 0, _catalog.Count - 1);
        return _catalog.Slice(boundedStart, count)
            .Select(static chapter => chapter.ChapterIndex)
            .ToArray();
    }

    public void MarkExplicitCacheStatusRequest(int chapterIndex)
    {
        if (_catalog.TryGet(chapterIndex, out _))
        {
            _explicitCacheStatusRequests.Add(chapterIndex);
        }
    }

    public void ClearStaleCacheDecorations(IReadOnlyCollection<int> requestedChapterIndices)
    {
        ArgumentNullException.ThrowIfNull(requestedChapterIndices);
        var requested = requestedChapterIndices.ToHashSet();
        foreach (var chapterIndex in _cacheDecorations.Snapshot().Keys)
        {
            if (requested.Contains(chapterIndex))
            {
                continue;
            }

            _cacheDecorations.Remove(chapterIndex);
            ReplaceChapterItem(chapterIndex, CreateChapterItem(_catalog[GetPosition(chapterIndex)]));
        }
    }

    public bool ApplyChapterCacheStatuses(
        IReadOnlyCollection<int> requestedChapterIndices,
        IReadOnlyCollection<ChapterCacheStatus> statuses)
    {
        ArgumentNullException.ThrowIfNull(requestedChapterIndices);
        ArgumentNullException.ThrowIfNull(statuses);
        var previousCurrentItem = _currentChapterItem;
        var statusesByChapter = statuses.ToDictionary(static status => status.ChapterIndex);
        foreach (var chapterIndex in requestedChapterIndices)
        {
            if (!_cacheDecorationWindow.Contains(chapterIndex) &&
                !_explicitCacheStatusRequests.Contains(chapterIndex))
            {
                continue;
            }

            if (!_catalog.TryGetPosition(chapterIndex, out _))
            {
                continue;
            }

            var status = statusesByChapter.GetValueOrDefault(chapterIndex);
            var formatted = ChapterCachePercentageFormatter.Format(
                status?.CachedSegmentCount ?? 0,
                status?.TotalSegmentCount);
            if (string.IsNullOrEmpty(formatted))
            {
                _cacheDecorations.Remove(chapterIndex);
            }
            else
            {
                _cacheDecorations.Set(chapterIndex, formatted);
            }

            ReplaceChapterItem(
                chapterIndex,
                CreateChapterItem(_catalog[GetPosition(chapterIndex)]));
            _explicitCacheStatusRequests.Remove(chapterIndex);
        }

        return !ReferenceEquals(previousCurrentItem, _currentChapterItem);
    }

    private EffectiveReadingProgress ProjectProgress(
        string bookId,
        BookDetailsChapterCatalog catalog,
        BookReadingPosition? readingPosition,
        PlaybackSnapshot snapshot) =>
        EffectiveReadingProgressProjector.Project(
            bookId,
            catalog.Items,
            readingPosition,
            snapshot,
            chapterIndex => catalog.TryGetPosition(chapterIndex, out var position) ? position : null);

    private void ApplyReadingProgress(EffectiveReadingProgress progress, bool notify = true)
    {
        var nextChapterIndex = progress.HasReadingProgress
            ? progress.CurrentChapterIndex
            : null;
        if (_currentChapterIndex != nextChapterIndex)
        {
            var previousChapterIndex = _currentChapterIndex;
            SetCurrentChapterDecoration(nextChapterIndex);
            if (previousChapterIndex is int previous)
            {
                ReplaceChapterItem(previous, CreateChapterItem(_catalog[GetPosition(previous)]), notify);
            }

            if (nextChapterIndex is int next)
            {
                ReplaceChapterItem(next, CreateChapterItem(_catalog[GetPosition(next)]), notify);
            }
        }
        else if (nextChapterIndex is int current)
        {
            ReplaceChapterItem(current, CreateChapterItem(_catalog[GetPosition(current)]), notify);
        }

        _currentChapterItem = nextChapterIndex is int chapterIndex &&
                              _catalog.TryGetPosition(chapterIndex, out var position) &&
                              (uint)position < (uint)_chapters.Count
            ? _chapters[position]
            : null;
    }

    private void SetCurrentChapterDecoration(int? chapterIndex)
    {
        _currentChapterDecoration.Clear();
        if (chapterIndex is int current)
        {
            _currentChapterDecoration.Set(current, true);
        }

        _currentChapterIndex = chapterIndex;
    }

    private BookDetailsChapterProjection CreateChapterItem(
        BookChapterSummary chapter,
        IReadOnlyDictionary<int, bool>? currentSnapshot = null,
        IReadOnlyDictionary<int, string>? cacheSnapshot = null)
    {
        var isCurrent = currentSnapshot is not null
            ? currentSnapshot.TryGetValue(chapter.ChapterIndex, out var current) && current
            : _currentChapterDecoration.TryGet(chapter.ChapterIndex, out current) && current;
        var cachePercentage = cacheSnapshot is not null
            ? cacheSnapshot.TryGetValue(chapter.ChapterIndex, out var cache) ? cache : string.Empty
            : _cacheDecorations.TryGet(chapter.ChapterIndex, out cache) ? cache : string.Empty;
        return new BookDetailsChapterProjection(chapter.ChapterIndex, chapter.Title, isCurrent, cachePercentage);
    }

    private void ReplaceChapterItem(int chapterIndex, BookDetailsChapterProjection item, bool notify = true)
    {
        if (!_catalog.TryGetPosition(chapterIndex, out var position) ||
            (uint)position >= (uint)_chapters.Count)
        {
            return;
        }

        var existing = _chapters[position];
        if (existing.IsCurrent == item.IsCurrent &&
            string.Equals(existing.CachePercentageText, item.CachePercentageText, StringComparison.Ordinal))
        {
            return;
        }

        _chapters.ReplaceAt(position, item, notify);
        if (_currentChapterIndex == chapterIndex)
        {
            _currentChapterItem = item;
        }
    }

    private void ApplyStoredDecorations()
    {
        foreach (var chapterIndex in _cacheDecorations.Snapshot().Keys)
        {
            if (_catalog.TryGetPosition(chapterIndex, out var position) &&
                (uint)position < (uint)_chapters.Count)
            {
                ReplaceChapterItem(chapterIndex, CreateChapterItem(_catalog[position]));
            }
        }
    }

    private int GetPosition(int chapterIndex)
    {
        return _catalog.TryGetPosition(chapterIndex, out var position)
            ? position
            : throw new InvalidOperationException($"Chapter {chapterIndex} is not in the catalog.");
    }
}
