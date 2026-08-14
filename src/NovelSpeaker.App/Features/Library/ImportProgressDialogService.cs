using System.Windows;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Features.Library;

public sealed class ImportProgressDialogService : IImportProgressDialogService
{
    private readonly IContentDialogService _contentDialogService;
    private readonly IUiScheduler _uiScheduler;
    private readonly OwnedTaskRegistry _operationTasks = new();

    public ImportProgressDialogService(
        IContentDialogService contentDialogService,
        IUiScheduler uiScheduler)
    {
        _contentDialogService = contentDialogService;
        _uiScheduler = uiScheduler;
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
            Text = "正在准备导入。"
        };
        progressTextBlock.SetResourceReference(FrameworkElement.StyleProperty, "App.Feedback.DialogMessage");
        var progressBar = new global::System.Windows.Controls.ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true,
            Margin = new Thickness(0, 16, 0, 0)
        };
        progressBar.SetResourceReference(FrameworkElement.StyleProperty, "App.Progress.Standard");

        ContentDialog? dialog = null;
        var cancelButton = new global::System.Windows.Controls.Button
        {
            Content = "取消",
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        cancelButton.SetResourceReference(FrameworkElement.StyleProperty, "App.Button.Secondary");
        cancelButton.Click += (_, _) =>
        {
            linkedCancellationTokenSource.Cancel();
            dialog?.Hide(ContentDialogResult.None);
        };

        var content = new global::System.Windows.Controls.StackPanel
        {
            Children =
            {
                AppDialogVisuals.CreateTitle(fileName),
                progressTextBlock,
                progressBar,
                cancelButton
            }
        };
        dialog = new ContentDialog
        {
            Title = "正在导入小说",
            Content = AppDialogVisuals.CreateBody(content),
            IsFooterVisible = false,
            DefaultButton = ContentDialogButton.Close
        };
        var isClosingAfterOperation = false;
        void OnDialogClosed(object sender, ContentDialogClosedEventArgs args)
        {
            if (!isClosingAfterOperation)
            {
                linkedCancellationTokenSource.Cancel();
            }
        }

        dialog.Closed += OnDialogClosed;

        var progress = new Progress<BookImportProgress>(update =>
        {
            void Apply()
            {
                progressTextBlock.Text = FormatMessage(update);
                progressBar.IsIndeterminate = update.IsIndeterminate || update.TotalBytes <= 0;
                if (!progressBar.IsIndeterminate)
                {
                    progressBar.Value = Math.Clamp(update.BytesProcessed * 100d / update.TotalBytes, 0, 100);
                }
            }

            if (!_uiScheduler.CheckAccess())
            {
                _operationTasks.Register(
                    _uiScheduler.InvokeAsync(Apply, linkedCancellationTokenSource.Token));
            }
            else
            {
                Apply();
            }
        });

        var showTask = _contentDialogService.ShowAsync(dialog, cancellationToken);

        try
        {
            return await operation(progress, linkedCancellationTokenSource.Token);
        }
        catch (OperationCanceledException) when (linkedCancellationTokenSource.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            return new LibraryImportCoordinatorResult(LibraryImportCoordinatorStatus.Cancelled);
        }
        finally
        {
            isClosingAfterOperation = true;
            dialog.Closed -= OnDialogClosed;
            dialog.Hide(ContentDialogResult.None);
            await AwaitDialogClosureAsync(showTask);
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
