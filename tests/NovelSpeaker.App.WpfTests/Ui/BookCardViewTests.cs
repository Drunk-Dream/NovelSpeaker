using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NovelSpeaker.App.Features.Library;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class BookCardViewTests
{
    [Fact]
    public void BookCardView_exposes_accessible_names_and_keeps_more_button_inside_card()
    {
        WpfTestHost.RunInSta(() =>
        {
            var item = new LibraryBookItemViewModel(
                "book-1",
                "三体",
                "刘慈欣",
                "第一章 科学边界",
                "剩余 5 章",
                0.5,
                true,
                "2026-06-30T00:00:00.0000000Z",
                new BookCoverGenerator().Generate("三体"),
                canDelete: true);
            var view = new BookCardView
            {
                Item = item,
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
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var openButton = VisualTreeTestHelper.FindDescendant<Button>(
                    view,
                    candidate => AutomationProperties.GetName(candidate) == item.AutomationName);
                var moreButton = Assert.IsType<Button>(view.FindName("MoreButton"));

                Assert.NotNull(openButton);
                Assert.Equal(item.MoreActionsAutomationName, AutomationProperties.GetName(moreButton));
                Assert.Equal("更多操作", moreButton.ToolTip);
                Assert.True(moreButton.TransformToAncestor(view).Transform(new Point(0, 0)).X >= 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

}
