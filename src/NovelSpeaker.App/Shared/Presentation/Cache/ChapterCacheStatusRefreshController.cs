using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;

namespace NovelSpeaker.App.Shared.Presentation.Cache;

/// <summary>
/// Coalesces chapter cache status refresh requests within one page activation.
/// The page remains responsible for deciding which book and chapters are affected.
/// </summary>
internal sealed class ChapterCacheStatusRefreshController
{
    private readonly ICacheWorkspaceService _cacheWorkspaceService;
    private readonly IUiScheduler _uiScheduler;
    private readonly Action<string, IReadOnlyCollection<int>, IReadOnlyCollection<ChapterCacheStatus>, bool> _applyStatuses;
    private readonly Action<Exception> _reportFailure;
    private readonly OwnedTaskRegistry _tasks = new();
    private readonly object _syncRoot = new();
    private readonly HashSet<int> _pendingChapterIndices = [];

    private CancellationTokenSource? _activationCancellationTokenSource;
    private string? _pendingBookId;
    private bool _pendingInitialProjection;
    private bool _isRefreshRunning;
    private int _activationGeneration;

    public ChapterCacheStatusRefreshController(
        ICacheWorkspaceService cacheWorkspaceService,
        IUiScheduler uiScheduler,
        Action<string, IReadOnlyCollection<int>, IReadOnlyCollection<ChapterCacheStatus>, bool> applyStatuses,
        Action<Exception> reportFailure)
    {
        _cacheWorkspaceService = cacheWorkspaceService;
        _uiScheduler = uiScheduler;
        _applyStatuses = applyStatuses;
        _reportFailure = reportFailure;
    }

    public void Activate(CancellationToken cancellationToken)
    {
        Deactivate();
        lock (_syncRoot)
        {
            _activationCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }
    }

    public void Deactivate()
    {
        CancellationTokenSource? cancellationTokenSource;
        lock (_syncRoot)
        {
            cancellationTokenSource = _activationCancellationTokenSource;
            _activationCancellationTokenSource = null;
            _activationGeneration++;
            _pendingChapterIndices.Clear();
            _pendingBookId = null;
            _pendingInitialProjection = false;
            _isRefreshRunning = false;
        }

        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    public void Request(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        bool isInitialProjection = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        if (chapterIndices.Count == 0)
        {
            return;
        }

        int generation;
        CancellationToken cancellationToken;
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
                _pendingInitialProjection = false;
            }

            _pendingChapterIndices.UnionWith(chapterIndices);
            _pendingInitialProjection |= isInitialProjection;
            if (_isRefreshRunning)
            {
                return;
            }

            _isRefreshRunning = true;
            generation = _activationGeneration;
        }

        _tasks.Register(
            ProcessRefreshesAsync(generation, cancellationToken),
            exception =>
            {
                if (IsCurrentGeneration(generation))
                {
                    _reportFailure(exception);
                }
            });
    }

    private async Task ProcessRefreshesAsync(int generation, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                string bookId;
                int[] chapterIndices;
                bool isInitialProjection;
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
                    isInitialProjection = _pendingInitialProjection;
                    _pendingChapterIndices.Clear();
                    _pendingInitialProjection = false;
                }

                var statuses = await _cacheWorkspaceService.GetChapterCacheStatusesAsync(
                    bookId,
                    chapterIndices,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                await _uiScheduler.InvokeAsync(
                    () =>
                    {
                        if (IsCurrentGeneration(generation))
                        {
                            _applyStatuses(bookId, chapterIndices, statuses, isInitialProjection);
                        }
                    },
                    cancellationToken);
            }
        }
        catch
        {
            lock (_syncRoot)
            {
                if (generation == _activationGeneration)
                {
                    _isRefreshRunning = false;
                }
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
