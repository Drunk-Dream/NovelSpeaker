using System.Windows;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Views;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed class ContinueListeningCardViewTests
{
    [Fact]
    public void ContinueListeningCardView_can_render_progress_ratio()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new ContinueListeningCardView
            {
                Item = new ContinueListeningItemViewModel(
                    "book-1",
                    "三体",
                    "第一章 科学边界",
                    "剩余 5 章",
                    0.5,
                    new BookCoverGenerator().Generate("三体"))
            };

            var window = new Window
            {
                Width = 600,
                Height = 280,
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
