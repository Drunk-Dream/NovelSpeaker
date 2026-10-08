using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

internal static class BookOperationFileTrust
{
    // Recursive directory operations must validate descendants with the same root trust policy.
    public static void VerifyTree(string directory, IAppStoragePathResolver resolver, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(resolver.ResolvePath(directory));
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(current)) continue;
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                var trusted = resolver.ResolvePath(entry);
                if (Directory.Exists(trusted)) pending.Push(trusted);
            }
        }
    }
}
