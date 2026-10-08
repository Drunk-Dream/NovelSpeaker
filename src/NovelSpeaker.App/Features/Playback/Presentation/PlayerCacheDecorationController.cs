using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Activation;

namespace NovelSpeaker.App.Features.Playback.Presentation;

/// <summary>
/// Owns page-scoped chapter cache decoration and chapter management state.
/// Process-owned cache batches remain owned by <see cref="IActiveCacheCoordinator"/>.
/// </summary>
internal sealed class PlayerCacheDecorationController
{
    private const int CacheDecorationWindowSize = 32;
    private const int SelectionDecorationResetThreshold = 64;

    private readonly IActiveCacheCoordinator _activeCacheCoordinator;
    private readonly ICacheReadModel _readModel;
    private readonly PlayerContentController _contentController;
    private readonly IUiScheduler _uiScheduler;
    private readonly Action<string, Exception> _reportFailure;
    private readonly PlayerChapterManagementController _selectionController;
    private readonly ChapterCacheViewQuerySlot _statusRefreshController;
    private readonly HashSet<int> _explicitStatusRequests = [];

    private PageActivationScope? _activation;
    private int _currentChapterIndex = -1;
    private string? _initializedBookId;
    private int _synchronizedCatalogVersion = -1;

    public PlayerCacheDecorationController(
        IActiveCacheCoordinator activeCacheCoordinator,
        ICacheReadModel readModel,
        PlayerContentController contentController,
        IUiScheduler uiScheduler,
        Action<string, Exception> reportFailure)
    {
        _activeCacheCoordinator = activeCacheCoordinator ?? throw new ArgumentNullException(nameof(activeCacheCoordinator));
        _readModel = readModel ?? throw new ArgumentNullException(nameof(readModel));
        _contentController = contentController ?? throw new ArgumentNullException(nameof(contentController));
        _uiScheduler = uiScheduler ?? throw new ArgumentNullException(nameof(uiScheduler));
        _reportFailure = reportFailure ?? throw new ArgumentNullException(nameof(reportFailure));
        _selectionController = new PlayerChapterManagementController(activeCacheCoordinator);
        _selectionController.StateChanged += OnSelectionStateChanged;
        _statusRefreshController = new ChapterCacheViewQuerySlot(
            readModel,
            uiScheduler,
            ApplyChapterCacheStatuses,
            exception => reportFailure("刷新章节缓存进度失败", exception));
    }

    public event EventHandler? StateChanged;

    public bool IsSelectionMode => _selectionController.IsSelectionMode;

    public int SelectedChapterCount => _selectionController.SelectedChapterCount;

    public string SelectionSummary => _selectionController.SelectionSummary;

    public string StatusText => _selectionController.StatusText;

    public bool HasActiveBatch => _selectionController.HasActiveBatch;

    public bool CanStart => _selectionController.CanStart;

    public void Activate(PageActivationScope activation)
    {
        Deactivate();
        _activation = activation;
        _statusRefreshController.Activate(activation.CancellationToken, activation);
        _activeCacheCoordinator.SnapshotChanged += OnActiveCacheSnapshotChanged;
        _readModel.Changed += OnCacheReadModelChanged;
        activation.Register(() =>
        {
            _activeCacheCoordinator.SnapshotChanged -= OnActiveCacheSnapshotChanged;
            _readModel.Changed -= OnCacheReadModelChanged;
        });
        activation.Register(() =>
        {
            if (ReferenceEquals(_activation, activation)) Deactivate();
        });
        _selectionController.ApplySnapshot(_activeCacheCoordinator.CurrentSnapshot);
    }

    public void Deactivate()
    {
        _activation = null;
        _statusRefreshController.Deactivate();
        _selectionController.ExitSelectionMode();
        _explicitStatusRequests.Clear();
    }

    public void SetCurrentChapterIndex(int chapterIndex) => _currentChapterIndex = chapterIndex;

    public void SynchronizeCatalog()
    {
        if (_synchronizedCatalogVersion == _contentController.ChapterCatalogVersion)
        {
            return;
        }

        if (_activation is not { IsCurrent: true } activation) return;
        _statusRefreshController.Activate(activation.CancellationToken, activation);
        _initializedBookId = null;
        _explicitStatusRequests.Clear();
        _selectionController.SetIndexedItems(
            _contentController.ChapterIndices,
            _contentController.ChapterPositions,
            resetSelection: true);
        foreach (var chapterIndex in _selectionController.SelectedChapterIndices)
        {
            if (_contentController.TryGetChapterItem(chapterIndex, out _))
            {
                _contentController.ApplyChapterSelection(chapterIndex, true);
            }
        }

        _synchronizedCatalogVersion = _contentController.ChapterCatalogVersion;
    }

    public void EnsureBookInitialized()
    {
        if (_contentController.LoadedBook is not { BookId: { Length: > 0 } bookId } ||
            string.Equals(_initializedBookId, bookId, StringComparison.Ordinal))
        {
            return;
        }

        _initializedBookId = bookId;
        RequestStatusRefresh(chapterIndex: null);
    }

    public bool HandleChapterClick(int chapterIndex, DesktopSelectionModifiers modifiers) =>
        _selectionController.HandleChapterClick(chapterIndex, modifiers);

    public bool HandleChapterRightClick(int chapterIndex) => _selectionController.HandleRightClick(chapterIndex);

    public void EnterSelectionMode()
    {
        _selectionController.ApplySnapshot(_activeCacheCoordinator.CurrentSnapshot);
        _selectionController.EnterSelectionMode();
    }

    public void ExitSelectionMode() => _selectionController.ExitSelectionMode();

    public bool TryExitSelectionMode() => _selectionController.ExitSelectionMode();

    public void SelectAll() => _selectionController.SelectAll();

    public async Task StartAsync(string bookId, int speakSpeed, CancellationToken cancellationToken) =>
        _ = await _selectionController.StartAsync(bookId, speakSpeed, cancellationToken);

    public void RequestDecorationWindow(int start, int count)
    {
        if (count <= 0)
        {
            return;
        }

        void Request()
        {
            if (_activation is not { IsCurrent: true } ||
                _contentController.LoadedBook is not { BookId: { Length: > 0 } bookId } ||
                _contentController.ChapterIndices.Count == 0)
            {
                return;
            }

            var chapterIndices = _contentController.GetChapterIndices(
                Math.Clamp(start, 0, _contentController.ChapterIndices.Count - 1),
                count);
            if (chapterIndices.Count == 0)
            {
                return;
            }

            _contentController.SetCacheDecorationWindow(chapterIndices);
            _statusRefreshController.Request(bookId, chapterIndices);
        }

        if (!_uiScheduler.CheckAccess())
        {
            RegisterUiTask(Request, "刷新章节缓存进度失败");
            return;
        }

        Request();
    }

    public void RequestStatusRefresh(int? chapterIndex)
    {
        if (!_uiScheduler.CheckAccess())
        {
            RegisterUiTask(() => QueueStatusRefresh(chapterIndex), "刷新章节缓存进度失败");
            return;
        }

        QueueStatusRefresh(chapterIndex);
    }

    private void QueueStatusRefresh(int? chapterIndex)
    {
        if (_activation is not { IsCurrent: true } ||
            _contentController.LoadedBook is not { BookId: { Length: > 0 } bookId })
        {
            return;
        }

        var chapterIndices = chapterIndex is null
            ? GetDecorationWindow()
            : _contentController.GetChapterPosition(chapterIndex.Value) is not null
                ? new[] { chapterIndex.Value }
                : Array.Empty<int>();

        if (chapterIndex is null)
        {
            _contentController.SetCacheDecorationWindow(chapterIndices);
        }
        else if (chapterIndices.Count > 0)
        {
            _explicitStatusRequests.Add(chapterIndex.Value);
        }

        _statusRefreshController.Request(bookId, chapterIndices);
    }

    private IReadOnlyList<int> GetDecorationWindow()
    {
        if (_contentController.ChapterIndices.Count == 0)
        {
            return [];
        }

        var currentPosition = _contentController.GetChapterPosition(_currentChapterIndex);
        if (currentPosition is null)
        {
            return _contentController.GetChapterIndices(0, CacheDecorationWindowSize);
        }

        var start = Math.Max(0, currentPosition.Value - (CacheDecorationWindowSize / 4));
        return _contentController.GetChapterIndices(start, CacheDecorationWindowSize);
    }

    private void ApplyChapterCacheStatuses(
        string bookId,
        IReadOnlyCollection<int> requestedChapterIndices,
        IReadOnlyList<CacheChapterView> views)
    {
        if (_activation is not { IsCurrent: true } ||
            !string.Equals(_contentController.LoadedBook?.BookId, bookId, StringComparison.Ordinal))
        {
            return;
        }

        var statusesByChapter = views.ToDictionary(static view => view.ChapterIndex, static view => view.Coverage);
        foreach (var chapterIndex in requestedChapterIndices)
        {
            if (!_contentController.IsChapterCacheDecorationRequested(chapterIndex) &&
                !_explicitStatusRequests.Contains(chapterIndex))
            {
                continue;
            }

            var status = statusesByChapter.GetValueOrDefault(chapterIndex);
            _contentController.ApplyChapterCacheStatus(
                chapterIndex,
                status?.CachedSegmentCount ?? 0,
                status?.TotalSegmentCount);
            _explicitStatusRequests.Remove(chapterIndex);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnSelectionStateChanged(object? sender, EventArgs eventArgs)
    {
        var replacements = _selectionController.ChangedChapterIndices
            .Select(chapterIndex => (chapterIndex, _selectionController.IsSelected(chapterIndex)))
            .ToArray();
        if (replacements.Length > SelectionDecorationResetThreshold)
        {
            _contentController.ApplyChapterSelections(replacements, notify: false);
            _contentController.NotifyChapterReset();
        }
        else
        {
            foreach (var (chapterIndex, isSelected) in replacements)
            {
                _contentController.ApplyChapterSelection(chapterIndex, isSelected);
            }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnActiveCacheSnapshotChanged(object? sender, ActiveCacheSnapshot snapshot)
    {
        var activation = _activation;
        RunOnUi(
            () => _selectionController.ApplySnapshot(snapshot),
            activation,
            "更新主动缓存状态失败");
    }

    private void OnCacheReadModelChanged(object? sender, CacheReadModelChange change)
    {
        RunOnUi(() => ApplyCacheReadModelChange(change), _activation, "刷新章节缓存进度失败");
    }

    private void ApplyCacheReadModelChange(CacheReadModelChange change)
    {
        var loadedBookId = _contentController.LoadedBook?.BookId;
        if (string.IsNullOrWhiteSpace(loadedBookId))
        {
            return;
        }

        foreach (var scope in change.Scopes)
        {
            switch (scope)
            {
                case CacheReadModelScope.Global:
                    RefreshObservedWindow(loadedBookId);
                    break;
                case CacheReadModelScope.Book book
                    when string.Equals(book.BookId, loadedBookId, StringComparison.Ordinal):
                    RefreshObservedWindow(loadedBookId);
                    break;
                case CacheReadModelScope.Chapters chapters
                    when string.Equals(chapters.BookId, loadedBookId, StringComparison.Ordinal):
                    foreach (var chapterIndex in chapters.ChapterIndices)
                    {
                        RequestStatusRefresh(chapterIndex);
                    }

                    break;
            }
        }
    }

    private void RefreshObservedWindow(string bookId)
    {
        if (_contentController.CacheDecorationWindow.Count == 0)
        {
            QueueStatusRefresh(chapterIndex: null);
            return;
        }

        var indices = _contentController.CacheDecorationWindow.ToHashSet();
        if (_contentController.GetChapterPosition(_currentChapterIndex) is not null)
        {
            indices.Add(_currentChapterIndex);
            _explicitStatusRequests.Add(_currentChapterIndex);
        }

        _statusRefreshController.Request(bookId, indices);
    }

    private void RunOnUi(Action action, PageActivationScope? activation, string failureTitle)
    {
        if (activation is not { IsCurrent: true }) return;
        activation.Run(token => _uiScheduler.InvokeAsync(
            () => activation.TryCommit(action), token),
            exception => _reportFailure(failureTitle, exception));
    }

    private void RegisterUiTask(Action action, string failureTitle) =>
        RunOnUi(action, _activation, failureTitle);
}
