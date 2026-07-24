using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Diagnostics;

public partial class DiagnosticsAboutPage : System.Windows.Controls.Page, INavigationAware, INavigableView<DiagnosticsAboutViewModel>
{
    private readonly PageActivationController _activation = new();

    public DiagnosticsAboutPage(DiagnosticsAboutViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public DiagnosticsAboutViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        ViewModel.Activate(activation.CancellationToken);
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
        ViewModel.Deactivate();
        return Task.CompletedTask;
    }
}
