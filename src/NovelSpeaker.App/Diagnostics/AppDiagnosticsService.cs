using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.App.Diagnostics;

public sealed class AppDiagnosticsService : IAppDiagnosticsService
{
    private const string Description = "Windows 10/11 桌面小说听书应用。";

    private readonly IAppDataDirectoryProvider _directories;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IAppSettingsService _settingsService;

    public AppDiagnosticsService(
        IAppDataDirectoryProvider directories,
        ISqliteConnectionFactory connectionFactory,
        IAppSettingsService settingsService)
    {
        _directories = directories;
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
    }

    public async Task<AppDiagnosticsSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        var schemaVersion = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));

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
        cancellationToken.ThrowIfCancellationRequested();

        var info = new ProcessStartInfo
        {
            FileName = _directories.LogsDirectoryPath,
            UseShellExecute = true
        };

        Process.Start(info);
        return Task.CompletedTask;
    }

    public async Task<string> GetRedactedSummaryAsync(CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        var settings = await _settingsService.LoadAsync(cancellationToken).ConfigureAwait(false);

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
        cancellationToken.ThrowIfCancellationRequested();

        var info = new ProcessStartInfo
        {
            FileName = _directories.RootDirectoryPath,
            UseShellExecute = true
        };

        Process.Start(info);
        return Task.CompletedTask;
    }

    public Task OpenThirdPartyNoticesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var noticesPath = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        if (!File.Exists(noticesPath))
        {
            throw new FileNotFoundException("未找到第三方许可证文件。", noticesPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = noticesPath,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private static string ResolveVersion()
    {
        var assembly = typeof(App).Assembly;
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
