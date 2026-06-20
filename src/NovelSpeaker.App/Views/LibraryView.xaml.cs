using System.Windows;
using System.Windows.Controls;
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
            await viewModel.ImportFileAsync(dialog.FileName, CancellationToken.None);
        }
    }

    private void ImportBorder_OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ImportBorder_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            await viewModel.ImportFileAsync(files[0], CancellationToken.None);
        }
    }

    private async void RetryEncodingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            await viewModel.RetryWithEncodingCommand.ExecuteAsync(null);
        }
    }
}
