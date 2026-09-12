using System.Text.Json;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Infrastructure.Cache;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Diagnostics;

public sealed class RollingFileLoggerProviderTests
{
    [Fact]
    public void Log_event_registry_has_unique_stable_definitions()
    {
        Assert.NotEmpty(LogEventRegistry.All);
        Assert.Equal(
            LogEventRegistry.All.Count,
            LogEventRegistry.All.Select(definition => definition.Id).Distinct().Count());
        Assert.All(
            LogEventRegistry.All,
            definition =>
            {
                Assert.True(definition.Id > 0);
                Assert.False(string.IsNullOrWhiteSpace(definition.EventName));
                Assert.False(string.IsNullOrWhiteSpace(definition.Category));
                Assert.NotNull(definition.Operation);
                Assert.False(string.IsNullOrWhiteSpace(definition.Operation!.Id.Value));
                Assert.False(string.IsNullOrWhiteSpace(definition.Description));
            });
    }

    [Fact]
    public async Task Logger_redacts_sensitive_values_and_rotates_bounded_files()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        await using var provider = new RollingFileLoggerProvider(
            directories,
            maxFileBytes: 220,
            maxTotalBytes: 660);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var logger = factory.CreateLogger("Tests");

        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            logger.LogInformation("Authorization=secret-{Index} speakText=novel-{TextIndex}", index, index))));

        await provider.FlushAsync();
        factory.Dispose();
        await provider.DisposeAsync();
        var files = Directory.GetFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl");
        var content = string.Join(Environment.NewLine, files.Select(File.ReadAllText));

        Assert.InRange(files.Length, 1, 4);
        Assert.DoesNotContain("secret-", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("novel-", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorization=***", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Logger_restarts_into_a_new_file_when_existing_file_has_insufficient_room()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var existingPath = Path.Combine(
            directories.LogsDirectoryPath,
            $"novelspeaker-{DateTime.UtcNow:yyyyMMdd}-000.jsonl");
        await File.WriteAllBytesAsync(existingPath, new byte[200]);

        await using (var provider = new RollingFileLoggerProvider(
                         directories,
                         maxFileBytes: 256,
                         maxTotalBytes: 1024))
        {
            provider.CreateLogger("RestartTests").LogInformation(
                "A record that cannot fit in the existing volume.");
            await provider.FlushAsync();
        }

        var files = Directory.GetFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl");
        Assert.Equal(2, files.Length);
        Assert.Equal(200, new FileInfo(existingPath).Length);
    }

    [Fact]
    public async Task Logger_writes_stable_json_schema_with_structured_exception_and_correlation()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var context = new ObservabilityContextAccessor("process-1");

        await using (var provider = new RollingFileLoggerProvider(directories, context))
        {
            using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            var logger = factory.CreateLogger("SchemaTests");
            using (context.Push(new CorrelationContext("process-1", "session-1", "activity-1")))
            {
                try
                {
                    throw new InvalidOperationException(
                        "outer",
                        new System.ComponentModel.Win32Exception(unchecked((int)0x80004005), "inner"));
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        LogEventRegistry.TtsRequestFailed.EventId,
                        exception,
                        "TTS request failed.");
                }
            }

            await provider.FlushAsync();
        }

        var line = Assert.Single(
            Directory.EnumerateFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl")
                .SelectMany(File.ReadLines));
        using var document = JsonDocument.Parse(line);
        var rootElement = document.RootElement;
        Assert.Equal(1, rootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(1101, rootElement.GetProperty("eventId").GetInt32());
        Assert.Equal("tts.request.failed", rootElement.GetProperty("eventName").GetString());
        Assert.Equal("speech", rootElement.GetProperty("category").GetString());
        Assert.Equal("tts.request", rootElement.GetProperty("operation").GetString());
        Assert.Equal("process-1", rootElement.GetProperty("processInstanceId").GetString());
        Assert.Equal("session-1", rootElement.GetProperty("diagnosticSessionId").GetString());
        Assert.Equal("activity-1", rootElement.GetProperty("activityId").GetString());
        Assert.Equal("System.InvalidOperationException", rootElement.GetProperty("exception").GetProperty("type").GetString());
        var inner = rootElement.GetProperty("exception").GetProperty("children")[0];
        Assert.Equal("System.ComponentModel.Win32Exception", inner.GetProperty("type").GetString());
        Assert.True(inner.GetProperty("hResult").GetInt32() != 0);
    }

    [Fact]
    public async Task Logger_context_is_omitted_when_no_diagnostic_scope_is_active()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var context = new ObservabilityContextAccessor("process-2");

        await using (var provider = new RollingFileLoggerProvider(directories, context))
        {
            using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
            factory.CreateLogger("CorrelationTests").LogWarning(
                LogEventRegistry.CacheCompletenessUnavailable.EventId,
                "Cache completeness unavailable.");
            await provider.FlushAsync();
        }

        var line = Assert.Single(
            Directory.EnumerateFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl")
                .SelectMany(File.ReadLines));
        using var document = JsonDocument.Parse(line);
        Assert.Equal("process-2", document.RootElement.GetProperty("processInstanceId").GetString());
        Assert.False(document.RootElement.TryGetProperty("diagnosticSessionId", out _));
        Assert.False(document.RootElement.TryGetProperty("activityId", out _));
    }

    [Fact]
    public async Task High_priority_records_keep_a_reserved_queue_when_information_is_full()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await using var provider = new RollingFileLoggerProvider(
            directories,
            queueCapacity: 2,
            maxFileBytes: 32 * 1024,
            maxTotalBytes: 64 * 1024);
        var logger = provider.CreateLogger("PriorityTests");

        Parallel.For(
            0,
            2000,
            index => logger.LogInformation("Information record {Index}.", index));
        logger.LogError(LogEventRegistry.StartupFailure.EventId, "High priority failure.");

        await provider.FlushAsync();
        await provider.DisposeAsync();
        var content = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl")
                .Select(File.ReadAllText));

        Assert.Contains("\"eventId\":1002", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logger_preserves_all_aggregate_exception_children()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await using (var provider = new RollingFileLoggerProvider(directories))
        {
            var logger = provider.CreateLogger("AggregateTests");
            logger.LogError(
                LogEventRegistry.TtsRequestFailed.EventId,
                new AggregateException(
                    "aggregate",
                    new InvalidOperationException("first"),
                    new IOException("second")),
                "Aggregate failure.");
            await provider.FlushAsync();
        }

        var line = Assert.Single(
            Directory.EnumerateFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl")
                .SelectMany(File.ReadLines));
        using var document = JsonDocument.Parse(line);
        var children = document.RootElement.GetProperty("exception").GetProperty("children");
        Assert.Equal(2, children.GetArrayLength());
        Assert.Equal("System.InvalidOperationException", children[0].GetProperty("type").GetString());
        Assert.Equal("System.IO.IOException", children[1].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Failure_reporters_write_redacted_structured_exceptions()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await using var provider = new RollingFileLoggerProvider(directories);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));

        new CacheCompletenessFailureReporter(
                factory.CreateLogger<CacheCompletenessFailureReporter>())
            .ReportCompletenessUnavailable(
                new IOException(@"C:\private\novel-content.txt Authorization=Bearer private-token"));
        new BookPlaybackContentFailureReporter(
                factory.CreateLogger<BookPlaybackContentFailureReporter>())
            .ReportChapterReadFailure(new InvalidOperationException("chapter read failed"));

        await provider.FlushAsync();
        await provider.DisposeAsync();
        var documents = Directory.EnumerateFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl")
            .SelectMany(File.ReadLines)
            .Select(line => JsonDocument.Parse(line))
            .ToArray();

        try
        {
            Assert.Equal(2, documents.Length);
            Assert.All(documents, document => Assert.True(document.RootElement.TryGetProperty("exception", out _)));
            Assert.DoesNotContain(
                "C:\\private",
                string.Join(
                    Environment.NewLine,
                    documents.Select(document => document.RootElement.GetProperty("message").GetString())),
                StringComparison.Ordinal);
        }
        finally
        {
            foreach (var document in documents)
            {
                document.Dispose();
            }
        }
    }

    [Fact]
    public async Task Full_queue_drops_records_without_blocking_and_shutdown_drains_remaining_records()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var provider = new RollingFileLoggerProvider(
            directories,
            queueCapacity: 2,
            maxFileBytes: 32 * 1024,
            maxTotalBytes: 64 * 1024);
        var logger = provider.CreateLogger("OverflowTests");

        Parallel.For(
            0,
            2000,
            index => logger.LogInformation(
                LogEventRegistry.StartupStage.EventId,
                "Stage {StageIndex} completed.",
                index));

        var dropped = provider.DroppedRecordCount;
        provider.Dispose();

        Assert.True(dropped > 0);
        Assert.NotEmpty(Directory.EnumerateFiles(directories.LogsDirectoryPath, "novelspeaker-*.jsonl"));
    }

    [Fact]
    public async Task Writer_failure_is_degraded_and_does_not_escape_the_logging_call()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        Directory.Delete(directories.LogsDirectoryPath);
        File.WriteAllText(directories.LogsDirectoryPath, "not a directory");

        await using var provider = new RollingFileLoggerProvider(directories);
        var logger = provider.CreateLogger("FailureTests");

        var exception = Record.Exception(() => logger.LogError(
            LogEventRegistry.StartupFailure.EventId,
            "The writer should degrade."));
        await provider.FlushAsync();

        Assert.Null(exception);
        Assert.True(provider.IsDegraded);
    }
}
