using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class GuardedNavigationServiceTests
{
    [Fact]
    public async Task New_guard_registration_replaces_old_page_and_old_disposal_keeps_new_guard_active()
    {
        var service = new NavigationGuardService();
        var oldCallCount = 0;
        var newCallCount = 0;
        var oldRegistration = service.Register(_ =>
        {
            oldCallCount++;
            return Task.FromResult(false);
        });
        using var newRegistration = service.Register(_ =>
        {
            newCallCount++;
            return Task.FromResult(false);
        });

        oldRegistration.Dispose();
        var canLeave = await service.ConfirmNavigationAsync(CancellationToken.None);

        Assert.False(canLeave);
        Assert.Equal(0, oldCallCount);
        Assert.Equal(1, newCallCount);
    }

    [Fact]
    public async Task Disposing_active_registration_removes_page_guard()
    {
        var service = new NavigationGuardService();
        var registration = service.Register(_ => Task.FromResult(false));

        registration.Dispose();

        Assert.True(await service.ConfirmNavigationAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Navigation_operations_confirm_the_active_guard_before_navigating()
    {
        foreach (var (operation, allowNavigation) in new[]
                 {
                     (NavigationOperation.NavigateBack, true),
                     (NavigationOperation.Route, true),
                     (NavigationOperation.NavigateBack, false),
                     (NavigationOperation.Route, false)
                 })
        {
            var guard = new ConfigurableNavigationGuardService(allowNavigation);
            var inner = new RecordingNavigationService();
            var service = new ShellNavigationAdapter(guard, inner);

            if (operation == NavigationOperation.NavigateBack)
            {
                Assert.True(await service.NavigateAsync(
                    AppRoutes.PlaybackSettings,
                    CancellationToken.None,
                    bypassGuard: true));
            }

            var result = operation switch
            {
                NavigationOperation.NavigateBack => await service.NavigateBackAsync(CancellationToken.None),
                NavigationOperation.Route => await service.NavigateAsync(
                    AppRoutes.Settings,
                    CancellationToken.None),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };

            Assert.Equal(allowNavigation, result);
            Assert.Equal(1, guard.ConfirmationCount);
            Assert.Equal(
                operation == NavigationOperation.NavigateBack
                    ? allowNavigation ? 2 : 1
                    : allowNavigation ? 1 : 0,
                inner.NavigationCount);
            Assert.False(service.IsBypassingGuard);
        }
    }

    public enum NavigationOperation
    {
        NavigateBack,
        Route
    }

    private sealed class ConfigurableNavigationGuardService : INavigationGuardService
    {
        private readonly bool _allowNavigation;

        public ConfigurableNavigationGuardService(bool allowNavigation)
        {
            _allowNavigation = allowNavigation;
        }

        public int ConfirmationCount { get; private set; }

        public IDisposable Register(Func<CancellationToken, Task<bool>> guard) => throw new NotSupportedException();

        public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken)
        {
            ConfirmationCount++;
            return Task.FromResult(_allowNavigation);
        }
    }

    private sealed class RecordingNavigationService : INavigationService
    {
        public int NavigationCount { get; private set; }

        public INavigationView GetNavigationControl() => throw new NotSupportedException();

        public bool GoBack() => throw new InvalidOperationException(
            "Application navigation must not use Wpf.Ui history.");

        public bool Navigate(Type pageType) => Record();
        public bool Navigate(Type pageType, object? dataContext) => Record();
        public bool Navigate(string pageIdOrTargetTag) => Record();
        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => Record();
        public bool NavigateWithHierarchy(Type pageType) => Record();
        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => Record();
        public void SetNavigationControl(INavigationView navigation) { }

        private bool Record()
        {
            NavigationCount++;
            return true;
        }
    }
}
