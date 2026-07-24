using System.Text;
using System.IO;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Speech.Security;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Writes minimal startup and crash diagnostics without recording sensitive content.
/// </summary>
public sealed class StartupDiagnosticsRecorder
{
    private readonly object _syncRoot = new();
    private readonly string _logPath;

    public StartupDiagnosticsRecorder(IAppDataDirectoryProvider directories)
    {
        Directory.CreateDirectory(directories.LogsDirectoryPath);
        _logPath = Path.Combine(directories.LogsDirectoryPath, "startup.log");
    }

    public void RecordStage(string stage, string message)
    {
        WriteLine("INFO", stage, message, null);
    }

    public void RecordFailure(string stage, string message, Exception? exception)
    {
        WriteLine("ERROR", stage, message, exception);
    }

    private void WriteLine(string level, string stage, string message, Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(DateTimeOffset.UtcNow.ToString("O"))
            .Append(" [")
            .Append(level)
            .Append("] ")
            .Append(stage)
            .Append(" - ")
            .Append(SensitiveDataRedactor.RedactPlainText(message));

        if (exception is not null)
        {
            builder
                .Append(" (")
                .Append(SensitiveDataRedactor.RedactPlainText(exception.ToString()))
                .Append(')');
        }

        lock (_syncRoot)
        {
            File.AppendAllText(_logPath, builder.AppendLine().ToString(), Encoding.UTF8);
        }
    }
}
