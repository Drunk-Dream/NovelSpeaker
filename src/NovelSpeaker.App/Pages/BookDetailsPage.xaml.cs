using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class BookDetailsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<BookDetailsViewModel>
{
    private readonly INavigationGuardService _navigationGuardService;
    private IDisposable? _guardRegistration;
    private bool _pendingCurrentChapterScroll;
    private FrameworkElement? _viewportHost;

    public BookDetailsPage(
        BookDetailsViewModel viewModel,
        INavigationGuardService navigationGuardService)
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        InitializeComponent();
        RootViewport.DataContext = ViewModel;
        ViewModel.Chapters.CollectionChanged += OnChaptersCollectionChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public BookDetailsViewModel ViewModel { get; }

    public BookDetailsNavigationRequest? LastRequest { get; private set; }

    public async Task OnNavigatedToAsync()
    {
        _guardRegistration?.Dispose();
        _guardRegistration = _navigationGuardService.Register(ViewModel.ConfirmLeaveAsync);

        LastRequest = DataContext as BookDetailsNavigationRequest;
        if (LastRequest is null)
        {
            return;
        }

        _pendingCurrentChapterScroll = true;
        await ViewModel.LoadAsync(LastRequest.BookId, CancellationToken.None);
        await ScrollCurrentChapterIntoViewAsync();
    }

    public Task OnNavigatedFromAsync()
    {
        _guardRegistration?.Dispose();
        _guardRegistration = null;
        _pendingCurrentChapterScroll = false;
        ViewModel.HandleNavigatedFrom();
        return Task.CompletedTask;
    }

    private async Task ScrollCurrentChapterIntoViewAsync()
    {
        if (!_pendingCurrentChapterScroll || ViewModel.CurrentChapterItem is null)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            ChaptersListBox.UpdateLayout();
            ChaptersListBox.ScrollIntoView(ViewModel.CurrentChapterItem);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
        _pendingCurrentChapterScroll = false;
    }

    private void OnChaptersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_pendingCurrentChapterScroll || !IsLoaded)
        {
            return;
        }

        _ = ScrollCurrentChapterIntoViewAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewportConstraint();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewportConstraint();
    }

    private void AttachViewportConstraint()
    {
        var viewportHost = FindAncestor<Frame>(this) as FrameworkElement ?? Window.GetWindow(this);
        if (ReferenceEquals(_viewportHost, viewportHost))
        {
            UpdateViewportHeight();
            return;
        }

        DetachViewportConstraint();
        _viewportHost = viewportHost;
        if (_viewportHost is null)
        {
            RootViewport.Height = double.NaN;
            return;
        }

        _viewportHost.SizeChanged += ViewportHost_OnSizeChanged;
        UpdateViewportHeight();
    }

    private void DetachViewportConstraint()
    {
        if (_viewportHost is not null)
        {
            _viewportHost.SizeChanged -= ViewportHost_OnSizeChanged;
            _viewportHost = null;
        }

        RootViewport.Height = double.NaN;
    }

    private void ViewportHost_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateViewportHeight();
    }

    private void UpdateViewportHeight()
    {
        if (_viewportHost is null)
        {
            RootViewport.Height = double.NaN;
            return;
        }

        var viewportHeight = _viewportHost.ActualHeight;
        RootViewport.Height = viewportHeight > 0 ? viewportHeight : double.NaN;
    }

    private static T? FindAncestor<T>(DependencyObject? start)
        where T : DependencyObject
    {
        for (var current = GetParent(start); current is not null; current = GetParent(current))
        {
            if (current is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject? dependencyObject)
    {
        if (dependencyObject is null)
        {
            return null;
        }

        return dependencyObject switch
        {
            FrameworkElement frameworkElement => frameworkElement.Parent ?? VisualTreeHelper.GetParent(frameworkElement),
            FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
            _ => VisualTreeHelper.GetParent(dependencyObject)
        };
    }
}
