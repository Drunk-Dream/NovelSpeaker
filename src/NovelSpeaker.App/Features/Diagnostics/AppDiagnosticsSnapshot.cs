namespace NovelSpeaker.App.Features.Diagnostics;

public sealed record AppDiagnosticsSnapshot(
    string AppName,
    string AppVersion,
    string Description,
    int DatabaseSchemaVersion,
    string AppDataDirectoryPath,
    string LogsDirectoryPath);
