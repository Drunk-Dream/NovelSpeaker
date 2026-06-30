using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Views;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Dialogs;

public sealed class ImportBookDialogService : IImportBookDialogService
{
    private readonly IContentDialogService _contentDialogService;
    private readonly IServiceProvider _serviceProvider;

    public ImportBookDialogService(
        IContentDialogService contentDialogService,
        IServiceProvider serviceProvider)
    {
        _contentDialogService = contentDialogService;
        _serviceProvider = serviceProvider;
    }

    public async Task<ImportBookDialogOutcome> ShowAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath) || _contentDialogService.GetDialogHostEx() is null)
        {
            return ImportBookDialogOutcome.Rejected;
        }

        var viewModel = _serviceProvider.GetRequiredService<ImportBookDialogViewModel>();
        var view = new ImportBookDialogView
        {
            DataContext = viewModel
        };
        var dialog = new ContentDialog
        {
            Title = "导入 TXT 小说",
            Content = view,
            CloseButtonText = string.Empty
        };

        ImportBookDialogOutcome? outcome = null;

        void HandleCloseRequested(ImportBookDialogOutcome requestedOutcome)
        {
            outcome = requestedOutcome;
            dialog.Hide(ContentDialogResult.None);
        }

        viewModel.CloseRequested += HandleCloseRequested;

        try
        {
            var showTask = _contentDialogService.ShowAsync(dialog, cancellationToken);
            await viewModel.InitializeAsync(filePath, cancellationToken);
            await showTask;
            return outcome ?? ImportBookDialogOutcome.Cancelled;
        }
        finally
        {
            viewModel.CloseRequested -= HandleCloseRequested;
            viewModel.Dismiss();
        }
    }
}
