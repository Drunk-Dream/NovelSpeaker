using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.Diagnostics;

public sealed partial class DiagnosticsAboutViewModel : SettingsSubpageViewModelBase
{
    private readonly IAppDiagnosticsService _diagnosticsService;
    private readonly IAppSettingsService _settingsService;
    private readonly IPresentationClipboard _clipboard;
    private readonly IPresentationFileDialogService _fileDialogs;
    private bool _isLoading;
    private int _logLevelVersion;
    private int _telemetryVersion;
    private readonly object _telemetryStateGate = new();

    public DiagnosticsAboutViewModel(
        IAppDiagnosticsService diagnosticsService,
        IAppSettingsService settingsService,
        IPresentationClipboard clipboard,
        IAppNavigator navigator,
        IAppFeedbackService feedbackService,
        IPresentationFileDialogService fileDialogs)
        : base(navigator, feedbackService)
    {
        _diagnosticsService = diagnosticsService;
        _settingsService = settingsService;
        _clipboard = clipboard;
        _fileDialogs = fileDialogs;
    }

    public IReadOnlyList<string> AvailableLogLevels => AppSettings.SupportedLogLevels;

    [ObservableProperty]
    private string appName = string.Empty;

    [ObservableProperty]
    private string appVersion = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private string databaseSchemaVersionText = string.Empty;

    [ObservableProperty]
    private string appDataDirectoryPath = string.Empty;

    [ObservableProperty]
    private string logsDirectoryPath = string.Empty;

    [ObservableProperty]
    private string selectedLogLevel = AppSettings.DefaultLogLevel;

    [ObservableProperty]
    private bool isPerformanceTelemetryEnabled;

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        Activate(cancellationToken);
        _isLoading = true;
        try
        {
            var snapshot = await _diagnosticsService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var settings = _settingsService.Current;

            AppName = snapshot.AppName;
            AppVersion = snapshot.AppVersion;
            Description = snapshot.Description;
            DatabaseSchemaVersionText = snapshot.DatabaseSchemaVersion.ToString();
            AppDataDirectoryPath = snapshot.AppDataDirectoryPath;
            LogsDirectoryPath = snapshot.LogsDirectoryPath;
            SelectedLogLevel = settings.LogLevel;
            IsPerformanceTelemetryEnabled = settings.EnablePerformanceTelemetry;
        }
        finally
        {
            if (IsCurrentActivation(cancellationToken))
            {
                _isLoading = false;
            }
        }
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

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenLogsDirectoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _diagnosticsService.OpenLogsDirectoryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowSaveFailure("打开日志目录失败", exception);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task CopyRedactedSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _diagnosticsService.GetRedactedSummaryAsync(cancellationToken).ConfigureAwait(false);
            await _clipboard.SetTextAsync(summary, cancellationToken).ConfigureAwait(false);
            ShowSuccess("诊断摘要已复制", "已复制不含正文、规则和凭据的诊断摘要。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowSaveFailure("复制诊断摘要失败", exception);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task OpenThirdPartyNoticesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _diagnosticsService.OpenThirdPartyNoticesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowSaveFailure("打开第三方许可证失败", exception);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ClearTelemetryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _diagnosticsService.ClearTelemetryAsync(cancellationToken).ConfigureAwait(false);
            ShowSuccess("性能遥测已清除", "本地性能遥测历史已清除，日志不受影响。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowSaveFailure("清除性能遥测失败", exception);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ExportDiagnosticsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var destinationPath = await _fileDialogs.PickSaveFileAsync(
                new PresentationFileDialogOptions(
                    "ZIP files (*.zip)|*.zip",
                    "NovelSpeaker-Diagnostics.zip"),
                cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return;
            }

            await _diagnosticsService.ExportDiagnosticsAsync(destinationPath, cancellationToken)
                .ConfigureAwait(false);
            ShowSuccess("诊断信息已导出", "已导出本地诊断信息；应用不会自动上传这些内容。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ShowSaveFailure("导出诊断信息失败", exception);
        }
    }

    partial void OnSelectedLogLevelChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        var version = Interlocked.Increment(ref _logLevelVersion);
        RunPageOperation(
            "保存日志级别失败",
            cancellationToken => SaveLogLevelAsync(value, version, cancellationToken));
    }

    partial void OnIsPerformanceTelemetryEnabledChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        int version;
        lock (_telemetryStateGate)
        {
            version = Interlocked.Increment(ref _telemetryVersion);
            _diagnosticsService.SetTelemetryCollectionEnabled(value);
        }

        RunPageOperation(
            "保存性能遥测设置失败",
            cancellationToken => SaveTelemetrySettingAsync(value, version, cancellationToken));
    }

    private async Task SaveTelemetrySettingAsync(bool value, int version, CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate { EnablePerformanceTelemetry = value },
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            lock (_telemetryStateGate)
            {
                if (!IsCurrentActivation(cancellationToken) ||
                    version != Volatile.Read(ref _telemetryVersion))
                {
                    return;
                }

                if (IsPerformanceTelemetryEnabled != settings.EnablePerformanceTelemetry)
                {
                    IsPerformanceTelemetryEnabled = settings.EnablePerformanceTelemetry;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreTelemetrySetting(version);
        }
        catch (Exception exception)
        {
            RestoreTelemetrySetting(version);
            if (IsCurrentActivation(cancellationToken) &&
                version == Volatile.Read(ref _telemetryVersion))
            {
                ShowSaveFailure("保存性能遥测设置失败", exception);
            }
        }
    }

    private void RestoreTelemetrySetting(int version)
    {
        lock (_telemetryStateGate)
        {
            if (version != Volatile.Read(ref _telemetryVersion))
            {
                return;
            }

            var persistedValue = _settingsService.Current.EnablePerformanceTelemetry;
            _diagnosticsService.SetTelemetryCollectionEnabled(persistedValue);
            if (IsPerformanceTelemetryEnabled == persistedValue)
            {
                return;
            }

            _isLoading = true;
            try
            {
                IsPerformanceTelemetryEnabled = persistedValue;
            }
            finally
            {
                _isLoading = false;
            }
        }
    }

    private async Task SaveLogLevelAsync(
        string value,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    LogLevel = value
                },
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentActivation(cancellationToken) ||
                version != Volatile.Read(ref _logLevelVersion))
            {
                return;
            }

            if (!string.Equals(SelectedLogLevel, settings.LogLevel, StringComparison.Ordinal))
            {
                SelectedLogLevel = settings.LogLevel;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentActivation(cancellationToken) &&
                version == Volatile.Read(ref _logLevelVersion))
            {
                ShowSaveFailure("保存日志级别失败", exception);
            }
        }
    }
}
