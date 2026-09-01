using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NovelSpeaker.App.Shared.Presentation.Books;
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
                var moreButton = Assert.IsType<Wpf.Ui.Controls.Button>(view.FindName("MoreButton"));

                Assert.NotNull(openButton);
                Assert.Same(view.FindResource("App.Button.InteractionHost"), openButton.Style);
                Assert.Equal(Brushes.Transparent, openButton.Background);
                Assert.Same(item, openButton.CommandParameter);
                Assert.Equal(item.MoreActionsAutomationName, AutomationProperties.GetName(moreButton));
                Assert.Equal("更多操作", moreButton.ToolTip);
                Assert.Same(view.FindResource("App.Button.Icon"), moreButton.Style.BasedOn);
                Assert.True(moreButton.TransformToAncestor(view).Transform(new Point(0, 0)).X >= 0);

                var surface = Assert.IsType<Border>(view.FindName("CardSurface"));
                Assert.Same(view.FindResource("App.Selection.CardItem"), surface.Style);
                Assert.Equal(new Thickness(0), surface.Padding);
                Assert.InRange(Math.Abs(openButton.ActualWidth - surface.ActualWidth), 0d, 2.1d);
                Assert.InRange(Math.Abs(openButton.ActualHeight - surface.ActualHeight), 0d, 2.1d);
                var progress = Assert.IsType<ProgressBar>(view.FindName("ReadingProgressBar"));
                Assert.Same(view.FindResource("App.Progress.Compact"), progress.Style);
                Assert.Equal(item.ProgressRatio, progress.Value);
                Assert.Equal(item.ProgressAutomationText, AutomationProperties.GetName(progress));

                var menu = Assert.IsType<ContextMenu>(moreButton.ContextMenu);
                Assert.Same(view.FindResource("App.Menu.ContextSurface"), menu.Style);
                var menuItems = menu.Items.OfType<MenuItem>().ToArray();
                Assert.Equal(2, menuItems.Length);
                Assert.Same(view.FindResource("App.Menu.Item"), menuItems[0].Style);
                Assert.Same(view.FindResource("App.Menu.DangerItem"), menuItems[1].Style);
                Assert.Equal("书籍详情", menuItems[0].Header);
                Assert.Equal("删除书籍", menuItems[1].Header);

                moreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                menu.ApplyTemplate();
                menu.UpdateLayout();

                Assert.True(menu.IsOpen);
                Assert.True(menu.ActualWidth >= 100d);
                Assert.All(menuItems, item => Assert.True(item.ActualHeight >= 24d));
                var raisedSurface = Assert.IsAssignableFrom<Brush>(view.FindResource("App.Brush.Surface.Raised"));
                Assert.Contains(
                    VisualTreeTestHelper.FindDescendants<Border>(menu),
                    border => ReferenceEquals(border.Background, raisedSurface));
                menu.IsOpen = false;
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void BookCardView_preserves_commands_tooltips_and_long_title_trimming()
    {
        WpfTestHost.RunInSta(() =>
        {
            var title = "一部拥有非常非常长标题并用于验证省略显示和完整提示信息的小说";
            var item = new LibraryBookItemViewModel(
                "book-long",
                title,
                "一位名字也很长的示例作者",
                "第一百二十三章 这同样是一个很长的当前章节标题",
                "剩余 99 章",
                0.42,
                true,
                null,
                new BookCoverGenerator().Generate(title),
                canDelete: true);
            var open = new TestCommand();
            var details = new TestCommand();
            var delete = new TestCommand();
            var view = new BookCardView
            {
                Width = 360,
                Item = item,
                OpenBookCommand = open,
                OpenBookDetailsCommand = details,
                DeleteBookCommand = delete
            };
            using var host = new WpfControlHost(view);
            host.MeasureArrange(new Size(360, 176));

            var openButton = Assert.IsType<Button>(view.FindName("OpenBookButton"));
            Assert.Same(open, openButton.Command);
            Assert.Same(item, openButton.CommandParameter);
            Assert.Same(details, view.OpenBookDetailsCommand);
            Assert.Same(delete, view.DeleteBookCommand);
            var titleText = VisualTreeTestHelper.FindDescendants<TextBlock>(view)
                .Single(text => text.Text == title);
            Assert.Equal(TextTrimming.CharacterEllipsis, titleText.TextTrimming);
            Assert.Equal(title, titleText.ToolTip);
            Assert.Equal(item.AutomationName, AutomationProperties.GetName(openButton));

            var contentStack = Assert.IsType<StackPanel>(view.FindName("BookInfoStackPanel"));
            var expectedContentWidth = 360d - (16d * 2d) - 104d - 16d;
            Assert.InRange(Math.Abs(contentStack.ActualWidth - expectedContentWidth), 0d, 2.1d);
            Assert.Equal(new Thickness(16), openButton.Padding);
            var cover = Assert.IsType<BookCoverView>(
                VisualTreeTestHelper.FindDescendant<BookCoverView>(view));
            Assert.Equal(104d, cover.ActualWidth);
            Assert.Equal(140d, cover.ActualHeight);
            var author = FindTextBlock(view, item.DisplayAuthor);
            var currentChapter = FindTextBlock(view, item.CurrentChapterTitle);
            var remainingChapters = FindTextBlock(view, item.RemainingChapterText);
            var measuredProgress = Assert.IsType<ProgressBar>(view.FindName("ReadingProgressBar"));
            Assert.InRange(Math.Abs(author.ActualWidth - contentStack.ActualWidth), 0d, 2.1d);
            Assert.InRange(Math.Abs(currentChapter.ActualWidth - contentStack.ActualWidth), 0d, 2.1d);
            Assert.InRange(Math.Abs(remainingChapters.ActualWidth - contentStack.ActualWidth), 0d, 2.1d);
            Assert.InRange(Math.Abs(measuredProgress.ActualWidth - contentStack.ActualWidth), 0d, 2.1d);

            var moreButton = Assert.IsType<Wpf.Ui.Controls.Button>(view.FindName("MoreButton"));
            var titleOrigin = titleText.TransformToAncestor(view).Transform(new Point());
            var moreOrigin = moreButton.TransformToAncestor(view).Transform(new Point());
            Assert.True(
                titleOrigin.X + titleText.ActualWidth <= moreOrigin.X + 1d,
                $"Title right edge {titleOrigin.X + titleText.ActualWidth:0.##} overlapped MoreButton left edge {moreOrigin.X:0.##}.");
            moreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = Assert.IsType<ContextMenu>(moreButton.ContextMenu);
            Assert.Same(view, menu.DataContext);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(
                nameof(BookCardView.OpenBookDetailsCommand),
                Assert.IsType<Binding>(BindingOperations.GetBinding(items[0], MenuItem.CommandProperty)).Path.Path);
            Assert.Equal(
                nameof(BookCardView.DeleteBookCommand),
                Assert.IsType<Binding>(BindingOperations.GetBinding(items[1], MenuItem.CommandProperty)).Path.Path);
            Assert.Equal(
                nameof(BookCardView.Item),
                Assert.IsType<Binding>(BindingOperations.GetBinding(items[0], MenuItem.CommandParameterProperty)).Path.Path);
            Assert.Equal(
                nameof(BookCardView.Item),
                Assert.IsType<Binding>(BindingOperations.GetBinding(items[1], MenuItem.CommandParameterProperty)).Path.Path);
        });
    }

    private static TextBlock FindTextBlock(BookCardView view, string text)
    {
        return VisualTreeTestHelper.FindDescendants<TextBlock>(
                view,
                candidate => candidate.Text == text)
            .Single();
    }

    private sealed class TestCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }

}
