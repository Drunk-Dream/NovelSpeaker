using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Domain.UnitTests.Books;

public sealed class BookIdentityTests
{
    [Fact]
    public void Create_normalizes_nfc_and_unicode_whitespace_for_both_fields()
    {
        var identity = BookIdentity.Create(" \tCafe\u0301\u00a0\u2003续篇\r\n ", "\u3000A\t\nB\u3000");

        Assert.Equal("Café 续篇", identity.NormalizedTitle);
        Assert.Equal("A B", identity.NormalizedAuthor);
        Assert.Equal(BookIdentity.Create("Café 续篇", "A B"), identity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\u3000")]
    public void Create_missing_author_is_one_valid_identity(string? author)
    {
        Assert.Equal(BookIdentity.Create("示例", ""), BookIdentity.Create("示例", author));
    }

    [Fact]
    public void Create_preserves_case_punctuation_brackets_and_subtitles()
    {
        var identity = BookIdentity.Create("Book（上）：续篇!", "A.B");

        Assert.Equal("Book（上）：续篇!", identity.NormalizedTitle);
        Assert.Equal("A.B", identity.NormalizedAuthor);
        Assert.NotEqual(identity, BookIdentity.Create("book（上）：续篇!", "A.B"));
        Assert.NotEqual(identity, BookIdentity.Create("Book上续篇", "AB"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\u3000")]
    public void Create_rejects_empty_title(string title)
    {
        Assert.Throws<ArgumentException>(() => BookIdentity.Create(title, null));
    }

    [Fact]
    public void Book_uses_the_same_identity_keys_when_current_state_changes()
    {
        var book = new Book("book", " Cafe\u0301 ", null, "local", DateTimeOffset.UnixEpoch,
            null, DateTimeOffset.UnixEpoch);
        var updated = book with { ActiveSourceBindingId = null, Description = "详情" };

        Assert.Equal("Café", updated.NormalizedTitle);
        Assert.Equal("", updated.NormalizedAuthor);
        Assert.Equal(book.Title, updated.Title);
        Assert.Equal("", updated.Author);
        Assert.Equal(book.Id, updated.Id);
    }
}
