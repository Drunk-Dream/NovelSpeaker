using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.App;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.TestKit.Wpf;

internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(CreateDispatcher);
    private static readonly SemaphoreSlim TestGate = new(1, 1);
    private static readonly object WindowGate = new();
    private static readonly HashSet<Window> TestWindows = [];
    private static readonly HashSet<DependencyObject> DiagnosticRoots = [];
    private static readonly DependencyProperty TestWindowProperty =
        DependencyProperty.RegisterAttached(
            "IsNovelSpeakerTestWindow",
            typeof(bool),
            typeof(WpfTestHost),
            new FrameworkPropertyMetadata(false));

    public static void RunInSta(
        Action action,
        [CallerMemberName] string testMember = "",
        [CallerFilePath] string testFile = "")
    {
        ArgumentNullException.ThrowIfNull(action);

        var testName = BuildTestName(testMember, testFile);
        TestGate.Wait();
        var existingWindows = CaptureWindows();
        try
        {
            SharedDispatcher.Value.Invoke(action);
            SharedDispatcher.Value.Invoke(() => AssertNoUnexpectedVisibleWindows(existingWindows));
        }
        catch (Exception exception)
        {
            WpfFailureDiagnostics.TryWrite(testName, exception, CaptureDiagnosticRoots());
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        finally
        {
            try
            {
                CloseTestWindows(existingWindows);
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
        var existingWindows = CaptureWindows();
        try
        {
            await SharedDispatcher.Value.InvokeAsync(action).Task.Unwrap();
            SharedDispatcher.Value.Invoke(() => AssertNoUnexpectedVisibleWindows(existingWindows));
        }
        catch (Exception exception)
        {
            WpfFailureDiagnostics.TryWrite(testName, exception, CaptureDiagnosticRoots());
            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        finally
        {
            try
            {
                CloseTestWindows(existingWindows);
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
        var services = CreateServices();
        return validate
            ? WpfStartupRuntime.BuildValidatedServiceProvider(services)
            : services.BuildServiceProvider();
    }

    public static async Task<ServiceProvider> BuildInitializedServiceProviderAsync(bool validate = false)
    {
        var isolatedDataDirectory = new IsolatedTestDataDirectory();
        var services = CreateServices();
        services.RemoveAll<IAppDataDirectoryProvider>();
        services.AddSingleton(isolatedDataDirectory);
        services.AddSingleton<IAppDataDirectoryProvider>(
            new LocalAppDataDirectoryProvider(isolatedDataDirectory.Path));

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

    private static ServiceCollection CreateServices()
    {
        EnsureApplicationResources();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovelSpeakerApplication();
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();
        return services;
    }

    private static Dispatcher CreateDispatcher()
    {
        Dispatcher? dispatcher = null;
        Exception? capturedException = null;
        using var initialized = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
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

            if (capturedException is null)
            {
                Dispatcher.Run();
            }
        });

        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!initialized.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("WPF test dispatcher initialization timed out.");
        }

        if (capturedException is not null)
        {
            ExceptionDispatchInfo.Capture(capturedException).Throw();
        }

        return dispatcher!;
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
        if (IsVisibleDiagnosticsEnabled())
        {
            return;
        }

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
                             !ReferenceEquals(window, global::System.Windows.Application.Current?.MainWindow) &&
                             !IsHiddenTestWindow(window))
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
                    if (window.IsVisible)
                    {
                        window.Close();
                    }

                    window.Content = null;
                }

                foreach (var window in global::System.Windows.Application.Current?.Windows
                             .Cast<Window>()
                             .Where(window => window.IsVisible && !existingWindows.Contains(window))
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

    private static bool IsVisibleDiagnosticsEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("NOVELSPEAKER_TEST_SHOW_WINDOWS"),
            "1",
            StringComparison.Ordinal);

    private static bool IsTestWindow(Window window) =>
        window.GetValue(TestWindowProperty) is true;

    private static bool IsHiddenTestWindow(Window window) =>
        !window.ShowInTaskbar &&
        !window.ShowActivated &&
        window.WindowStartupLocation == WindowStartupLocation.Manual &&
        (window.Left + window.ActualWidth <= SystemParameters.VirtualScreenLeft ||
         window.Top + window.ActualHeight <= SystemParameters.VirtualScreenTop);

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
}
