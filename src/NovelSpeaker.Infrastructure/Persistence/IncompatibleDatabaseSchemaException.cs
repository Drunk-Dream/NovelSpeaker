namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Indicates that the local SQLite schema is too old for the current app version to use safely.
/// </summary>
public sealed class IncompatibleDatabaseSchemaException : InvalidOperationException
{
    public IncompatibleDatabaseSchemaException(int detectedVersion, int requiredVersion)
        : base(
            $"检测到不受支持的本地数据库版本 {detectedVersion}，当前版本需要至少 {requiredVersion}。请删除本地 NovelSpeaker 数据目录后重新启动，并重新导入书籍。")
    {
        DetectedVersion = detectedVersion;
        RequiredVersion = requiredVersion;
    }

    public int DetectedVersion { get; }

    public int RequiredVersion { get; }
}
