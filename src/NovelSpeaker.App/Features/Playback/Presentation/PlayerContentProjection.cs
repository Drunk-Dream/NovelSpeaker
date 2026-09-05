using System.Collections.ObjectModel;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Features.Playback.Presentation;

/// <summary>
/// Owns the playback page's book/chapter content cache and its chapter/segment item projection.
/// Playback session state remains owned by <see cref="IPlaybackSession"/>.
/// </summary>
internal sealed class PlayerContentProjection
{
    private readonly IBookPlaybackContentService _contentService;
    private readonly IUiScheduler _uiScheduler;
    private readonly object _syncRoot = new();
    private readonly Dictionary<int, PlaybackChapterContent> _chapterCache = [];
    private readonly ResettableObservableCollection<PlayerChapterItemViewModel> _chapters = [];
    private readonly ResettableObservableCollection<PlayerSegmentItemViewModel> _segments = [];
    private readonly SparseCatalogDecoration<bool> _currentChapterDecoration = new();
    private readonly SparseCatalogDecoration<bool> _activeCacheSelectionDecoration = new();
    private readonly SparseCatalogDecoration<string> _cacheDecorations = new();
    private readonly HashSet<int> _cacheDecorationWindow = [];

    private PlaybackBookContent? _loadedBook;
    private IndexedCatalog<PlaybackChapterSummaryMetadata> _chapterCatalog =
        new([], static chapter => chapter.ChapterIndex);
    private int _loadedChapterIndex = -1;
    private int _bookLoadVersion;
    private int _chapterLoadVersion;
    private long _lastContentRevision;
    private long _positionRevision;
    private int _latestPositionChapterIndex = -1;
    private int _latestPositionSegmentIndex;
    private int _latestPositionSegmentCount;
    private string? _bookLoadTarget;
    private CancellationTokenSource? _bookProjectionCancellation;

    public PlayerContentProjection(IBookPlaybackContentService contentService, IUiScheduler? uiScheduler = null)
    {
        _contentService = contentService ?? throw new ArgumentNullException(nameof(contentService));
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
    }

    public ObservableCollection<PlayerChapterItemViewModel> Chapters => _chapters;

    public ObservableCollection<PlayerSegmentItemViewModel> Segments => _segments;

    public PlaybackBookContent? LoadedBook => _loadedBook;

    public int ChapterCatalogVersion { get; private set; }

    public IReadOnlyList<int> ChapterIndices => _chapterCatalog.Keys;

    public IReadOnlyDictionary<int, int> ChapterPositions => _chapterCatalog.Positions;

    public PlayerChapterItemViewModel? CurrentChapterItem { get; private set; }

    public PlayerSegmentItemViewModel? CurrentSegmentItem { get; private set; }

    public string CurrentChapterTitle { get; private set; } = "尚未定位章节";

    public int CurrentChapterSegmentCount { get; private set; }

    public bool CanGoToPreviousChapter { get; private set; }

    public bool CanGoToNextChapter { get; private set; }

    public bool CanGoToPreviousSegment { get; private set; }

    public bool CanGoToNextSegment { get; private set; }

    public bool IsChapterLoaded(int chapterIndex) =>
        chapterIndex >= 0 && _loadedChapterIndex == chapterIndex;

    public void InvalidatePendingLoads()
    {
        CancellationTokenSource? projectionCancellation;
        lock (_syncRoot)
        {
            ++_bookLoadVersion;
            ++_chapterLoadVersion;
            _bookLoadTarget = null;
            projectionCancellation = _bookProjectionCancellation;
            _bookProjectionCancellation = null;
        }

        projectionCancellation?.Cancel();
    }

    public bool TryGetChapterItem(int chapterIndex, out PlayerChapterItemViewModel item)
    {
        if (_chapterCatalog.TryGetPosition(chapterIndex, out var position))
        {
            if (position >= _chapters.Count)
            {
                item = null!;
                return false;
            }

            item = _chapters[position];
            return true;
        }

        item = null!;
        return false;
    }

    public int? GetChapterPosition(int chapterIndex) =>
        _chapterCatalog.TryGetPosition(chapterIndex, out var position) ? position : null;

    public bool IsChapterCacheDecorationRequested(int chapterIndex) =>
        _cacheDecorationWindow.Contains(chapterIndex);

    public IReadOnlyList<int> GetChapterIndices(int start, int count) =>
        _chapterCatalog
            .Slice(start, count)
            .Select(static chapter => chapter.ChapterIndex)
            .ToArray();

    public void ApplyChapterCacheStatus(
        int chapterIndex,
        int cachedSegmentCount,
        int? totalSegmentCount)
    {
        if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
        {
            return;
        }

        var cachePercentage = ChapterCachePercentageFormatter.Format(cachedSegmentCount, totalSegmentCount);
        if (string.IsNullOrEmpty(cachePercentage))
        {
            _cacheDecorations.Remove(chapterIndex);
        }
        else
        {
            _cacheDecorations.Set(chapterIndex, cachePercentage);
        }

        ReplaceChapterItem(chapterIndex, CreateChapterItem(_chapterCatalog[position]));
    }

    public void SetCacheDecorationWindow(IReadOnlyCollection<int> chapterIndices)
    {
        ArgumentNullException.ThrowIfNull(chapterIndices);
        var requested = chapterIndices.ToHashSet();
        var replacements = new List<(int Index, PlayerChapterItemViewModel Item)>();
        foreach (var chapterIndex in _cacheDecorations.Snapshot().Keys)
        {
            if (requested.Contains(chapterIndex))
            {
                continue;
            }

            _cacheDecorations.Remove(chapterIndex);
            if (_chapterCatalog.TryGetPosition(chapterIndex, out var position) &&
                (position < _chapters.Count ||
                 _chapters.IsProjectionPending ||
                 _chapters.IsReplacing))
            {
                replacements.Add((position, CreateChapterItem(_chapterCatalog[position])));
            }
        }

        _cacheDecorationWindow.Clear();
        _cacheDecorationWindow.UnionWith(requested);
        if (replacements.Count > 0)
        {
            _chapters.ReplaceAtMany(replacements);
        }
    }

    public void ApplyChapterSelection(int chapterIndex, bool isSelected)
    {
        if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
        {
            return;
        }

        if (isSelected)
        {
            _activeCacheSelectionDecoration.Set(chapterIndex, true);
        }
        else
        {
            _activeCacheSelectionDecoration.Remove(chapterIndex);
        }

        ReplaceChapterItem(chapterIndex, CreateChapterItem(_chapterCatalog[position]));
    }

    public void ApplyChapterSelections(
        IReadOnlyCollection<(int ChapterIndex, bool IsSelected)> selections,
        bool notify = true)
    {
        var replacements = new List<(int Index, PlayerChapterItemViewModel Item)>(selections.Count);
        foreach (var (chapterIndex, isSelected) in selections)
        {
            if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
            {
                continue;
            }

            if (isSelected)
            {
                _activeCacheSelectionDecoration.Set(chapterIndex, true);
            }
            else
            {
                _activeCacheSelectionDecoration.Remove(chapterIndex);
            }

            replacements.Add((position, CreateChapterItem(_chapterCatalog[position])));
        }

        if (replacements.Count > 0)
        {
            _chapters.ReplaceAtMany(replacements, notify);
            if (CurrentChapterItem is { } currentItem &&
                replacements.FirstOrDefault(replacement => replacement.Item.ChapterIndex == currentItem.ChapterIndex) is var currentReplacement &&
                currentReplacement.Item is not null)
            {
                CurrentChapterItem = currentReplacement.Item;
            }
        }
    }

    public void NotifyChapterReset() => _chapters.NotifyReset();

    public async Task<PlaybackBookContent?> EnsureBookLoadedAsync(
        string bookId,
        int currentChapterIndex,
        int currentSegmentIndex,
        CancellationToken cancellationToken)
    {
        if (_loadedBook is not null &&
            string.Equals(_loadedBook.BookId, bookId, StringComparison.Ordinal) &&
            _bookLoadTarget is null)
        {
            return _loadedBook;
        }

        var loadVersion = BeginBookLoad(bookId);
        var book = await _contentService.GetBookAsync(bookId, cancellationToken);
        if (loadVersion != _bookLoadVersion || book is null)
        {
            if (loadVersion == _bookLoadVersion)
            {
                _bookLoadTarget = null;
            }

            return null;
        }

        if (!await ApplyLoadedBookAsync(
                book,
                loadVersion,
                currentChapterIndex,
                currentSegmentIndex,
                cancellationToken) ||
            loadVersion != Volatile.Read(ref _bookLoadVersion))
        {
            return null;
        }

        lock (_syncRoot)
        {
            if (loadVersion == _bookLoadVersion)
            {
                _bookLoadTarget = null;
            }
        }

        return loadVersion == Volatile.Read(ref _bookLoadVersion) ? book : null;
    }

    public async Task EnsureContentLoadedAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.BookId) || snapshot.ChapterIndex < 0)
        {
            return;
        }

        var book = await EnsureBookLoadedAsync(
            snapshot.BookId,
            snapshot.ChapterIndex,
            snapshot.SegmentIndex,
            cancellationToken);
        if (book is null)
        {
            return;
        }

        var bookLoadVersion = _bookLoadVersion;
        if (!ReferenceEquals(_loadedBook, book))
        {
            return;
        }

        if (snapshot.ContentRevision != _lastContentRevision)
        {
            _chapterCache.Remove(snapshot.ChapterIndex);
            if (_loadedChapterIndex == snapshot.ChapterIndex)
            {
                _loadedChapterIndex = -1;
            }

            _lastContentRevision = snapshot.ContentRevision;
        }

        if (!await EnsureChapterLoadedAsync(book.BookId, snapshot.ChapterIndex, cancellationToken) ||
            bookLoadVersion != _bookLoadVersion ||
            !ReferenceEquals(_loadedBook, book))
        {
            return;
        }

        ApplyPosition(snapshot.ChapterIndex, snapshot.SegmentIndex, snapshot.SegmentCount);
    }

    public void ApplyPosition(int chapterIndex, int segmentIndex, int segmentCount)
    {
        lock (_syncRoot)
        {
            _positionRevision++;
            _latestPositionChapterIndex = chapterIndex;
            _latestPositionSegmentIndex = segmentIndex;
            _latestPositionSegmentCount = segmentCount;
            if (segmentCount > 0)
            {
                CurrentChapterSegmentCount = segmentCount;
            }

            UpdateChapterProjection(chapterIndex);
            UpdateSegmentProjection(segmentIndex);
            UpdateNavigationAvailability(chapterIndex, segmentIndex);
        }
    }

    public string ResolveChapterTitle(int chapterIndex)
    {
        if (_loadedBook is not null)
        {
            if (_chapterCatalog.TryGet(chapterIndex, out var chapter))
            {
                return chapter.Title;
            }
        }

        return "尚未定位章节";
    }

    private async Task<bool> EnsureChapterLoadedAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        if (_loadedBook is null || !string.Equals(_loadedBook.BookId, bookId, StringComparison.Ordinal))
        {
            return false;
        }

        // Playback emits multiple snapshots for a single transition. Retain projected items so a
        // virtualized container is not discarded while the View is centering the current segment.
        if (_loadedChapterIndex == chapterIndex)
        {
            return true;
        }

        if (_chapterCache.TryGetValue(chapterIndex, out var cachedChapter))
        {
            ApplyChapterContent(cachedChapter);
            return true;
        }

        var loadVersion = ++_chapterLoadVersion;
        var chapter = await _contentService.GetChapterAsync(bookId, chapterIndex, cancellationToken);
        if (loadVersion != _chapterLoadVersion || chapter is null)
        {
            return false;
        }

        _chapterCache[chapter.ChapterIndex] = chapter;
        ApplyChapterContent(chapter);
        return true;
    }

    private int BeginBookLoad(string bookId)
    {
        CancellationTokenSource? previousProjectionCancellation;
        int loadVersion;
        lock (_syncRoot)
        {
            loadVersion = ++_bookLoadVersion;
            _bookLoadTarget = bookId;
            ++_chapterLoadVersion;
            previousProjectionCancellation = _bookProjectionCancellation;
            _bookProjectionCancellation = null;
        }

        previousProjectionCancellation?.Cancel();
        return loadVersion;
    }

    private async Task<bool> ApplyLoadedBookAsync(
        PlaybackBookContent book,
        int loadVersion,
        int currentChapterIndex,
        int currentSegmentIndex,
        CancellationToken cancellationToken)
    {
        var projectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_syncRoot)
        {
            if (loadVersion != _bookLoadVersion)
            {
                projectionCancellation.Dispose();
                return false;
            }

            _bookProjectionCancellation = projectionCancellation;
        }

        try
        {
            long initialPositionRevision;
            lock (_syncRoot)
            {
                initialPositionRevision = _positionRevision;
            }

            var chapterCatalog = await Task.Run(
                () => new IndexedCatalog<PlaybackChapterSummaryMetadata>(
                    book.Chapters
                        .Select(chapter => new PlaybackChapterSummaryMetadata(chapter.ChapterIndex, chapter.Title))
                        .ToArray(),
                    static chapter => chapter.ChapterIndex),
                projectionCancellation.Token).ConfigureAwait(true);

            projectionCancellation.Token.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                if (loadVersion != _bookLoadVersion)
                {
                    return false;
                }

                var isDifferentBook = !string.Equals(_loadedBook?.BookId, book.BookId, StringComparison.Ordinal);
                var projectedChapterIndex = initialPositionRevision == _positionRevision
                    ? currentChapterIndex
                    : _latestPositionChapterIndex;
                var projectedSegmentIndex = initialPositionRevision == _positionRevision
                    ? currentSegmentIndex
                    : _latestPositionSegmentIndex;
                var projectedSegmentCount = _latestPositionSegmentCount;
                _loadedBook = book;

                if (isDifferentBook)
                {
                    ++_chapterLoadVersion;
                    _chapterCache.Clear();
                    _loadedChapterIndex = -1;
                    _lastContentRevision = 0;
                    CurrentChapterSegmentCount = 0;
                    _segments.Clear();
                }

                _chapterCatalog = chapterCatalog;
                _currentChapterDecoration.Clear();
                var hasCurrentChapter = _chapterCatalog.TryGetPosition(projectedChapterIndex, out var currentChapterPosition);
                if (hasCurrentChapter)
                {
                    _currentChapterDecoration.Set(projectedChapterIndex, true);
                }

                _latestPositionChapterIndex = projectedChapterIndex;
                _latestPositionSegmentIndex = projectedSegmentIndex;
                _latestPositionSegmentCount = projectedSegmentCount;

                _activeCacheSelectionDecoration.Clear();
                _cacheDecorations.Clear();
                _cacheDecorationWindow.Clear();
                ChapterCatalogVersion++;
            }

            var currentDecoration = _currentChapterDecoration.Snapshot();
            var selectionDecoration = _activeCacheSelectionDecoration.Snapshot();
            var cacheDecoration = _cacheDecorations.Snapshot();
            await _chapters.ReplaceWithInBatchesAsync(
                _chapterCatalog.Items,
                chapter => CreateChapterItem(chapter, currentDecoration, selectionDecoration, cacheDecoration),
                _uiScheduler,
                projectionCancellation.Token);

            lock (_syncRoot)
            {
                if (loadVersion != _bookLoadVersion)
                {
                    return false;
                }

                var finalChapterIndex = _latestPositionChapterIndex;
                var finalSegmentIndex = _latestPositionSegmentIndex;
                var finalSegmentCount = _latestPositionSegmentCount;
                var previouslyCurrentChapters = _currentChapterDecoration
                    .Snapshot()
                    .Keys
                    .ToArray();
                _currentChapterDecoration.Clear();
                if (_chapterCatalog.TryGetPosition(finalChapterIndex, out _))
                {
                    _currentChapterDecoration.Set(finalChapterIndex, true);
                }

                foreach (var chapterIndex in previouslyCurrentChapters
                    .Append(finalChapterIndex)
                    .Distinct())
                {
                    if (_chapterCatalog.TryGetPosition(chapterIndex, out var position))
                    {
                        ReplaceChapterItem(
                            chapterIndex,
                            CreateChapterItem(_chapterCatalog[position]));
                    }
                }
                var hasCurrentChapter = _chapterCatalog.TryGetPosition(finalChapterIndex, out var currentChapterPosition);
                CurrentChapterItem = null;
                CurrentChapterTitle = hasCurrentChapter
                    ? _chapterCatalog.Items[currentChapterPosition].Title
                    : "尚未定位章节";
                if (finalSegmentCount > 0)
                {
                    CurrentChapterSegmentCount = finalSegmentCount;
                }

                UpdateChapterProjection(finalChapterIndex);
                UpdateSegmentProjection(finalSegmentIndex);
                UpdateNavigationAvailability(finalChapterIndex, finalSegmentIndex);
            }

            return true;
        }
        finally
        {
            lock (_syncRoot)
            {
                if (ReferenceEquals(_bookProjectionCancellation, projectionCancellation))
                {
                    _bookProjectionCancellation = null;
                }
            }

            projectionCancellation.Dispose();
        }
    }

    private void ApplyChapterContent(PlaybackChapterContent chapter)
    {
        lock (_syncRoot)
        {
            _loadedChapterIndex = chapter.ChapterIndex;
            CurrentChapterSegmentCount = chapter.Segments.Count;
            CurrentChapterTitle = chapter.Title;
            _segments.ReplaceWith(
                chapter.Segments.Where(segment =>
                    !segment.IsChapterTitle &&
                    !string.IsNullOrEmpty(segment.DisplayText)),
                segment => new PlayerSegmentItemViewModel(
                    chapter.ChapterIndex,
                    segment.SegmentIndex,
                    segment.DisplayText));
        }
    }

    private void UpdateChapterProjection(int currentChapterIndex, bool notify = true)
    {
        if (CurrentChapterItem is { } previousItem && previousItem.ChapterIndex != currentChapterIndex)
        {
            ApplyCurrentChapterDecoration(previousItem.ChapterIndex, isCurrent: false, notify: notify);
        }

        if (!_chapterCatalog.TryGetPosition(currentChapterIndex, out var position))
        {
            CurrentChapterItem = null;
            return;
        }

        if (position >= _chapters.Count)
        {
            ApplyCurrentChapterDecoration(currentChapterIndex, isCurrent: true, notify: notify);
            CurrentChapterItem = null;
            return;
        }

        ApplyCurrentChapterDecoration(currentChapterIndex, isCurrent: true, notify: notify);
        var currentItem = _chapters[position];
        CurrentChapterItem = currentItem;
        CurrentChapterTitle = _chapterCatalog.Items[position].Title;
    }

    private void ApplyCurrentChapterDecoration(int chapterIndex, bool isCurrent, bool notify = true)
    {
        if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
        {
            return;
        }

        if (isCurrent)
        {
            _currentChapterDecoration.Set(chapterIndex, true);
        }
        else
        {
            _currentChapterDecoration.Remove(chapterIndex);
        }

        if (position >= _chapters.Count)
        {
            if (_chapters.IsProjectionPending || _chapters.IsReplacing)
            {
                ReplaceChapterItem(chapterIndex, CreateChapterItem(_chapterCatalog[position]), notify);
            }

            return;
        }

        var existing = _chapters[position];

        var item = CreateChapterItem(_chapterCatalog[position]);
        if (existing.IsCurrent != item.IsCurrent ||
            existing.IsSelectedForActiveCache != item.IsSelectedForActiveCache ||
            !string.Equals(existing.CachePercentageText, item.CachePercentageText, StringComparison.Ordinal))
        {
            ReplaceChapterItem(chapterIndex, item, notify);
        }
    }

    private void ReplaceChapterItem(
        int chapterIndex,
        PlayerChapterItemViewModel item,
        bool notify = true)
    {
        if (!_chapterCatalog.TryGetPosition(chapterIndex, out var position))
        {
            return;
        }

        if (position >= _chapters.Count &&
            !_chapters.IsReplacing &&
            !_chapters.IsProjectionPending)
        {
            return;
        }

        if (position >= _chapters.Count)
        {
            _chapters.ReplaceAt(position, item, notify);
            return;
        }

        var existing = _chapters[position];
        if (existing.IsCurrent == item.IsCurrent &&
            existing.IsSelectedForActiveCache == item.IsSelectedForActiveCache &&
            string.Equals(existing.CachePercentageText, item.CachePercentageText, StringComparison.Ordinal))
        {
            return;
        }

        _chapters.ReplaceAt(position, item, notify);
        if (CurrentChapterItem?.ChapterIndex == chapterIndex)
        {
            CurrentChapterItem = item;
        }
    }

    private PlayerChapterItemViewModel CreateChapterItem(
        PlaybackChapterSummaryMetadata chapter,
        IReadOnlyDictionary<int, bool>? currentSnapshot = null,
        IReadOnlyDictionary<int, bool>? selectionSnapshot = null,
        IReadOnlyDictionary<int, string>? cacheSnapshot = null)
    {
        var isCurrent = currentSnapshot is not null
            ? currentSnapshot.TryGetValue(chapter.ChapterIndex, out var current) && current
            : _currentChapterDecoration.TryGet(chapter.ChapterIndex, out current) && current;
        var isSelected = selectionSnapshot is not null
            ? selectionSnapshot.TryGetValue(chapter.ChapterIndex, out var selected) && selected
            : _activeCacheSelectionDecoration.TryGet(chapter.ChapterIndex, out selected) && selected;
        var cachePercentage = cacheSnapshot is not null
            ? cacheSnapshot.TryGetValue(chapter.ChapterIndex, out var cache) ? cache : string.Empty
            : _cacheDecorations.TryGet(chapter.ChapterIndex, out cache) ? cache : string.Empty;
        return new PlayerChapterItemViewModel(
            chapter.ChapterIndex,
            chapter.Title,
            isCurrent,
            isSelected,
            cachePercentage);
    }

    private void UpdateSegmentProjection(int currentSegmentIndex)
    {
        PlayerSegmentItemViewModel? currentItem = null;
        foreach (var segment in Segments)
        {
            var distance = Math.Abs(segment.SegmentIndex - currentSegmentIndex);
            segment.IsCurrent = segment.SegmentIndex == currentSegmentIndex;
            segment.VisualOpacity = distance switch
            {
                0 => 1d,
                1 => 0.82d,
                2 => 0.68d,
                3 => 0.58d,
                _ => 0.46d
            };
            segment.IsInteractive = true;
            if (segment.IsCurrent)
            {
                currentItem = segment;
            }
        }

        CurrentSegmentItem = currentItem;
    }

    private void UpdateNavigationAvailability(int currentChapterIndex, int currentSegmentIndex)
    {
        if (_loadedBook is null || currentChapterIndex < 0)
        {
            CanGoToPreviousChapter = false;
            CanGoToNextChapter = false;
        }
        else
        {
            var chapterPosition = GetChapterPosition(currentChapterIndex) ?? -1;
            CanGoToPreviousChapter = chapterPosition > 0;
            CanGoToNextChapter = chapterPosition >= 0 && chapterPosition < _loadedBook.Chapters.Count - 1;
        }

        CanGoToPreviousSegment = currentSegmentIndex > 0;
        CanGoToNextSegment = CurrentChapterSegmentCount > 0 &&
                             currentSegmentIndex >= 0 &&
                             currentSegmentIndex < CurrentChapterSegmentCount - 1;
    }

}
