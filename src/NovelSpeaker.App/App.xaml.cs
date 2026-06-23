using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Infrastructure.DependencyInjection;

namespace NovelSpeaker.App;

/// <summary>
/// Configures the desktop composition root and starts the shell window.
/// </summary>
public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        _serviceProvider = services.BuildServiceProvider();

        var initializer = _serviceProvider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.Show();
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
}

