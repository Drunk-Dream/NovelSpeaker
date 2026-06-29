using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Pages;

public partial class SettingsPage : System.Windows.Controls.Page, IAppNavigationPage
{
    private readonly SettingsViewModel _viewModel;
    private bool _hasLoaded;

    public SettingsPage(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        SettingsView.DataContext = viewModel;
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
}
