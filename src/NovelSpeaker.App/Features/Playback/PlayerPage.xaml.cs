using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Presentation;
using Wpf.Ui.Abstractions.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace NovelSpeaker.App.Features.Playback;

public partial class PlayerPage : System.Windows.Controls.Page, INavigationAware, INavigableView<PlayerViewModel>, IKeyboardShortcutTarget, ITransientEscapeHandler
{
    private readonly PageActivationController _activation = new();
    private readonly IKeyboardShortcutTargetRegistry? _shortcutTargets;
    private readonly PageEventOperationRunner _eventOperations;

    public PlayerPage(
        PlayerViewModel viewModel,
        IKeyboardShortcutTargetRegistry? shortcutTargets = null,
        PageEventOperationRunner? eventOperations = null)
    {
        ViewModel = viewModel;
        _shortcutTargets = shortcutTargets;
        _eventOperations = eventOperations ?? PageEventOperationRunner.DesignTime;
        InitializeComponent();
        PlayerView.DataContext = ViewModel;
    }

    public PlayerViewModel ViewModel { get; }

    private void PlayerPage_OnLoaded(object sender, RoutedEventArgs e)
    {
        // Frame navigation is an inheritance boundary, so resolve the visual viewport
        // after attachment rather than relying on an ancestor binding across the Page.
        for (var ancestor = VisualTreeHelper.GetParent(this); ancestor is not null;
             ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is ScrollContentPresenter viewport)
            {
                RootViewport.SetBinding(HeightProperty, new Binding(nameof(ActualHeight)) { Source = viewport });
                return;
            }
        }

        // A finite, non-scrolling host can use ordinary Stretch layout.
        RootViewport.ClearValue(HeightProperty);
    }

    private void PlayerPage_OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Release the old host and keep reattachment bounded before Loaded runs again.
        RootViewport.SetValue(HeightProperty, 0d);
    }

    public bool TryHandleEscape() => ViewModel.TryHandleEscape();

    public async Task OnNavigatedToAsync()
    {
        using var operation = _eventOperations.StartCriticalLoad(NovelSpeaker.Application.Observability.OperationCatalog.UiPlayerLoad);
        var activation = _activation.Activate();
        PlayerView.ActivationToken = activation.CancellationToken;
        ViewModel.OnPageNavigatedTo(activation);
        if (_shortcutTargets is not null)
        {
            activation.Register(_shortcutTargets.Register(this));
        }
        var request = DataContext as PlayerRoute;

        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            if (request is not null && activation.IsCurrent)
            {
                await ViewModel.HandleNavigationAsync(request, activation.CancellationToken);
            }
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Succeeded());
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Cancelled());
        }
        catch
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Failed("page-load-failed"));
            throw;
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        PlayerView.ActivationToken = new CancellationToken(canceled: true);
        return Task.CompletedTask;
    }

    public async Task<bool> HandleKeyboardShortcutAsync(
        KeyboardShortcutAction action,
        string? argument,
        CancellationToken cancellationToken)
    {
        if (argument is not null || _activation.Current is not { IsCurrent: true } activation)
        {
            return false;
        }

        var command = action switch
        {
            KeyboardShortcutAction.TogglePlayback => ViewModel.TogglePlayPauseCommand,
            KeyboardShortcutAction.PreviousSegment => ViewModel.PreviousSegmentCommand,
            KeyboardShortcutAction.NextSegment => ViewModel.NextSegmentCommand,
            KeyboardShortcutAction.PreviousChapter => ViewModel.PreviousChapterCommand,
            KeyboardShortcutAction.NextChapter => ViewModel.NextChapterCommand,
            _ => null
        };
        if (command is null || !command.CanExecute(null))
        {
            return false;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            activation.CancellationToken);
        try
        {
            await command.ExecuteAsync(null).WaitAsync(linkedCancellation.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (activation.CancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return true;
    }
}
