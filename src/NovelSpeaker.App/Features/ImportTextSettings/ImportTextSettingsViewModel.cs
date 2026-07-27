using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.ImportTextSettings;

public sealed partial class ImportTextSettingsViewModel : SettingsSubpageViewModelBase
{
    private const int DebounceDelayMilliseconds = 500;

    private readonly IAppSettingsService _settingsService;
    private readonly TimeProvider _timeProvider;
    private bool _isLoading;
    private CancellationTokenSource? _templateDebounceCts;
    private CancellationTokenSource? _thresholdDebounceCts;
    private int _templateVersion;
    private int _thresholdVersion;
    private int _longParagraphSplitVersion;

    public ImportTextSettingsViewModel(
        IAppSettingsService settingsService,
        IAppNavigator navigator,
        IAppFeedbackService feedbackService,
        TimeProvider? timeProvider = null)
        : base(navigator, feedbackService)
    {
        _settingsService = settingsService;
        _timeProvider = timeProvider ?? TimeProvider.System;
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
        Activate(cancellationToken);
        _isLoading = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = _settingsService.Current;
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

    public override void Deactivate()
    {
        CancelPendingSave(ref _templateDebounceCts);
        CancelPendingSave(ref _thresholdDebounceCts);
        base.Deactivate();
    }

    public async Task CommitBookFileNameTemplateAsync(CancellationToken cancellationToken)
    {
        CompleteOrCancelPendingSave(ref _templateDebounceCts, cancellationToken);
        var version = Interlocked.Increment(ref _templateVersion);

        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    BookFileNameTemplate = BookFileNameTemplateText
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
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
            if (!cancellationToken.IsCancellationRequested &&
                version == Volatile.Read(ref _templateVersion))
            {
                ShowSaveFailure("保存文件名模板失败", exception);
            }
        }
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenRegexReplacementRulesAsync(CancellationToken cancellationToken)
    {
        return Navigator.NavigateAsync(AppRoutes.RegexReplacementRules, cancellationToken);
    }

    public async Task CommitLongParagraphThresholdAsync(CancellationToken cancellationToken)
    {
        CompleteOrCancelPendingSave(ref _thresholdDebounceCts, cancellationToken);
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

            cancellationToken.ThrowIfCancellationRequested();
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
            if (!cancellationToken.IsCancellationRequested &&
                version == Volatile.Read(ref _thresholdVersion))
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
        RunPageOperation(
            "保存超长段落拆分设置失败",
            cancellationToken => SaveLongParagraphSplittingAsync(value, version, cancellationToken));
    }

    partial void OnLongParagraphThresholdTextChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        ScheduleDebouncedCommit(ref _thresholdDebounceCts, ct => CommitLongParagraphThresholdAsync(ct));
    }

    private async Task SaveLongParagraphSplittingAsync(
        bool value,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    EnableLongParagraphSplitting = value
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentActivation(cancellationToken) ||
                version != Volatile.Read(ref _longParagraphSplitVersion))
            {
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentActivation(cancellationToken) &&
                version == Volatile.Read(ref _longParagraphSplitVersion))
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
        CancellationTokenSource? operationCts = null;

        RunPageOperation(
            "保存文本设置失败",
            currentActivationToken =>
            {
                operationCts = CancellationTokenSource.CreateLinkedTokenSource(currentActivationToken);
                return RunDebouncedCommitAsync(
                    operationCts,
                    currentActivationToken,
                    commitAsync);
            });
        cancellationTokenSource = operationCts;
    }

    private async Task RunDebouncedCommitAsync(
        CancellationTokenSource operationCts,
        CancellationToken activationToken,
        Func<CancellationToken, Task> commitAsync)
    {
        var cancellationToken = operationCts.Token;
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(DebounceDelayMilliseconds), _timeProvider, cancellationToken);
            activationToken.ThrowIfCancellationRequested();
            if (!IsCurrentActivation(activationToken))
            {
                return;
            }

            await commitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            operationCts.Dispose();
        }
    }

    private static void CancelPendingSave(ref CancellationTokenSource? cancellationTokenSource)
    {
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
    }

    private static void CompleteOrCancelPendingSave(
        ref CancellationTokenSource? cancellationTokenSource,
        CancellationToken commitToken)
    {
        if (cancellationTokenSource is not null &&
            cancellationTokenSource.Token == commitToken)
        {
            cancellationTokenSource = null;
            return;
        }

        CancelPendingSave(ref cancellationTokenSource);
    }
}
