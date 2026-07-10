using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class RegexReplacementRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<RegexReplacementRulesViewModel>
{
    public RegexReplacementRulesPage(RegexReplacementRulesViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); Workspace.DataContext = viewModel; }
    public RegexReplacementRulesViewModel ViewModel { get; }
    public Task OnNavigatedToAsync() => ViewModel.LoadAsync(CancellationToken.None);
    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
