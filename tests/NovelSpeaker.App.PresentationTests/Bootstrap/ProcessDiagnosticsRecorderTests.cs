using System.Text.Json;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Diagnostics;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Bootstrap;

public sealed class ProcessDiagnosticsRecorderTests
{
    [Fact]
    public async Task Runtime_failure_keeps_detailed_exception_in_logs_and_only_stable_facts_in_diagnostics()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var directories = new AppDataDirectoryProvider(root);
            var context = new ObservabilityContextAccessor("process-one");
            context.SetDiagnosticSession("session-one");
            await using var provider = new RollingFileLoggerProvider(directories, context);
            var recorder = new ProcessDiagnosticsRecorder(provider.CreateLogger("ProcessDiagnosticsRecorder"));
            var consumer = new RecordingConsumer();
            var hub = new ObservabilityHub(context, [consumer]);
            Exception exception;
            try
            {
                throw new InvalidOperationException("Safe outer exception.", new IOException("Safe inner exception."));
            }
            catch (Exception caught)
            {
                exception = caught;
            }

            recorder.RecordProcessFailure(ProcessFailure.FatalUi, "dispatcher", "UI failed.", exception, hub);

            // A failed logger cannot suppress the independent diagnostic sink.
            new ProcessDiagnosticsRecorder(new ThrowingLogger()).RecordProcessFailure(
                ProcessFailure.FatalUi, "dispatcher", "UI failed.", exception, hub);
            Assert.Equal(2, consumer.Events.Count);

            // A failed diagnostic consumer cannot suppress production exception evidence.
            recorder.RecordProcessFailure(ProcessFailure.FatalRuntime, "runtime", "Runtime failed.", exception,
                new ObservabilityHub(context, [new ThrowingConsumer()]));
            await provider.FlushAsync();
            await provider.DisposeAsync();
            var lines = Directory.EnumerateFiles(directories.LogsDirectoryPath, "*.jsonl")
                .SelectMany(File.ReadLines).ToArray();
            Assert.Equal(2, lines.Length);
            using var log = JsonDocument.Parse(lines[0]);
            Assert.Equal("app.runtime.failure", log.RootElement.GetProperty("eventName").GetString());
            Assert.Equal("Critical", log.RootElement.GetProperty("level").GetString());
            var details = log.RootElement.GetProperty("exception");
            Assert.Equal(typeof(InvalidOperationException).FullName, details.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(details.GetProperty("stackTrace").GetString()));
            Assert.Equal(typeof(IOException).FullName, details.GetProperty("children")[0].GetProperty("type").GetString());

            var diagnosticEvent = consumer.Events[0];
            Assert.Equal("app.process.failure", diagnosticEvent.Definition.Id.Value);
            Assert.Equal(context.Current, diagnosticEvent.Context);
            var fields = diagnosticEvent.Fields.Values.ToDictionary(value => value.Field.Name, value => value.StringValue);
            Assert.Equal("dispatcher", fields["source"]);
            Assert.Equal("fatal", fields["severity"]);
            Assert.Equal("exit", fields["action"]);

        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RecordFailure_writes_only_safe_message_and_exception_type()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var directories = new AppDataDirectoryProvider(root);
            await using var provider = new RollingFileLoggerProvider(
                directories,
                maxFileBytes: 4096,
                maxTotalBytes: 8192);
            var recorder = new ProcessDiagnosticsRecorder(
                provider.CreateLogger("ProcessDiagnosticsRecorder"));

            recorder.RecordStartupFailure(
                "database",
                "无法初始化或恢复本地数据库。",
                new InvalidOperationException(
                    @"C:\Users\reader\Novel Library\My Book\chapter 1.txt Authorization=Bearer private-token https://tts.example/audio?token=private body=正文机密句"));

            await provider.FlushAsync();
            await provider.DisposeAsync();

            var log = string.Join(
                Environment.NewLine,
                Directory.EnumerateFiles(directories.LogsDirectoryPath, "*.jsonl")
                    .Select(File.ReadAllText));

            Assert.Contains("无法初始化或恢复本地数据库。", log, StringComparison.Ordinal);
            Assert.Contains("\"type\":\"System.InvalidOperationException\"", log, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\Users", log, StringComparison.Ordinal);
            Assert.DoesNotContain("Novel Library", log, StringComparison.Ordinal);
            Assert.DoesNotContain("chapter 1.txt", log, StringComparison.Ordinal);
            Assert.DoesNotContain("private-token", log, StringComparison.Ordinal);
            Assert.DoesNotContain("tts.example", log, StringComparison.Ordinal);
            Assert.DoesNotContain("正文机密句", log, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class RecordingConsumer : IObservabilityConsumer
    {
        public List<DiagnosticEvent> Events { get; } = [];
        public void OnOperationStarted(OperationStarted operation) { }
        public void OnOperationCompleted(OperationCompleted operation) { }
        public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }

    private sealed class ThrowingConsumer : IObservabilityConsumer
    {
        public void OnOperationStarted(OperationStarted operation) { }
        public void OnOperationCompleted(OperationCompleted operation) { }
        public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent) => throw new IOException("Sink unavailable.");
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new IOException("Logger unavailable.");
    }
}
