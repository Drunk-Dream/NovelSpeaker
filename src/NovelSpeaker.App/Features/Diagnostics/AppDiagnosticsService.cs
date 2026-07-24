using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Settings;
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

    public AppDiagnosticsService(
        IAppDataDirectoryProvider directories,
        IDatabaseSchemaVersionProvider schemaVersionProvider,
        IAppSettingsService settingsService,
        IPresentationLauncher launcher)
    {
        _directories = directories;
        _schemaVersionProvider = schemaVersionProvider;
        _settingsService = settingsService;
        _launcher = launcher;
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
            _directories.LogsDirectoryPath);
    }

    public Task OpenLogsDirectoryAsync(CancellationToken cancellationToken)
    {
        return _launcher.OpenAsync(_directories.LogsDirectoryPath, cancellationToken);
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
            $"应用数据目录：{snapshot.AppDataDirectoryPath}",
            $"日志目录：{snapshot.LogsDirectoryPath}");
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
