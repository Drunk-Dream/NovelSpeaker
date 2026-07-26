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
    public void Text_input_and_editable_combo_are_editing_contexts()
    {
        WpfTestHost.RunInSta(() =>
        {
            var resolver = new WpfShortcutContextResolver();
            var dialogHost = new Grid();

            var textContext = resolver.Resolve(false, new TextBox(), dialogHost);
            var comboContext = resolver.Resolve(
                false,
                new ComboBox { IsEditable = true },
                dialogHost);

            Assert.True(textContext.IsTextEditing);
            Assert.True(comboContext.IsTextEditing);
        });
    }

    [Fact]
    public void Menu_item_is_a_transient_context()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new WpfShortcutContextResolver().Resolve(
                false,
                new System.Windows.Controls.MenuItem(),
                new Grid());

            Assert.True(context.IsTransientUiOpen);
        });
    }

    [Fact]
    public void Focus_inside_generic_popup_is_a_transient_context()
    {
        WpfTestHost.RunInSta(() =>
        {
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
                window.Show();
                popup.IsOpen = true;
                popupContent.Focus();

                var context = new WpfShortcutContextResolver().Resolve(
                    false,
                    popupContent,
                    new Grid());

                Assert.True(context.IsTransientUiOpen);
            }
            finally
            {
                popup.IsOpen = false;
                window.Close();
            }
        });
    }

    [Fact]
    public void Visible_content_dialog_is_a_transient_context()
    {
        WpfTestHost.RunInSta(() =>
        {
            var dialogHost = new Grid();
            dialogHost.Children.Add(new Wpf.Ui.Controls.ContentDialog());
            var window = new Window { Content = dialogHost };

            try
            {
                window.Show();
                window.UpdateLayout();

                var context = new WpfShortcutContextResolver().Resolve(
                    false,
                    new Button(),
                    dialogHost);

                Assert.True(context.IsTransientUiOpen);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Plain_window_content_preserves_player_context_without_suppression()
    {
        WpfTestHost.RunInSta(() =>
        {
            var button = new Button();
            var dialogHost = new Grid();
            var window = new Window { Content = button };

            try
            {
                window.Show();

                var context = new WpfShortcutContextResolver().Resolve(
                    true,
                    button,
                    dialogHost);

                Assert.True(context.IsPlayerPageActive);
                Assert.False(context.IsTextEditing);
                Assert.False(context.IsTransientUiOpen);
            }
            finally
            {
                window.Close();
            }
        });
    }
}
