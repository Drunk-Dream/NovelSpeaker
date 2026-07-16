namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Indicates that the local SQLite schema is outside the range supported by this app version.
/// </summary>
public sealed class IncompatibleDatabaseSchemaException : InvalidOperationException
{
    public IncompatibleDatabaseSchemaException(
        int detectedVersion,
        int minimumSupportedVersion,
        int currentVersion)
        : base(
            $"检测到不受支持的本地数据库版本 {detectedVersion}，当前应用支持版本 {minimumSupportedVersion} 到 {currentVersion}。数据库未被修改；请使用兼容版本的 NovelSpeaker。")
    {
        DetectedVersion = detectedVersion;
        MinimumSupportedVersion = minimumSupportedVersion;
        CurrentVersion = currentVersion;
    }

    public int DetectedVersion { get; }

    public int MinimumSupportedVersion { get; }

    public int CurrentVersion { get; }

    public int RequiredVersion => CurrentVersion;
}
