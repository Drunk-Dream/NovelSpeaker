using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Domain.UnitTests.Books;

public sealed class CurrentCatalogTests
{
    [Fact]
    public void Catalog_retains_complete_snapshot_when_preparation_buffer_changes()
    {
        var entry = new Chapter("chapter", "book", "binding", 0, 10, "第一章");
        var entries = new[] { entry };
        var catalog = new CurrentCatalog("book", "binding", entries);
        entries[0] = entry with { Title = "changed" };

        Assert.Equal(entry, Assert.Single(catalog.Entries));
    }

    [Theory]
    [InlineData("other-book", "binding", 0)]
    [InlineData("book", "other-binding", 0)]
    [InlineData("book", "binding", 1)]
    public void Catalog_rejects_mixed_ownership_or_incomplete_ordinals(string bookId, string bindingId, int ordinal)
    {
        var entry = new Chapter("chapter", bookId, bindingId, ordinal, 0, "第一章");

        Assert.Throws<ArgumentException>(() => new CurrentCatalog("book", "binding", [entry]));
    }
}
