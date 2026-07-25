using NovelSpeaker.App.Shared.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using System.Windows;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Theming;

public sealed class MainWindowAppearanceConfiguratorTests
{
    [Fact]
    public void Configure_uses_mica_when_backdrop_assignment_succeeds()
    {
        var adapter = new FakeFluentWindowAppearanceAdapter();

        RunSta(() =>
        {
            var configurator = new MainWindowAppearanceConfigurator(adapter, NullLogger<MainWindowAppearanceConfigurator>.Instance);
            configurator.Configure(new Wpf.Ui.Controls.FluentWindow());
        });

        Assert.False(adapter.ExtendsContentIntoTitleBarValue);
        Assert.Equal(WindowBackdropType.Mica, adapter.LastBackdropType);
        Assert.Equal([WindowBackdropType.Mica], adapter.AttemptedBackdropTypes);
    }

    [Fact]
    public void Configure_ignores_standard_window_instances()
    {
        var adapter = new FakeFluentWindowAppearanceAdapter();

        RunSta(() =>
        {
            var configurator = new MainWindowAppearanceConfigurator(adapter, NullLogger<MainWindowAppearanceConfigurator>.Instance);
            configurator.Configure(new Window());
        });

        Assert.Empty(adapter.AttemptedBackdropTypes);
    }

    [Fact]
    public void Configure_does_not_retry_backdrop_when_mica_assignment_throws()
    {
        var adapter = new FakeFluentWindowAppearanceAdapter { ThrowOnMica = true };
        Exception? exception = null;

        RunSta(() =>
        {
            var configurator = new MainWindowAppearanceConfigurator(adapter, NullLogger<MainWindowAppearanceConfigurator>.Instance);
            exception = Record.Exception(() => configurator.Configure(new Wpf.Ui.Controls.FluentWindow()));
        });

        Assert.Null(exception);
        Assert.False(adapter.ExtendsContentIntoTitleBarValue);
        Assert.Null(adapter.LastBackdropType);
        Assert.Equal([WindowBackdropType.Mica], adapter.AttemptedBackdropTypes);
    }

    private static void RunSta(Action action)
    {
        Exception? capturedException = null;

        var thread = new Thread(() =>
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

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(capturedException);
    }

    private sealed class FakeFluentWindowAppearanceAdapter : IFluentWindowAppearanceAdapter
    {
        public bool ThrowOnMica { get; set; }

        public bool ExtendsContentIntoTitleBarValue { get; private set; }

        public WindowBackdropType? LastBackdropType { get; private set; }

        public List<WindowBackdropType> AttemptedBackdropTypes { get; } = [];

        public void SetExtendsContentIntoTitleBar(Wpf.Ui.Controls.FluentWindow window, bool value)
        {
            ExtendsContentIntoTitleBarValue = value;
        }

        public void SetBackdrop(Wpf.Ui.Controls.FluentWindow window, WindowBackdropType backdropType)
        {
            AttemptedBackdropTypes.Add(backdropType);

            if (ThrowOnMica && backdropType == WindowBackdropType.Mica)
            {
                throw new InvalidOperationException("Mica not supported");
            }

            LastBackdropType = backdropType;
        }
    }
}
