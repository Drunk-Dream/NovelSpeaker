using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Cache;

public partial class CacheManagementPage : System.Windows.Controls.Page, INavigationAware, INavigableView<CacheManagementViewModel>
{
    private readonly PageActivationController _activation = new();

    public CacheManagementPage(CacheManagementViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public CacheManagementViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        activation.Register(ViewModel.HandleNavigatedFrom);
        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
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

    private void Page_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.A &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            ViewModel.HandleSelectAllChapters())
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && ViewModel.HandleClearChapterSelection())
        {
            e.Handled = true;
        }
    }
}
