using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class DiagnosticsAboutPage : System.Windows.Controls.Page, INavigationAware, INavigableView<DiagnosticsAboutViewModel>
{
    public DiagnosticsAboutPage(DiagnosticsAboutViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public DiagnosticsAboutViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        return ViewModel.LoadAsync(CancellationToken.None);
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }
}
