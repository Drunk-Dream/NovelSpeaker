using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class CacheManagementPage : System.Windows.Controls.Page, INavigationAware, INavigableView<CacheManagementViewModel>
{
    public CacheManagementPage(CacheManagementViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public CacheManagementViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        return ViewModel.LoadAsync(CancellationToken.None);
    }

    public Task OnNavigatedFromAsync()
    {
        ViewModel.HandleNavigatedFrom();
        return Task.CompletedTask;
    }
}
