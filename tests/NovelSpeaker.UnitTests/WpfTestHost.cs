using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App;
using NovelSpeaker.Infrastructure.DependencyInjection;
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

    public static ServiceProvider BuildServiceProvider()
    {
        EnsureApplicationResources();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();
        return services.BuildServiceProvider();
    }

    public static void EnsureApplicationResources()
    {
        _ = SharedDispatcher.Value;
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

                var application = new global::NovelSpeaker.App.App();
                var initializeComponent = typeof(global::NovelSpeaker.App.App).GetMethod(
                    "InitializeComponent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                initializeComponent?.Invoke(application, null);
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
        initialized.Wait();

        if (capturedException is not null)
        {
            throw capturedException;
        }

        return dispatcher!;
    }
}
