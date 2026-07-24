namespace NovelSpeaker.App.Bootstrap;

internal enum StartupStage
{
    Directories,
    Settings,
    Logging,
    DependencyInjection,
    Database,
    Theme,
    Shell
}
