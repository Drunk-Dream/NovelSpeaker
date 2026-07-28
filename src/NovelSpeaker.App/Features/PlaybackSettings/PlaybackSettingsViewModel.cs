using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.PlaybackSettings;

public sealed partial class PlaybackSettingsViewModel : SettingsSubpageViewModelBase
{
    private const int DebounceDelayMilliseconds = 500;

    private readonly IAppSettingsService _settingsService;
    private readonly IPlaybackSession _playbackCoordinator;
    private readonly IAppNavigator _navigator;
    private readonly TimeProvider _timeProvider;
    private bool _isLoading;
    private CancellationTokenSource? _defaultSpeakSpeedDebounceCts;
    private CancellationTokenSource? _prefetchCountDebounceCts;
    private int _defaultSpeakSpeedVersion;
    private int _prefetchCountVersion;
    private int _readChapterTitleVersion;

    public PlaybackSettingsViewModel(
        IAppSettingsService settingsService,
        IPlaybackSession playbackCoordinator,
        IAppNavigator navigator,
        IAppFeedbackService feedbackService,
        TimeProvider? timeProvider = null)
        : base(navigator, feedbackService)
    {
        _settingsService = settingsService;
        _playbackCoordinator = playbackCoordinator;
        _navigator = navigator;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [ObservableProperty]
    private string defaultSpeakSpeedText = AppSettings.DefaultSpeakSpeedValue.ToString();

    [ObservableProperty]
    private string defaultSpeakSpeedErrorText = string.Empty;

    [ObservableProperty]
    private string prefetchCountText = AppSettings.DefaultPrefetchCountValue.ToString();

    [ObservableProperty]
    private string prefetchCountErrorText = string.Empty;

    [ObservableProperty]
    private bool readChapterTitle;

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        Activate(cancellationToken);
        _isLoading = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = _settingsService.Current;
            DefaultSpeakSpeedText = settings.DefaultSpeakSpeed.ToString();
            DefaultSpeakSpeedErrorText = string.Empty;
            PrefetchCountText = settings.PrefetchCount.ToString();
            PrefetchCountErrorText = string.Empty;
            ReadChapterTitle = settings.ReadChapterTitle;
        }
        finally
        {
            _isLoading = false;
        }
    }

    public override void Deactivate()
    {
        CancelPendingSave(ref _defaultSpeakSpeedDebounceCts);
        CancelPendingSave(ref _prefetchCountDebounceCts);
        base.Deactivate();
    }

    [RelayCommand]
    private Task OpenTtsRulesAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.TtsRules, cancellationToken);
    }

    public async Task CommitDefaultSpeakSpeedAsync(CancellationToken cancellationToken)
    {
        CompleteOrCancelPendingSave(ref _defaultSpeakSpeedDebounceCts, cancellationToken);
        var version = Interlocked.Increment(ref _defaultSpeakSpeedVersion);

        if (!int.TryParse(DefaultSpeakSpeedText, out var parsedSpeed))
        {
            DefaultSpeakSpeedErrorText = $"请输入 {AppSettings.MinSpeakSpeed} 到 {AppSettings.MaxSpeakSpeed} 的整数。";
            return;
        }

        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    DefaultSpeakSpeed = parsedSpeed
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _defaultSpeakSpeedVersion))
            {
                return;
            }

            DefaultSpeakSpeedText = settings.DefaultSpeakSpeed.ToString();
            DefaultSpeakSpeedErrorText = string.Empty;

            if (!string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookId) &&
                _playbackCoordinator.CurrentSnapshot.SpeakSpeed != settings.DefaultSpeakSpeed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _playbackCoordinator.ChangeSpeedAsync(settings.DefaultSpeakSpeed, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested &&
                version == Volatile.Read(ref _defaultSpeakSpeedVersion))
            {
                ShowSaveFailure("更新语速失败", exception);
            }
        }
    }

    public async Task CommitPrefetchCountAsync(CancellationToken cancellationToken)
    {
        CompleteOrCancelPendingSave(ref _prefetchCountDebounceCts, cancellationToken);
        var version = Interlocked.Increment(ref _prefetchCountVersion);

        if (!int.TryParse(PrefetchCountText, out var parsedCount))
        {
            PrefetchCountErrorText = $"请输入 0 到 {AppSettings.DefaultPrefetchCountValue} 的整数。";
            return;
        }

        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    PrefetchCount = parsedCount
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _prefetchCountVersion))
            {
                return;
            }

            PrefetchCountText = settings.PrefetchCount.ToString();
            PrefetchCountErrorText = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested &&
                version == Volatile.Read(ref _prefetchCountVersion))
            {
                ShowSaveFailure("保存预取段落数量失败", exception);
            }
        }
    }

    partial void OnDefaultSpeakSpeedTextChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        ScheduleDebouncedCommit(ref _defaultSpeakSpeedDebounceCts, ct => CommitDefaultSpeakSpeedAsync(ct));
    }

    partial void OnPrefetchCountTextChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        ScheduleDebouncedCommit(ref _prefetchCountDebounceCts, ct => CommitPrefetchCountAsync(ct));
    }

    partial void OnReadChapterTitleChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        var version = Interlocked.Increment(ref _readChapterTitleVersion);
        RunPageOperation(
            "保存朗读标题设置失败",
            cancellationToken => SaveReadChapterTitleAsync(value, version, cancellationToken));
    }

    private async Task SaveReadChapterTitleAsync(
        bool value,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            await _settingsService.UpdateAsync(
                new AppSettingsUpdate { ReadChapterTitle = value },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentActivation(cancellationToken) ||
                version != Volatile.Read(ref _readChapterTitleVersion))
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
                version == Volatile.Read(ref _readChapterTitleVersion))
            {
                ShowSaveFailure("保存朗读标题设置失败", exception);
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
            "保存播放设置失败",
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
        var pendingSave = Interlocked.Exchange(ref cancellationTokenSource, null);
        if (pendingSave is null)
        {
            return;
        }

        try
        {
            pendingSave.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The debounce operation completed and disposed this CTS concurrently.
        }
        finally
        {
            pendingSave.Dispose();
        }
    }

    private static void CompleteOrCancelPendingSave(
        ref CancellationTokenSource? cancellationTokenSource,
        CancellationToken commitToken)
    {
        if (cancellationTokenSource is not null && cancellationTokenSource.Token == commitToken)
        {
            cancellationTokenSource = null;
            return;
        }

        CancelPendingSave(ref cancellationTokenSource);
    }
}
