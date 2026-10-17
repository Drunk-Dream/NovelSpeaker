using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.Playback.Components;

public partial class PlayerView
{
    private double? _catalogDragStartWidth;
    private double? _catalogKeyboardStartWidth;

    private void PlayerView_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged) return;
        // A viewport change is never a new preference, even during a user gesture.
        CancelCatalogResize();
        ApplyCatalogWidth();
    }

    private void ApplyCatalogWidth()
    {
        if (ActualWidth <= 0) return;
        // The Grid can be arranged wider than its host when its columns' desired
        // size exceeds the viewport. Use the bounded View, not that overflowing Grid.
        var availableWidth = ActualWidth - PlayerLayout.Margin.Left - PlayerLayout.Margin.Right;
        var maximum = Math.Max(CatalogColumn.MinWidth,
            availableWidth - ContentColumns.ColumnDefinitions[1].Width.Value -
            ContentColumns.ColumnDefinitions[2].MinWidth);
        CatalogColumn.MaxWidth = maximum;
        CatalogColumn.Width = new GridLength(Math.Clamp(
            _viewModel?.PreferredCatalogWidth ?? AppSettings.DefaultPlayerCatalogWidth,
            CatalogColumn.MinWidth, maximum));
    }

    private void CancelCatalogResize()
    {
        _catalogDragStartWidth = null;
        _catalogKeyboardStartWidth = null;
        if (CatalogSplitter.IsDragging) CatalogSplitter.CancelDrag();
    }

    private void CatalogSplitter_OnDragStarted(object sender, DragStartedEventArgs e)
    {
        _catalogDragStartWidth = CatalogColumn.ActualWidth;
    }

    private async void CatalogSplitter_OnDragCompleted(object sender, DragCompletedEventArgs e)
    {
        var startWidth = _catalogDragStartWidth;
        _catalogDragStartWidth = null;
        if (!e.Canceled)
        {
            await CommitCatalogResizeAsync(startWidth);
        }
        else
        {
            ApplyCatalogWidth();
        }
    }

    private void CatalogSplitter_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right)
        {
            _catalogKeyboardStartWidth ??= CatalogColumn.ActualWidth;
        }
    }

    private async void CatalogSplitter_OnKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Left or Key.Right)) return;
        await CommitCatalogKeyboardResizeAsync();
    }

    private async void CatalogSplitter_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await CommitCatalogKeyboardResizeAsync();
    }

    private Task CommitCatalogKeyboardResizeAsync()
    {
        var startWidth = _catalogKeyboardStartWidth;
        _catalogKeyboardStartWidth = null;
        return CommitCatalogResizeAsync(startWidth);
    }

    private Task CommitCatalogResizeAsync(double? startWidth)
    {
        var width = CatalogColumn.Width.Value;
        // A click, canceled drag or arrow pressed at a limit must not replace a
        // larger preference that is currently constrained by the viewport.
        return startWidth is { } initial && Math.Abs(width - initial) > 0.01 && _viewModel is { } viewModel
            ? RunEventOperationAsync(() => viewModel.CommitCatalogWidthAsync(width, ActivationToken), "保存目录宽度失败")
            : Task.CompletedTask;
    }
}
