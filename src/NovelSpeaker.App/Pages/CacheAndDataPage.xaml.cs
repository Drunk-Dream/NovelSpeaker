using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class CacheAndDataPage : System.Windows.Controls.Page, INavigationAware, INavigableView<CacheAndDataViewModel>
{
    public CacheAndDataPage(CacheAndDataViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public CacheAndDataViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        return ViewModel.LoadAsync(CancellationToken.None);
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }

    private async void CacheLimitValueTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.CommitCacheLimitAsync(CancellationToken.None);
    }

    private async void CacheLimitValueTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await ViewModel.CommitCacheLimitAsync(CancellationToken.None);
    }

    private void CacheLimitUnitComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CacheLimitUnitComboBox.SelectedItem is string unit)
        {
            ViewModel.ChangeCacheLimitUnit(unit);
        }
    }
}
