namespace NovelSpeaker.App.Shell.Activation;

public interface IShellPlatformAdapter
{
    void ConfigureInfrastructure(ShellHostElements host);

    void InitializeNavigation(ShellHostElements host);

    void ConfigureNavigationPresenter(ShellHostElements host);
}
