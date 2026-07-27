using NovelSpeaker.App.Bootstrap;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Bootstrap;

public sealed class BackgroundTaskRegistryTests
{
    [Fact]
    public async Task Registered_failure_is_observed_and_recorded_safely()
    {
        var diagnostics = new RecordingLifecycleDiagnostics();
        var registry = new BackgroundTaskRegistry(diagnostics, TimeProvider.System);

        registry.Register(
            "cache-maintenance",
            _ => Task.FromException(new InvalidOperationException("private cache path")),
            CancellationToken.None);

        var completed = await registry.WaitForCompletionAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.True(completed);
        var failure = Assert.Single(diagnostics.Failures);
        Assert.Equal("cache-maintenance", failure.Name);
        Assert.Equal("后台任务执行失败。", failure.SafeMessage);
        Assert.IsType<InvalidOperationException>(failure.Exception);
    }

    [Fact]
    public async Task Shutdown_timeout_is_reported_without_waiting_for_worker_completion()
    {
        var diagnostics = new RecordingLifecycleDiagnostics();
        var timeProvider = new ManualTimeProvider();
        var registry = new BackgroundTaskRegistry(diagnostics, timeProvider);
        var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        registry.Register("cache-maintenance", _ => worker.Task, CancellationToken.None);

        var waitTask = registry.WaitForCompletionAsync(
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        Assert.False(await waitTask);
        Assert.Equal(
            ("background-tasks", "等待后台任务退出超时，将继续关闭。"),
            Assert.Single(diagnostics.Stages));

        worker.SetResult();
    }

    [Fact]
    public async Task Successful_worker_remains_observed_when_stage_diagnostic_throws()
    {
        var registry = new BackgroundTaskRegistry(
            new ThrowingLifecycleDiagnostics(throwOnStage: true),
            TimeProvider.System);

        registry.Register(
            "cache-maintenance",
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.True(await registry.WaitForCompletionAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None));
    }

    [Fact]
    public async Task Failed_worker_remains_observed_when_failure_diagnostic_throws()
    {
        var registry = new BackgroundTaskRegistry(
            new ThrowingLifecycleDiagnostics(throwOnFailure: true),
            TimeProvider.System);

        registry.Register(
            "cache-maintenance",
            _ => Task.FromException(new IOException("private cache path")),
            CancellationToken.None);

        Assert.True(await registry.WaitForCompletionAsync(
            TimeSpan.FromSeconds(1),
            CancellationToken.None));
    }

    [Fact]
    public async Task Timeout_returns_false_when_timeout_diagnostic_throws()
    {
        var timeProvider = new ManualTimeProvider();
        var registry = new BackgroundTaskRegistry(
            new ThrowingLifecycleDiagnostics(throwOnStage: true),
            timeProvider);
        var worker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        registry.Register("cache-maintenance", _ => worker.Task, CancellationToken.None);

        var waitTask = registry.WaitForCompletionAsync(
            TimeSpan.FromSeconds(3),
            CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        Assert.False(await waitTask);
        worker.SetResult();
    }

    private sealed class RecordingLifecycleDiagnostics : IProcessLifecycleDiagnostics
    {
        public List<(string Name, string SafeMessage)> Stages { get; } = [];

        public List<(string Name, string SafeMessage, Exception Exception)> Failures { get; } = [];

        public void RecordStage(string name, string safeMessage) =>
            Stages.Add((name, safeMessage));

        public void RecordFailure(string name, string safeMessage, Exception exception) =>
            Failures.Add((name, safeMessage, exception));
    }

    private sealed class ThrowingLifecycleDiagnostics(
        bool throwOnStage = false,
        bool throwOnFailure = false) : IProcessLifecycleDiagnostics
    {
        public void RecordStage(string name, string safeMessage)
        {
            if (throwOnStage)
            {
                throw new InvalidOperationException("diagnostic stage failure");
            }
        }

        public void RecordFailure(string name, string safeMessage, Exception exception)
        {
            if (throwOnFailure)
            {
                throw new InvalidOperationException("diagnostic failure");
            }
        }
    }
}
