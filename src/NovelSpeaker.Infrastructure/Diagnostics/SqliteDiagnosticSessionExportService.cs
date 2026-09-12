using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Reads one self-contained diagnostic session and creates the problem-diagnostics ZIP.
/// </summary>
public sealed class SqliteDiagnosticSessionExportService : IDiagnosticSessionExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly SqliteDiagnosticSessionStore _store;
    private readonly IAppDataDirectoryProvider _directories;
    private readonly DiagnosticRegistry _registry;

    public SqliteDiagnosticSessionExportService(
        SqliteDiagnosticSessionStore store,
        IAppDataDirectoryProvider directories,
        DiagnosticRegistry? registry = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _registry = registry ?? DiagnosticRegistry.Default;
    }

    public Task ExportLastEndedAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var path = _store.LastEndedPath
            ?? throw new FileNotFoundException("当前进程没有刚结束的诊断会话。");
        return ExportAsync(path, destinationPath, cancellationToken);
    }

    public async Task ExportAsync(
        string sessionFilePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();

        var sourcePath = Path.GetFullPath(sessionFilePath);
        var outputPath = Path.GetFullPath(destinationPath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("未找到诊断会话文件。", sourcePath);
        }

        if (string.Equals(sourcePath, outputPath, GetPathComparison()))
        {
            throw new InvalidOperationException("诊断导出文件不能覆盖源会话文件。");
        }

        var data = await ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        var logLines = ReadRelatedLogLines(data.Session.SessionId);
        var temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var parent = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.SequentialScan))
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
                WriteEntry(archive, "summary.md", BuildSummary(data));
                WriteEntry(archive, "timeline.md", BuildTimeline(data));
                WriteEntry(archive, "logs.jsonl", BuildLogs(logLines));
                WriteEntry(archive, "environment.json", JsonSerializer.Serialize(data.Environment, JsonOptions));
                WriteEntry(archive, "diagnostics-schema.json", BuildSchema(data));
                await CopyEntryAsync(archive, "session.nsdiag", sourcePath, cancellationToken).ConfigureAwait(false);
                archive.CreateEntry("attachments/");

                foreach (var attachment in data.Attachments)
                {
                    await WriteAttachmentAsync(archive, attachment, cancellationToken).ConfigureAwait(false);
                }
            }

            File.Move(temporaryPath, outputPath, true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private async Task<ExportData> ReadAsync(string sourcePath, CancellationToken cancellationToken)
    {
        SqliteRuntimeInitializer.EnsureInitialized();
        await using var connection = new SqliteConnection(
            $"Data Source={sourcePath};Mode=ReadOnly;Cache=Private;Pooling=False");
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var session = await ReadSessionAsync(connection, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("诊断会话文件缺少 Session 记录。");
        var processes = await ReadProcessesAsync(connection, cancellationToken).ConfigureAwait(false);
        var activities = await ReadActivitiesAsync(connection, cancellationToken).ConfigureAwait(false);
        var events = await ReadTimelineRowsAsync(connection, "Events", cancellationToken).ConfigureAwait(false);
        var snapshots = await ReadTimelineRowsAsync(connection, "Snapshots", cancellationToken).ConfigureAwait(false);
        var resourceSampleCount = await ReadCountAsync(connection, "ResourceSamples", cancellationToken).ConfigureAwait(false);
        var anonymousAssociationCount = await ReadCountAsync(connection, "AnonymousObjectAssociations", cancellationToken).ConfigureAwait(false);
        var environment = await ReadEnvironmentAsync(connection, cancellationToken).ConfigureAwait(false);
        var attachments = await ReadAttachmentsAsync(connection, cancellationToken).ConfigureAwait(false);

        return new ExportData(session, processes, activities, events, snapshots, environment, attachments)
        {
            ResourceSampleCount = checked((int)resourceSampleCount),
            AnonymousAssociationCount = checked((int)anonymousAssociationCount)
        };
    }

    private static async Task<SessionRow?> ReadSessionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SessionId, State, StartedAtUtc, EndedAtUtc, HardCapBytes, RecordedBytes, CaptureStopped, CaptureStoppedReason, EndedUnexpectedly FROM Sessions LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new SessionRow(
            reader.GetString(0),
            reader.GetString(1),
            ParseTimestamp(reader.GetString(2)),
            reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt64(6) != 0,
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt64(8) != 0);
    }

    private static async Task<IReadOnlyList<ProcessRow>> ReadProcessesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<ProcessRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ProcessInstanceId, AppVersion, StartedAtUtc, EndedAtUtc, EndReason FROM Processes ORDER BY StartedAtUtc, ProcessInstanceId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new ProcessRow(
                reader.GetString(0),
                reader.GetString(1),
                ParseTimestamp(reader.GetString(2)),
                reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<ActivityRow>> ReadActivitiesAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<ActivityRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ActivityId, OperationId, ParentActivityId, ProcessInstanceId, StartedAtUtc, EndedAtUtc, Outcome, FailureCode FROM Activities ORDER BY StartedAtUtc, ActivityId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new ActivityRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                ParseTimestamp(reader.GetString(4)),
                reader.IsDBNull(5) ? null : ParseTimestamp(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<TimelineRow>> ReadTimelineRowsAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        if (tableName is not ("Events" or "Snapshots"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        var result = new List<TimelineRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT TimestampUtc, DefinitionId, ProcessInstanceId, ActivityId, FieldsJson FROM {tableName} ORDER BY RowId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new TimelineRow(
                ParseTimestamp(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                tableName == "Snapshots"));
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadEnvironmentAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Environment ORDER BY Key;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    private static async Task<long> ReadCountAsync(
        SqliteConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        if (tableName is not ("ResourceSamples" or "Attachments" or "AnonymousObjectAssociations"))
        {
            throw new ArgumentOutOfRangeException(nameof(tableName));
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static async Task<IReadOnlyList<AttachmentRow>> ReadAttachmentsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var result = new List<AttachmentRow>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT AttachmentId, CapturedAtUtc, ProcessInstanceId, MimeType, PixelWidth, PixelHeight, ByteLength, Payload FROM Attachments ORDER BY CapturedAtUtc, AttachmentId;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var payload = (byte[])reader[7];
            result.Add(new AttachmentRow(
                reader.GetString(0),
                ParseTimestamp(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt64(6),
                payload));
        }

        return result;
    }

    private string BuildSchema(ExportData data)
    {
        var usedDefinitions = data.Events.Concat(data.Snapshots)
            .Select(row => row.DefinitionId)
            .ToHashSet(StringComparer.Ordinal);
        var usedOperations = data.Activities
            .Select(activity => activity.OperationId)
            .ToHashSet(StringComparer.Ordinal);
        return JsonSerializer.Serialize(
            new
            {
                schemaVersion = SqliteDiagnosticSessionStore.CurrentSchemaVersion,
                definitions = _registry.Definitions
                    .Where(definition => usedDefinitions.Contains(definition.Id.Value))
                    .Select(definition => new
                    {
                        id = definition.Id.Value,
                        kind = definition.Kind.ToString(),
                        domain = definition.Domain,
                        description = definition.Description,
                        fields = definition.Fields.Select(field => new
                        {
                            name = field.Name,
                            type = field.Type.ToString(),
                            privacy = field.Privacy.ToString(),
                            description = field.Description,
                            allowedValues = field.AllowedValues
                        })
                    }),
                operations = OperationCatalog.All
                    .Where(operation => usedOperations.Contains(operation.Id.Value))
                    .Select(operation => new
                    {
                        id = operation.Id.Value,
                        name = operation.Name,
                        description = operation.Description
                    })
            },
            JsonOptions);
    }

    private static string BuildSummary(ExportData data)
    {
        var session = data.Session;
        var endedText = session.EndedAtUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "未结束";
        return string.Join(
            Environment.NewLine,
            [
                "# NovelSpeaker 问题诊断摘要",
                string.Empty,
                "本文件是诊断会话的客观摘要，不推断问题根因。",
                string.Empty,
                $"- 会话状态：{session.State}",
                $"- 会话开始：{session.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture)}",
                $"- 会话结束：{endedText}",
                $"- Process 数量：{data.Processes.Count}",
                $"- Activity 数量：{data.Activities.Count}",
                $"- Event 数量：{data.Events.Count}",
                $"- Snapshot 数量：{data.Snapshots.Count}",
            $"- 资源采样数量：{data.ResourceSampleCount}",
                $"- 匿名对象关联数量：{data.AnonymousAssociationCount}",
                $"- 主动截图数量：{data.Attachments.Count}",
                $"- 硬容量上限：{session.HardCapBytes} bytes",
                $"- 记录估算大小：{session.RecordedBytes} bytes",
                $"- 是否因容量停止：{session.CaptureStopped}",
                $"- 采集停止原因：{session.CaptureStoppedReason ?? "none"}",
                $"- 是否记录到意外 Process 结束：{session.EndedUnexpectedly}"
            ]);
    }

    private static string BuildTimeline(ExportData data)
    {
        var builder = new StringBuilder("# 诊断时间线\n\n");
        builder.AppendLine("时间线只列出生命周期、Activity、关键 Event/Snapshot；资源采样不逐条展开。\n");
        builder.AppendLine("## Process");
        foreach (var process in data.Processes)
        {
            builder.Append("- ").Append(process.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture))
                .Append(" process=").Append(process.ProcessInstanceId)
                .Append(" appVersion=").Append(process.AppVersion)
                .Append(" endReason=").Append(process.EndReason ?? "active").AppendLine();
        }

        builder.AppendLine("\n## Activity");
        foreach (var activity in data.Activities)
        {
            builder.Append("- ").Append(activity.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture))
                .Append(" operation=").Append(activity.OperationId)
                .Append(" activity=").Append(activity.ActivityId)
                .Append(" outcome=").Append(activity.Outcome ?? "active").AppendLine();
        }

        builder.AppendLine("\n## Event and Snapshot");
        foreach (var row in data.Events.Concat(data.Snapshots).OrderBy(row => row.TimestampUtc))
        {
            builder.Append("- ").Append(row.TimestampUtc.ToString("O", CultureInfo.InvariantCulture))
                .Append(row.IsSnapshot ? " snapshot=" : " event=").Append(row.DefinitionId)
                .Append(" process=").Append(row.ProcessInstanceId)
                .Append(" activity=").Append(row.ActivityId ?? "none").AppendLine();
        }

        return builder.ToString();
    }

    private static string BuildLogs(IReadOnlyList<string> lines) =>
        lines.Count == 0 ? string.Empty : string.Join('\n', lines) + "\n";

    private IReadOnlyList<string> ReadRelatedLogLines(string sessionId)
    {
        var lines = new List<string>();
        try
        {
            if (!Directory.Exists(_directories.LogsDirectoryPath))
            {
                return lines;
            }

            foreach (var path in Directory.EnumerateFiles(_directories.LogsDirectoryPath, "novelspeaker-*.jsonl"))
            {
                try
                {
                    foreach (var line in File.ReadLines(path))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        using var document = JsonDocument.Parse(line);
                        if (document.RootElement.TryGetProperty("diagnosticSessionId", out var value) &&
                            string.Equals(value.GetString(), sessionId, StringComparison.Ordinal))
                        {
                            lines.Add(line);
                        }
                    }
                }
                catch
                {
                    // A missing or malformed log file must not prevent session export.
                }
            }
        }
        catch
        {
        }

        return lines;
    }

    private static async Task CopyEntryAsync(
        ZipArchive archive,
        string name,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        await using var output = entry.Open();
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteAttachmentAsync(
        ZipArchive archive,
        AttachmentRow attachment,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry($"attachments/{attachment.AttachmentId}.png", CompressionLevel.Fastest);
        await using var output = entry.Open();
        await output.WriteAsync(attachment.Payload, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record ExportData(
        SessionRow Session,
        IReadOnlyList<ProcessRow> Processes,
        IReadOnlyList<ActivityRow> Activities,
        IReadOnlyList<TimelineRow> Events,
        IReadOnlyList<TimelineRow> Snapshots,
        IReadOnlyDictionary<string, string> Environment,
        IReadOnlyList<AttachmentRow> Attachments)
    {
        public int ResourceSampleCount { get; init; }

        public int AnonymousAssociationCount { get; init; }
    }

    private sealed record SessionRow(
        string SessionId,
        string State,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        long HardCapBytes,
        long RecordedBytes,
        bool CaptureStopped,
        string? CaptureStoppedReason,
        bool EndedUnexpectedly);

    private sealed record ProcessRow(
        string ProcessInstanceId,
        string AppVersion,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        string? EndReason);

    private sealed record ActivityRow(
        string ActivityId,
        string OperationId,
        string? ParentActivityId,
        string ProcessInstanceId,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset? EndedAtUtc,
        string? Outcome,
        string? FailureCode);

    private sealed record TimelineRow(
        DateTimeOffset TimestampUtc,
        string DefinitionId,
        string ProcessInstanceId,
        string? ActivityId,
        string FieldsJson,
        bool IsSnapshot);

    private sealed record AttachmentRow(
        string AttachmentId,
        DateTimeOffset CapturedAtUtc,
        string ProcessInstanceId,
        string MimeType,
        int PixelWidth,
        int PixelHeight,
        long ByteLength,
        byte[] Payload);
}
