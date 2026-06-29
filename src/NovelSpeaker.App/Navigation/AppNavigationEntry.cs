namespace NovelSpeaker.App.Navigation;

public sealed record AppNavigationEntry(
    AppPageKind PageKind,
    AppPrimaryDestination PrimaryDestination,
    SettingsSection? SettingsSection = null,
    object? Parameter = null)
{
    public static AppNavigationEntry CreatePrimary(AppPrimaryDestination destination)
    {
        return destination switch
        {
            AppPrimaryDestination.Library => new AppNavigationEntry(AppPageKind.Library, destination),
            AppPrimaryDestination.Settings => new AppNavigationEntry(AppPageKind.SettingsHome, destination, Navigation.SettingsSection.Home),
            _ => throw new ArgumentOutOfRangeException(nameof(destination))
        };
    }

    public static AppNavigationEntry CreateSettings(SettingsSection section)
    {
        return section switch
        {
            Navigation.SettingsSection.Home => CreatePrimary(AppPrimaryDestination.Settings),
            Navigation.SettingsSection.TtsRules => new AppNavigationEntry(AppPageKind.TtsRules, AppPrimaryDestination.Settings, section),
            Navigation.SettingsSection.ChapterRules => new AppNavigationEntry(AppPageKind.ChapterRules, AppPrimaryDestination.Settings, section),
            Navigation.SettingsSection.CacheManagement => new AppNavigationEntry(AppPageKind.CacheManagement, AppPrimaryDestination.Settings, section),
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
    }
}
