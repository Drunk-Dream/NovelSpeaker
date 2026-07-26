using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Cache;

public partial class CacheAndDataPage : System.Windows.Controls.Page, INavigationAware, INavigableView<CacheAndDataViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly PageEventOperationRunner _eventOperations;

    public CacheAndDataPage(
        CacheAndDataViewModel viewModel,
        PageEventOperationRunner eventOperations)
    {
        ViewModel = viewModel;
        _eventOperations = eventOperations;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public CacheAndDataViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        ViewModel.Activate(activation);
        activation.Register(ViewModel.Deactivate);
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

    private async void CacheLimitValueTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await _eventOperations.RunAsync(
            _activation,
            "保存缓存上限失败",
            ViewModel.CommitCacheLimitAsync);
    }

    private async void CacheLimitValueTextBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        await _eventOperations.RunAsync(
            _activation,
            "保存缓存上限失败",
            ViewModel.CommitCacheLimitAsync);
    }

    private void CacheLimitUnitComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CacheLimitUnitComboBox.SelectedItem is string unit)
        {
            ViewModel.ChangeCacheLimitUnit(unit);
        }
    }
}
