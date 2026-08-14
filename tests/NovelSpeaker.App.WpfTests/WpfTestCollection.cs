using Xunit;

namespace NovelSpeaker.App.WpfTests;

[CollectionDefinition("WpfDispatcher", DisableParallelization = true)]
public sealed class WpfDispatcherCollection : ICollectionFixture<WpfTestHostFixture>;

public sealed class WpfTestHostFixture : IDisposable
{
    public void Dispose() => WpfTestHost.Shutdown();
}
