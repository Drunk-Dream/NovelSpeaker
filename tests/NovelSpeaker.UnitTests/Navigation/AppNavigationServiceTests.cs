using NovelSpeaker.App.Navigation;
using Xunit;

namespace NovelSpeaker.UnitTests.Navigation;

public sealed class AppNavigationServiceTests
{
    [Fact]
    public void NavigateToPrimary_switches_between_library_and_settings()
    {
        var service = new AppNavigationService();

        var changed = service.NavigateToPrimary(AppPrimaryDestination.Settings);

        Assert.True(changed);
        Assert.Equal(AppPageKind.SettingsHome, service.CurrentEntry.PageKind);
        Assert.Equal(AppPrimaryDestination.Settings, service.CurrentEntry.PrimaryDestination);
    }

    [Fact]
    public void NavigateToPrimary_is_idempotent_for_same_destination()
    {
        var service = new AppNavigationService();

        var changed = service.NavigateToPrimary(AppPrimaryDestination.Library);

        Assert.False(changed);
        Assert.False(service.CanGoBack);
        Assert.Equal(AppPageKind.Library, service.CurrentEntry.PageKind);
    }

    [Fact]
    public void Parameter_navigation_and_back_restore_previous_entry()
    {
        var service = new AppNavigationService();

        service.NavigateToSettings(SettingsSection.TtsRules);
        service.NavigateToPlayer(new PlayerNavigationRequest("book-1"));
        service.NavigateToBookDetails(new BookDetailsNavigationRequest("book-2"));

        Assert.True(service.CanGoBack);
        Assert.Equal(AppPageKind.BookDetails, service.CurrentEntry.PageKind);
        Assert.Equal("book-2", Assert.IsType<BookDetailsNavigationRequest>(service.CurrentEntry.Parameter).BookId);

        Assert.True(service.GoBack());
        Assert.Equal(AppPageKind.Player, service.CurrentEntry.PageKind);
        Assert.Equal("book-1", Assert.IsType<PlayerNavigationRequest>(service.CurrentEntry.Parameter).BookId);

        Assert.True(service.GoBack());
        Assert.Equal(AppPageKind.TtsRules, service.CurrentEntry.PageKind);
        Assert.Equal(SettingsSection.TtsRules, service.CurrentEntry.SettingsSection);
    }
}
