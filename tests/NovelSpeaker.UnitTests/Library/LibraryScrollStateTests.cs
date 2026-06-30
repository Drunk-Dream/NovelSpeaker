using NovelSpeaker.App.Library;
using Xunit;

namespace NovelSpeaker.UnitTests.Library;

public sealed class LibraryScrollStateTests
{
    [Fact]
    public void Capture_and_restore_uses_first_visible_book_anchor()
    {
        var state = new LibraryScrollState();
        state.Capture(
        [
            new LibraryVisibleBookPosition("book-hidden", -120, -24),
            new LibraryVisibleBookPosition("book-2", 18, 180),
            new LibraryVisibleBookPosition("book-3", 200, 360)
        ]);

        var restored = state.TryGetRestoreOffset(
        [
            new LibraryVisibleBookPosition("book-1", 0, 160),
            new LibraryVisibleBookPosition("book-2", 260, 420),
            new LibraryVisibleBookPosition("book-3", 430, 590)
        ],
            out var offset);

        Assert.True(restored);
        Assert.Equal(242, offset, precision: 3);
    }

    [Fact]
    public void TryGetRestoreOffset_returns_false_when_anchor_book_is_missing()
    {
        var state = new LibraryScrollState();
        state.Capture(
        [
            new LibraryVisibleBookPosition("book-2", 24, 184)
        ]);

        var restored = state.TryGetRestoreOffset(
        [
            new LibraryVisibleBookPosition("book-7", 0, 160)
        ],
            out var offset);

        Assert.False(restored);
        Assert.Equal(0, offset);
    }
}
