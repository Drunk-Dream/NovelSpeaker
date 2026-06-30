using System.Windows;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Views;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed class BookCardViewTests
{
    [Fact]
    public void BookCardView_can_render_read_only_progress_ratio()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new BookCardView
            {
                Item = new LibraryBookItemViewModel(
                    "book-1",
                    "三体",
                    "刘慈欣",
                    "第一章 科学边界",
                    "剩余 5 章",
                    0.5,
                    true,
                    "2026-06-30T00:00:00.0000000Z",
                    new BookCoverGenerator().Generate("三体"),
                    canDelete: true),
            };

            var window = new Window
            {
                Width = 480,
                Height = 240,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = view
            };

            try
            {
                window.Show();
                window.UpdateLayout();
            }
            finally
            {
                window.Close();
            }
        });
    }
}
