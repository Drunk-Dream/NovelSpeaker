using NovelSpeaker.Application.Books;

namespace NovelSpeaker.TestKit.Books;

public sealed class FakeBookChanges : IBookSourceChangeSource
{
    public event EventHandler<BookCommittedChange>? Changed;

    public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

    public void Publish(BookCommittedChange change) => Changed?.Invoke(this, change);
}
