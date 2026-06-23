namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Ensures Microsoft.Data.Sqlite uses the Windows-provided winsqlite3 runtime instead of bundling e_sqlite3.
/// </summary>
internal static class SqliteRuntimeInitializer
{
    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        SQLitePCL.Batteries_V2.Init();
    }
}
