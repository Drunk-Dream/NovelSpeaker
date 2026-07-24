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
}
