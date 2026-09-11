using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.Cache;

public sealed partial class CacheAndDataViewModel : SettingsSubpageViewModelBase
{
    private const string CleanupImpactMessage = "此操作只会清理音频缓存，不会删除书籍、章节、阅读进度、TTS 规则或章节规则。";
    private const int DebounceDelayMilliseconds = 500;
    private const long Megabyte = 1024L * 1024;
    private const long Gigabyte = 1024L * 1024 * 1024;

    private readonly IAppSettingsService _settingsService;
    private readonly IAudioCacheStore _cacheStore;
    private readonly ICacheCatalog _cacheCatalog;
    private readonly ICacheInvalidationCoordinator _invalidationCoordinator;
    private readonly IAppDiagnosticsService _diagnosticsService;
    private readonly IAppNavigator _navigator;
    private readonly IAppDialogService _dialogService;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IUiScheduler _uiScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly OwnedTaskRegistry _liveRefreshTasks = new();
    private readonly object _overviewRefreshSync = new();
    private CancellationTokenSource? _cacheLimitDebounceCts;
    private CacheOverviewModel? _overview;
    private TaskCompletionSource? _overviewRefreshCompletion;
    private bool _overviewRefreshRequested;
    private int _overviewRefreshVersion;
    private int _overviewAppliedVersion;
    private bool _isInvalidationRegistered;
    private bool _isLoading;
    private int _cacheLimitVersion;
    private long _savedCacheLimitBytes = AppSettings.DefaultCacheLimitBytes;

    public CacheAndDataViewModel(
        IAppSettingsService settingsService,
        IAudioCacheStore cacheStore,
        ICacheCatalog cacheCatalog,
        ICacheInvalidationCoordinator invalidationCoordinator,
        IAppDiagnosticsService diagnosticsService,
        IAppNavigator navigator,
        IAppDialogService dialogService,
        IAppFeedbackService feedbackService,
        TimeProvider? timeProvider = null,
        IUiScheduler? uiScheduler = null)
        : base(navigator, feedbackService)
    {
        _settingsService = settingsService;
        _cacheStore = cacheStore;
        _cacheCatalog = cacheCatalog;
        _invalidationCoordinator = invalidationCoordinator;
        _diagnosticsService = diagnosticsService;
        _navigator = navigator;
        _dialogService = dialogService;
        _feedbackService = feedbackService;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<string> CacheLimitUnits { get; } = ["GB", "MB"];

    [ObservableProperty]
    private bool isOverviewLoaded;

    [ObservableProperty]
    private bool hasLoadError;

    [ObservableProperty]
    private string loadErrorMessage = string.Empty;

    [ObservableProperty]
    private string totalCacheSizeText = "0 B";

    [ObservableProperty]
    private string cacheEntryCountText = "0 项缓存";

    [ObservableProperty]
    private string usageText = string.Empty;

    [ObservableProperty]
    private double usagePercentage;

    [ObservableProperty]
    private string cacheLimitValueText = "2";

    [ObservableProperty]
    private string cacheLimitErrorText = string.Empty;

    [ObservableProperty]
    private string selectedCacheLimitUnit = "GB";

    [ObservableProperty]
    private bool isClearingAll;

    public bool CanClearAll =>
        IsOverviewLoaded &&
        !_isLoading &&
        !IsClearingAll &&
        _overview is { EntryCount: > 0 };

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        Activate(cancellationToken);
        RegisterInvalidationSubscription();
        _isLoading = true;
        NotifyClearAllCommandState();
        HasLoadError = false;
        LoadErrorMessage = string.Empty;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = _settingsService.Current;
            _savedCacheLimitBytes = settings.CacheLimitBytes;
            ApplyCacheLimit(_savedCacheLimitBytes);
            await RequestOverviewRefreshAsync(cancellationToken);
            if (IsCurrentActivation(cancellationToken))
            {
                IsOverviewLoaded = true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (IsCurrentActivation(cancellationToken))
            {
                HasLoadError = true;
                LoadErrorMessage = "加载缓存总览失败，请重试。";
                IsOverviewLoaded = false;
            }
        }
        finally
        {
            if (IsCurrentActivation(cancellationToken))
            {
                _isLoading = false;
                NotifyClearAllCommandState();
            }
        }
    }

    public override void Deactivate()
    {
        CancelPendingSave();
        UnregisterInvalidationSubscription();
        TaskCompletionSource? retiredRefresh;
        lock (_overviewRefreshSync)
        {
            _overviewRefreshVersion++;
            _overviewRefreshRequested = false;
            retiredRefresh = _overviewRefreshCompletion;
            _overviewRefreshCompletion = null;
        }

        retiredRefresh?.TrySetCanceled();
        base.Deactivate();
    }

    [RelayCommand]
    private Task RetryAsync(CancellationToken cancellationToken)
    {
        return LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private Task OpenCacheManagementAsync(CancellationToken cancellationToken)
    {
        return _navigator.NavigateAsync(AppRoutes.CacheManagement, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanClearAll), AllowConcurrentExecutions = false)]
    private async Task ClearAllAsync(CancellationToken cancellationToken)
    {
        if (!CanClearAll)
        {
            return;
        }

        var decision = await _dialogService.ShowConfirmationAsync(
            "清理全部缓存",
            $"将清理全部音频缓存。{CleanupImpactMessage}",
            "清理",
            "取消",
            cancellationToken);
        if (decision != AppConfirmationDecision.Confirm)
        {
            return;
        }

        IsClearingAll = true;
        try
        {
            var overviewVersion = GetOverviewRefreshVersion();
            var result = await _cacheStore.ClearAllAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshOverviewAfterCacheMutationAsync(overviewVersion, cancellationToken);
            ShowCleanupFeedback(result);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _feedbackService.ShowProjectedNotification("清理失败", _feedbackService.Project(exception));
        }
        finally
        {
            IsClearingAll = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenAppDataDirectoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _diagnosticsService.OpenAppDataDirectoryAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowSaveFailure("打开应用数据目录失败", exception);
        }
    }

    public void ChangeCacheLimitUnit(string newUnit)
    {
        if (string.IsNullOrWhiteSpace(newUnit) || string.Equals(newUnit, SelectedCacheLimitUnit, StringComparison.Ordinal))
        {
            return;
        }

        if (TryParseCacheLimitBytes(CacheLimitValueText, SelectedCacheLimitUnit, out var bytes, out _))
        {
            SelectedCacheLimitUnit = newUnit;
            CacheLimitValueText = ConvertBytesToUnitValue(bytes, newUnit).ToString();
            CacheLimitErrorText = string.Empty;
        }
        else
        {
            SelectedCacheLimitUnit = newUnit;
        }

        if (!_isLoading)
        {
            ScheduleDebouncedCommit();
        }
    }

    public async Task CommitCacheLimitAsync(CancellationToken cancellationToken)
    {
        CompleteOrCancelPendingSave(cancellationToken);
        var version = Interlocked.Increment(ref _cacheLimitVersion);

        if (!TryParseCacheLimitBytes(CacheLimitValueText, SelectedCacheLimitUnit, out var cacheLimitBytes, out var errorMessage))
        {
            CacheLimitErrorText = errorMessage;
            return;
        }

        if (cacheLimitBytes == _savedCacheLimitBytes)
        {
            CacheLimitErrorText = string.Empty;
            return;
        }

        var overviewForDecision = await GetCurrentOverviewForCacheLimitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var requiresTrim = overviewForDecision is not null &&
                           cacheLimitBytes < overviewForDecision.TotalSizeBytes;
        if (requiresTrim)
        {
            var decision = await _dialogService.ShowConfirmationAsync(
                "降低缓存上限",
                "新的缓存上限低于当前占用。保存后会按最近最少使用顺序立即清理缓存；不会删除书籍、章节、阅读进度、TTS 规则或章节规则。",
                "保存并清理",
                "取消",
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (decision != AppConfirmationDecision.Confirm)
            {
                if (version == Volatile.Read(ref _cacheLimitVersion))
                {
                    ApplyCacheLimit(_savedCacheLimitBytes);
                    CacheLimitErrorText = string.Empty;
                }

                return;
            }
        }

        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    CacheLimitBytes = cacheLimitBytes
                },
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (version != Volatile.Read(ref _cacheLimitVersion))
            {
                return;
            }

            _savedCacheLimitBytes = settings.CacheLimitBytes;
            ApplyCacheLimit(_savedCacheLimitBytes);
            CacheLimitErrorText = string.Empty;

            if (requiresTrim)
            {
                var overviewVersion = GetOverviewRefreshVersion();
                await _cacheStore.RunMaintenanceAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await RefreshOverviewAfterCacheMutationAsync(overviewVersion, cancellationToken);
            }

            if (!requiresTrim)
            {
                await RequestOverviewRefreshAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (requiresTrim && _overview?.IsOverLimit == true)
            {
                _feedbackService.ShowWarning("缓存仍高于上限", "仍有受保护的正在使用缓存，停止播放后可继续清理。");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested &&
                version == Volatile.Read(ref _cacheLimitVersion))
            {
                ShowSaveFailure("保存缓存上限失败", exception);
            }
        }
    }

    partial void OnCacheLimitValueTextChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        ScheduleDebouncedCommit();
    }

    private Task RequestOverviewRefreshAsync(CancellationToken cancellationToken)
    {
        var refreshCancellationToken = ActivationToken.IsCancellationRequested
            ? cancellationToken
            : ActivationToken;
        TaskCompletionSource completion;
        var startRefresh = false;
        lock (_overviewRefreshSync)
        {
            _overviewRefreshRequested = true;
            if (_overviewRefreshCompletion is null)
            {
                completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _overviewRefreshCompletion = completion;
                startRefresh = true;
            }
            else
            {
                completion = _overviewRefreshCompletion;
            }
        }

        if (startRefresh)
        {
            _liveRefreshTasks.Register(
                RefreshOverviewLoopAsync(completion, refreshCancellationToken),
                exception => ReportOverviewRefreshFailure(exception, refreshCancellationToken));
        }

        return completion.Task;
    }

    private async Task RefreshOverviewLoopAsync(
        TaskCompletionSource completion,
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                int requestVersion;
                lock (_overviewRefreshSync)
                {
                    if (!_overviewRefreshRequested)
                    {
                        completion.TrySetResult();
                        return;
                    }

                    _overviewRefreshRequested = false;
                    requestVersion = _overviewRefreshVersion;
                }

                var overview = await _cacheCatalog
                    .GetOverviewAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                await _uiScheduler.InvokeAsync(
                    () =>
                    {
                        lock (_overviewRefreshSync)
                        {
                            if (requestVersion != _overviewRefreshVersion ||
                                !IsCurrentActivation(cancellationToken))
                            {
                                return;
                            }

                            _overviewAppliedVersion = requestVersion;
                            ApplyOverview(overview);
                        }
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
            throw;
        }
        finally
        {
            var restart = false;
            lock (_overviewRefreshSync)
            {
                if (ReferenceEquals(_overviewRefreshCompletion, completion))
                {
                    _overviewRefreshCompletion = null;
                    restart = _overviewRefreshRequested &&
                              _isInvalidationRegistered &&
                              IsCurrentActivation(cancellationToken);
                }
            }

            if (restart)
            {
                StartBackgroundOverviewRefresh(cancellationToken);
            }
        }
    }

    private void ApplyOverview(CacheOverviewModel overview)
    {
        _overview = overview;
        TotalCacheSizeText = CacheCleanupFeedbackFormatter.FormatBytes(overview.TotalSizeBytes);
        CacheEntryCountText = $"{overview.EntryCount} 项缓存";
        UsageText = $"已用 {CacheCleanupFeedbackFormatter.FormatBytes(overview.TotalSizeBytes)} / 上限 {CacheCleanupFeedbackFormatter.FormatBytes(overview.LimitBytes)}";
        UsagePercentage = overview.LimitBytes <= 0
            ? 0
            : Math.Clamp(overview.TotalSizeBytes * 100d / overview.LimitBytes, 0, 100);
        NotifyClearAllCommandState();
    }

    private int GetOverviewRefreshVersion()
    {
        lock (_overviewRefreshSync)
        {
            return _overviewRefreshVersion;
        }
    }

    private Task EnsureOverviewCurrentAsync(CancellationToken cancellationToken)
    {
        lock (_overviewRefreshSync)
        {
            if (_overview is not null &&
                !_overviewRefreshRequested &&
                _overviewRefreshCompletion is null &&
                _overviewAppliedVersion >= _overviewRefreshVersion)
            {
                return Task.CompletedTask;
            }
        }

        return RequestOverviewRefreshAsync(cancellationToken);
    }

    private async Task<CacheOverviewModel?> GetCurrentOverviewForCacheLimitAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await EnsureOverviewCurrentAsync(cancellationToken).ConfigureAwait(false);
            lock (_overviewRefreshSync)
            {
                if (_overview is not null &&
                    !_overviewRefreshRequested &&
                    _overviewRefreshCompletion is null &&
                    _overviewAppliedVersion >= _overviewRefreshVersion)
                {
                    return _overview;
                }
            }
        }
    }

    private async Task RefreshOverviewAfterCacheMutationAsync(
        int previousOverviewVersion,
        CancellationToken cancellationToken)
    {
        await _invalidationCoordinator
            .FlushPendingAsync(cancellationToken)
            .ConfigureAwait(false);

        var refreshRequired = false;
        lock (_overviewRefreshSync)
        {
            refreshRequired = _overviewRefreshVersion == previousOverviewVersion ||
                              _overviewAppliedVersion < _overviewRefreshVersion;
        }

        if (refreshRequired)
        {
            await RequestOverviewRefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private void RegisterInvalidationSubscription()
    {
        if (_isInvalidationRegistered)
        {
            return;
        }

        _invalidationCoordinator.BatchPublished += OnInvalidationBatchPublished;
        _isInvalidationRegistered = true;
    }

    private void UnregisterInvalidationSubscription()
    {
        if (!_isInvalidationRegistered)
        {
            return;
        }

        _invalidationCoordinator.BatchPublished -= OnInvalidationBatchPublished;
        _isInvalidationRegistered = false;
    }

    private void OnInvalidationBatchPublished(object? sender, CacheInvalidationBatch batch)
    {
        if (!batch.Changes.Any(static change =>
                change.Aspects.HasFlag(CacheInvalidationAspect.PhysicalSummary)))
        {
            return;
        }

        lock (_overviewRefreshSync)
        {
            if (!_isInvalidationRegistered || !IsCurrentActivation(ActivationToken))
            {
                return;
            }

            _overviewRefreshVersion++;
        }

        StartBackgroundOverviewRefresh(ActivationToken);
    }

    private void StartBackgroundOverviewRefresh(CancellationToken cancellationToken)
    {
        _liveRefreshTasks.Register(
            RequestOverviewRefreshAsync(cancellationToken));
    }

    private void ReportOverviewRefreshFailure(Exception exception, CancellationToken refreshCancellationToken)
    {
        if (_isInvalidationRegistered && IsCurrentActivation(refreshCancellationToken))
        {
            _feedbackService.ShowProjectedNotification(
                "刷新缓存总览失败",
                _feedbackService.Project(exception));
        }
    }

    private void ApplyCacheLimit(long cacheLimitBytes)
    {
        _isLoading = true;
        try
        {
            if (cacheLimitBytes % Gigabyte == 0)
            {
                SelectedCacheLimitUnit = "GB";
                CacheLimitValueText = (cacheLimitBytes / Gigabyte).ToString();
            }
            else
            {
                SelectedCacheLimitUnit = "MB";
                CacheLimitValueText = Math.Max(1, cacheLimitBytes / Megabyte).ToString();
            }
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ScheduleDebouncedCommit()
    {
        CancelPendingSave();

        RunPageOperation(
            "保存缓存上限失败",
            currentActivationToken =>
            {
                var operationCts = CancellationTokenSource.CreateLinkedTokenSource(currentActivationToken);
                _cacheLimitDebounceCts = operationCts;
                return RunDebouncedCommitAsync(
                    operationCts,
                    currentActivationToken);
            });
    }

    private async Task RunDebouncedCommitAsync(
        CancellationTokenSource operationCts,
        CancellationToken activationToken)
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

            await CommitCacheLimitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            operationCts.Dispose();
        }
    }

    private void CancelPendingSave()
    {
        _cacheLimitDebounceCts?.Cancel();
        _cacheLimitDebounceCts?.Dispose();
        _cacheLimitDebounceCts = null;
    }

    partial void OnIsClearingAllChanged(bool value)
    {
        NotifyClearAllCommandState();
    }

    private void NotifyClearAllCommandState()
    {
        OnPropertyChanged(nameof(CanClearAll));
        ClearAllCommand.NotifyCanExecuteChanged();
    }

    private void ShowCleanupFeedback(AudioCacheStoreCleanupResult result)
    {
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

    private void CompleteOrCancelPendingSave(CancellationToken commitToken)
    {
        if (_cacheLimitDebounceCts is not null &&
            _cacheLimitDebounceCts.Token == commitToken)
        {
            _cacheLimitDebounceCts = null;
            return;
        }

        CancelPendingSave();
    }

    private static bool TryParseCacheLimitBytes(
        string valueText,
        string unit,
        out long cacheLimitBytes,
        out string errorMessage)
    {
        cacheLimitBytes = 0;
        errorMessage = string.Empty;

        if (!long.TryParse(valueText, out var value) || value <= 0)
        {
            errorMessage = "请输入正整数。";
            return false;
        }

        var unitBytes = string.Equals(unit, "MB", StringComparison.Ordinal) ? Megabyte : Gigabyte;
        try
        {
            cacheLimitBytes = checked(value * unitBytes);
        }
        catch (OverflowException)
        {
            errorMessage = "输入值过大。";
            return false;
        }

        if (cacheLimitBytes < AppSettings.MinCacheLimitBytes)
        {
            errorMessage = "缓存上限不能低于 256 MB。";
            return false;
        }

        return true;
    }

    private static long ConvertBytesToUnitValue(long bytes, string unit)
    {
        var unitBytes = string.Equals(unit, "MB", StringComparison.Ordinal) ? Megabyte : Gigabyte;
        return Math.Max(1, (long)Math.Ceiling(bytes / (double)unitBytes));
    }
}
