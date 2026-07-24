using System.Windows;

namespace NovelSpeaker.App.Bootstrap;

public partial class StartupStatusWindow : Window
{
    public StartupStatusWindow(StartupStatusViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
