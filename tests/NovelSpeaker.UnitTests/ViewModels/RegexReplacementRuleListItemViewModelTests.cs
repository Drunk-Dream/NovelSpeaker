using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class RegexReplacementRuleListItemViewModelTests
{
    [Fact]
    public void Rule_state_uses_localized_non_color_text()
    {
        var rule = new RegexReplacementRuleListItemViewModel(
            Guid.NewGuid(),
            "规则",
            "pattern",
            false,
            RegexReplacementScope.Both,
            false,
            null);

        Assert.Equal("已禁用", rule.EnabledStateText);
        Assert.Equal("显示与朗读", rule.ScopeDisplayName);

        rule.IsEnabled = true;

        Assert.Equal("已启用", rule.EnabledStateText);
        Assert.Contains("已启用", rule.AutomationName);
    }
}
