using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Shared.Presentation;
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
    private readonly TimeProvider _timeProvider;
    private bool _isLoading;
    private readonly LatestOperationSlot _defaultSpeakSpeedSave = new();
    private readonly LatestOperationSlot _prefetchCountSave = new();
    private readonly LatestOperationSlot _readChapterTitleSave = new();

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
        _defaultSpeakSpeedSave.Cancel();
        _prefetchCountSave.Cancel();
        _readChapterTitleSave.Cancel();
        base.Deactivate();
    }

    public Task CommitDefaultSpeakSpeedAsync(CancellationToken cancellationToken) =>
        RunLatestSaveAsync(_defaultSpeakSpeedSave, "更新语速失败", SaveDefaultSpeakSpeedAsync, cancellationToken);

    private async Task SaveDefaultSpeakSpeedAsync(LatestOperationSlot.Operation operation)
    {
        if (!int.TryParse(DefaultSpeakSpeedText, out var speed))
        {
            operation.TryCommit(() => DefaultSpeakSpeedErrorText = $"请输入 {AppSettings.MinSpeakSpeed} 到 {AppSettings.MaxSpeakSpeed} 的整数。");
            return;
        }

        var settings = await _settingsService.UpdateAsync(
            new AppSettingsUpdate { DefaultSpeakSpeed = speed }, operation.CancellationToken);
        if (!operation.TryCommit(() =>
        {
            _isLoading = true;
            try
            {
                DefaultSpeakSpeedText = settings.DefaultSpeakSpeed.ToString();
                DefaultSpeakSpeedErrorText = string.Empty;
            }
            finally { _isLoading = false; }
        })) return;

        if (!string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookId) &&
            _playbackCoordinator.CurrentSnapshot.SpeakSpeed != settings.DefaultSpeakSpeed)
            await _playbackCoordinator.ChangeSpeedAsync(settings.DefaultSpeakSpeed, operation.CancellationToken);
    }

    public Task CommitPrefetchCountAsync(CancellationToken cancellationToken) =>
        RunLatestSaveAsync(_prefetchCountSave, "保存预取段落数量失败", SavePrefetchCountAsync, cancellationToken);

    private async Task SavePrefetchCountAsync(LatestOperationSlot.Operation operation)
    {
        if (!int.TryParse(PrefetchCountText, out var count))
        {
            operation.TryCommit(() => PrefetchCountErrorText = $"请输入 0 到 {AppSettings.DefaultPrefetchCountValue} 的整数。");
            return;
        }

        var settings = await _settingsService.UpdateAsync(
            new AppSettingsUpdate { PrefetchCount = count }, operation.CancellationToken);
        operation.TryCommit(() =>
        {
            _isLoading = true;
            try
            {
                PrefetchCountText = settings.PrefetchCount.ToString();
                PrefetchCountErrorText = string.Empty;
            }
            finally { _isLoading = false; }
        });
    }

    partial void OnDefaultSpeakSpeedTextChanged(string value)
    {
        if (_isLoading) return;
        ScheduleLatestSave(_defaultSpeakSpeedSave, "更新语速失败", async operation =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(DebounceDelayMilliseconds), _timeProvider, operation.CancellationToken);
            if (operation.IsCurrent) await SaveDefaultSpeakSpeedAsync(operation);
        });
    }

    partial void OnPrefetchCountTextChanged(string value)
    {
        if (_isLoading) return;
        ScheduleLatestSave(_prefetchCountSave, "保存预取段落数量失败", async operation =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(DebounceDelayMilliseconds), _timeProvider, operation.CancellationToken);
            if (operation.IsCurrent) await SavePrefetchCountAsync(operation);
        });
    }

    partial void OnReadChapterTitleChanged(bool value)
    {
        if (_isLoading) return;
        ScheduleLatestSave(_readChapterTitleSave, "保存朗读标题设置失败", operation =>
            _settingsService.UpdateAsync(new AppSettingsUpdate { ReadChapterTitle = value }, operation.CancellationToken));
    }
}
