using System.Diagnostics;
using System.Reflection;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.App.Diagnostics;

public sealed class AppDiagnosticsService : IAppDiagnosticsService
{
    private const string Description = "Windows 10/11 桌面小说听书应用。";

    private readonly IAppDataDirectoryProvider _directories;
    private readonly ISqliteConnectionFactory _connectionFactory;

    public AppDiagnosticsService(
        IAppDataDirectoryProvider directories,
        ISqliteConnectionFactory connectionFactory)
    {
        _directories = directories;
        _connectionFactory = connectionFactory;
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
