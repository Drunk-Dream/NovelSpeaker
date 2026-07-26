using NovelSpeaker.App.Bootstrap;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Bootstrap;

public sealed class StartupDiagnosticsRecorderTests
{
    [Fact]
    public void RecordFailure_writes_only_safe_message_and_exception_type()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var directories = new LocalAppDataDirectoryProvider(root);
            var recorder = new StartupDiagnosticsRecorder(directories);

            recorder.RecordFailure(
                "database",
                "无法初始化或恢复本地数据库。",
                new InvalidOperationException(
                    @"C:\Users\reader\secret.db Authorization=Bearer private-token https://tts.example/audio?token=private body=正文机密句"));

            var log = File.ReadAllText(Path.Combine(directories.LogsDirectoryPath, "startup.log"));

            Assert.Contains("无法初始化或恢复本地数据库。", log, StringComparison.Ordinal);
            Assert.Contains("ExceptionType=InvalidOperationException", log, StringComparison.Ordinal);
            Assert.DoesNotContain("C:\\Users", log, StringComparison.Ordinal);
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
