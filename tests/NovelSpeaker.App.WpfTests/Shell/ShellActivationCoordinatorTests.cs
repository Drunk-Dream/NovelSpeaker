using System.Windows;
using System.Runtime.CompilerServices;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Shell;

[Collection("WpfDispatcher")]
public sealed class ShellActivationCoordinatorTests
{
    [Fact]
    public void Repeated_activation_configures_process_infrastructure_and_navigation_once()
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigation = new RecordingNavigationAdapter();
            var platform = new RecordingPlatformAdapter();
            using var coordinator = CreateCoordinator(navigation, platform);
            var host = CreateHost();

            coordinator.ActivateAsync(host, 1280).GetAwaiter().GetResult();
            coordinator.ActivateAsync(host, 1000).GetAwaiter().GetResult();

            Assert.Equal(1, platform.InfrastructureConfigurationCount);
            Assert.Equal(1, platform.NavigationInitializationCount);
            Assert.Equal(2, platform.PresenterConfigurationCount);
            Assert.Equal(1, navigation.NavigateCount);
            Assert.Equal(AppRouteId.Library, navigation.LastRoute?.Id);
            Assert.True(navigation.LastBypassGuard);
        });
    }

    [Fact]
    public void Navigated_event_updates_selection_before_projecting_player_context()
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigation = new RecordingNavigationAdapter
            {
                CurrentRoute = new PlayerRoute("book-1")
            };
            using var coordinator = CreateCoordinator(navigation, new RecordingPlatformAdapter());

            coordinator.HandleNavigated(EventArgs.Empty);

            Assert.Equal(1, navigation.SynchronizeSelectionCount);
            Assert.True(coordinator.IsPlayerPageActive);
        });
    }

    [Fact]
    public async Task Shell_navigation_request_is_cancelled_and_forwarded_unless_adapter_is_bypassing()
    {
        var navigation = new RecordingNavigationAdapter();
        using var coordinator = CreateCoordinator(navigation, new RecordingPlatformAdapter());
        var eventArgs = CreateNavigatingEventArgs();
        using var cancellation = new CancellationTokenSource();

        await coordinator.HandleNavigationRequestAsync(
            eventArgs,
            cancellation.Token);

        Assert.True(eventArgs.Cancel);
        Assert.Equal(1, navigation.NavigateFromShellCount);
        Assert.Equal(cancellation.Token, navigation.LastNavigationRequestToken);

        navigation.IsBypassingGuard = true;
        var bypassedEventArgs = CreateNavigatingEventArgs();
        await coordinator.HandleNavigationRequestAsync(
            bypassedEventArgs,
            CancellationToken.None);

        Assert.False(bypassedEventArgs.Cancel);
        Assert.Equal(1, navigation.NavigateFromShellCount);
    }

    [Fact]
    public async Task Activation_rejects_initial_navigation_result_that_arrives_after_disposal()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var navigationCompletion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var navigation = new RecordingNavigationAdapter
            {
                NavigationResult = navigationCompletion.Task
            };
            var layout = new RecordingLayoutController();
            var coordinator = CreateCoordinator(navigation, new RecordingPlatformAdapter(), layout);

            var activationTask = coordinator.ActivateAsync(CreateHost(), 1280);
            coordinator.Dispose();
            navigationCompletion.SetResult(true);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => activationTask);
            Assert.Equal(0, layout.UpdateCount);
        });
    }

    private static ShellActivationCoordinator CreateCoordinator(
        IShellNavigationAdapter navigation,
        IShellPlatformAdapter platform,
        IShellLayoutController? layout = null)
    {
        return new ShellActivationCoordinator(
            layout ?? new ShellLayoutController(),
            navigation,
            platform,
            new ProcessShutdownGate());
    }

    private sealed class ProcessShutdownGate : IProcessShutdownGate
    {
        private CancellationTokenSource _cancellation = new();

        public bool IsShutdownRequested { get; private set; }

        public CancellationToken ShutdownToken => _cancellation.Token;

        public bool TryBeginShutdown()
        {
            if (IsShutdownRequested)
            {
                return false;
            }

            IsShutdownRequested = true;
            _cancellation.Cancel();
            return true;
        }

        public void CancelShutdownRequest()
        {
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
            IsShutdownRequested = false;
        }
    }

    private static NavigatingCancelEventArgs CreateNavigatingEventArgs()
    {
        return (NavigatingCancelEventArgs)RuntimeHelpers.GetUninitializedObject(
            typeof(NavigatingCancelEventArgs));
    }

    private static ShellHostElements CreateHost()
    {
        return new ShellHostElements(
            new Window(),
            new NavigationView(),
            new NavigationViewItem(),
            new NavigationViewItem(),
            new NavigationViewItem(),
            new ContentDialogHost(),
            new SnackbarPresenter());
    }

    private sealed class RecordingPlatformAdapter : IShellPlatformAdapter
    {
        public int InfrastructureConfigurationCount { get; private set; }

        public int NavigationInitializationCount { get; private set; }

        public int PresenterConfigurationCount { get; private set; }

        public void ConfigureInfrastructure(ShellHostElements host) =>
            InfrastructureConfigurationCount++;

        public void InitializeNavigation(ShellHostElements host) =>
            NavigationInitializationCount++;

        public void ConfigureNavigationPresenter(ShellHostElements host) =>
            PresenterConfigurationCount++;
    }

    private sealed class RecordingLayoutController : IShellLayoutController
    {
        public bool IsPaneOpen => true;

        public int UpdateCount { get; private set; }

        public event EventHandler<bool>? PaneStateChanged
        {
            add { }
            remove { }
        }

        public void UpdateWindowWidth(double width) => UpdateCount++;

        public void HandlePaneStateChanged(bool isPaneOpen)
        {
        }
    }

    private sealed class RecordingNavigationAdapter : IShellNavigationAdapter
    {
        public bool IsBypassingGuard { get; set; }

        public bool IsPlayerPageActive => CurrentRoute.Id == AppRouteId.Player;

        public AppRoute CurrentRoute { get; set; } = AppRoutes.Library;

        public AppRouteId CurrentRouteId => CurrentRoute.Id;

        public int NavigateCount { get; private set; }

        public int SynchronizeSelectionCount { get; private set; }

        public int NavigateFromShellCount { get; private set; }

        public AppRoute? LastRoute { get; private set; }

        public bool LastBypassGuard { get; private set; }

        public CancellationToken LastNavigationRequestToken { get; private set; }

        public Task<bool> NavigationResult { get; init; } = Task.FromResult(true);

        public void Initialize(
            INavigationView navigationView,
            NavigationViewItem libraryItem,
            NavigationViewItem settingsItem,
            NavigationViewItem playbackItem)
        {
        }

        public Task<bool> NavigateBackAsync(
            CancellationToken cancellationToken,
            bool bypassGuard = false) => Task.FromResult(false);

        public Task<bool> NavigateAsync(
            AppRoute route,
            CancellationToken cancellationToken,
            bool bypassGuard = false)
        {
            NavigateCount++;
            LastRoute = route;
            LastBypassGuard = bypassGuard;
            CurrentRoute = route;
            return NavigationResult;
        }

        public Task<bool> NavigateFromShellAsync(
            NavigatingCancelEventArgs eventArgs,
            CancellationToken cancellationToken)
        {
            NavigateFromShellCount++;
            LastNavigationRequestToken = cancellationToken;
            return Task.FromResult(true);
        }

        public void SynchronizeSelection(EventArgs eventArgs)
        {
            SynchronizeSelectionCount++;
        }
    }
}
