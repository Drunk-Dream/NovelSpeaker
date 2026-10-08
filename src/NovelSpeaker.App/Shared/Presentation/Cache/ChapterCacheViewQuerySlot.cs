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
    private readonly OwnedTaskRegistry _tasks = new();
    private readonly object _syncRoot = new();
    private readonly HashSet<int> _pendingChapterIndices = [];

    private CancellationTokenSource? _activationCancellationTokenSource;
    private string? _pendingBookId;
    private bool _isRefreshRunning;
    private int _activationGeneration;
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

    public void Activate(CancellationToken cancellationToken, PageActivationScope? pageActivation = null)
    {
        Deactivate();
        lock (_syncRoot)
        {
            _activationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pageActivation = pageActivation;
        }
    }

    public void Deactivate()
    {
        CancellationTokenSource? cancellationTokenSource;
        lock (_syncRoot)
        {
            cancellationTokenSource = _activationCancellationTokenSource;
            _activationCancellationTokenSource = null;
            _pageActivation = null;
            _activationGeneration++;
            _pendingChapterIndices.Clear();
            _pendingBookId = null;
            _isRefreshRunning = false;
        }

        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
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

        int generation;
        CancellationToken cancellationToken;
        PageActivationScope? pageActivation;
        lock (_syncRoot)
        {
            if (_activationCancellationTokenSource is not { IsCancellationRequested: false } cancellationTokenSource)
            {
                return;
            }

            cancellationToken = cancellationTokenSource.Token;
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
            generation = _activationGeneration;
            pageActivation = _pageActivation;
        }

        StartRefresh(generation, cancellationToken, pageActivation);
    }

    private void StartRefresh(int generation, CancellationToken cancellationToken, PageActivationScope? pageActivation)
    {
        var task = ProcessRefreshesAsync(generation, cancellationToken);
        void ReportFailure(Exception exception)
        {
            if (IsCurrentGeneration(generation))
            {
                _reportFailure(exception);
            }
        }
        if (pageActivation is not null) pageActivation.Register(task, ReportFailure);
        else _tasks.Register(task, ReportFailure);
    }

    private async Task ProcessRefreshesAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                string bookId;
                int[] chapterIndices;
                lock (_syncRoot)
                {
                    if (generation != _activationGeneration)
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
                        if (IsCurrentGeneration(generation))
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
                if (generation == _activationGeneration)
                {
                    _isRefreshRunning = false;
                    restartPending = _pendingChapterIndices.Count > 0 &&
                                     _activationCancellationTokenSource is { IsCancellationRequested: false };
                    if (restartPending)
                    {
                        _isRefreshRunning = true;
                        pageActivation = _pageActivation;
                    }
                }
            }

            if (restartPending)
            {
                StartRefresh(generation, cancellationToken, pageActivation);
            }

            throw;
        }
    }

    private bool IsCurrentGeneration(int generation)
    {
        lock (_syncRoot)
        {
            return generation == _activationGeneration &&
                   _activationCancellationTokenSource is { IsCancellationRequested: false };
        }
    }
}
