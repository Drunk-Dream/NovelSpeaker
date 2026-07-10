using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class ImportTextSettingsViewModel : SettingsSubpageViewModelBase
{
    private const int DebounceDelayMilliseconds = 500;

    private readonly IAppSettingsService _settingsService;
    private bool _isLoading;
    private CancellationTokenSource? _templateDebounceCts;
    private CancellationTokenSource? _thresholdDebounceCts;
    private int _templateVersion;
    private int _thresholdVersion;
    private int _longParagraphSplitVersion;

    public ImportTextSettingsViewModel(
        IAppSettingsService settingsService,
        INavigationService navigationService,
        IAppFeedbackService feedbackService)
        : base(navigationService, feedbackService)
    {
        _settingsService = settingsService;
    }

    [ObservableProperty]
    private string bookFileNameTemplateText = string.Empty;

    [ObservableProperty]
    private bool enableLongParagraphSplitting;

    [ObservableProperty]
    private string longParagraphThresholdText = string.Empty;

    [ObservableProperty]
    private string longParagraphThresholdErrorText = string.Empty;

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsService.LoadAsync(cancellationToken);
            BookFileNameTemplateText = settings.BookFileNameTemplate ?? string.Empty;
            EnableLongParagraphSplitting = settings.EnableLongParagraphSplitting;
            LongParagraphThresholdText = settings.LongParagraphThreshold.ToString();
            LongParagraphThresholdErrorText = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    public async Task CommitBookFileNameTemplateAsync(CancellationToken cancellationToken)
    {
        CancelPendingSave(ref _templateDebounceCts);
        var version = Interlocked.Increment(ref _templateVersion);

        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    BookFileNameTemplate = BookFileNameTemplateText
                },
                cancellationToken);

            if (version != Volatile.Read(ref _templateVersion))
            {
                return;
            }

            BookFileNameTemplateText = settings.BookFileNameTemplate ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _templateVersion))
            {
                ShowSaveFailure("保存文件名模板失败", exception);
            }
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void OpenRegexReplacementRules()
    {
        NavigationService.NavigateWithHierarchy(typeof(RegexReplacementRulesPage));
    }

    public async Task CommitLongParagraphThresholdAsync(CancellationToken cancellationToken)
    {
        CancelPendingSave(ref _thresholdDebounceCts);
        var version = Interlocked.Increment(ref _thresholdVersion);

        if (!int.TryParse(LongParagraphThresholdText, out var parsedThreshold))
        {
            LongParagraphThresholdErrorText = "请输入整数。";
            return;
        }

        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    LongParagraphThreshold = parsedThreshold
                },
                cancellationToken);

            if (version != Volatile.Read(ref _thresholdVersion))
            {
                return;
            }

            LongParagraphThresholdText = settings.LongParagraphThreshold.ToString();
            LongParagraphThresholdErrorText = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _thresholdVersion))
            {
                ShowSaveFailure("保存长段拆分阈值失败", exception);
            }
        }
    }

    partial void OnBookFileNameTemplateTextChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        ScheduleDebouncedCommit(ref _templateDebounceCts, ct => CommitBookFileNameTemplateAsync(ct));
    }

    partial void OnEnableLongParagraphSplittingChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        var version = Interlocked.Increment(ref _longParagraphSplitVersion);
        _ = SaveLongParagraphSplittingAsync(value, version);
    }

    partial void OnLongParagraphThresholdTextChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        ScheduleDebouncedCommit(ref _thresholdDebounceCts, ct => CommitLongParagraphThresholdAsync(ct));
    }

    private async Task SaveLongParagraphSplittingAsync(bool value, int version)
    {
        try
        {
            await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    EnableLongParagraphSplitting = value
                },
                CancellationToken.None);

            if (version != Volatile.Read(ref _longParagraphSplitVersion))
            {
                return;
            }
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _longParagraphSplitVersion))
            {
                ShowSaveFailure("保存超长段落拆分设置失败", exception);
            }
        }
    }

    private void ScheduleDebouncedCommit(
        ref CancellationTokenSource? cancellationTokenSource,
        Func<CancellationToken, Task> commitAsync)
    {
        CancelPendingSave(ref cancellationTokenSource);
        cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;

        _ = RunDebouncedCommitAsync(token, commitAsync);
    }

    private static async Task RunDebouncedCommitAsync(
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> commitAsync)
    {
        try
        {
            await Task.Delay(DebounceDelayMilliseconds, cancellationToken);
            await commitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static void CancelPendingSave(ref CancellationTokenSource? cancellationTokenSource)
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
    }
}
