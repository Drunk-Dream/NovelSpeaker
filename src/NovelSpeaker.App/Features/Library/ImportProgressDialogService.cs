using System.Windows;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Library;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Features.Library;

public sealed class ImportProgressDialogService : IImportProgressDialogService
{
    private readonly IContentDialogService _contentDialogService;

    public ImportProgressDialogService(IContentDialogService contentDialogService)
    {
        _contentDialogService = contentDialogService;
    }

    public async Task<LibraryImportCoordinatorResult> RunAsync(
        string fileName,
        Func<IProgress<BookImportProgress>, CancellationToken, Task<LibraryImportCoordinatorResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(operation);

        if (_contentDialogService.GetDialogHostEx() is null)
        {
            return await operation(new Progress<BookImportProgress>(), cancellationToken);
        }

        using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressTextBlock = new global::System.Windows.Controls.TextBlock
        {
            Text = "正在准备导入。",
            TextWrapping = TextWrapping.Wrap
        };
        var progressBar = new global::System.Windows.Controls.ProgressBar
        {
            Height = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true
        };

        ContentDialog? dialog = null;
        var cancelButton = new global::System.Windows.Controls.Button
        {
            Content = "取消",
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        cancelButton.Click += (_, _) =>
        {
            linkedCancellationTokenSource.Cancel();
            dialog?.Hide(ContentDialogResult.None);
        };

        dialog = new ContentDialog
        {
            Title = "正在导入小说",
            CloseButtonText = string.Empty,
            Content = new global::System.Windows.Controls.StackPanel
            {
                Children =
                {
                    new global::System.Windows.Controls.TextBlock
                    {
                        Text = fileName,
                        FontWeight = FontWeights.SemiBold
                    },
                    progressTextBlock,
                    progressBar,
                    cancelButton
                }
            }
        };

        var progress = new Progress<BookImportProgress>(update =>
        {
            var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
            void Apply()
            {
                progressTextBlock.Text = FormatMessage(update);
                progressBar.IsIndeterminate = update.IsIndeterminate || update.TotalBytes <= 0;
                if (!progressBar.IsIndeterminate)
                {
                    progressBar.Value = Math.Clamp(update.BytesProcessed * 100d / update.TotalBytes, 0, 100);
                }
            }

            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                _ = dispatcher.InvokeAsync(Apply);
            }
            else
            {
                Apply();
            }
        });

        var showTask = _contentDialogService.ShowAsync(dialog, cancellationToken);

        try
        {
            var result = await operation(progress, linkedCancellationTokenSource.Token);
            dialog.Hide(ContentDialogResult.None);
            await AwaitDialogClosureAsync(showTask);
            return result;
        }
        catch (OperationCanceledException) when (linkedCancellationTokenSource.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            dialog.Hide(ContentDialogResult.None);
            await AwaitDialogClosureAsync(showTask);
            return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Cancelled);
        }
    }

    private static string FormatMessage(BookImportProgress progress)
    {
        if (progress.IsIndeterminate || progress.TotalBytes <= 0)
        {
            return progress.Message;
        }

        var percent = Math.Clamp(progress.BytesProcessed * 100d / progress.TotalBytes, 0, 100);
        return $"{progress.Message} {percent:0.#}%";
    }

    private static async Task AwaitDialogClosureAsync(Task showTask)
    {
        try
        {
            await showTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
