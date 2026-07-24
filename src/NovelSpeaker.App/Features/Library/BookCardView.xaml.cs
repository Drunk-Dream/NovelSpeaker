using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NovelSpeaker.App.Features.Library;

public partial class BookCardView : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(LibraryBookItemViewModel),
            typeof(BookCardView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty OpenBookCommandProperty =
        DependencyProperty.Register(
            nameof(OpenBookCommand),
            typeof(ICommand),
            typeof(BookCardView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty OpenBookDetailsCommandProperty =
        DependencyProperty.Register(
            nameof(OpenBookDetailsCommand),
            typeof(ICommand),
            typeof(BookCardView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DeleteBookCommandProperty =
        DependencyProperty.Register(
            nameof(DeleteBookCommand),
            typeof(ICommand),
            typeof(BookCardView),
            new PropertyMetadata(null));

    public BookCardView()
    {
        InitializeComponent();
    }

    public LibraryBookItemViewModel? Item
    {
        get => (LibraryBookItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public ICommand? OpenBookCommand
    {
        get => (ICommand?)GetValue(OpenBookCommandProperty);
        set => SetValue(OpenBookCommandProperty, value);
    }

    public ICommand? OpenBookDetailsCommand
    {
        get => (ICommand?)GetValue(OpenBookDetailsCommandProperty);
        set => SetValue(OpenBookDetailsCommandProperty, value);
    }

    public ICommand? DeleteBookCommand
    {
        get => (ICommand?)GetValue(DeleteBookCommandProperty);
        set => SetValue(DeleteBookCommandProperty, value);
    }

    private void MoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.DataContext = this;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }
}
