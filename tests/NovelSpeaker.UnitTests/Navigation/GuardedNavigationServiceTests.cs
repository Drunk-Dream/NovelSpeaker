using NovelSpeaker.App.Navigation;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Navigation;

public sealed class GuardedNavigationServiceTests
{
    [Theory]
    [InlineData(NavigationOperation.GoBack, true)]
    [InlineData(NavigationOperation.PageId, true)]
    [InlineData(NavigationOperation.Hierarchy, true)]
    [InlineData(NavigationOperation.GoBack, false)]
    [InlineData(NavigationOperation.PageId, false)]
    [InlineData(NavigationOperation.Hierarchy, false)]
    public async Task Navigation_operations_confirm_the_active_guard_before_navigating(
        NavigationOperation operation,
        bool allowNavigation)
    {
        var guard = new ConfigurableNavigationGuardService(allowNavigation);
        var inner = new RecordingNavigationService();
        var service = new GuardedNavigationService(guard, inner);

        var result = operation switch
        {
            NavigationOperation.GoBack => await service.GoBackAsync(CancellationToken.None),
            NavigationOperation.PageId => await service.NavigateAsync("settings", CancellationToken.None),
            NavigationOperation.Hierarchy => await service.NavigateWithHierarchyAsync(
                typeof(System.Windows.Controls.Page),
                null,
                CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        Assert.Equal(allowNavigation, result);
        Assert.Equal(1, guard.ConfirmationCount);
        Assert.Equal(allowNavigation ? 1 : 0, inner.NavigationCount);
        Assert.False(service.IsBypassingGuard);
    }

    public enum NavigationOperation
    {
        GoBack,
        PageId,
        Hierarchy
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

        public bool GoBack()
        {
            NavigationCount++;
            return true;
        }

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
