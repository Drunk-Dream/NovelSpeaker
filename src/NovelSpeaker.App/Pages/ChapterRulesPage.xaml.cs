using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class ChapterRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<ChapterRulesViewModel>
{
    private readonly INavigationService _navigationService;
    private bool _hasLoaded;

    public ChapterRulesPage(INavigationService navigationService, ChapterRulesViewModel viewModel)
    {
        _navigationService = navigationService;
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
        return Task.CompletedTask;
    }

    private void BackButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _ = _navigationService.GoBack();
    }
}
