using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Navigation;

[Collection("WpfDispatcher")]
public sealed class RulePageNavigationGuardTests
{
    [Theory]
    [InlineData(RulePageKind.Tts)]
    [InlineData(RulePageKind.Chapter)]
    [InlineData(RulePageKind.RegexReplacement)]
    public async Task Rule_page_activation_registers_current_view_model_and_leave_unregisters_it(
        RulePageKind pageKind)
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            await using var provider = WpfTestHost.BuildServiceProvider();
            var guard = new RecordingNavigationGuardService();
            var (page, viewModel) = CreatePage(pageKind, provider, guard);

            await OnNavigatedToAsync(pageKind, page);

            Assert.Equal(1, guard.RegistrationCount);
            Assert.True(await guard.ConfirmNavigationAsync(CancellationToken.None));
            Assert.Same(viewModel, guard.LastInvokedGuard?.Target);
            Assert.Equal("ConfirmLeaveAsync", guard.LastInvokedGuard?.Method.Name);

            var oldRegistration = guard.Registrations.Single();
            await OnNavigatedFromAsync(pageKind, page);
            Assert.True(await guard.ConfirmNavigationAsync(CancellationToken.None));

            await OnNavigatedToAsync(pageKind, page);
            Assert.Equal(2, guard.RegistrationCount);
            oldRegistration.Dispose();

            Assert.True(await guard.ConfirmNavigationAsync(CancellationToken.None));
            Assert.Same(viewModel, guard.LastInvokedGuard?.Target);

            await OnNavigatedFromAsync(pageKind, page);

            Assert.True(await guard.ConfirmNavigationAsync(CancellationToken.None));
            Assert.Null(guard.CurrentGuard);
        });
    }

    public enum RulePageKind
    {
        Tts,
        Chapter,
        RegexReplacement
    }

    private static (Page Page, object ViewModel) CreatePage(
        RulePageKind pageKind,
        IServiceProvider provider,
        INavigationGuardService guard)
    {
        return pageKind switch
        {
            RulePageKind.Tts => CreateTtsPage(provider, guard),
            RulePageKind.Chapter => CreateChapterPage(provider, guard),
            RulePageKind.RegexReplacement => CreateRegexReplacementPage(provider, guard),
            _ => throw new ArgumentOutOfRangeException(nameof(pageKind))
        };
    }

    private static (Page, object) CreateTtsPage(IServiceProvider provider, INavigationGuardService guard)
    {
        var viewModel = provider.GetRequiredService<TtsRulesViewModel>();
        return (
            new TtsRulesPage(
                viewModel,
                guard,
                provider.GetRequiredService<IPresentationFileDialogService>(),
                provider.GetRequiredService<IPresentationClipboard>(),
                provider.GetRequiredService<PageEventOperationRunner>()),
            viewModel);
    }

    private static (Page, object) CreateChapterPage(IServiceProvider provider, INavigationGuardService guard)
    {
        var viewModel = provider.GetRequiredService<ChapterRulesViewModel>();
        return (
            new ChapterRulesPage(
                viewModel,
                guard,
                provider.GetRequiredService<PageEventOperationRunner>()),
            viewModel);
    }

    private static (Page, object) CreateRegexReplacementPage(IServiceProvider provider, INavigationGuardService guard)
    {
        var viewModel = provider.GetRequiredService<RegexReplacementRulesViewModel>();
        return (
            new RegexReplacementRulesPage(
                viewModel,
                guard,
                provider.GetRequiredService<PageEventOperationRunner>()),
            viewModel);
    }

    private static Task OnNavigatedFromAsync(RulePageKind pageKind, Page page)
    {
        return pageKind switch
        {
            RulePageKind.Tts => ((TtsRulesPage)page).OnNavigatedFromAsync(),
            RulePageKind.Chapter => ((ChapterRulesPage)page).OnNavigatedFromAsync(),
            RulePageKind.RegexReplacement => ((RegexReplacementRulesPage)page).OnNavigatedFromAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(pageKind))
        };
    }

    private static Task OnNavigatedToAsync(RulePageKind pageKind, Page page)
    {
        return pageKind switch
        {
            RulePageKind.Tts => ((TtsRulesPage)page).OnNavigatedToAsync(),
            RulePageKind.Chapter => ((ChapterRulesPage)page).OnNavigatedToAsync(),
            RulePageKind.RegexReplacement => ((RegexReplacementRulesPage)page).OnNavigatedToAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(pageKind))
        };
    }

    private static void RaiseLoaded(Page page)
    {
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
    }

    private static void RaiseUnloaded(Page page)
    {
        page.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
    }

    private sealed class RecordingNavigationGuardService : INavigationGuardService
    {
        private Func<CancellationToken, Task<bool>>? _currentGuard;

        public int RegistrationCount => Registrations.Count;

        public List<IDisposable> Registrations { get; } = [];

        public Func<CancellationToken, Task<bool>>? CurrentGuard => _currentGuard;

        public Func<CancellationToken, Task<bool>>? LastInvokedGuard { get; private set; }

        public IDisposable Register(Func<CancellationToken, Task<bool>> guard)
        {
            _currentGuard = guard;
            var registration = new Registration(this, guard);
            Registrations.Add(registration);
            return registration;
        }

        public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken)
        {
            LastInvokedGuard = _currentGuard;
            return _currentGuard?.Invoke(cancellationToken) ?? Task.FromResult(true);
        }

        private void Unregister(Func<CancellationToken, Task<bool>> guard)
        {
            if (ReferenceEquals(_currentGuard, guard))
            {
                _currentGuard = null;
            }
        }

        private sealed class Registration : IDisposable
        {
            private readonly RecordingNavigationGuardService _owner;
            private readonly Func<CancellationToken, Task<bool>> _guard;
            private bool _isDisposed;

            public Registration(
                RecordingNavigationGuardService owner,
                Func<CancellationToken, Task<bool>> guard)
            {
                _owner = owner;
                _guard = guard;
            }

            public void Dispose()
            {
                if (_isDisposed)
                {
                    return;
                }

                _isDisposed = true;
                _owner.Unregister(_guard);
            }
        }
    }
}
