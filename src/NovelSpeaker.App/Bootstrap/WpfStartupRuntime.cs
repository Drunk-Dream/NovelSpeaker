using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Settings;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Implements startup stages at the WPF composition boundary.
/// </summary>
internal sealed class WpfStartupRuntime : IStartupRuntime
{
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
    private StartupStatusWindow? _statusWindow;
    private LocalAppDataDirectoryProvider? _directories;
    private JsonAppSettingsStore? _settingsStore;
    private StartupDiagnosticsRecorder? _diagnostics;
    private ServiceProvider? _serviceProvider;

    public WpfStartupRuntime(Dispatcher dispatcher, Action<MainWindow> setMainWindow)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _setMainWindow = setMainWindow ?? throw new ArgumentNullException(nameof(setMainWindow));
    }

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
                _statusViewModel.StatusText = text.Status;
                _statusViewModel.DetailText = text.Detail;
            },
            DispatcherPriority.Background,
            cancellationToken);
    }

    public async Task PrepareDirectoriesAsync(CancellationToken cancellationToken)
    {
        _directories = new LocalAppDataDirectoryProvider();
        await _directories.EnsureCreatedAsync(cancellationToken).ConfigureAwait(true);
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
        services.AddNovelSpeakerApplication(settings);
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

#if DEBUG
        _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
#else
        _serviceProvider = services.BuildServiceProvider();
#endif
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
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

    public Task ShowShellAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = RequireServices().GetRequiredService<MainWindow>();
        _setMainWindow(window);
        CloseStartupStatus();
        window.Show();
        StartBackgroundCacheMaintenance(cancellationToken);
        return Task.CompletedTask;
    }

    public void RecordFailure(StartupStage stage, string safeMessage, Exception exception)
    {
        _diagnostics?.RecordFailure(stage.ToString(), safeMessage, exception);
    }

    public void ShowStartupFailure(StartupFailure failure)
    {
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
    }

    private LocalAppDataDirectoryProvider RequireDirectories() =>
        _directories ?? throw new InvalidOperationException("应用数据目录尚未初始化。");

    private ServiceProvider RequireServices() =>
        _serviceProvider ?? throw new InvalidOperationException("应用服务容器尚未初始化。");

    private void StartBackgroundCacheMaintenance(CancellationToken processToken)
    {
        var cacheWorkspace = RequireServices().GetRequiredService<ICacheWorkspaceService>();
        _ = Task.Run(async () =>
        {
            try
            {
                await cacheWorkspace.TrimToConfiguredLimitAsync(processToken).ConfigureAwait(false);
                _diagnostics?.RecordStage("audio-cache-maintenance", "后台音频缓存维护完成。");
            }
            catch (OperationCanceledException) when (processToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _diagnostics?.RecordFailure(
                    "audio-cache-maintenance-failed",
                    "后台音频缓存维护失败。",
                    exception);
            }
        });
    }

    private static LogLevel ParseLogLevel(string? value)
    {
        return Enum.TryParse<LogLevel>(value, ignoreCase: true, out var result)
            ? result
            : LogLevel.Information;
    }
}
