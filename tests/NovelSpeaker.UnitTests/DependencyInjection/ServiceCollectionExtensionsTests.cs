using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Infrastructure.DependencyInjection;
using Xunit;

namespace NovelSpeaker.UnitTests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNovelSpeakerInfrastructure_registers_core_services()
    {
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddNovelSpeakerInfrastructure();
                services.AddNovelSpeakerDesktop();

                using var provider = services.BuildServiceProvider();

                Assert.IsType<MainWindowViewModel>(provider.GetRequiredService<MainWindowViewModel>());
                Assert.IsType<ChapterRulesViewModel>(provider.GetRequiredService<ChapterRulesViewModel>());
                Assert.IsAssignableFrom<IAppDataDirectoryProvider>(provider.GetRequiredService<IAppDataDirectoryProvider>());
                Assert.IsAssignableFrom<IDatabaseInitializer>(provider.GetRequiredService<IDatabaseInitializer>());
                Assert.IsAssignableFrom<IChapterRuleRepository>(provider.GetRequiredService<IChapterRuleRepository>());
                Assert.IsType<MainWindow>(provider.GetRequiredService<MainWindow>());
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(capturedException);
    }
}
