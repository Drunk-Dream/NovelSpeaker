using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.App;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.TestKit.Wpf;

internal static class WpfTestHost
{
    internal const string AllowVisibleWindowsEnvironmentVariable =
        "NOVELSPEAKER_TEST_ALLOW_VISIBLE_WINDOWS";

    private static readonly bool AllowVisibleWindows = IsVisibleWindowsAllowedValue(
        Environment.GetEnvironmentVariable(AllowVisibleWindowsEnvironmentVariable));
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(CreateDispatcher);
    private static readonly SemaphoreSlim TestGate = new(1, 1);
    private static readonly object WindowGate = new();
    private static readonly HashSet<Window> TestWindows = [];
    private static readonly HashSet<DependencyObject> DiagnosticRoots = [];
    private static readonly TimeSpan DispatcherShutdownTimeout = TimeSpan.FromSeconds(5);
    private static WindowsTestDesktopThread? _dispatcherThread;
    private static WindowsTestDesktop? _desktop;
    private static WindowsTestDesktopInfo? _desktopInfo;
    private static InitializationSignal? _initializationSignal;
    private static int _initializationTimeoutRequested;
    private static readonly DependencyProperty TestWindowProperty =
        DependencyProperty.RegisterAttached(
            "IsNovelSpeakerTestWindow",
            typeof(bool),
            typeof(WpfTestHost),
            new FrameworkPropertyMetadata(false));

    internal static WindowsTestDesktopInfo CurrentDesktop
    {
        get
        {
            _ = SharedDispatcher.Value;
            return _desktopInfo ?? throw new InvalidOperationException(
                "WPF test Desktop information was not initialized.");
        }
    }

    internal static bool IsVisibleWindowsAllowed => AllowVisibleWindows;

    internal static int TrackedWindowCount
    {
        get
        {
            lock (WindowGate)
            {
                return TestWindows.Count;
            }
        }
    }

    public static void RunInSta(
        Action action,
        [CallerMemberName] string testMember = "",
        [CallerFilePath] string testFile = "")
    {
        ArgumentNullException.ThrowIfNull(action);

        var testName = BuildTestName(testMember, testFile);
        TestGate.Wait();
        HashSet<Window>? existingWindows = null;
        try
        {
            existingWindows = CaptureWindows();
            SharedDispatcher.Value.Invoke(action);
            SharedDispatcher.Value.Invoke(() => AssertNoUnexpectedVisibleWindows(existingWindows));
        }
        catch (Exception exception)
        {
            TryWriteFailureDiagnostics(testName, exception);
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        finally
        {
            try
            {
                if (existingWindows is not null)
                {
                    CloseTestWindows(existingWindows);
                }
            }
            finally
            {
                TestGate.Release();
            }
        }
    }

    public static async Task RunInStaAsync(
        Func<Task> action,
        [CallerMemberName] string testMember = "",
        [CallerFilePath] string testFile = "")
    {
        ArgumentNullException.ThrowIfNull(action);

        var testName = BuildTestName(testMember, testFile);
        await TestGate.WaitAsync();
        HashSet<Window>? existingWindows = null;
        try
        {
            existingWindows = CaptureWindows();
            await SharedDispatcher.Value.InvokeAsync(action).Task.Unwrap();
            SharedDispatcher.Value.Invoke(() => AssertNoUnexpectedVisibleWindows(existingWindows));
        }
        catch (Exception exception)
        {
            TryWriteFailureDiagnostics(testName, exception);
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        finally
        {
            try
            {
                if (existingWindows is not null)
                {
                    CloseTestWindows(existingWindows);
                }
            }
            finally
            {
                TestGate.Release();
            }
        }
    }

    public static Task DrainDispatcherAsync(
        DispatcherPriority priority = DispatcherPriority.ApplicationIdle)
    {
        return SharedDispatcher.Value.InvokeAsync(static () => { }, priority).Task;
    }

    public static ServiceProvider BuildServiceProvider(bool validate = false)
    {
        var isolatedDataDirectory = new IsolatedTestDataDirectory();
        try
        {
            var services = CreateServices(isolatedDataDirectory);
            return validate
                ? WpfStartupRuntime.BuildValidatedServiceProvider(services)
                : services.BuildServiceProvider();
        }
        catch
        {
            isolatedDataDirectory.Dispose();
            throw;
        }
    }

    public static async Task<ServiceProvider> BuildInitializedServiceProviderAsync(bool validate = false)
    {
        var isolatedDataDirectory = new IsolatedTestDataDirectory();
        var services = CreateServices(isolatedDataDirectory);

        var provider = validate
            ? WpfStartupRuntime.BuildValidatedServiceProvider(services)
            : services.BuildServiceProvider();

        try
        {
            await provider
                .GetRequiredService<IDatabaseInitializer>()
                .InitializeAsync(CancellationToken.None);
            return provider;
        }
        catch
        {
            await provider.DisposeAsync();
            isolatedDataDirectory.Dispose();
            throw;
        }
    }

    public static void EnsureApplicationResources()
    {
        _ = SharedDispatcher.Value;
    }

    internal static void RegisterWindow(Window window)
    {
        window.SetValue(TestWindowProperty, true);
        lock (WindowGate)
        {
            TestWindows.Add(window);
        }
    }

    internal static void RegisterDiagnosticRoot(DependencyObject root)
    {
        lock (WindowGate)
        {
            DiagnosticRoots.Add(root);
        }
    }

    internal static bool IsVisibleWindowsAllowedValue(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal);

    internal static void RequestDispatcherShutdown(Dispatcher dispatcher, Action shutdownApplication)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(shutdownApplication);

        // CancellationToken.None is intentional: this is final test-host cleanup;
        // the synchronous dispatcher request itself remains explicitly time-bound.
        dispatcher.Invoke(
            () =>
            {
                Exception? shutdownException = null;
                try
                {
                    shutdownApplication();
                }
                catch (Exception exception)
                {
                    shutdownException = exception;
                }
                finally
                {
                    try
                    {
                        CaptureException(ref shutdownException, Dispatcher.ExitAllFrames);
                    }
                    finally
                    {
                        try
                        {
                            CaptureException(ref shutdownException, dispatcher.InvokeShutdown);
                        }
                        finally
                        {
                            CaptureException(ref shutdownException, () => PostQuitMessage(0));
                        }
                    }
                }

                if (shutdownException is not null)
                {
                    ExceptionDispatchInfo.Capture(shutdownException).Throw();
                }
            },
            DispatcherPriority.Send,
            CancellationToken.None,
            DispatcherShutdownTimeout);
    }

    internal static void Shutdown()
    {
        Dispatcher? dispatcher = null;
        if (SharedDispatcher.IsValueCreated)
        {
            try
            {
                dispatcher = SharedDispatcher.Value;
            }
            catch
            {
                // The Lazy may have failed while the native cleanup owners were
                // already registered. Continue with those owners below.
            }
        }

        if (dispatcher is not null && !dispatcher.HasShutdownFinished && dispatcher.CheckAccess())
        {
            throw new InvalidOperationException(
                "WPF test host shutdown must be requested from outside the dispatcher thread.");
        }

        Exception? shutdownException = null;
        if (dispatcher is not null && !dispatcher.HasShutdownFinished)
        {
            CaptureException(
                ref shutdownException,
                () => RequestDispatcherShutdown(
                    dispatcher,
                    () => global::System.Windows.Application.Current?.Shutdown()));
        }

        CaptureException(
            ref shutdownException,
            () =>
            {
                if (_dispatcherThread is not null)
                {
                    CompleteDispatcherThreadShutdown();
                }
            });
        CaptureException(
            ref shutdownException,
            () =>
            {
                if (_dispatcherThread is null && _desktop is not null)
                {
                    ReleaseRetainedDesktopHandle();
                }
            });
        CaptureException(ref shutdownException, WindowsTestDesktop.RetryPendingCleanup);
        CaptureException(
            ref shutdownException,
            () =>
            {
                if (_dispatcherThread is null && _initializationSignal is not null)
                {
                    ReleaseInitializationSignal(_initializationSignal);
                }
            });

        if (shutdownException is not null)
        {
            ExceptionDispatchInfo.Capture(shutdownException).Throw();
        }
    }

    private static ServiceCollection CreateServices(IsolatedTestDataDirectory isolatedDataDirectory)
    {
        EnsureApplicationResources();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(isolatedDataDirectory);
        services.AddSingleton<IAppDataDirectoryProvider>(
            new AppDataDirectoryProvider(isolatedDataDirectory.Path));
        services.AddNovelSpeakerApplication();
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();
        return services;
    }

    private static Dispatcher CreateDispatcher()
    {
        Dispatcher? dispatcher = null;
        WindowsTestDesktop? desktop = null;
        Exception? capturedException = null;
        var initialized = new InitializationSignal();
        _initializationSignal = initialized;

        WindowsTestDesktopThread thread;
        try
        {
            thread = WindowsTestDesktopThread.Start(() =>
            {
                try
                {
                    desktop = WindowsTestDesktop.Attach(AllowVisibleWindows);
                    _desktop = desktop;
                    _desktopInfo = desktop.Info;
                    desktop.InitializeSta();
                    dispatcher = Dispatcher.CurrentDispatcher;

                    var application = new global::NovelSpeaker.App.Bootstrap.App();
                    var initializeComponent = typeof(global::NovelSpeaker.App.Bootstrap.App).GetMethod(
                        "InitializeComponent",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    initializeComponent?.Invoke(application, null);
                    application.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                }
                catch (Exception exception)
                {
                    capturedException = exception;
                }
                finally
                {
                    initialized.Set();
                }

                if (capturedException is null && Volatile.Read(ref _initializationTimeoutRequested) == 0)
                {
                    try
                    {
                        Dispatcher.Run();
                    }
                    finally
                    {
                        desktop!.PrepareThreadShutdown();
                    }
                }
                else
                {
                    desktop?.PrepareThreadShutdown();
                }
            });
        }
        catch
        {
            ReleaseInitializationSignal(initialized);
            throw;
        }
        _dispatcherThread = thread;

        if (!initialized.Wait(TimeSpan.FromSeconds(5)))
        {
            Volatile.Write(ref _initializationTimeoutRequested, 1);
            Exception? shutdownRequestException = null;
            CaptureException(
                ref shutdownRequestException,
                () => Volatile.Read(ref dispatcher)?.BeginInvokeShutdown(DispatcherPriority.Send));
            if (!thread.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                var timeoutException = new TimeoutException(
                    "WPF test dispatcher initialization timed out and its native thread did not exit.");
                throw shutdownRequestException is null
                    ? timeoutException
                    : new AggregateException(timeoutException, shutdownRequestException);
            }

            Exception? cleanupException = null;
            try
            {
                CompleteExitedThread(thread, desktop, initialized);
            }
            catch (Exception exception)
            {
                cleanupException = exception;
            }
            finally
            {
                CaptureException(
                    ref cleanupException,
                    () => ReleaseInitializationSignal(initialized));
            }

            if (cleanupException is not null)
            {
                shutdownRequestException = shutdownRequestException is null
                    ? cleanupException
                    : new AggregateException(shutdownRequestException, cleanupException);
            }

            var initializationTimeout = new TimeoutException(
                "WPF test dispatcher initialization timed out.");
            throw shutdownRequestException is null
                ? initializationTimeout
                : new AggregateException(initializationTimeout, shutdownRequestException);
        }

        if (capturedException is not null)
        {
            if (!thread.WaitForExit(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException(
                    "WPF test dispatcher initialization failed and its native thread did not exit.");
            }

            Exception? cleanupException = null;
            try
            {
                CompleteExitedThread(thread, desktop, initialized);
            }
            catch (Exception exception)
            {
                cleanupException = exception;
            }
            finally
            {
                CaptureException(
                    ref cleanupException,
                    () => ReleaseInitializationSignal(initialized));
            }
            if (cleanupException is not null)
            {
                throw new AggregateException(
                    "WPF test dispatcher initialization failed and cleanup also failed.",
                    capturedException,
                    cleanupException);
            }

            ExceptionDispatchInfo.Capture(capturedException).Throw();
        }

        ReleaseInitializationSignal(initialized);
        return dispatcher!;
    }

    private static void CompleteDispatcherThreadShutdown()
    {
        var thread = _dispatcherThread ?? throw new InvalidOperationException(
            "WPF test dispatcher thread was not registered.");
        if (!thread.WaitForExit(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("WPF test dispatcher thread did not exit during cleanup.");
        }

        CompleteExitedThread(thread, _desktop, _initializationSignal);
    }

    private static void CompleteExitedThread(
        WindowsTestDesktopThread thread,
        WindowsTestDesktop? desktop,
        InitializationSignal? initializationSignal)
    {
        Exception? cleanupException = null;
        try
        {
            desktop?.ReleaseDesktopHandle();
            WindowsTestDesktop.RetryPendingCleanup();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        try
        {
            thread.Dispose();
        }
        catch (Exception exception)
        {
            cleanupException = cleanupException is null
                ? exception
                : new AggregateException(cleanupException, exception);
        }

        try
        {
            if (initializationSignal is not null)
            {
                ReleaseInitializationSignal(initializationSignal);
            }
        }
        catch (Exception exception)
        {
            cleanupException = cleanupException is null
                ? exception
                : new AggregateException(cleanupException, exception);
        }

        if (thread.IsDisposed && ReferenceEquals(_dispatcherThread, thread))
        {
            _dispatcherThread = null;
        }

        if (desktop?.IsDesktopHandleReleased == true && ReferenceEquals(_desktop, desktop))
        {
            _desktop = null;
            _desktopInfo = null;
        }

        if (cleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
        }
    }

    private static void ReleaseInitializationSignal(InitializationSignal signal)
    {
        signal.Dispose();
        if (ReferenceEquals(_initializationSignal, signal))
        {
            _initializationSignal = null;
        }
    }

    private static void CaptureException(ref Exception? capturedException, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            capturedException = capturedException is null
                ? exception
                : new AggregateException(capturedException, exception);
        }
    }

    private static void ReleaseRetainedDesktopHandle()
    {
        var desktop = _desktop ?? throw new InvalidOperationException(
            "WPF test Desktop cleanup was requested without a Desktop owner.");
        desktop.ReleaseDesktopHandle();
        if (desktop.IsDesktopHandleReleased && ReferenceEquals(_desktop, desktop))
        {
            _desktop = null;
            _desktopInfo = null;
        }
    }

    private static HashSet<Window> CaptureWindows()
    {
        return SharedDispatcher.Value.Invoke(() =>
            global::System.Windows.Application.Current?.Windows.Cast<Window>().ToHashSet() ?? []);
    }

    private static IReadOnlyList<DependencyObject> CaptureDiagnosticRoots()
    {
        return SharedDispatcher.Value.Invoke(() =>
        {
            lock (WindowGate)
            {
                return DiagnosticRoots
                    .Where(root => root is not Window window || window.IsVisible)
                    .ToArray();
            }
        });
    }

    private static void AssertNoUnexpectedVisibleWindows(HashSet<Window> existingWindows)
    {
        Window[] trackedWindows;
        lock (WindowGate)
        {
            trackedWindows = TestWindows.ToArray();
        }

        var unexpected = global::System.Windows.Application.Current?.Windows
            .Cast<Window>()
            .Where(window => window.IsVisible &&
                             !existingWindows.Contains(window) &&
                             !trackedWindows.Contains(window) &&
                             !IsTestWindow(window) &&
                             !ReferenceEquals(window, global::System.Windows.Application.Current?.MainWindow))
            .ToArray() ?? [];
        if (unexpected.Length > 0)
        {
            throw new InvalidOperationException(
                $"WPF test left visible window(s): {string.Join(", ", unexpected.Select(window => window.GetType().Name))}");
        }
    }

    private static void CloseTestWindows(HashSet<Window> existingWindows)
    {
        SharedDispatcher.Value.Invoke(() =>
        {
            Window[] windows;
            lock (WindowGate)
            {
                windows = TestWindows.ToArray();
                TestWindows.Clear();
            }

            try
            {
                foreach (var window in windows)
                {
                    if (window.IsVisible || window.IsLoaded)
                    {
                        window.Close();
                    }

                    window.Content = null;
                }

                foreach (var window in global::System.Windows.Application.Current?.Windows
                             .Cast<Window>()
                             .Where(window =>
                                 (window.IsVisible || window.IsLoaded) &&
                                 !existingWindows.Contains(window))
                             .ToArray() ?? [])
                {
                    window.Close();
                }
            }
            finally
            {
                lock (WindowGate)
                {
                    DiagnosticRoots.Clear();
                }
            }
        });
    }

    private static bool IsTestWindow(Window window) =>
        window.GetValue(TestWindowProperty) is true;

    private static void TryWriteFailureDiagnostics(string testName, Exception exception)
    {
        try
        {
            var roots = SharedDispatcher.IsValueCreated
                ? CaptureDiagnosticRoots()
                : [];
            WpfFailureDiagnostics.TryWrite(testName, exception, roots);
        }
        catch
        {
            // A Desktop initialization failure has no WPF tree to diagnose.
        }
    }

    private static string BuildTestName(string testMember, string testFile) =>
        $"{Path.GetFileNameWithoutExtension(testFile)}.{testMember}";

    private sealed class IsolatedTestDataDirectory : IDisposable
    {
        public IsolatedTestDataDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "NovelSpeakerWpfTests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class InitializationSignal : IDisposable
    {
        private readonly ManualResetEventSlim _event = new();

        public bool Wait(TimeSpan timeout) => _event.Wait(timeout);

        public void Set() => _event.Set();

        public void Dispose() => _event.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int exitCode);
}
