using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Diagnostics;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class CacheAndDataViewModel : SettingsSubpageViewModelBase
{
    private const int DebounceDelayMilliseconds = 500;
    private const long Megabyte = 1024L * 1024;
    private const long Gigabyte = 1024L * 1024 * 1024;

    private readonly IAppSettingsService _settingsService;
    private readonly ICacheWorkspaceService _cacheWorkspaceService;
    private readonly IAppDiagnosticsService _diagnosticsService;
    private readonly INavigationService _navigationService;
    private readonly IAppDialogService _dialogService;
    private readonly IAppFeedbackService _feedbackService;
    private CancellationTokenSource? _cacheLimitDebounceCts;
    private CacheOverviewModel? _overview;
    private bool _isLoading;
    private int _cacheLimitVersion;
    private long _savedCacheLimitBytes = AppSettings.DefaultCacheLimitBytes;

    public CacheAndDataViewModel(
        IAppSettingsService settingsService,
        ICacheWorkspaceService cacheWorkspaceService,
        IAppDiagnosticsService diagnosticsService,
        INavigationService navigationService,
        IAppDialogService dialogService,
        IAppFeedbackService feedbackService)
        : base(navigationService, feedbackService)
    {
        _settingsService = settingsService;
        _cacheWorkspaceService = cacheWorkspaceService;
        _diagnosticsService = diagnosticsService;
        _navigationService = navigationService;
        _dialogService = dialogService;
        _feedbackService = feedbackService;
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

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        HasLoadError = false;
        LoadErrorMessage = string.Empty;

        try
        {
            var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);
            _savedCacheLimitBytes = settings.CacheLimitBytes;
            ApplyCacheLimit(_savedCacheLimitBytes);
            await RefreshOverviewAsync(cancellationToken).ConfigureAwait(false);
            IsOverviewLoaded = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            HasLoadError = true;
            LoadErrorMessage = "加载缓存总览失败，请重试。";
            IsOverviewLoaded = false;
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand]
    private Task RetryAsync(CancellationToken cancellationToken)
    {
        return LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private void OpenCacheManagement()
    {
        _navigationService.NavigateWithHierarchy(typeof(CacheManagementPage));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenAppDataDirectoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _diagnosticsService.OpenAppDataDirectoryAsync(cancellationToken).ConfigureAwait(false);
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
        CancelPendingSave();
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

        var requiresTrim = _overview is not null && cacheLimitBytes < _overview.TotalSizeBytes;
        if (requiresTrim)
        {
            var decision = await _dialogService.ShowConfirmationAsync(
                "降低缓存上限",
                "新的缓存上限低于当前占用。保存后会按最近最少使用顺序立即清理缓存；不会删除书籍、章节、阅读进度、TTS 规则或章节规则。",
                "保存并清理",
                "取消",
                cancellationToken).ConfigureAwait(false);
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
                cancellationToken).ConfigureAwait(false);

            if (version != Volatile.Read(ref _cacheLimitVersion))
            {
                return;
            }

            _savedCacheLimitBytes = settings.CacheLimitBytes;
            ApplyCacheLimit(_savedCacheLimitBytes);
            CacheLimitErrorText = string.Empty;

            if (requiresTrim)
            {
                await _cacheWorkspaceService.TrimToConfiguredLimitAsync(cancellationToken).ConfigureAwait(false);
            }

            await RefreshOverviewAsync(cancellationToken).ConfigureAwait(false);
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
            if (version == Volatile.Read(ref _cacheLimitVersion))
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

    private async Task RefreshOverviewAsync(CancellationToken cancellationToken)
    {
        _overview = await _cacheWorkspaceService.GetOverviewAsync(cancellationToken).ConfigureAwait(false);
        TotalCacheSizeText = CacheCleanupFeedbackFormatter.FormatBytes(_overview.TotalSizeBytes);
        CacheEntryCountText = $"{_overview.EntryCount} 项缓存";
        UsageText = $"已用 {CacheCleanupFeedbackFormatter.FormatBytes(_overview.TotalSizeBytes)} / 上限 {CacheCleanupFeedbackFormatter.FormatBytes(_overview.LimitBytes)}";
        UsagePercentage = _overview.LimitBytes <= 0
            ? 0
            : Math.Clamp(_overview.TotalSizeBytes * 100d / _overview.LimitBytes, 0, 100);
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
        _cacheLimitDebounceCts = new CancellationTokenSource();
        var localCts = _cacheLimitDebounceCts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelayMilliseconds, localCts.Token).ConfigureAwait(false);
                await CommitCacheLimitAsync(localCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    private void CancelPendingSave()
    {
        _cacheLimitDebounceCts?.Cancel();
        _cacheLimitDebounceCts?.Dispose();
        _cacheLimitDebounceCts = null;
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
