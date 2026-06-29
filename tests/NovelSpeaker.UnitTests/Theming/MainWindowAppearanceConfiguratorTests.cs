using NovelSpeaker.App.Theming;
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
            var configurator = new MainWindowAppearanceConfigurator(adapter);
            configurator.Configure(new Wpf.Ui.Controls.FluentWindow());
        });

        Assert.True(adapter.ExtendsContentIntoTitleBarValue);
        Assert.Equal(WindowBackdropType.Mica, adapter.LastBackdropType);
    }

    [Fact]
    public void Configure_falls_back_to_none_when_mica_assignment_throws()
    {
        var adapter = new FakeFluentWindowAppearanceAdapter { ThrowOnMica = true };
        Exception? exception = null;

        RunSta(() =>
        {
            var configurator = new MainWindowAppearanceConfigurator(adapter);
            exception = Record.Exception(() => configurator.Configure(new Wpf.Ui.Controls.FluentWindow()));
        });

        Assert.Null(exception);
        Assert.True(adapter.ExtendsContentIntoTitleBarValue);
        Assert.Equal(WindowBackdropType.None, adapter.LastBackdropType);
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

        public void SetExtendsContentIntoTitleBar(Wpf.Ui.Controls.FluentWindow window, bool value)
        {
            ExtendsContentIntoTitleBarValue = value;
        }

        public void SetBackdrop(Wpf.Ui.Controls.FluentWindow window, WindowBackdropType backdropType)
        {
            if (ThrowOnMica && backdropType == WindowBackdropType.Mica)
            {
                throw new InvalidOperationException("Mica not supported");
            }

            LastBackdropType = backdropType;
        }
    }
}
