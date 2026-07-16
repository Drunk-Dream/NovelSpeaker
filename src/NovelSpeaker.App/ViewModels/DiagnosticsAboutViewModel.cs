using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Diagnostics;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class DiagnosticsAboutViewModel : SettingsSubpageViewModelBase
{
    private readonly IAppDiagnosticsService _diagnosticsService;
    private readonly IAppSettingsService _settingsService;
    private readonly IClipboardService _clipboardService;
    private bool _isLoading;
    private int _logLevelVersion;

    public DiagnosticsAboutViewModel(
        IAppDiagnosticsService diagnosticsService,
        IAppSettingsService settingsService,
        IClipboardService clipboardService,
        INavigationService navigationService,
        IAppFeedbackService feedbackService)
        : base(navigationService, feedbackService)
    {
        _diagnosticsService = diagnosticsService;
        _settingsService = settingsService;
        _clipboardService = clipboardService;
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

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        try
        {
            var snapshot = await _diagnosticsService.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var settings = _settingsService.Current;

            AppName = snapshot.AppName;
            AppVersion = snapshot.AppVersion;
            Description = snapshot.Description;
            DatabaseSchemaVersionText = snapshot.DatabaseSchemaVersion.ToString();
            AppDataDirectoryPath = snapshot.AppDataDirectoryPath;
            LogsDirectoryPath = snapshot.LogsDirectoryPath;
            SelectedLogLevel = settings.LogLevel;
        }
        finally
        {
            _isLoading = false;
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
            _clipboardService.SetText(summary);
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

    partial void OnSelectedLogLevelChanged(string value)
    {
        if (_isLoading)
        {
            return;
        }

        var version = Interlocked.Increment(ref _logLevelVersion);
        _ = SaveLogLevelAsync(value, version);
    }

    private async Task SaveLogLevelAsync(string value, int version)
    {
        try
        {
            var settings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    LogLevel = value
                },
                CancellationToken.None).ConfigureAwait(false);

            if (version != Volatile.Read(ref _logLevelVersion))
            {
                return;
            }

            if (!string.Equals(SelectedLogLevel, settings.LogLevel, StringComparison.Ordinal))
            {
                SelectedLogLevel = settings.LogLevel;
            }
        }
        catch (Exception exception)
        {
            if (version == Volatile.Read(ref _logLevelVersion))
            {
                ShowSaveFailure("保存日志级别失败", exception);
            }
        }
    }
}
