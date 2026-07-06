using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.Pages;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class BookDetailsViewModel : ObservableObject
{
    private readonly IBookManagementService _bookManagementService;
    private readonly ICacheWorkspaceService _cacheWorkspaceService;
    private readonly IBookCoverGenerator _bookCoverGenerator;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IAppDialogService _dialogService;
    private readonly IBookDeleteDialogService _deleteDialogService;
    private readonly IBookCatalogInvalidationState _catalogInvalidationState;
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly INavigationService _navigationService;
    private BookDetails? _loadedDetails;
    private string? _bookId;

    public BookDetailsViewModel(
        IBookManagementService bookManagementService,
        ICacheWorkspaceService cacheWorkspaceService,
        IBookCoverGenerator bookCoverGenerator,
        IAppFeedbackService feedbackService,
        IAppDialogService dialogService,
        IBookDeleteDialogService deleteDialogService,
        IBookCatalogInvalidationState catalogInvalidationState,
        IPlaybackCoordinator playbackCoordinator,
        INavigationService navigationService)
    {
        _bookManagementService = bookManagementService;
        _cacheWorkspaceService = cacheWorkspaceService;
        _bookCoverGenerator = bookCoverGenerator;
        _feedbackService = feedbackService;
        _dialogService = dialogService;
        _deleteDialogService = deleteDialogService;
        _catalogInvalidationState = catalogInvalidationState;
        _playbackCoordinator = playbackCoordinator;
        _navigationService = navigationService;
        Cover = _bookCoverGenerator.Generate("未命名书籍");
    }

    public ObservableCollection<BookDetailsChapterItemViewModel> Chapters { get; } = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool hasBook;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string editTitle = string.Empty;

    [ObservableProperty]
    private string editAuthor = string.Empty;

    [ObservableProperty]
    private string displayAuthor = "未知作者";

    [ObservableProperty]
    private string originalFileName = string.Empty;

    [ObservableProperty]
    private string encoding = string.Empty;

    [ObservableProperty]
    private string totalChapterCountText = string.Empty;

    [ObservableProperty]
    private string currentChapterText = "未开始";

    [ObservableProperty]
    private double progressRatio;

    [ObservableProperty]
    private string progressText = "0%";

    [ObservableProperty]
    private string cacheSizeText = "0 B";

    [ObservableProperty]
    private GeneratedBookCover cover;

    public bool HasUnsavedChanges => _loadedDetails is not null &&
        (!string.Equals(_loadedDetails.Title, NormalizeTitle(EditTitle), StringComparison.Ordinal) ||
         !string.Equals(NormalizeAuthor(_loadedDetails.Author), NormalizeAuthor(EditAuthor), StringComparison.Ordinal));

    public bool CanSave => _loadedDetails is not null &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(NormalizeTitle(EditTitle)) &&
        HasUnsavedChanges;

    public bool CanCancelEdit => _loadedDetails is not null && !IsBusy && HasUnsavedChanges;

    public async Task LoadAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        _bookId = bookId;
        IsBusy = true;
        StatusMessage = string.Empty;

        try
        {
            var details = await _bookManagementService.GetBookDetailsAsync(bookId, cancellationToken);
            if (details is null)
            {
                ClearBook();
                StatusMessage = "未找到这本书，可能已经被删除。";
                return;
            }

            ApplyDetails(details);
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            ClearBook();
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("加载书籍详情失败", projected);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    [RelayCommand]
    private Task BackAsync(CancellationToken cancellationToken)
    {
        return RequestNavigateBackAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_loadedDetails is null || string.IsNullOrWhiteSpace(_bookId))
        {
            return;
        }

        await SaveCoreAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanCancelEdit))]
    private void CancelEdit()
    {
        if (_loadedDetails is null)
        {
            return;
        }

        EditTitle = _loadedDetails.Title;
        EditAuthor = _loadedDetails.Author ?? string.Empty;
    }

    [RelayCommand]
    private async Task ClearCacheAsync(CancellationToken cancellationToken)
    {
        if (_loadedDetails is null || string.IsNullOrWhiteSpace(_bookId) || IsBusy)
        {
            return;
        }

        var decision = await _dialogService.ShowConfirmationAsync(
            "清理本书缓存",
            "将删除这本书的音频缓存，不会删除书籍、阅读进度或内部 TXT。",
            "清理",
            "取消",
            cancellationToken);
        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _cacheWorkspaceService.ClearBookAsync(_bookId, cancellationToken);
            var details = await _bookManagementService.GetBookDetailsAsync(_bookId, cancellationToken);
            if (details is not null)
            {
                ApplyDetails(details, preserveEditor: true);
            }

            var feedback = CacheCleanupFeedbackFormatter.Format(result, "缓存已清理", "缓存已部分清理");
            if (feedback.IsWarning)
            {
                _feedbackService.ShowWarning(feedback.Title, feedback.Message);
            }
            else
            {
                _feedbackService.ShowSuccess(feedback.Title, feedback.Message);
            }
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("清理缓存失败", projected);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    [RelayCommand]
    private async Task DeleteBookAsync(CancellationToken cancellationToken)
    {
        if (_loadedDetails is null || string.IsNullOrWhiteSpace(_bookId) || IsBusy)
        {
            return;
        }

        var deleteDecision = await _deleteDialogService.ShowAsync(
            new BookDeleteDialogRequest(
                _loadedDetails.Title,
                IsCurrentPlaybackBook(_bookId)),
            cancellationToken);
        if (!deleteDecision.IsConfirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (IsCurrentPlaybackBook(_bookId))
            {
                await _playbackCoordinator.HandleBookDeletedAsync(_bookId, cancellationToken);
            }

            var result = await _bookManagementService.DeleteAsync(
                new BookDeleteRequest(_bookId, deleteDecision.DeleteAudioCache),
                cancellationToken);
            if (result is null)
            {
                StatusMessage = "这本书已不存在。";
                _catalogInvalidationState.Invalidate();
                _ = _navigationService.GoBack();
                return;
            }

            _catalogInvalidationState.Invalidate();
            _feedbackService.ShowSuccess("删除成功", $"已删除《{_loadedDetails.Title}》。");
            _ = _navigationService.GoBack();
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("删除书籍失败", projected);
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    public async Task RequestNavigateBackAsync(CancellationToken cancellationToken)
    {
        if (!HasUnsavedChanges)
        {
            _ = _navigationService.GoBack();
            return;
        }

        var decision = await _dialogService.ShowUnsavedChangesAsync(
            "未保存的修改",
            "书名或作者尚未保存。要先保存再返回吗？",
            "保存",
            "放弃",
            "取消",
            cancellationToken);

        switch (decision)
        {
            case UnsavedChangesDecision.Save:
                if (await SaveCoreAsync(cancellationToken))
                {
                    _ = _navigationService.GoBack();
                }

                break;
            case UnsavedChangesDecision.Discard:
                CancelEdit();
                _ = _navigationService.GoBack();
                break;
            default:
                break;
        }
    }

    partial void OnEditTitleChanged(string value)
    {
        NotifyCommandStateChanged();
    }

    partial void OnEditAuthorChanged(string value)
    {
        NotifyCommandStateChanged();
    }

    private async Task<bool> SaveCoreAsync(CancellationToken cancellationToken)
    {
        if (_loadedDetails is null || string.IsNullOrWhiteSpace(_bookId))
        {
            return false;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        NotifyCommandStateChanged();

        try
        {
            var updated = await _bookManagementService.UpdateMetadataAsync(
                new BookMetadataUpdateRequest(
                    _bookId,
                    NormalizeTitle(EditTitle),
                    NormalizeAuthor(EditAuthor)),
                cancellationToken);
            ApplyDetails(updated);
            _catalogInvalidationState.Invalidate();
            await _playbackCoordinator.RefreshBookMetadataAsync(_bookId, cancellationToken);
            _feedbackService.ShowSuccess("已保存", "书名和作者已更新。");
            return true;
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("保存书籍信息失败", projected);
            return false;
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStateChanged();
        }
    }

    private void ApplyDetails(BookDetails details, bool preserveEditor = false)
    {
        _loadedDetails = details;
        HasBook = true;
        Title = details.Title;
        DisplayAuthor = string.IsNullOrWhiteSpace(details.Author) ? "未知作者" : details.Author.Trim();
        OriginalFileName = details.OriginalFileName;
        Encoding = details.Encoding;
        TotalChapterCountText = $"共 {details.TotalChapterCount} 章";
        CurrentChapterText = details.HasReadingProgress
            ? details.Chapters.FirstOrDefault(chapter => chapter.IsCurrent)?.Title ?? $"第 {details.CurrentChapterIndex.GetValueOrDefault() + 1} 章"
            : "未开始";
        ProgressRatio = Math.Clamp(details.OverallProgress, 0, 1);
        ProgressText = $"{ProgressRatio:P0}";
        CacheSizeText = FormatBytes(details.CachedAudioBytes);
        Cover = _bookCoverGenerator.Generate(details.Title);

        Chapters.ReplaceWith(details.Chapters, chapter => new BookDetailsChapterItemViewModel(
            chapter.ChapterIndex,
            $"第 {chapter.ChapterIndex + 1} 章",
            chapter.Title,
            $"偏移 {chapter.StartOffset} · 长度 {chapter.Length}",
            chapter.IsCurrent));

        if (!preserveEditor || !HasUnsavedChanges)
        {
            EditTitle = details.Title;
            EditAuthor = details.Author ?? string.Empty;
        }

        StatusMessage = string.Empty;
        NotifyCommandStateChanged();
    }

    private void ClearBook()
    {
        _loadedDetails = null;
        HasBook = false;
        Title = string.Empty;
        EditTitle = string.Empty;
        EditAuthor = string.Empty;
        DisplayAuthor = "未知作者";
        OriginalFileName = string.Empty;
        Encoding = string.Empty;
        TotalChapterCountText = string.Empty;
        CurrentChapterText = "未开始";
        ProgressRatio = 0;
        ProgressText = "0%";
        CacheSizeText = "0 B";
        Cover = _bookCoverGenerator.Generate("未命名书籍");
        Chapters.Clear();
        NotifyCommandStateChanged();
    }

    private bool IsCurrentPlaybackBook(string bookId)
    {
        return string.Equals(_playbackCoordinator.CurrentSnapshot.BookId, bookId, StringComparison.Ordinal);
    }

    private void NotifyCommandStateChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanCancelEdit));
        SaveCommand.NotifyCanExecuteChanged();
        CancelEditCommand.NotifyCanExecuteChanged();
    }

    private static string NormalizeTitle(string? value)
    {
        return (value ?? string.Empty).Trim();
    }

    private static string? NormalizeAuthor(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string FormatBytes(long bytes)
    {
        const double scale = 1024d;
        if (bytes < scale)
        {
            return $"{bytes} B";
        }

        var units = new[] { "KB", "MB", "GB", "TB" };
        var size = bytes / scale;
        var unitIndex = 0;
        while (size >= scale && unitIndex < units.Length - 1)
        {
            size /= scale;
            unitIndex++;
        }

        return $"{size:0.#} {units[unitIndex]}";
    }
}
