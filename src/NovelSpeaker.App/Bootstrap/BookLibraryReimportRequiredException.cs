using System.IO;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>Safe startup classification; the original failure remains available to diagnostics.</summary>
internal sealed class BookLibraryReimportRequiredException(Exception innerException)
    : IOException("The local book library requires reimport.", innerException);
