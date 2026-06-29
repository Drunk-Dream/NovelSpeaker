using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Pages;

public partial class ChapterRulesPage : System.Windows.Controls.Page, IAppNavigationPage
{
    private readonly IAppNavigationService _navigationService;
    private readonly ChapterRulesViewModel _viewModel;
    private bool _hasLoaded;

    public ChapterRulesPage(IAppNavigationService navigationService, ChapterRulesViewModel viewModel)
    {
        _navigationService = navigationService;
        _viewModel = viewModel;
        InitializeComponent();
        ChapterRulesView.DataContext = viewModel;
    }

    public async Task OnNavigatedToAsync(AppNavigationEntry entry, CancellationToken cancellationToken)
    {
        if (_hasLoaded)
        {
            return;
        }

        await _viewModel.LoadAsync(cancellationToken);
        _hasLoaded = true;
    }

    private void BackButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _navigationService.GoBack();
    }
}
