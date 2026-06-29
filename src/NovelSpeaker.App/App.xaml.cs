using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.App.Theming;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App;

/// <summary>
/// Configures the desktop composition root and starts the shell window.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private StartupDiagnosticsRecorder? _startupDiagnostics;
    private StartupStatusViewModel? _startupStatusViewModel;
    private StartupStatusWindow? _startupStatusWindow;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var directories = new LocalAppDataDirectoryProvider();
        _startupDiagnostics = new StartupDiagnosticsRecorder(directories);
        _startupStatusViewModel = new StartupStatusViewModel();
        _startupStatusWindow = new StartupStatusWindow(_startupStatusViewModel);
        _startupStatusWindow.Show();

        try
        {
            await RunStartupAsync();
        }
        catch (Exception exception)
        {
            HandleStartupFailure(exception);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        else
        {
            _serviceProvider?.Dispose();
        }

        base.OnExit(e);
    }

    private async Task RunStartupAsync()
    {
        await ReportStartupStageAsync("startup", "正在准备启动日志目录。", "正在建立启动诊断日志。");

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        await ReportStartupStageAsync("dependency-injection", "正在创建服务容器。", "正在装配应用服务。");
        _serviceProvider = services.BuildServiceProvider();

        var directories = _serviceProvider.GetRequiredService<IAppDataDirectoryProvider>();
        _startupDiagnostics?.RecordStage("paths", $"Root={directories.RootDirectoryPath}; Database={directories.DatabasePath}; Logs={directories.LogsDirectoryPath}");

        await ReportStartupStageAsync("storage", "正在初始化应用数据目录。", "正在准备数据库和本地目录。");
        await directories.EnsureCreatedAsync(CancellationToken.None);

        await ReportStartupStageAsync("database-initialization", "正在初始化数据库。", "正在运行数据库迁移并准备默认数据。");
        var initializer = _serviceProvider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);

        await ReportStartupStageAsync("theme", "正在应用界面主题。", "正在根据设置准备浅色、深色或系统主题。");
        var themeCoordinator = _serviceProvider.GetRequiredService<AppThemeStartupCoordinator>();
        await themeCoordinator.ApplyAsync(CancellationToken.None);

        await ReportStartupStageAsync("shell", "正在创建主窗口。", "启动完成后将进入书库首页。");
        var window = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = window;
        _startupStatusWindow?.Close();
        _startupStatusWindow = null;
        window.Show();
        StartBackgroundCacheMaintenance();
    }

    private async Task ReportStartupStageAsync(string stage, string status, string detail)
    {
        _startupDiagnostics?.RecordStage(stage, status);
        if (_startupStatusViewModel is null)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            _startupStatusViewModel.StatusText = status;
            _startupStatusViewModel.DetailText = detail;
        }, DispatcherPriority.Background);
    }

    private void HandleStartupFailure(Exception exception)
    {
        _startupDiagnostics?.RecordFailure("startup-failed", "应用启动失败。", exception);
        _startupStatusWindow?.Close();
        _startupStatusWindow = null;

        var message = exception switch
        {
            Infrastructure.Persistence.IncompatibleDatabaseSchemaException incompatible => incompatible.Message,
            _ => "应用启动失败。请稍后重试，或检查本地数据目录和日志文件。"
        };

        MessageBox.Show(
            message,
            "NovelSpeaker 启动失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        Shutdown(-1);
    }

    private void StartBackgroundCacheMaintenance()
    {
        if (_serviceProvider is null)
        {
            return;
        }

        var cacheManagement = _serviceProvider.GetRequiredService<IAudioCacheManagementService>();
        _ = Task.Run(async () =>
        {
            try
            {
                await cacheManagement.RunMaintenanceAsync(CancellationToken.None);
                _startupDiagnostics?.RecordStage("audio-cache-maintenance", "后台音频缓存维护完成。");
            }
            catch (Exception exception)
            {
                _startupDiagnostics?.RecordFailure("audio-cache-maintenance-failed", "后台音频缓存维护失败。", exception);
            }
        });
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _startupDiagnostics?.RecordFailure("dispatcher-unhandled-exception", "UI 线程出现未处理异常。", e.Exception);
        MessageBox.Show(
            "应用遇到未处理错误，即将关闭。请查看日志了解更多信息。",
            "NovelSpeaker 发生错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        _startupDiagnostics?.RecordFailure(
            "appdomain-unhandled-exception",
            "后台线程出现未处理异常。",
            e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _startupDiagnostics?.RecordFailure("task-unobserved-exception", "检测到未观察的任务异常。", e.Exception);
        e.SetObserved();
    }
}

