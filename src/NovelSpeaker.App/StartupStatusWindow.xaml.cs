using System.Windows;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App;

public partial class StartupStatusWindow : Window
{
    public StartupStatusWindow(StartupStatusViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
