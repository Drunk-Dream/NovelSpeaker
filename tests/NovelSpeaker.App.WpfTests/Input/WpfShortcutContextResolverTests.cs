using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NovelSpeaker.App.Shell.Input;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Input;

[Collection("WpfDispatcher")]
public sealed class WpfShortcutContextResolverTests
{
    [Fact]
    public void Shortcut_context_suppresses_global_actions_for_editing_popup_and_dialog_ui()
    {
        WpfTestHost.RunInSta(() =>
        {
            var resolver = new WpfShortcutContextResolver();
            var placementTarget = new Button();
            var popupContent = new Button();
            var popup = new Popup
            {
                PlacementTarget = placementTarget,
                Child = popupContent,
                StaysOpen = true
            };
            var window = new Window { Content = placementTarget };

            try
            {
                WpfWindowHost.Show(window);

                var editingContext = resolver.Resolve(false, new TextBox(), new Grid(), null);
                Assert.True(editingContext.IsTextEditing);

                popup.IsOpen = true;
                popupContent.Focus();
                var popupContext = resolver.Resolve(false, popupContent, new Grid(), null);
                Assert.True(popupContext.IsTransientUiOpen);

                popup.IsOpen = false;
                var dialogHost = new Grid();
                dialogHost.Children.Add(new Wpf.Ui.Controls.ContentDialog());
                window.Content = dialogHost;
                window.UpdateLayout();

                var dialogContext = resolver.Resolve(false, new Button(), dialogHost, null);
                Assert.True(dialogContext.IsTransientUiOpen);
            }
            finally
            {
                popup.IsOpen = false;
                window.Close();
            }
        });
    }
}
