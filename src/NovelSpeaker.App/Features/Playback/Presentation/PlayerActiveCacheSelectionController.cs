using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.App.Shared.Presentation.Selection;

namespace NovelSpeaker.App.Features.Playback.Presentation;

/// <summary>
/// Owns the player page's temporary chapter-selection mode while projecting the
/// process-owned active-cache snapshot without duplicating batch state.
/// </summary>
public sealed class PlayerActiveCacheSelectionController
{
    private readonly IActiveCacheCoordinator _activeCacheCoordinator;
    private readonly DesktopSelectionController<int> _selection = new();
    private ActiveCacheSnapshot? _activeSnapshot;
    private string? _startStatusText;

    public PlayerActiveCacheSelectionController(IActiveCacheCoordinator activeCacheCoordinator)
    {
        _activeCacheCoordinator = activeCacheCoordinator;
        _activeSnapshot = activeCacheCoordinator.CurrentSnapshot;
        _selection.SelectionChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? StateChanged;

    public bool IsSelectionMode { get; private set; }

    public IReadOnlyList<int> SelectedChapterIndices => _selection.SelectedItems;

    public int SelectedChapterCount => _selection.Count;

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

    public void SetChapters(IEnumerable<int> chapterIndices)
    {
        _selection.SetItems(chapterIndices);
    }

    public void EnterSelectionMode()
    {
        if (IsSelectionMode)
        {
            return;
        }

        IsSelectionMode = true;
        _startStatusText = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool HandleChapterClick(int chapterIndex, DesktopSelectionModifiers modifiers)
    {
        if (!IsSelectionMode)
        {
            return false;
        }

        _selection.Click(chapterIndex, modifiers);
        return true;
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

        IsSelectionMode = false;
        _selection.Clear();
        _startStatusText = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool IsSelected(int chapterIndex) => _selection.IsSelected(chapterIndex);

    public void ApplySnapshot(ActiveCacheSnapshot? snapshot)
    {
        _activeSnapshot = snapshot;
        _startStatusText = null;
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

        var result = await _activeCacheCoordinator.StartAsync(
            new StartActiveCacheRequest(bookId, SelectedChapterIndices, speakSpeed),
            cancellationToken);
        if (result.IsAccepted)
        {
            ExitSelectionMode();
        }
        else
        {
            _startStatusText = result.ErrorSummary;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }
}
