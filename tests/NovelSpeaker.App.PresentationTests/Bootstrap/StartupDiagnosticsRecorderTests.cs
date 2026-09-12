using NovelSpeaker.App.Bootstrap;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Diagnostics;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Bootstrap;

public sealed class StartupDiagnosticsRecorderTests
{
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
            var recorder = new StartupDiagnosticsRecorder(
                provider.CreateLogger("StartupDiagnosticsRecorder"));

            recorder.RecordFailure(
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
}
