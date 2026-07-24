using NovelSpeaker.App.Bootstrap;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.Bootstrap;

public sealed class StartupCoordinatorTests
{
    public static TheoryData<int> RequiredStages =>
        new()
        {
            (int)StartupStage.Directories,
            (int)StartupStage.Settings,
            (int)StartupStage.Logging,
            (int)StartupStage.DependencyInjection,
            (int)StartupStage.Database,
            (int)StartupStage.Shell
        };

    [Fact]
    public async Task StartAsync_runs_required_stages_in_order_and_uses_one_settings_snapshot()
    {
        var runtime = new RecordingStartupRuntime();
        await using var coordinator = new StartupCoordinator(runtime);

        var result = await coordinator.StartAsync();

        Assert.True(result.IsSuccessful);
        Assert.False(result.IsCancelled);
        Assert.Null(result.Failure);
        Assert.Equal(1, runtime.SettingsLoadCalls);
        Assert.Same(runtime.LoadedSettings, runtime.LoggingSettings);
        Assert.Same(runtime.LoadedSettings, runtime.DependencyInjectionSettings);
        Assert.Equal(
            [
                StartupStage.Directories,
                StartupStage.Settings,
                StartupStage.Logging,
                StartupStage.DependencyInjection,
                StartupStage.Database,
                StartupStage.Theme,
                StartupStage.Shell
            ],
            runtime.CompletedStages);
        Assert.Equal(1, runtime.ShellCalls);
    }

    [Theory]
    [MemberData(nameof(RequiredStages))]
    public async Task StartAsync_projects_each_required_stage_failure_and_blocks_shell(int stageValue)
    {
        var stage = (StartupStage)stageValue;
        const string sensitive =
            @"C:\Users\reader\Novel\secret.txt Authorization=Bearer private-token https://tts.example/audio?token=private body=正文机密句";
        var runtime = new RecordingStartupRuntime
        {
            FailureStage = stage,
            Failure = new InvalidOperationException(sensitive)
        };
        await using var coordinator = new StartupCoordinator(runtime);

        var result = await coordinator.StartAsync();

        Assert.False(result.IsSuccessful);
        Assert.False(result.IsCancelled);
        Assert.Equal(stage, result.Failure?.Stage);
        Assert.Single(runtime.VisibleFailures);
        Assert.DoesNotContain("C:\\Users", runtime.VisibleFailures[0], StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", runtime.VisibleFailures[0], StringComparison.Ordinal);
        Assert.DoesNotContain("tts.example", runtime.VisibleFailures[0], StringComparison.Ordinal);
        Assert.DoesNotContain("正文机密句", runtime.VisibleFailures[0], StringComparison.Ordinal);
        Assert.Equal(stage == StartupStage.Shell ? 1 : 0, runtime.ShellCalls);
        Assert.True(runtime.StatusClosed);
    }

    [Fact]
    public async Task StartAsync_database_or_recovery_failure_prevents_theme_and_shell()
    {
        var runtime = new RecordingStartupRuntime
        {
            FailureStage = StartupStage.Database,
            Failure = new InvalidDataException("恢复记录包含 C:\\private\\content.txt")
        };
        await using var coordinator = new StartupCoordinator(runtime);

        var result = await coordinator.StartAsync();

        Assert.False(result.IsSuccessful);
        Assert.Equal(StartupStage.Database, result.Failure?.Stage);
        Assert.DoesNotContain(StartupStage.Theme, runtime.CompletedStages);
        Assert.Equal(0, runtime.ShellCalls);
    }

    [Fact]
    public async Task StartAsync_theme_failure_records_safe_diagnostic_and_continues_to_shell()
    {
        var runtime = new RecordingStartupRuntime
        {
            FailureStage = StartupStage.Theme,
            Failure = new InvalidOperationException("Theme path C:\\Users\\reader\\secret")
        };
        await using var coordinator = new StartupCoordinator(runtime);

        var result = await coordinator.StartAsync();

        Assert.True(result.IsSuccessful);
        Assert.Equal(1, runtime.ShellCalls);
        Assert.Equal(1, runtime.FallbackThemeCalls);
        var diagnostic = Assert.Single(runtime.RecordedFailures);
        Assert.Equal(StartupStage.Theme, diagnostic.Stage);
        Assert.DoesNotContain("C:\\Users", diagnostic.SafeMessage, StringComparison.Ordinal);
        Assert.Empty(runtime.VisibleFailures);
    }

    [Theory]
    [MemberData(nameof(AllStages))]
    public async Task StartAsync_treats_cancellation_at_any_stage_as_normal_control_flow(int stageValue)
    {
        var stage = (StartupStage)stageValue;
        using var cancellation = new CancellationTokenSource();
        var runtime = new RecordingStartupRuntime
        {
            CancellationStage = stage,
            Cancel = cancellation.Cancel
        };
        await using var coordinator = new StartupCoordinator(runtime);

        var result = await coordinator.StartAsync(cancellation.Token);

        Assert.False(result.IsSuccessful);
        Assert.True(result.IsCancelled);
        Assert.Null(result.Failure);
        Assert.Empty(runtime.VisibleFailures);
        Assert.Empty(runtime.RecordedFailures);
        Assert.True(runtime.StatusClosed);
        Assert.Equal(stage == StartupStage.Shell ? 1 : 0, runtime.ShellCalls);
    }

    [Fact]
    public async Task Cancel_cancels_process_token_used_by_startup_stages()
    {
        var runtime = new RecordingStartupRuntime();
        await using var coordinator = new StartupCoordinator(runtime);
        runtime.BeforeStage = stage =>
        {
            if (stage == StartupStage.Database)
            {
                coordinator.Cancel();
            }
        };

        var result = await coordinator.StartAsync();

        Assert.True(result.IsCancelled);
        Assert.True(coordinator.ProcessToken.IsCancellationRequested);
        Assert.Equal(0, runtime.ShellCalls);
    }

    [Fact]
    public async Task Cancel_after_success_cancels_token_retained_by_runtime_background_work()
    {
        var runtime = new RecordingStartupRuntime();
        await using var coordinator = new StartupCoordinator(runtime);

        var result = await coordinator.StartAsync();

        Assert.True(result.IsSuccessful);
        Assert.False(runtime.ShellProcessToken.IsCancellationRequested);

        coordinator.Cancel();
        await runtime.BackgroundCancellation;

        Assert.True(runtime.ShellProcessToken.IsCancellationRequested);
    }

    public static TheoryData<int> AllStages =>
        new()
        {
            (int)StartupStage.Directories,
            (int)StartupStage.Settings,
            (int)StartupStage.Logging,
            (int)StartupStage.DependencyInjection,
            (int)StartupStage.Database,
            (int)StartupStage.Theme,
            (int)StartupStage.Shell
        };

    private sealed class RecordingStartupRuntime : IStartupRuntime
    {
        public AppSettings LoadedSettings { get; } = AppSettings.Default with
        {
            Theme = "Dark",
            LogLevel = "Warning"
        };

        public StartupStage? FailureStage { get; init; }

        public Exception Failure { get; init; } = new InvalidOperationException("failure");

        public StartupStage? CancellationStage { get; init; }

        public Action Cancel { get; init; } = static () => { };

        public Action<StartupStage>? BeforeStage { get; set; }

        public List<StartupStage> CompletedStages { get; } = [];

        public List<(StartupStage Stage, string SafeMessage)> RecordedFailures { get; } = [];

        public List<string> VisibleFailures { get; } = [];

        public int SettingsLoadCalls { get; private set; }

        public int ShellCalls { get; private set; }

        public int FallbackThemeCalls { get; private set; }

        public AppSettings? LoggingSettings { get; private set; }

        public AppSettings? DependencyInjectionSettings { get; private set; }

        public bool StatusClosed { get; private set; }

        public CancellationToken ShellProcessToken { get; private set; }

        public Task BackgroundCancellation => _backgroundCancellation.Task;

        private readonly TaskCompletionSource _backgroundCancellation =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _backgroundCancellationRegistration;

        public void ShowStartupStatus()
        {
        }

        public Task ReportStageAsync(StartupStage stage, CancellationToken cancellationToken)
        {
            BeforeStage?.Invoke(stage);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task PrepareDirectoriesAsync(CancellationToken cancellationToken) =>
            CompleteAsync(StartupStage.Directories, cancellationToken);

        public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken)
        {
            SettingsLoadCalls++;
            await CompleteAsync(StartupStage.Settings, cancellationToken);
            return LoadedSettings;
        }

        public Task InitializeLoggingAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            LoggingSettings = settings;
            return CompleteAsync(StartupStage.Logging, cancellationToken);
        }

        public Task BuildServicesAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            DependencyInjectionSettings = settings;
            return CompleteAsync(StartupStage.DependencyInjection, cancellationToken);
        }

        public Task InitializeDatabaseAsync(CancellationToken cancellationToken) =>
            CompleteAsync(StartupStage.Database, cancellationToken);

        public Task ApplyThemeAsync(CancellationToken cancellationToken) =>
            CompleteAsync(StartupStage.Theme, cancellationToken);

        public Task ApplyFallbackThemeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FallbackThemeCalls++;
            return Task.CompletedTask;
        }

        public Task ShowShellAsync(CancellationToken cancellationToken)
        {
            ShellCalls++;
            ShellProcessToken = cancellationToken;
            _backgroundCancellationRegistration = cancellationToken.Register(
                () => _backgroundCancellation.TrySetResult());
            return CompleteAsync(StartupStage.Shell, cancellationToken);
        }

        public void RecordFailure(StartupStage stage, string safeMessage, Exception exception)
        {
            RecordedFailures.Add((stage, safeMessage));
        }

        public void ShowStartupFailure(StartupFailure failure)
        {
            VisibleFailures.Add($"{failure.Title} {failure.Message}");
        }

        public void CloseStartupStatus()
        {
            StatusClosed = true;
        }

        public ValueTask DisposeAsync()
        {
            _backgroundCancellationRegistration.Dispose();
            return ValueTask.CompletedTask;
        }

        private Task CompleteAsync(StartupStage stage, CancellationToken cancellationToken)
        {
            if (CancellationStage == stage)
            {
                Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (FailureStage == stage)
            {
                throw Failure;
            }

            cancellationToken.ThrowIfCancellationRequested();
            CompletedStages.Add(stage);
            return Task.CompletedTask;
        }
    }
}
