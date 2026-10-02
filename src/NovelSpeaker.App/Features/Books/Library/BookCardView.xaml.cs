using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.Shared.Presentation.Selection;

namespace NovelSpeaker.App.Features.Books.Library;

public partial class BookCardView : UserControl
{
    public static readonly DependencyProperty ManagementOwnerProperty = DependencyProperty.Register(
        nameof(ManagementOwner), typeof(LibraryViewModel), typeof(BookCardView), new PropertyMetadata(null));

    public LibraryViewModel? ManagementOwner
    {
        get => (LibraryViewModel?)GetValue(ManagementOwnerProperty);
        set => SetValue(ManagementOwnerProperty, value);
    }
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(LibraryBookCardProjection),
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
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Item is null || ManagementOwner is null || MoreButton.IsMouseOver) return;
            var modifiers = DesktopSelectionInput.ReadModifiers();
            if (modifiers != DesktopSelectionModifiers.None && ManagementOwner.HandleBookClick(Item, modifiers)) e.Handled = true;
        };
        PreviewMouseRightButtonDown += (_, e) =>
        {
            OpenContextMenu();
            e.Handled = true;
        };
    }

    public LibraryBookCardProjection? Item
    {
        get => (LibraryBookCardProjection?)GetValue(ItemProperty);
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
        OpenContextMenu();
    }

    private void OpenContextMenu()
    {
        ManagementOwner?.PrepareBookContextCommand.Execute(Item);
        MoreButton.ContextMenu.DataContext = this;
        MoreButton.ContextMenu.PlacementTarget = MoreButton;
        MoreButton.ContextMenu.IsOpen = true;
    }
}
