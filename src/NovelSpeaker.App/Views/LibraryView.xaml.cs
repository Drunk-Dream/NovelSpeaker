using System.Windows;
using System.Windows.Controls;
using NovelSpeaker.App.Input;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class LibraryView : UserControl
{
    public ITextFilePicker? TextFilePicker { get; set; }

    public LibraryView()
    {
        InitializeComponent();
    }

    private async void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        await ShowImportFileDialogAsync();
    }

    private void RootGrid_OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RootGrid_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        await viewModel.ImportFilesAsync(files ?? [], CancellationToken.None);
    }

    private async Task ShowImportFileDialogAsync()
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        if (TextFilePicker is null)
        {
            return;
        }

        var filePath = await TextFilePicker.PickSingleTextFileAsync(CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            await viewModel.ImportFilesAsync([filePath], CancellationToken.None);
        }
    }
}
