using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.App.Shared.Presentation.Selection;

namespace NovelSpeaker.App.Features.Playback.Presentation;

/// <summary>
/// Owns the player page's temporary chapter-selection mode while projecting the
/// process-owned active-cache snapshot without duplicating batch state.
/// </summary>
internal sealed class PlayerChapterManagementController
{
    private readonly IActiveCacheCoordinator _activeCacheCoordinator;
    private readonly ManagementSelectionController<int> _selection = new();
    private ActiveCacheSnapshot? _activeSnapshot;
    private string? _startStatusText;

    public PlayerChapterManagementController(IActiveCacheCoordinator activeCacheCoordinator)
    {
        _activeCacheCoordinator = activeCacheCoordinator;
        _activeSnapshot = activeCacheCoordinator.CurrentSnapshot;
        _selection.StateChanged += (_, _) =>
        {
            ChangedChapterIndices = _selection.ChangedItems;
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? StateChanged;

    public IReadOnlyList<int> ChangedChapterIndices { get; private set; } = [];

    public bool IsSelectionMode => _selection.IsManagementMode;

    public IReadOnlyList<int> SelectedChapterIndices => _selection.SelectedItems;

    public int SelectedChapterCount => _selection.SelectedCount;

    public bool HasActiveBatch =>
        _activeSnapshot?.Status is
            ActiveCacheBatchStatus.Waiting or
            ActiveCacheBatchStatus.Running or
            ActiveCacheBatchStatus.Cancelling;

    public bool CanStart => IsSelectionMode && SelectedChapterCount > 0 && !HasActiveBatch;

    public string SelectionSummary => $"已选择 {SelectedChapterCount} 章";

    public string StatusText => HasActiveBatch
        ? "已有主动缓存批次正在运行，完成或取消后可开始新批次。"
        : _startStatusText ?? string.Empty;

    public void SetChapters(IEnumerable<int> chapterIndices, bool resetSelection = false)
    {
        if (resetSelection) _selection.Reset();
        _selection.SetItems(chapterIndices);
    }

    public void SetIndexedItems(
        IReadOnlyList<int> chapterIndices,
        IReadOnlyDictionary<int, int> positions,
        bool resetSelection = false)
    {
        if (resetSelection) _selection.Reset();
        _selection.SetIndexedItems(chapterIndices, positions);
    }

    public void EnterSelectionMode()
    {
        if (IsSelectionMode)
        {
            return;
        }

        _startStatusText = null;
        _selection.Enter();
    }

    public bool HandleChapterClick(int chapterIndex, DesktopSelectionModifiers modifiers)
    {
        return _selection.HandleClick(chapterIndex, modifiers);
    }

    public void SelectAll()
    {
        if (IsSelectionMode)
        {
            _selection.SelectAll();
        }
    }

    public bool ExitSelectionMode()
    {
        if (!IsSelectionMode)
        {
            return false;
        }

        _startStatusText = null;
        return _selection.Exit();
    }

    public bool HandleRightClick(int chapterIndex) => _selection.HandleRightClick(chapterIndex);

    public bool IsSelected(int chapterIndex) => _selection.IsSelected(chapterIndex);

    public void ApplySnapshot(ActiveCacheSnapshot? snapshot)
    {
        _activeSnapshot = snapshot;
        _startStatusText = null;
        ChangedChapterIndices = [];
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ActiveCacheStartResult?> StartAsync(
        string bookId,
        int speakSpeed,
        CancellationToken cancellationToken)
    {
        ApplySnapshot(_activeCacheCoordinator.CurrentSnapshot);
        if (!CanStart)
        {
            return null;
        }

        var selectedChapterIndices = SelectedChapterIndices.ToArray();

        var result = await _activeCacheCoordinator.StartAsync(
            new StartActiveCacheRequest(bookId, selectedChapterIndices, speakSpeed),
            cancellationToken);
        if (result.IsAccepted)
        {
            ExitSelectionMode();
        }
        else
        {
            _startStatusText = result.ErrorSummary;
            ChangedChapterIndices = [];
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }
}
