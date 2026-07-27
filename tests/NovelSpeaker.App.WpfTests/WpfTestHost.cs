using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.App;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;
using System.Reflection;
using System.Windows.Threading;
using Xunit;

namespace NovelSpeaker.UnitTests;

internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(CreateDispatcher);

    public static void RunInSta(Action action)
    {
        Exception? capturedException = null;

        SharedDispatcher.Value.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        Assert.Null(capturedException);
    }

    public static async Task RunInStaAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = SharedDispatcher.Value.InvokeAsync(async () =>
        {
            try
            {
                await action();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });

        await completion.Task;
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
            throw;
        }
    }

    public static void EnsureApplicationResources()
    {
        _ = SharedDispatcher.Value;
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
                dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;

                var application = new global::NovelSpeaker.App.Bootstrap.App();
                var initializeComponent = typeof(global::NovelSpeaker.App.Bootstrap.App).GetMethod(
                    "InitializeComponent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                initializeComponent?.Invoke(application, null);
                application.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
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
                System.Windows.Threading.Dispatcher.Run();
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
            throw capturedException;
        }

        return dispatcher!;
    }

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
            if (System.IO.Directory.Exists(Path))
            {
                System.IO.Directory.Delete(Path, recursive: true);
            }
        }
    }
}
