using Microsoft.Extensions.Logging;

namespace NovelSpeaker.TestKit.Common;

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<CapturedLogEntry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new CapturedLogEntry(logLevel, formatter(state, exception), exception));
    }
}

internal sealed record CapturedLogEntry(LogLevel Level, string Message, Exception? Exception);
