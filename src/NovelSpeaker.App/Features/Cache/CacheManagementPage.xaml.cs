using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shared.Presentation.Scrolling;
using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Cache;

public partial class CacheManagementPage : System.Windows.Controls.Page, INavigationAware, INavigableView<CacheManagementViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly PageEventOperationRunner _eventOperations;
    private ScrollViewer? _chapterScrollViewer;

    public CacheManagementPage(
        CacheManagementViewModel viewModel,
        PageEventOperationRunner? eventOperations = null)
    {
        ViewModel = viewModel;
        _eventOperations = eventOperations ?? PageEventOperationRunner.DesignTime;
        DataContext = ViewModel;
        InitializeComponent();
        ChaptersListBox.Loaded += ChaptersListBox_OnLoaded;
        ChaptersListBox.Unloaded += ChaptersListBox_OnUnloaded;
    }

    public CacheManagementViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        using var operation = _eventOperations.StartCriticalLoad();
        var activation = _activation.Activate();
        activation.Register(ViewModel.HandleNavigatedFrom);
        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Succeeded());
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Cancelled());
        }
        catch
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Failed("page-load-failed"));
            throw;
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }

    private void ChapterCard_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: CachedChapterListItemViewModel chapter })
        {
            return;
        }

        var modifiers = DesktopSelectionModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= DesktopSelectionModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= DesktopSelectionModifiers.Shift;
        }

        ViewModel.HandleChapterClick(chapter, modifiers);
    }

    private void ChapterCard_OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not Button { DataContext: CachedChapterListItemViewModel chapter } button ||
            button.ContextMenu is null)
        {
            return;
        }

        if (!chapter.IsSelected)
        {
            ViewModel.HandleChapterClick(chapter, DesktopSelectionModifiers.None);
        }

        button.ContextMenu.DataContext = DataContext;
        button.ContextMenu.PlacementTarget = button;
    }

    private void Page_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            ViewModel.HandleSelectAllChapters())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && ViewModel.TryHandleEscape())
        {
            e.Handled = true;
        }
    }

    private void ChaptersListBox_OnLoaded(object sender, RoutedEventArgs e)
    {
        _chapterScrollViewer = FindDescendant<ScrollViewer>(ChaptersListBox);
        if (_chapterScrollViewer is null)
        {
            return;
        }

        _chapterScrollViewer.ScrollChanged += ChaptersScrollViewer_OnScrollChanged;
        RequestVisibleChapterDecorationWindow();
    }

    private void ChaptersListBox_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_chapterScrollViewer is not null)
        {
            _chapterScrollViewer.ScrollChanged -= ChaptersScrollViewer_OnScrollChanged;
            _chapterScrollViewer = null;
        }
    }

    private void ChaptersScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange != 0 || e.ViewportHeightChange != 0 || e.ExtentHeightChange != 0)
        {
            RequestVisibleChapterDecorationWindow();
        }
    }

    private void RequestVisibleChapterDecorationWindow()
    {
        if (_chapterScrollViewer is null)
        {
            return;
        }

        var (start, count) = VirtualizedCatalogViewport.GetWindow(
            ChaptersListBox,
            _chapterScrollViewer,
            ViewModel.Chapters.Count,
            32);
        ViewModel.RequestChapterDecorationWindow(start, count);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typedChild)
            {
                return typedChild;
            }

            if (FindDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
