using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class PlaybackSettingsViewModel : SettingsSubpageViewModelBase
{
    private const int DebounceDelayMilliseconds = 500;

    private readonly IAppSettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private bool _isLoading;
    private CancellationTokenSource? _defaultSpeakSpeedDebounceCts;
    private CancellationTokenSource? _prefetchCountDebounceCts;
    private int _defaultSpeakSpeedVersion;
    private int _prefetchCountVersion;

    public PlaybackSettingsViewModel(
        IAppSettingsService settingsService,
        INavigationService navigationService,
        IAppFeedbackService feedbackService)
        : base(navigationService, feedbackService)
    {
        _settingsService = settingsService;
        _navigationService = navigationService;
    }

    [ObservableProperty]
    private string defaultSpeakSpeedText = AppSettings.DefaultSpeakSpeedValue.ToString();

    [ObservableProperty]
    private string defaultSpeakSpeedErrorText = string.Empty;

    [ObservableProperty]
    private string prefetchCountText = AppSettings.DefaultPrefetchCountValue.ToString();

    [ObservableProperty]
    private string prefetchCountErrorText = string.Empty;

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = _settingsService.Current;
            DefaultSpeakSpeedText = settings.DefaultSpeakSpeed.ToString();
            DefaultSpeakSpeedErrorText = string.Empty;
            PrefetchCountText = settings.PrefetchCount.ToString();
            PrefetchCountErrorText = string.Empty;
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand]
    private void OpenTtsRules()
    {
        _navigationService.NavigateWithHierarchy(typeof(TtsRulesPage));
    }

    public async Task CommitDefaultSpeakSpeedAsync(CancellationToken cancellationToken)
    {
        CancelPendingSave(ref _defaultSpeakSpeedDebounceCts);
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

            if (version != Volatile.Read(ref _defaultSpeakSpeedVersion))
            {
                return;
            }

            DefaultSpeakSpeedText = settings.DefaultSpeakSpeed.ToString();
            DefaultSpeakSpeedErrorText = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _defaultSpeakSpeedVersion))
            {
                ShowSaveFailure("保存默认语速失败", exception);
            }
        }
    }

    public async Task CommitPrefetchCountAsync(CancellationToken cancellationToken)
    {
        CancelPendingSave(ref _prefetchCountDebounceCts);
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
            if (version == Volatile.Read(ref _prefetchCountVersion))
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
