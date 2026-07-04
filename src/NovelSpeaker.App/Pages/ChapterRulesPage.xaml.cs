using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class ChapterRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<ChapterRulesViewModel>
{
    private bool _hasLoaded;

    public ChapterRulesPage(ChapterRulesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ChapterRulesView.DataContext = ViewModel;
    }

    public ChapterRulesViewModel ViewModel { get; }

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
