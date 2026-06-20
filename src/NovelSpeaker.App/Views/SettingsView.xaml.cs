using System.Windows.Controls;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
        {
            await viewModel.LoadAsync(CancellationToken.None);
        }
    }
}
