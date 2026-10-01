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
    private CancellationTokenSource? _thresholdDebounceCts;
    private int _thresholdVersion;
    private int _longParagraphSplitVersion;
    private int _blankLineSplitVersion;

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
    private bool enableLongParagraphSplitting;

    [ObservableProperty]
    private bool splitChaptersOnBlankLines;

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
            EnableLongParagraphSplitting = settings.EnableLongParagraphSplitting;
            SplitChaptersOnBlankLines = settings.SplitChaptersOnBlankLines;
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
        CancelPendingSave(ref _thresholdDebounceCts);
        base.Deactivate();
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenChapterRulesAsync(CancellationToken cancellationToken) =>
        Navigator.NavigateAsync(AppRoutes.ChapterRules, cancellationToken);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenFileNameMetadataRulesAsync(CancellationToken cancellationToken) =>
        Navigator.NavigateAsync(AppRoutes.FileNameMetadataRules, cancellationToken);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private Task OpenTextHeaderMetadataRulesAsync(CancellationToken cancellationToken) =>
        Navigator.NavigateAsync(AppRoutes.TextHeaderMetadataRules, cancellationToken);

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

    partial void OnSplitChaptersOnBlankLinesChanged(bool value)
    {
        if (_isLoading) return;
        var version = Interlocked.Increment(ref _blankLineSplitVersion);
        RunPageOperation(
            "保存空行分章设置失败",
            cancellationToken => SaveBlankLineSplittingAsync(value, version, cancellationToken));
    }

    private async Task SaveBlankLineSplittingAsync(bool value, int version, CancellationToken cancellationToken)
    {
        try
        {
            await _settingsService.UpdateAsync(
                new AppSettingsUpdate { SplitChaptersOnBlankLines = value }, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentActivation(cancellationToken) || version != Volatile.Read(ref _blankLineSplitVersion)) return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentActivation(cancellationToken) && version == Volatile.Read(ref _blankLineSplitVersion))
            {
                ShowSaveFailure("保存空行分章设置失败", exception);
            }
        }
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
