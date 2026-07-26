using Xunit;
using NovelSpeaker.Domain.Common;

namespace NovelSpeaker.App.PresentationTests;

public sealed class AppInfoTests
{
    [Fact]
    public void ProductName_is_stable()
    {
        Assert.Equal("NovelSpeaker", AppInfo.ProductName);
    }
}
