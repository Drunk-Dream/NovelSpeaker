using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private async void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ShowImportFileDialogAsync();
    }

    private void RootScrollViewer_OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RootScrollViewer_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        await viewModel.ImportFilesAsync(files ?? [], CancellationToken.None);
    }

    private async void RootScrollViewer_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.O || (Keyboard.Modifiers & ModifierKeys.Control) == 0)
        {
            return;
        }

        e.Handled = true;
        await ShowImportFileDialogAsync();
    }

    private async Task ShowImportFileDialogAsync()
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            await viewModel.ImportFilesAsync([dialog.FileName], CancellationToken.None);
        }
    }
}
