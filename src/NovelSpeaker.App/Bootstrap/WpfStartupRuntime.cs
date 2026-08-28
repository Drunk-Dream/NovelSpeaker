using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Desktop.MediaControls;
using NovelSpeaker.App.Desktop.Lifecycle;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Settings;

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
    private readonly BackgroundTaskRegistry _backgroundTasks;
    private readonly TimeSpan _backgroundShutdownTimeout;
    private StartupStatusWindow? _statusWindow;
    private AppDataDirectoryProvider? _directories;
    private JsonAppSettingsStore? _settingsStore;
    private StartupDiagnosticsRecorder? _diagnostics;
    private ServiceProvider? _serviceProvider;

    public WpfStartupRuntime(Dispatcher dispatcher, Action<MainWindow> setMainWindow)
    {
        _backgroundShutdownTimeout = BackgroundShutdownTimeout;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _setMainWindow = setMainWindow ?? throw new ArgumentNullException(nameof(setMainWindow));
        _backgroundTasks = new BackgroundTaskRegistry(this, TimeProvider.System);
    }

    internal WpfStartupRuntime(
        Dispatcher dispatcher,
        Action<MainWindow> setMainWindow,
        TimeSpan backgroundShutdownTimeout)
        : this(dispatcher, setMainWindow)
    {
        if (backgroundShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(backgroundShutdownTimeout));
        }

        _backgroundShutdownTimeout = backgroundShutdownTimeout;
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
        _diagnostics?.RecordStage(stage.ToString(), text.Status);

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
        _diagnostics = new StartupDiagnosticsRecorder(RequireDirectories());
        return Task.CompletedTask;
    }

    public Task BuildServicesAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directories = RequireDirectories();
        var settingsStore = _settingsStore
            ?? throw new InvalidOperationException("启动设置存储尚未初始化。");
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(ParseLogLevel(settings.LogLevel));
            builder.AddDebug();
        });
        services.AddSingleton<IAppDataDirectoryProvider>(directories);
        services.AddSingleton(settingsStore);
        services.AddSingleton<IAppSettingsStore>(settingsStore);
        services.AddSingleton<IProcessShutdownGate>(_shutdownGate);
        services.AddNovelSpeakerApplication(settings);
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        _serviceProvider = BuildValidatedServiceProvider(services);
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

    public Task InitializeDatabaseAsync(CancellationToken cancellationToken) =>
        RequireServices()
            .GetRequiredService<IDatabaseInitializer>()
            .InitializeAsync(cancellationToken);

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
        await CompleteShellStartupAsync(
            RunStartupCacheMaintenanceAsync,
            desktopLifecycle.StartAsync,
            RequireServices().GetRequiredService<IMediaControlCoordinator>().StartAsync,
            CloseStartupStatus,
            cancellationToken).ConfigureAwait(true);
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
        _backgroundTasks.StopAccepting();
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
                _serviceProvider.GetRequiredService<IChapterExportCoordinator>(),
                _serviceProvider.GetRequiredService<ICacheWorkspaceBackgroundTaskOwner>(),
                cancellationToken).ConfigureAwait(false);
        }

        await _backgroundTasks.WaitForCompletionAsync(
            BackgroundShutdownTimeout,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task WaitForBackgroundTasksAsync(
        IChapterExportCoordinator chapterExportCoordinator,
        ICacheWorkspaceBackgroundTaskOwner cacheWorkspaceBackgroundTaskOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chapterExportCoordinator);
        ArgumentNullException.ThrowIfNull(cacheWorkspaceBackgroundTaskOwner);

        try
        {
            await chapterExportCoordinator
                .CancelAsync(cancellationToken)
                .WaitAsync(_backgroundShutdownTimeout, TimeProvider.System, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            RecordLifecycleFailure(
                "chapter-export-shutdown",
                "等待章节导出后台任务退出超时，将继续关闭。",
                exception);
        }

        try
        {
            await cacheWorkspaceBackgroundTaskOwner
                .StopBackgroundOperationsAsync(cancellationToken)
                .WaitAsync(_backgroundShutdownTimeout, TimeProvider.System, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            RecordLifecycleFailure(
                "chapter-speech-plan-shutdown",
                "等待章节朗读清单后台任务退出超时，将继续关闭。",
                exception);
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Settings updates are atomically persisted before publication and the current
        // rolling logger writes synchronously, so neither collaborator has buffered state.
        return Task.CompletedTask;
    }

    public void RecordFailure(StartupStage stage, string safeMessage, Exception exception)
    {
        _diagnostics?.RecordFailure(stage.ToString(), safeMessage, exception);
    }

    public void RecordLifecycleFailure(string name, string safeMessage, Exception exception)
    {
        _diagnostics?.RecordFailure(name, safeMessage, exception);
    }

    public void RecordStage(string name, string safeMessage)
    {
        _diagnostics?.RecordStage(name, safeMessage);
    }

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

    public async ValueTask DisposeAsync()
    {
        CloseStartupStatus();
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync().ConfigureAwait(false);
            _serviceProvider = null;
        }

        _shutdownGate.Dispose();
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
