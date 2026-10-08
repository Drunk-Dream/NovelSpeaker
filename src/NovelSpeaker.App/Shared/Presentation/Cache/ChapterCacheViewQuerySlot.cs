using NovelSpeaker.Application.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Activation;

namespace NovelSpeaker.App.Shared.Presentation.Cache;

/// <summary>
/// Coalesces page-owned chapter window queries within one catalog activation.
/// Cache owns composition, invalidation and repair; callers own the display window.
/// </summary>
internal sealed class ChapterCacheViewQuerySlot
{
    private readonly ICacheReadModel _readModel;
    private readonly IUiScheduler _uiScheduler;
    private readonly Action<string, IReadOnlyCollection<int>, IReadOnlyList<CacheChapterView>> _applyViews;
    private readonly Action<Exception> _reportFailure;
    private readonly LatestOperationSlot _catalogObservation = new();
    private readonly object _syncRoot = new();
    private readonly HashSet<int> _pendingChapterIndices = [];

    private string? _pendingBookId;
    private bool _isRefreshRunning;
    private PageActivationScope? _pageActivation;

    public ChapterCacheViewQuerySlot(
        ICacheReadModel readModel,
        IUiScheduler uiScheduler,
        Action<string, IReadOnlyCollection<int>, IReadOnlyList<CacheChapterView>> applyViews,
        Action<Exception> reportFailure)
    {
        _readModel = readModel;
        _uiScheduler = uiScheduler;
        _applyViews = applyViews;
        _reportFailure = reportFailure;
    }

    public void Activate(CancellationToken cancellationToken, PageActivationScope pageActivation)
    {
        Deactivate();
        lock (_syncRoot)
        {
            _catalogObservation.Begin(cancellationToken, pageActivation);
            _pageActivation = pageActivation;
        }
    }

    public void Deactivate()
    {
        lock (_syncRoot)
        {
            _catalogObservation.Cancel();
            _pageActivation = null;
            _pendingChapterIndices.Clear();
            _pendingBookId = null;
            _isRefreshRunning = false;
        }
    }

    public void Request(
        string bookId,
        IReadOnlyCollection<int> chapterIndices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        if (chapterIndices.Count == 0)
        {
            return;
        }

        LatestOperationSlot.Operation observation;
        CancellationToken cancellationToken;
        PageActivationScope? pageActivation;
        lock (_syncRoot)
        {
            if (_catalogObservation.Current is not { IsCurrent: true } current)
            {
                return;
            }

            cancellationToken = current.CancellationToken;
            if (!string.Equals(_pendingBookId, bookId, StringComparison.Ordinal))
            {
                _pendingChapterIndices.Clear();
                _pendingBookId = bookId;
            }

            _pendingChapterIndices.UnionWith(chapterIndices);
            if (_isRefreshRunning)
            {
                return;
            }

            _isRefreshRunning = true;
            observation = current;
            pageActivation = _pageActivation;
        }

        StartRefresh(observation, cancellationToken, pageActivation!);
    }

    private void StartRefresh(LatestOperationSlot.Operation observation, CancellationToken cancellationToken, PageActivationScope pageActivation)
    {
        var task = ProcessRefreshesAsync(observation, cancellationToken);
        void ReportFailure(Exception exception)
        {
            if (observation.IsCurrent)
            {
                _reportFailure(exception);
            }
        }
        pageActivation.Register(task, ReportFailure);
    }

    private async Task ProcessRefreshesAsync(LatestOperationSlot.Operation observation, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                string bookId;
                int[] chapterIndices;
                lock (_syncRoot)
                {
                    if (!observation.IsCurrent)
                    {
                        return;
                    }

                    if (_pendingChapterIndices.Count == 0 || string.IsNullOrWhiteSpace(_pendingBookId))
                    {
                        _isRefreshRunning = false;
                        return;
                    }

                    bookId = _pendingBookId;
                    chapterIndices = [.. _pendingChapterIndices];
                    _pendingChapterIndices.Clear();
                }

                var result = await _readModel.GetChaptersAsync(
                    bookId,
                    chapterIndices,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                await _uiScheduler.InvokeAsync(
                    () =>
                    {
                        if (observation.IsCurrent)
                        {
                            _applyViews(bookId, chapterIndices, result.Value);
                        }
                    },
                    cancellationToken);
            }
        }
        catch
        {
            var restartPending = false;
            PageActivationScope? pageActivation = null;
            lock (_syncRoot)
            {
                if (observation.IsCurrent)
                {
                    _isRefreshRunning = false;
                    restartPending = _pendingChapterIndices.Count > 0;
                    if (restartPending)
                    {
                        _isRefreshRunning = true;
                        pageActivation = _pageActivation;
                    }
                }
            }

            if (restartPending)
            {
                StartRefresh(observation, cancellationToken, pageActivation!);
            }

            throw;
        }
    }
}
