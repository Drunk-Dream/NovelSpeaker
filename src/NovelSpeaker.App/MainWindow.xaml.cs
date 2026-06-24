using System.Windows;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App;

/// <summary>
/// Hosts the application shell and forwards top-level navigation clicks.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void LibraryButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.ShowLibrary();
    private void PlayerButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.ShowPlayer();
    private void RulesButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.ShowRules();
    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.ShowSettings();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.Rules.LoadAsync(CancellationToken.None);
    }
}
