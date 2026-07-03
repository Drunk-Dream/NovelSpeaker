using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class TtsRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<TtsRulesViewModel>
{
    private bool _hasLoaded;

    public TtsRulesPage(TtsRulesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        TtsRulesView.DataContext = ViewModel;
    }

    public TtsRulesViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        if (_hasLoaded)
        {
            return;
        }

        await ViewModel.LoadAsync(CancellationToken.None);
        _hasLoaded = true;
    }

    public Task OnNavigatedFromAsync()
    {
        ViewModel.HandleNavigatedFrom();
        return Task.CompletedTask;
    }
}
