using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shared.Presentation.Rules;

public sealed class RuleToggleSwitch : ToggleSwitch
{
    protected override AutomationPeer OnCreateAutomationPeer() =>
        new RuleToggleSwitchAutomationPeer(this);

    internal void ToggleFromAutomation()
    {
        var commandTarget = CommandTarget ?? this;
        var canExecute = Command switch
        {
            null => true,
            RoutedCommand routedCommand => routedCommand.CanExecute(CommandParameter, commandTarget),
            _ => Command.CanExecute(CommandParameter)
        };
        if (!IsEnabled || !canExecute)
        {
            throw new ElementNotEnabledException();
        }

        base.OnToggle();
        if (Command is RoutedCommand executableRoutedCommand)
        {
            executableRoutedCommand.Execute(CommandParameter, commandTarget);
        }
        else
        {
            Command?.Execute(CommandParameter);
        }
    }
}

internal sealed class RuleToggleSwitchAutomationPeer(RuleToggleSwitch owner)
    : ToggleButtonAutomationPeer(owner), IToggleProvider
{
    protected override string GetClassNameCore() => nameof(RuleToggleSwitch);

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Toggle ? this : base.GetPattern(patternInterface);

    void IToggleProvider.Toggle() => ((RuleToggleSwitch)Owner).ToggleFromAutomation();
}
