using System.Collections.ObjectModel;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Shared.Presentation;

namespace NovelSpeaker.App.Features.Playback.Presentation;

/// <summary>
/// Owns the playback page's book/chapter content cache and its chapter/segment item projection.
/// Playback session state remains owned by <see cref="IPlaybackSession"/>.
/// </summary>
internal sealed class PlayerContentProjection
{
    private readonly IBookPlaybackContentService _contentService;
    private readonly object _syncRoot = new();
    private readonly Dictionary<int, PlaybackChapterContent> _chapterCache = [];

    private PlaybackBookContent? _loadedBook;
    private int _loadedChapterIndex = -1;
    private int _bookLoadVersion;
    private int _chapterLoadVersion;
    private long _lastContentRevision;

    public PlayerContentProjection(IBookPlaybackContentService contentService)
    {
        _contentService = contentService;
    }

    public ObservableCollection<PlayerChapterItemViewModel> Chapters { get; } = [];

    public ObservableCollection<PlayerSegmentItemViewModel> Segments { get; } = [];

    public PlaybackBookContent? LoadedBook => _loadedBook;

    public PlayerChapterItemViewModel? CurrentChapterItem { get; private set; }

    public PlayerSegmentItemViewModel? CurrentSegmentItem { get; private set; }

    public string CurrentChapterTitle { get; private set; } = "尚未定位章节";

    public int CurrentChapterSegmentCount { get; private set; }

    public bool CanGoToPreviousChapter { get; private set; }

    public bool CanGoToNextChapter { get; private set; }

    public bool CanGoToPreviousSegment { get; private set; }

    public bool CanGoToNextSegment { get; private set; }

    public async Task<PlaybackBookContent?> EnsureBookLoadedAsync(
        string bookId,
        int currentChapterIndex,
        int currentSegmentIndex,
        CancellationToken cancellationToken)
    {
        if (_loadedBook is not null && string.Equals(_loadedBook.BookId, bookId, StringComparison.Ordinal))
        {
            return _loadedBook;
        }

        var loadVersion = ++_bookLoadVersion;
        var book = await _contentService.GetBookAsync(bookId, cancellationToken);
        if (loadVersion != _bookLoadVersion || book is null)
        {
            return null;
        }

        ApplyLoadedBook(book, currentChapterIndex, currentSegmentIndex);
        return book;
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

        if (snapshot.ContentRevision != _lastContentRevision)
        {
            _chapterCache.Remove(snapshot.ChapterIndex);
            if (_loadedChapterIndex == snapshot.ChapterIndex)
            {
                _loadedChapterIndex = -1;
            }

            _lastContentRevision = snapshot.ContentRevision;
        }

        await EnsureChapterLoadedAsync(book.BookId, snapshot.ChapterIndex, cancellationToken);
        ApplyPosition(snapshot.ChapterIndex, snapshot.SegmentIndex, snapshot.SegmentCount);
    }

    public void ApplyPosition(int chapterIndex, int segmentIndex, int segmentCount)
    {
        lock (_syncRoot)
        {
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
            var chapter = _loadedBook.Chapters.FirstOrDefault(item => item.ChapterIndex == chapterIndex);
            if (chapter is not null)
            {
                return chapter.Title;
            }
        }

        return "尚未定位章节";
    }

    private async Task EnsureChapterLoadedAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        if (_loadedBook is null || !string.Equals(_loadedBook.BookId, bookId, StringComparison.Ordinal))
        {
            return;
        }

        // Playback emits multiple snapshots for a single transition. Retain projected items so a
        // virtualized container is not discarded while the View is centering the current segment.
        if (_loadedChapterIndex == chapterIndex)
        {
            return;
        }

        if (_chapterCache.TryGetValue(chapterIndex, out var cachedChapter))
        {
            ApplyChapterContent(cachedChapter);
            return;
        }

        var loadVersion = ++_chapterLoadVersion;
        var chapter = await _contentService.GetChapterAsync(bookId, chapterIndex, cancellationToken);
        if (loadVersion != _chapterLoadVersion || chapter is null)
        {
            return;
        }

        _chapterCache[chapter.ChapterIndex] = chapter;
        ApplyChapterContent(chapter);
    }

    private void ApplyLoadedBook(
        PlaybackBookContent book,
        int currentChapterIndex,
        int currentSegmentIndex)
    {
        lock (_syncRoot)
        {
            var isDifferentBook = !string.Equals(_loadedBook?.BookId, book.BookId, StringComparison.Ordinal);
            _loadedBook = book;

            if (isDifferentBook)
            {
                _chapterCache.Clear();
                _loadedChapterIndex = -1;
                _lastContentRevision = 0;
                CurrentChapterSegmentCount = 0;
                Segments.Clear();
            }

            Chapters.ReplaceWith(
                book.Chapters,
                chapter => new PlayerChapterItemViewModel(chapter.ChapterIndex, chapter.Title));
            UpdateChapterProjection(currentChapterIndex);
            UpdateSegmentProjection(currentSegmentIndex);
            UpdateNavigationAvailability(currentChapterIndex, currentSegmentIndex);
        }
    }

    private void ApplyChapterContent(PlaybackChapterContent chapter)
    {
        lock (_syncRoot)
        {
            _loadedChapterIndex = chapter.ChapterIndex;
            CurrentChapterSegmentCount = chapter.Segments.Count;
            CurrentChapterTitle = chapter.Title;
            Segments.ReplaceWith(
                chapter.Segments.Where(segment => !string.IsNullOrEmpty(segment.DisplayText)),
                segment => new PlayerSegmentItemViewModel(
                    chapter.ChapterIndex,
                    segment.SegmentIndex,
                    segment.DisplayText));
        }
    }

    private void UpdateChapterProjection(int currentChapterIndex)
    {
        PlayerChapterItemViewModel? currentItem = null;
        foreach (var chapter in Chapters)
        {
            var isCurrent = chapter.ChapterIndex == currentChapterIndex;
            chapter.IsCurrent = isCurrent;
            if (isCurrent)
            {
                currentItem = chapter;
                CurrentChapterTitle = chapter.Title;
            }
        }

        CurrentChapterItem = currentItem;
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
            var chapterPosition = GetChapterPosition(currentChapterIndex);
            CanGoToPreviousChapter = chapterPosition > 0;
            CanGoToNextChapter = chapterPosition >= 0 && chapterPosition < _loadedBook.Chapters.Count - 1;
        }

        CanGoToPreviousSegment = currentSegmentIndex > 0;
        CanGoToNextSegment = CurrentChapterSegmentCount > 0 &&
                             currentSegmentIndex >= 0 &&
                             currentSegmentIndex < CurrentChapterSegmentCount - 1;
    }

    private int GetChapterPosition(int chapterIndex)
    {
        if (_loadedBook is null)
        {
            return -1;
        }

        for (var index = 0; index < _loadedBook.Chapters.Count; index++)
        {
            if (_loadedBook.Chapters[index].ChapterIndex == chapterIndex)
            {
                return index;
            }
        }

        return -1;
    }
}
