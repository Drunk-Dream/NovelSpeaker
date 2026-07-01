using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class PlayerView : UserControl
{
    private PlayerViewModel? _viewModel;

    public PlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        AttachViewModel(DataContext as PlayerViewModel);
        _viewModel?.UpdateLayoutWidth(ActualWidth);
        EnsureCurrentChapterVisible();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        AttachViewModel(e.NewValue as PlayerViewModel);
    }

    private void PlayerView_OnSizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        _viewModel?.UpdateLayoutWidth(e.NewSize.Width);
    }

    private void AttachViewModel(PlayerViewModel? viewModel)
    {
        if (viewModel is null || ReferenceEquals(_viewModel, viewModel))
        {
            _viewModel = viewModel;
            return;
        }

        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.CurrentChapterItem))
        {
            Dispatcher.BeginInvoke(EnsureCurrentChapterVisible, DispatcherPriority.Background);
        }
    }

    private void EnsureCurrentChapterVisible()
    {
        if (_viewModel?.CurrentChapterItem is null)
        {
            return;
        }

        WideChaptersListBox.ScrollIntoView(_viewModel.CurrentChapterItem);
        DrawerChaptersListBox.ScrollIntoView(_viewModel.CurrentChapterItem);
    }
}
