using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Dialogs;

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
                new BookDeleteDialogRequest("三体", false, true),
                CancellationToken.None);

            Assert.True(result.IsConfirmed);
            Assert.False(result.DeleteAudioCache);
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
            var stackPanel = Assert.IsType<StackPanel>(dialog.Content);
            var deleteCacheCheckBox = Assert.IsType<CheckBox>(stackPanel.Children[3]);
            deleteCacheCheckBox.IsChecked = false;

            return Task.Run(() => NextResult, cancellationToken);
        }
    }
}
