namespace NovelSpeaker.App.Navigation;

public interface IAppPageResolver
{
    object Resolve(AppNavigationEntry entry);
}
