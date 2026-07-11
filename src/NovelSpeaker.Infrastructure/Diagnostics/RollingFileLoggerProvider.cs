using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Infrastructure.Speech;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Writes redacted application logs to a bounded set of local files.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    public const long DefaultMaxFileBytes = 10L * 1024 * 1024;
    public const int DefaultMaxFileCount = 10;

    private readonly object _syncRoot = new();
    private readonly string _logDirectoryPath;
    private readonly long _maxFileBytes;
    private readonly int _maxFileCount;

    public RollingFileLoggerProvider(
        IAppDataDirectoryProvider directories,
        long maxFileBytes = DefaultMaxFileBytes,
        int maxFileCount = DefaultMaxFileCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFileCount, 1);

        _logDirectoryPath = directories.LogsDirectoryPath;
        _maxFileBytes = maxFileBytes;
        _maxFileCount = maxFileCount;
    }

    public ILogger CreateLogger(string categoryName) => new RollingFileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(LogLevel logLevel, string categoryName, string message, Exception? exception)
    {
        var line = string.Join(
            " ",
            DateTimeOffset.UtcNow.ToString("O"),
            $"[{logLevel.ToString().ToUpperInvariant()}]",
            $"[{categoryName}]",
            SensitiveDataRedactor.RedactPlainText(message),
            exception is null ? string.Empty : SensitiveDataRedactor.RedactPlainText(exception.ToString()))
            .TrimEnd() + Environment.NewLine;

        lock (_syncRoot)
        {
            Directory.CreateDirectory(_logDirectoryPath);
            var logPath = Path.Combine(_logDirectoryPath, "novelspeaker.log");
            if (File.Exists(logPath) && new FileInfo(logPath).Length + System.Text.Encoding.UTF8.GetByteCount(line) > _maxFileBytes)
            {
                RotateFiles(logPath);
            }

            File.AppendAllText(logPath, line, System.Text.Encoding.UTF8);
        }
    }

    private void RotateFiles(string logPath)
    {
        for (var index = _maxFileCount - 1; index >= 1; index--)
        {
            var targetPath = Path.Combine(_logDirectoryPath, $"novelspeaker.{index}.log");
            if (index == _maxFileCount - 1 && File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            var sourcePath = index == 1
                ? logPath
                : Path.Combine(_logDirectoryPath, $"novelspeaker.{index - 1}.log");
            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, targetPath, overwrite: true);
            }
        }
    }

    private sealed class RollingFileLogger : ILogger
    {
        private readonly RollingFileLoggerProvider _provider;
        private readonly string _categoryName;

        public RollingFileLogger(RollingFileLoggerProvider provider, string categoryName)
        {
            _provider = provider;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                _provider.Write(logLevel, _categoryName, formatter(state, exception), exception);
            }
        }
    }
}
