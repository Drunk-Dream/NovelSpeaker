using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.App.Shared.Presentation.Platform;
using BootstrapApp = NovelSpeaker.App.Bootstrap.App;

namespace NovelSpeaker.App.Features.Diagnostics;

public sealed class AppDiagnosticsService : IAppDiagnosticsService
{
    private const string Description = "Windows 10/11 桌面小说听书应用。";

    private readonly IAppDataDirectoryProvider _directories;
    private readonly IDatabaseSchemaVersionProvider _schemaVersionProvider;
    private readonly IAppSettingsService _settingsService;
    private readonly IPresentationLauncher _launcher;
    private readonly IPerformanceTelemetryService _telemetry;
    private readonly IDiagnosticSessionExportService _sessionExport;
    private readonly IDiagnosticToolLauncher _toolLauncher;

    public AppDiagnosticsService(
        IAppDataDirectoryProvider directories,
        IDatabaseSchemaVersionProvider schemaVersionProvider,
        IAppSettingsService settingsService,
        IPresentationLauncher launcher,
        IPerformanceTelemetryService telemetry,
        IDiagnosticSessionExportService sessionExport,
        IDiagnosticToolLauncher toolLauncher)
    {
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _schemaVersionProvider = schemaVersionProvider ?? throw new ArgumentNullException(nameof(schemaVersionProvider));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _sessionExport = sessionExport ?? throw new ArgumentNullException(nameof(sessionExport));
        _toolLauncher = toolLauncher ?? throw new ArgumentNullException(nameof(toolLauncher));
    }

    public async Task<AppDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var schemaVersion = await _schemaVersionProvider
            .GetCurrentVersionAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AppDiagnosticsSnapshot(
            "NovelSpeaker",
            ResolveVersion(),
            Description,
            schemaVersion,
            _directories.RootDirectoryPath,
            _directories.LogsDirectoryPath,
            _directories.DiagnosticsDirectoryPath);
    }

    public Task OpenLogsDirectoryAsync(CancellationToken cancellationToken)
    {
        return _launcher.OpenAsync(_directories.LogsDirectoryPath, cancellationToken);
    }

    public Task OpenDiagnosticsDirectoryAsync(CancellationToken cancellationToken)
    {
        return _launcher.OpenAsync(_directories.DiagnosticsDirectoryPath, cancellationToken);
    }

    public Task OpenDiagnosticToolAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _toolLauncher.Open();
        return Task.CompletedTask;
    }

    public async Task<string> GetRedactedSummaryAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var settings = _settingsService.Current;

        return string.Join(
            Environment.NewLine,
            $"应用：{snapshot.AppName}",
            $"应用版本：{snapshot.AppVersion}",
            $"数据库版本：{snapshot.DatabaseSchemaVersion}",
            $"Windows：{RuntimeInformation.OSDescription}",
            $".NET：{RuntimeInformation.FrameworkDescription}",
            $"主题：{settings.Theme}",
            $"日志级别：{settings.LogLevel}",
            "应用数据目录：已设置",
            "日志目录：已设置");
    }

    public Task OpenAppDataDirectoryAsync(CancellationToken cancellationToken)
    {
        return _launcher.OpenAsync(_directories.RootDirectoryPath, cancellationToken);
    }

    public Task OpenThirdPartyNoticesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (!File.Exists(noticesPath))
        {
            throw new FileNotFoundException("未找到第三方许可证文件。", noticesPath);
        }

        return _launcher.OpenAsync(noticesPath, cancellationToken);
    }

    public Task ClearTelemetryAsync(CancellationToken cancellationToken) =>
        _telemetry.ClearAsync(cancellationToken);

    public Task ExportDiagnosticsAsync(string destinationPath, CancellationToken cancellationToken) =>
        _telemetry.ExportAsync(destinationPath, cancellationToken);

    public Task ExportLastDiagnosticSessionAsync(string destinationPath, CancellationToken cancellationToken) =>
        _sessionExport.ExportLastEndedAsync(destinationPath, cancellationToken);

    public Task ExportDiagnosticSessionAsync(
        string sessionFilePath,
        string destinationPath,
        CancellationToken cancellationToken) =>
        _sessionExport.ExportAsync(sessionFilePath, destinationPath, cancellationToken);

    public void SetTelemetryCollectionEnabled(bool enabled) =>
        _telemetry.SetCollectionEnabled(enabled);

    private static string ResolveVersion()
    {
        var assembly = typeof(BootstrapApp).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "未知版本";
    }
}
