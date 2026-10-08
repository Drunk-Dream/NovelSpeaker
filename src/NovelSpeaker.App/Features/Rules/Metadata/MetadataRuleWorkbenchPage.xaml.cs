using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Rules.Metadata;

public partial class MetadataRuleWorkbenchPage : System.Windows.Controls.Page,
    INavigationAware, INavigableView<MetadataRuleWorkbenchViewModel>, ITransientEscapeHandler
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _guards;
    private readonly PageEventOperationRunner _operations;
    public ICommand SelectionCommand { get; }

    protected MetadataRuleWorkbenchPage(
        MetadataRuleWorkbenchViewModel viewModel,
        INavigationGuardService guards,
        PageEventOperationRunner operations)
        : this()
    {
        ViewModel = viewModel;
        _guards = guards;
        _operations = operations;
        SelectionCommand = new AsyncRelayCommand<MetadataRuleRow>(SelectWithModifiersAsync);
        DataContext = viewModel;
    }

    internal MetadataRuleWorkbenchPage()
    {
        ViewModel = null!;
        _guards = null!;
        _operations = PageEventOperationRunner.DesignTime;
        SelectionCommand = new AsyncRelayCommand<MetadataRuleRow>(SelectWithModifiersAsync);
        InitializeComponent();
    }

    public MetadataRuleWorkbenchViewModel ViewModel { get; }

    public bool TryHandleEscape() => ViewModel?.TryHandleEscape() == true;

    private Task SelectWithModifiersAsync(MetadataRuleRow? item)
    {
        var modifiers = DesktopSelectionInput.ReadModifiers();
        return _operations.RunAsync(_activation, "选择元数据规则失败",
            token => ViewModel.SelectRuleWithModifiersAsync(item, modifiers, token));
    }

    public async Task OnNavigatedToAsync()
    {
        using var operation = _operations.StartCriticalLoad(NovelSpeaker.Application.Observability.OperationCatalog.UiRulesLoad);
        var activation = _activation.Activate();
        ViewModel.HandleNavigatedTo(activation);
        activation.Register(_guards.Register(ViewModel.ConfirmLeaveAsync));
        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
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
        return Task.CompletedTask;
    }
    private void ManagementItem_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement { DataContext: MetadataRuleRow item })
            ViewModel.HandleRuleRightClick(item);
    }

    private void ManagementItem_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Apps || e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            if (sender is System.Windows.FrameworkElement { DataContext: MetadataRuleRow item })
                ViewModel.HandleRuleRightClick(item);
    }

}
