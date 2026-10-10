using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Dialogs;

[Collection("WpfDispatcher")]
public sealed class BookDeleteDialogServiceTests
{
    [Fact]
    public async Task ShowAsync_preserves_ui_thread_access_after_async_dialog_completion()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var contentDialogService = new AsyncFakeContentDialogService
            {
                NextResult = ContentDialogResult.Primary
            };
            var service = new BookDeleteDialogService(contentDialogService);

            var result = await service.ShowAsync(
                new BookDeleteDialogRequest("三体", false),
                CancellationToken.None);

            Assert.True(result.IsConfirmed);
        });
    }

    private sealed class AsyncFakeContentDialogService : IContentDialogService
    {
        private readonly ContentPresenter _presenter = new();
        private readonly ContentDialogHost _host = new();

        public ContentDialogResult NextResult { get; set; }

        public void SetDialogHost(ContentPresenter contentPresenter)
        {
        }

        public void SetContentPresenter(ContentPresenter contentPresenter)
        {
        }

        public void SetDialogHost(ContentDialogHost contentDialogHost)
        {
        }

        public ContentPresenter GetDialogHost() => _presenter;

        public ContentPresenter GetContentPresenter() => _presenter;

        public ContentDialogHost GetDialogHostEx() => _host;

        public Task<ContentDialogResult> ShowAsync(ContentDialog dialog, CancellationToken cancellationToken)
        {
            Assert.Equal(ContentDialogButton.Primary, dialog.DefaultButton);
            Assert.Equal(ControlAppearance.Danger, dialog.PrimaryButtonAppearance);
            Assert.Equal(ControlAppearance.Secondary, dialog.CloseButtonAppearance);
            var body = Assert.IsType<Border>(dialog.Content);
            Assert.Same(global::System.Windows.Application.Current.FindResource("App.Feedback.DialogBody"), body.Style);
            Assert.Equal(new Thickness(0), body.BorderThickness);
            Assert.Null(body.Effect);
            var stackPanel = Assert.IsType<StackPanel>(body.Child);
            Assert.Contains(stackPanel.Children.OfType<global::System.Windows.Controls.TextBlock>(), text => text.Text.Contains("关联音频缓存", StringComparison.Ordinal));
            Assert.Contains(stackPanel.Children.OfType<global::System.Windows.Controls.TextBlock>(), text => text.Text.Contains("外部 TXT", StringComparison.Ordinal));
            Assert.DoesNotContain(stackPanel.Children.Cast<UIElement>(), child => child is CheckBox);

            return Task.Run(() => NextResult, cancellationToken);
        }
    }
}
