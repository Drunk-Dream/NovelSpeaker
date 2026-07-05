using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class TtsRulesView : UserControl
{
    public TtsRulesView()
    {
        InitializeComponent();
    }

    private async void ImportFromFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TtsRulesViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            await viewModel.ImportFromFileAsync(dialog.FileName, CancellationToken.None);
        }
    }

    private async void ImportFromClipboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TtsRulesViewModel viewModel)
        {
            return;
        }

        if (!Clipboard.ContainsText())
        {
            viewModel.NotifyClipboardTextMissing();
            return;
        }

        await viewModel.ImportJsonTextAsync(Clipboard.GetText(), "剪贴板", CancellationToken.None);
    }

    private async void ExportDraftButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not TtsRulesViewModel viewModel)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "tts-rule.json"
        };

        if (dialog.ShowDialog() == true)
        {
            await viewModel.ExportDraftToFileAsync(dialog.FileName, CancellationToken.None);
        }
    }
}
