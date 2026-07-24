using NovelSpeaker.App.Activation;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class RegexReplacementRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<RegexReplacementRulesViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;

    public RegexReplacementRulesPage(
        RegexReplacementRulesViewModel viewModel,
        INavigationGuardService navigationGuardService)
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        InitializeComponent();
        Workspace.DataContext = viewModel;
    }

    public RegexReplacementRulesViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        activation.Register(_navigationGuardService.Register(ViewModel.ConfirmLeaveAsync));
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
}
