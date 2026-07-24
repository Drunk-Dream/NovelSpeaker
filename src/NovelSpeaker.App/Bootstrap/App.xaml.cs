using System.Windows;
using System.Windows.Threading;
using NovelSpeaker.App.Shell.Input;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Bridges WPF process events to the bootstrap coordinator.
/// </summary>
public partial class App : System.Windows.Application
{
    private StartupCoordinator? _startupCoordinator;

    public App()
    {
        MouseWheelScrollBehavior.EnableApplicationWideHandling();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var runtime = new WpfStartupRuntime(Dispatcher, window => MainWindow = window);
        _startupCoordinator = new StartupCoordinator(runtime);
        runtime.ShutdownRequestedAsync = _startupCoordinator.ShutdownAsync;
        try
        {
            var result = await _startupCoordinator.StartAsync();
            if (!result.IsSuccessful)
            {
                Shutdown(result.IsCancelled ? 0 : -1);
            }
        }
        catch (OperationCanceledException) when (_startupCoordinator.ProcessToken.IsCancellationRequested)
        {
            Shutdown(0);
        }
        catch (Exception exception)
        {
            _startupCoordinator.RecordUnhandledFailure(
                "startup-bridge-failed",
                "启动事件桥接出现未处理异常。",
                exception);
            MessageBox.Show(
                "应用启动失败。请稍后重试。",
                "NovelSpeaker 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var shutdownTask = _startupCoordinator?.DisposeAsync().AsTask();
        if (shutdownTask is not null && !shutdownTask.Wait(TimeSpan.FromSeconds(5)))
        {
            _startupCoordinator?.RecordUnhandledFailure(
                "exit-bridge-timeout",
                "同步退出桥接超时，将由进程结束回收剩余资源。",
                null);
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _startupCoordinator?.RecordUnhandledFailure(
            "dispatcher-unhandled-exception",
            "UI 线程出现未处理异常。",
            e.Exception);
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
        _startupCoordinator?.RecordUnhandledFailure(
            "appdomain-unhandled-exception",
            "后台线程出现未处理异常。",
            e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _startupCoordinator?.RecordUnhandledFailure(
            "task-unobserved-exception",
            "检测到未观察的任务异常。",
            e.Exception);
        e.SetObserved();
    }
}

