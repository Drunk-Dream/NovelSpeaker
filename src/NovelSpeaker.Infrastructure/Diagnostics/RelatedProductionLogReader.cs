using System.Text;
using System.Text.Json;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Diagnostics;

internal enum RelatedLogReadStatus
{
    Complete,
    Partial,
    Unavailable
}

internal sealed record RelatedLogReadResult(
    IReadOnlyList<string> Lines,
    RelatedLogReadStatus Status,
    Exception? FirstFailure);

/// <summary>
/// Reads production JSONL for one diagnostic session without owning or changing the log files.
/// </summary>
internal sealed class RelatedProductionLogReader(
    IAppDataDirectoryProvider directories,
    IAppStoragePathResolver pathResolver)
{
    public RelatedLogReadResult Read(string sessionId, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        Exception? firstFailure = null;
        var readableRecords = 0;
        var utf8 = new UTF8Encoding(false, true);

        try
        {
            var directory = pathResolver.ResolvePath(directories.LogsDirectoryPath);
            if (!Directory.Exists(directory))
            {
                return new RelatedLogReadResult(lines, RelatedLogReadStatus.Unavailable,
                    new DirectoryNotFoundException("Production log directory is unavailable."));
            }

            foreach (var path in Directory.EnumerateFiles(directory, "novelspeaker-*.jsonl"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var stream = new FileStream(pathResolver.ResolvePath(path), FileMode.Open,
                        FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var lineBuffer = new MemoryStream();
                    var readBuffer = new byte[64 * 1024];
                    var firstLine = true;

                    void ProcessLine()
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var bytes = lineBuffer.GetBuffer().AsSpan(0, checked((int)lineBuffer.Length));
                        if (firstLine && bytes.Length >= 3 &&
                            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                        {
                            bytes = bytes[3..];
                        }

                        firstLine = false;
                        try
                        {
                            var line = utf8.GetString(bytes);
                            if (string.IsNullOrWhiteSpace(line))
                            {
                                return;
                            }

                            using var document = JsonDocument.Parse(line);
                            if (document.RootElement.ValueKind != JsonValueKind.Object)
                            {
                                firstFailure ??= new JsonException("Production log record is not an object.");
                                return;
                            }

                            if (document.RootElement.TryGetProperty("diagnosticSessionId", out var value))
                            {
                                if (value.ValueKind != JsonValueKind.String)
                                {
                                    firstFailure ??= new JsonException("Production log correlation is invalid.");
                                    return;
                                }

                                if (string.Equals(value.GetString(), sessionId, StringComparison.Ordinal))
                                {
                                    lines.Add(line);
                                }
                            }

                            readableRecords++;
                        }
                        catch (JsonException exception)
                        {
                            firstFailure ??= exception;
                        }
                        catch (DecoderFallbackException exception)
                        {
                            firstFailure ??= exception;
                        }
                    }

                    int count;
                    while ((count = stream.Read(readBuffer)) > 0)
                    {
                        var start = 0;
                        for (var index = 0; index < count; index++)
                        {
                            if (readBuffer[index] != (byte)'\n')
                            {
                                continue;
                            }

                            lineBuffer.Write(readBuffer, start, index - start);
                            ProcessLine();
                            lineBuffer.SetLength(0);
                            start = index + 1;
                        }

                        lineBuffer.Write(readBuffer, start, count - start);
                    }

                    if (lineBuffer.Length > 0)
                    {
                        ProcessLine();
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    firstFailure ??= exception;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            firstFailure ??= exception;
        }

        var status = firstFailure is null
            ? RelatedLogReadStatus.Complete
            : readableRecords > 0
                ? RelatedLogReadStatus.Partial
                : RelatedLogReadStatus.Unavailable;
        return new RelatedLogReadResult(lines, status, firstFailure);
    }
}
