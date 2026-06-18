using Xunit;
using NovelSpeaker.Domain.Common;

namespace NovelSpeaker.UnitTests.Common;

public sealed class AppInfoTests
{
    [Fact]
    public void ProductName_is_stable()
    {
        Assert.Equal("NovelSpeaker", AppInfo.ProductName);
    }
}
