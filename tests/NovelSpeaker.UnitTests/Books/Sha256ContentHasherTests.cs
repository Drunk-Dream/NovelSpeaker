using NovelSpeaker.Infrastructure.Books.Text;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class Sha256ContentHasherTests
{
    [Fact]
    public async Task ComputeFileHashAsync_returns_stable_hex_hash()
    {
        var filePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(filePath, "hash me");

        var hasher = new Sha256ContentHasher();
        var hash = await hasher.ComputeFileHashAsync(filePath, progress: null, CancellationToken.None);

        Assert.Equal("eb201af5aaf0d60629d3d2a61e466cfc0fedb517add831ecac5235e1daa963d6", hash);
    }
}
