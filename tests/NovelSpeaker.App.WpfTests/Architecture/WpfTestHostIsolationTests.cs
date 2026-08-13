using Xunit;

namespace NovelSpeaker.App.WpfTests.Architecture;

[Collection("WpfDispatcher")]
public sealed class WpfTestHostIsolationTests
{
    [Fact]
    public void Default_host_binds_the_shared_dispatcher_to_an_isolated_desktop()
    {
        WpfTestHost.RunInSta(() =>
        {
            Assert.True(WpfTestHost.CurrentDesktop.IsIsolated);
            Assert.False(WpfTestHost.IsVisibleWindowsAllowed);
        });
    }

    [Fact]
    public void Visible_attach_keeps_the_interactive_strategy_without_native_desktop_changes()
    {
        var nativeApi = new FakeWindowsTestDesktopNativeApi();

        using var desktop = WindowsTestDesktop.Attach(allowVisibleWindows: true, nativeApi);

        Assert.False(desktop.Info.IsIsolated);
        Assert.Equal("interactive", desktop.Info.Name);
        Assert.Equal(0, nativeApi.CreateCallCount);
        Assert.Equal(0, nativeApi.BindCallCount);
        Assert.Equal(0, nativeApi.CloseCallCount);
    }

    [Fact]
    public void Visible_window_policy_requires_the_explicit_value()
    {
        foreach (var (value, expected) in new[]
                 {
                     ((string?)null, false),
                     (string.Empty, false),
                     ("0", false),
                     ("true", false),
                     ("1", true)
                 })
        {
            Assert.Equal(expected, WpfTestHost.IsVisibleWindowsAllowedValue(value));
        }
    }

    [Fact]
    public void Desktop_creation_failure_fails_closed_without_binding_a_fallback_desktop()
    {
        var nativeApi = new FakeWindowsTestDesktopNativeApi
        {
            Created = IntPtr.Zero,
            LastError = 5
        };

        var exception = Assert.Throws<WindowsTestDesktopInitializationException>(() =>
            WindowsTestDesktop.Attach(allowVisibleWindows: false, nativeApi));

        Assert.Contains("create the isolated Windows Desktop", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, nativeApi.BindCallCount);
        Assert.Equal(0, nativeApi.CloseCallCount);
    }

    [Fact]
    public void Desktop_binding_failure_closes_the_created_desktop_without_fallback()
    {
        var nativeApi = new FakeWindowsTestDesktopNativeApi
        {
            BindResult = false,
            LastError = 170
        };

        var exception = Assert.Throws<WindowsTestDesktopInitializationException>(() =>
            WindowsTestDesktop.Attach(allowVisibleWindows: false, nativeApi));

        Assert.Contains("bind the WPF test thread", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, nativeApi.BindCallCount);
        Assert.Equal(1, nativeApi.CloseCallCount);
        Assert.Equal(nativeApi.Created, nativeApi.Closed);
    }

    [Fact]
    public void Binding_failure_with_release_failure_retains_the_desktop_for_retry()
    {
        var nativeApi = new FakeWindowsTestDesktopNativeApi
        {
            BindResult = false,
            CloseResult = false,
            LastError = 170
        };

        Assert.Throws<AggregateException>(() =>
            WindowsTestDesktop.Attach(allowVisibleWindows: false, nativeApi));

        Assert.Equal(1, nativeApi.CloseCallCount);
        nativeApi.CloseResult = true;
        WindowsTestDesktop.RetryPendingCleanup();

        Assert.Equal(2, nativeApi.CloseCallCount);
    }

    [Fact]
    public void Successful_isolated_desktop_release_restores_then_closes_once()
    {
        var nativeApi = new FakeWindowsTestDesktopNativeApi();
        using var desktop = WindowsTestDesktop.Attach(allowVisibleWindows: false, nativeApi);

        desktop.PrepareThreadShutdown();
        desktop.ReleaseDesktopHandle();
        desktop.ReleaseDesktopHandle();

        Assert.True(desktop.ThreadDesktopRestored);
        Assert.Equal(2, nativeApi.BindCallCount);
        Assert.Equal(1, nativeApi.CloseCallCount);
    }

    [Fact]
    public void Test_window_cleanup_remains_owned_by_the_shared_host_boundary()
    {
        WpfTestHost.RunInSta(() =>
        {
            _ = WpfWindowHost.Show(new System.Windows.Window
            {
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStyle = System.Windows.WindowStyle.None
            });
        });

        Assert.Equal(0, WpfTestHost.TrackedWindowCount);
    }

    private sealed class FakeWindowsTestDesktopNativeApi : IWindowsTestDesktopNativeApi
    {
        public IntPtr Created { get; init; } = new(2);

        public IntPtr CurrentDesktop { get; init; } = new(1);

        public bool BindResult { get; init; } = true;

        public bool CloseResult { get; set; } = true;

        public int LastError { get; init; } = 6;

        public int BindCallCount { get; private set; }

        public int CreateCallCount { get; private set; }

        public int CloseCallCount { get; private set; }

        public IntPtr Closed { get; private set; }

        public IntPtr GetCurrent() => CurrentDesktop;

        public IntPtr Create(string name, uint desiredAccess)
        {
            CreateCallCount++;
            return Created;
        }

        public bool Bind(IntPtr desktop)
        {
            BindCallCount++;
            return BindResult;
        }

        public bool Close(IntPtr desktop)
        {
            CloseCallCount++;
            Closed = desktop;
            return CloseResult;
        }

        public int GetLastError() => LastError;
    }
}
