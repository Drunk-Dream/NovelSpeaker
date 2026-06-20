using System.Windows.Controls;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class ChapterRulesView : UserControl
{
    public ChapterRulesView()
    {
        InitializeComponent();
    }

    private async void ImportDefaultsButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ChapterRulesViewModel viewModel)
        {
            await viewModel.ImportDefaultsAsync(CancellationToken.None);
        }
    }
}
