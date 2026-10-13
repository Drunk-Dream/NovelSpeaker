using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Desktop.MediaControls;
using NovelSpeaker.App.Desktop.Lifecycle;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Settings;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Implements startup stages at the WPF composition boundary.
/// </summary>
internal sealed class WpfStartupRuntime : IStartupRuntime, IProcessLifecycleDiagnostics
{
    private static readonly TimeSpan BackgroundShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly IReadOnlyDictionary<StartupStage, (string Status, string Detail)> StageText =
        new Dictionary<StartupStage, (string, string)>
        {
            [StartupStage.Directories] = ("正在初始化应用数据目录。", "正在准备数据库、设置和日志目录。"),
            [StartupStage.Settings] = ("正在读取应用设置。", "设置将在本次启动中作为同一份快照使用。"),
            [StartupStage.Logging] = ("正在建立启动诊断日志。", "诊断信息不会记录正文、凭据或完整请求。"),
            [StartupStage.DependencyInjection] = ("正在创建服务容器。", "正在装配并校验应用服务。"),
            [StartupStage.Database] = ("正在初始化数据库。", "正在运行迁移、恢复操作并准备默认数据。"),
            [StartupStage.Theme] = ("正在应用界面主题。", "正在准备浅色、深色或系统主题。"),
            [StartupStage.Shell] = ("正在创建主窗口。", "启动完成后将进入书库首页。")
        };

    private readonly Dispatcher _dispatcher;
    private readonly Action<MainWindow> _setMainWindow;
    private readonly StartupStatusViewModel _statusViewModel = new();
    private readonly ProcessShutdownGate _shutdownGate = new();
    private readonly TimeSpan _backgroundShutdownTimeout;
    private StartupStatusWindow? _statusWindow;
    private AppDataDirectoryProvider? _directories;
    private JsonAppSettingsStore? _settingsStore;
    private ProcessDiagnosticsRecorder? _diagnostics;
    private IObservability? _observability;
    private RollingFileLoggerProvider? _loggerProvider;
    private ObservabilityContextAccessor? _observabilityContextAccessor;
    private IOperationScope? _startupOperation;
    private IOperationScope? _shutdownOperation;
    private ServiceProvider? _serviceProvider;
    private CancellationTokenSource? _backgroundShutdownBudget;
    private bool _backgroundDrainIncomplete;
    private Task? _activeCacheStopTask;

    public WpfStartupRuntime(Dispatcher dispatcher, Action<MainWindow> setMainWindow)
    {
        _backgroundShutdownTimeout = BackgroundShutdownTimeout;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _setMainWindow = setMainWindow ?? throw new ArgumentNullException(nameof(setMainWindow));
    }

    internal WpfStartupRuntime(
        Dispatcher dispatcher,
        Action<MainWindow> setMainWindow,
        TimeSpan backgroundShutdownTimeout,
        ServiceProvider? serviceProvider = null)
        : this(dispatcher, setMainWindow)
    {
        if (backgroundShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(backgroundShutdownTimeout));
        }

        _backgroundShutdownTimeout = backgroundShutdownTimeout;
        _serviceProvider = serviceProvider;
    }

    public Func<CancellationToken, Task>? ShutdownRequestedAsync { get; set; }

    public void ShowStartupStatus()
    {
        _statusWindow = new StartupStatusWindow(_statusViewModel);
        _statusWindow.Show();
    }

    public async Task ReportStageAsync(StartupStage stage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = StageText[stage];
        _diagnostics?.RecordStartupStage(stage.ToString(), text.Status);

        await _dispatcher.InvokeAsync(
            () =>
            {
                _statusViewModel.ReportStage(text.Status, text.Detail);
            },
            DispatcherPriority.Background,
            cancellationToken);
    }

    public async Task PrepareDirectoriesAsync(CancellationToken cancellationToken)
    {
        _directories = CreateAppDataDirectoryProvider(new AppDataRootResolver());
        await _directories.EnsureCreatedAsync(cancellationToken).ConfigureAwait(true);
    }

    internal static AppDataDirectoryProvider CreateAppDataDirectoryProvider(AppDataRootResolver rootResolver)
    {
        ArgumentNullException.ThrowIfNull(rootResolver);
        return new AppDataDirectoryProvider(rootResolver.ResolveRootDirectoryPath());
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken)
    {
        var directories = RequireDirectories();
        _settingsStore = new JsonAppSettingsStore(directories);
        return await _settingsStore.LoadAsync(cancellationToken).ConfigureAwait(true);
    }

    public Task InitializeLoggingAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _observabilityContextAccessor = new ObservabilityContextAccessor();
        _loggerProvider = new RollingFileLoggerProvider(
            RequireDirectories(),
            _observabilityContextAccessor);
        _diagnostics = new ProcessDiagnosticsRecorder(
            _loggerProvider.CreateLogger("ProcessDiagnosticsRecorder"));
        return Task.CompletedTask;
    }

    public Task BuildServicesAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directories = RequireDirectories();
        var settingsStore = _settingsStore
            ?? throw new InvalidOperationException("启动设置存储尚未初始化。");
        var loggerProvider = _loggerProvider
            ?? throw new InvalidOperationException("生产日志尚未初始化。");
        var observabilityContextAccessor = _observabilityContextAccessor
            ?? throw new InvalidOperationException("Observability context 尚未初始化。");
        var services = new ServiceCollection();
        services.AddSingleton(loggerProvider);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(ParseLogLevel(settings.LogLevel));
            builder.AddDebug();
        });
        services.AddSingleton<IAppDataDirectoryProvider>(directories);
        services.AddSingleton(observabilityContextAccessor);
        services.AddSingleton(settingsStore);
        services.AddSingleton<IAppSettingsStore>(settingsStore);
        services.AddSingleton<IProcessShutdownGate>(_shutdownGate);
        services.AddNovelSpeakerApplication(settings);
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        _serviceProvider = BuildValidatedServiceProvider(services);
        _observability = _serviceProvider.GetRequiredService<IObservability>();
        _startupOperation = _observability.StartOperation(OperationCatalog.AppStartup);
        _serviceProvider.GetRequiredService<ICacheReadModel>();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    internal static ServiceProvider BuildValidatedServiceProvider(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    public async Task InitializeDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RequireServices()
                .GetRequiredService<IDatabaseInitializer>()
                .InitializeAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (IncompatibleBookLibraryException exception)
        {
            throw new BookLibraryReimportRequiredException(exception);
        }

        try
        {
            await RequireServices()
                .GetRequiredService<IDiagnosticToolLauncher>()
                .RecoverAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            RecordLifecycleFailure(
                "diagnostic-session-recovery",
                "恢复诊断会话失败，将继续启动。",
                exception);
        }
    }

    public Task ApplyThemeAsync(CancellationToken cancellationToken) =>
        RequireServices()
            .GetRequiredService<AppThemeStartupCoordinator>()
            .ApplyAsync(cancellationToken);

    public Task ApplyFallbackThemeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireServices().GetRequiredService<AppThemeStartupCoordinator>().Apply("System");
        return Task.CompletedTask;
    }

    public async Task ShowShellAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = RequireServices().GetRequiredService<MainWindow>();
        RequireServices()
            .GetRequiredService<WindowsTrayLifecycleAdapter>()
            .AttachMainWindow(window);
        var shutdownAsync = ShutdownRequestedAsync
            ?? throw new InvalidOperationException("应用关闭回调尚未配置。");
        RequireServices().GetRequiredService<IProcessShutdownRequest>().Configure(shutdownAsync);
        var desktopLifecycle = RequireServices().GetRequiredService<IDesktopLifecycleCoordinator>();
        window.ConfigureDesktopLifecycle(
            desktopLifecycle.RequestMainWindowCloseAsync,
            () => desktopLifecycle.IsExitApproved);
        _setMainWindow(window);
        try
        {
            await CompleteShellStartupAsync(
                RunStartupCacheMaintenanceAsync,
                desktopLifecycle.StartAsync,
                RequireServices().GetRequiredService<IMediaControlCoordinator>().StartAsync,
                CloseStartupStatus,
                cancellationToken).ConfigureAwait(true);
            RequireServices().GetRequiredService<IDiagnosticToolLauncher>().OpenIfSessionActive();
            _startupOperation?.Complete(OperationResult.Succeeded());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _startupOperation?.Complete(OperationResult.Cancelled());
            throw;
        }
        catch
        {
            _startupOperation?.Complete(OperationResult.Failed("startup-shell-failed"));
            throw;
        }
    }

    internal static async Task CompleteShellStartupAsync(
        Func<CancellationToken, Task> runStartupMaintenanceAsync,
        Func<CancellationToken, Task> startDesktopLifecycleAsync,
        Func<CancellationToken, Task> startMediaControlsAsync,
        Action closeStartupStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runStartupMaintenanceAsync);
        ArgumentNullException.ThrowIfNull(startDesktopLifecycleAsync);
        ArgumentNullException.ThrowIfNull(startMediaControlsAsync);
        ArgumentNullException.ThrowIfNull(closeStartupStatus);

        await runStartupMaintenanceAsync(cancellationToken).ConfigureAwait(true);
        await startDesktopLifecycleAsync(cancellationToken).ConfigureAwait(true);
        await startMediaControlsAsync(cancellationToken).ConfigureAwait(true);
        closeStartupStatus();
    }

    public void BeginShutdown()
    {
        _shutdownGate.TryBeginShutdown();
        if (_serviceProvider is not null && _activeCacheStopTask is null)
        {
            _activeCacheStopTask = _serviceProvider.GetRequiredService<IActiveCacheLifetime>()
                .StopAsync(CancellationToken.None);
            ObserveLateFailure(_activeCacheStopTask);
        }
        if (_serviceProvider is not null && _shutdownOperation is null)
        {
            _shutdownOperation = _serviceProvider
                .GetRequiredService<IObservability>()
                .StartOperation(OperationCatalog.AppShutdown);
        }
    }

    public async Task StopDesktopLifecycleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_serviceProvider is null)
        {
            return;
        }

        await _serviceProvider
            .GetRequiredService<IDesktopLifecycleCoordinator>()
            .StopAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StopMediaControlsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_serviceProvider is null)
        {
            return;
        }

        await _serviceProvider
            .GetRequiredService<IMediaControlCoordinator>()
            .StopAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task StopPlaybackAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_serviceProvider is null)
        {
            return;
        }

        await _serviceProvider
            .GetRequiredService<PlaybackCoordinator>()
            .DisposeAsync()
            .AsTask()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task WaitForBackgroundTasksAsync(CancellationToken cancellationToken)
    {
        if (_serviceProvider is not null)
        {
            await WaitForBackgroundTasksAsync(
                _serviceProvider.GetRequiredService<IActiveCacheLifetime>(),
                _serviceProvider.GetRequiredService<IChapterExportCoordinator>(),
                _serviceProvider.GetRequiredService<ISpeechPlanRepairLifetime>(),
                _serviceProvider.GetRequiredService<ICacheChangeLifetime>(),
                cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task WaitForBackgroundTasksAsync(
        IActiveCacheLifetime activeCacheLifetime,
        IChapterExportCoordinator chapterExportCoordinator,
        ISpeechPlanRepairLifetime speechPlanRepairCoordinator,
        ICacheChangeLifetime? cacheChangeLifetime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activeCacheLifetime);
        ArgumentNullException.ThrowIfNull(chapterExportCoordinator);
        ArgumentNullException.ThrowIfNull(speechPlanRepairCoordinator);
        var budget = GetBackgroundShutdownBudget();
        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(budget, cancellationToken);
        var token = waitCancellation.Token;

        // Cancel both batch owners before waiting for either one; they share one budget.
        await Task.WhenAll(
            DrainAsync("active-cache-shutdown", stopToken => _activeCacheStopTask ?? activeCacheLifetime.StopAsync(stopToken), token),
            DrainAsync("chapter-export-shutdown", chapterExportCoordinator.CancelAsync, token))
            .ConfigureAwait(false);

        await DrainAsync("chapter-speech-plan-shutdown", speechPlanRepairCoordinator.StopAsync, token)
            .ConfigureAwait(false);

        if (cacheChangeLifetime is not null)
        {
            await DrainAsync("cache-invalidation-shutdown", cacheChangeLifetime.StopAsync, token)
                .ConfigureAwait(false);
        }
    }

    private CancellationToken GetBackgroundShutdownBudget()
    {
        _backgroundShutdownBudget ??= new CancellationTokenSource(_backgroundShutdownTimeout);
        return _backgroundShutdownBudget.Token;
    }

    private async Task DrainAsync(string name, Func<CancellationToken, Task> stopAsync, CancellationToken token)
    {
        Task? task = null;
        try
        {
            task = stopAsync(token);
            await task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _backgroundDrainIncomplete = true;
            if (task is not null) ObserveLateFailure(task);
            try
            {
                RecordLifecycleFailure(name, "后台任务未完成排空，将继续尽力关闭。", exception);
            }
            catch
            {
                // A failing diagnostic sink must not prevent the remaining stop requests.
            }
        }
    }

    private static void ObserveLateFailure(Task task) =>
        _ = task.ContinueWith(static faulted => _ = faulted.Exception,
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _loggerProvider?.FlushAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public void RecordFailure(StartupStage stage, string safeMessage, Exception exception)
    {
        _diagnostics?.RecordStartupFailure(stage.ToString(), safeMessage, exception);
    }

    public void RecordLifecycleFailure(string name, string safeMessage, Exception? exception)
    {
        _diagnostics?.RecordLifecycleFailure(name, safeMessage, exception);
    }

    public void RecordStage(string name, string safeMessage)
    {
        _diagnostics?.RecordLifecycleStage(name, safeMessage);
    }

    public void RecordProcessFailure(ProcessFailure failure, string source, string safeMessage, Exception? exception) =>
        _diagnostics?.RecordProcessFailure(failure, source, safeMessage, exception, _observability);

    void IProcessLifecycleDiagnostics.RecordFailure(
        string name,
        string safeMessage,
        Exception exception)
    {
        RecordLifecycleFailure(name, safeMessage, exception);
    }

    public void ShowStartupFailure(StartupFailure failure)
    {
        _statusViewModel.ShowFailure(failure);
        MessageBox.Show(
            failure.Message,
            failure.Title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public void CloseStartupStatus()
    {
        _statusWindow?.Close();
        _statusWindow = null;
    }

    public async Task NotifyProcessExitAsync(ProcessExitReason exitReason, CancellationToken cancellationToken)
    {
        _startupOperation?.Complete(OperationResult.Cancelled());
        _shutdownOperation?.Complete(_backgroundDrainIncomplete
            ? OperationResult.Failed("background-shutdown-incomplete") : OperationResult.Succeeded());
        _diagnostics?.RecordProcessExit(exitReason);
        if (_serviceProvider is not null)
        {
            await _serviceProvider
                .GetRequiredService<IDiagnosticSessionService>()
                .NotifyProcessShutdownAsync(exitReason, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        CloseStartupStatus();
        if (_serviceProvider is not null)
        {
            // Container disposal must not restart a fresh timeout for each owner after
            // the process drain has already exhausted its budget.
            await DrainAsync("container-disposal", _ => _serviceProvider.DisposeAsync().AsTask(),
                GetBackgroundShutdownBudget()).ConfigureAwait(false);
            _serviceProvider = null;
        }

        if (_loggerProvider is not null)
        {
            await DrainAsync("logger-disposal", _ => _loggerProvider.DisposeAsync().AsTask(),
                GetBackgroundShutdownBudget()).ConfigureAwait(false);
            _loggerProvider = null;
        }

        _shutdownGate.Dispose();
        _backgroundShutdownBudget?.Dispose();
    }

    private AppDataDirectoryProvider RequireDirectories() =>
        _directories ?? throw new InvalidOperationException("应用数据目录尚未初始化。");

    private ServiceProvider RequireServices() =>
        _serviceProvider ?? throw new InvalidOperationException("应用服务容器尚未初始化。");

    private async Task RunStartupCacheMaintenanceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cacheStore = RequireServices().GetRequiredService<IAudioCacheStore>();
            await cacheStore
                .RunStartupMaintenanceAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            try
            {
                RecordLifecycleFailure(
                    "audio-cache-maintenance",
                    "启动缓存维护失败，将继续启动。",
                    exception);
            }
            catch
            {
                // Startup maintenance is best effort; diagnostics must not prevent the shell
                // from becoming interactive when their own sink is unavailable.
            }
        }
    }

    private static LogLevel ParseLogLevel(string? value)
    {
        return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var result)
            ? result
            : LogLevel.Information;
    }
}
