# 数据模型与持久化

## 应用数据目录

建议：

```text
%LocalAppData%\NovelSpeaker\
├─ app.db
├─ settings.json
├─ Books\
│  └─ <book-id>\
│     └─ original.txt
├─ Cache\
│  └─ Tts\
│     ├─ ab\
│     │  └─ <sha256>.mp3
│     └─ ...
├─ Secrets\
└─ Logs\
```

## 数据库表

### Books

```text
Id TEXT PRIMARY KEY
Title TEXT NOT NULL
Author TEXT NULL
OriginalFileName TEXT NOT NULL
StoredFilePath TEXT NOT NULL
SourceHash TEXT NOT NULL
Encoding TEXT NOT NULL
ImportedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

### Chapters

```text
Id TEXT PRIMARY KEY
BookId TEXT NOT NULL
ChapterIndex INTEGER NOT NULL
Title TEXT NOT NULL
Content TEXT NOT NULL
StartOffset INTEGER NOT NULL
Length INTEGER NOT NULL
UNIQUE(BookId, ChapterIndex)
```

第一版可将章节正文直接存 SQLite。若后期遇到超大文件性能问题，再考虑单独内容文件。

### ReadingProgress

```text
BookId TEXT PRIMARY KEY
ChapterIndex INTEGER NOT NULL
SegmentIndex INTEGER NOT NULL
CharacterOffset INTEGER NOT NULL
AudioPositionMilliseconds INTEGER NOT NULL
UpdatedAt TEXT NOT NULL
```

### HttpTtsRules

```text
Id INTEGER PRIMARY KEY
Name TEXT NOT NULL
RuleJson TEXT NOT NULL
IsEnabled INTEGER NOT NULL
LastUsedAt TEXT NULL
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

`RuleJson` 保存的是 NovelSpeaker 自有规则 JSON，而不是导入源的原始 Legado JSON。
Legado 规则只作为导入输入，导入成功后系统内部统一以转换后的规范规则作为唯一持久化和导出格式。

### AudioCacheEntries

```text
CacheKey TEXT PRIMARY KEY
BookId TEXT NULL
RuleId INTEGER NOT NULL
FilePath TEXT NOT NULL
ContentType TEXT NULL
FileSize INTEGER NOT NULL
DurationMilliseconds INTEGER NULL
CreatedAt TEXT NOT NULL
LastAccessedAt TEXT NOT NULL
Status INTEGER NOT NULL
```

## 设置存储

非敏感设置可以保存到 `settings.json`：

```json
{
  "selectedRuleId": 10001,
  "speechSpeed": 10,
  "cacheLimitBytes": 2147483648,
  "prefetchCount": 2,
  "theme": "System",
  "closeBehavior": "Exit"
}
```

敏感信息不得放在该文件中。

## 登录信息与密钥

可采用：

- Windows DPAPI。
- Windows Credential Manager。

第一版建议封装：

```csharp
public interface ISecretStore
{
    Task SetAsync(string key, string value);
    Task<string?> GetAsync(string key);
    Task DeleteAsync(string key);
}
```

规则 JSON 中只保留引用，不保留明文密钥。

## 小说导入

步骤：

1. 读取少量字节检测 BOM。
2. 严格尝试 UTF-8。
3. 回退到 GB18030。
4. 规范化换行。
5. 清除不可见控制字符，但保留合理空白。
6. 计算源文件 SHA-256。
7. 检查重复。
8. 解析章节。
9. 在事务中写入 Books 和 Chapters。
10. 复制原始文件。
11. 导入失败时回滚数据库和临时文件。

## 文本分段

不建议保存每个段落为数据库记录。

打开章节时动态生成：

```csharp
public sealed record SpeechSegment(
    int SegmentIndex,
    int StartOffset,
    int Length,
    string DisplayText,
    string SpeechText);
```

好处：

- 数据库更小。
- 分段算法更新后不必迁移大量记录。
- 可使用算法版本号管理缓存兼容性。

风险：

- 分段算法改变后旧段落索引可能漂移。

解决：

- 保存字符偏移。
- 每个分段算法定义版本号。
- 恢复时优先根据字符偏移寻找最近段落。

## 缓存写入

要求：

1. 先下载到 `<cache-key>.tmp`。
2. 完成后刷新并关闭流。
3. 可选执行基础音频验证。
4. 原子重命名为最终文件。
5. 最后写入数据库状态。
6. 启动时清理残留 `.tmp`。
7. 数据库记录存在但文件缺失时删除记录。
8. 文件存在但记录缺失时可延迟清理。

## LRU 清理

清理触发：

- 应用启动后延迟执行。
- 新缓存写入后超过上限。
- 用户手动清理。

清理顺序：

- `LastAccessedAt` 最早优先。
- 不删除当前播放文件。
- 不删除当前预取正在写入的文件。
- 清理失败写日志，但不阻止播放。

## 数据库迁移

使用简单显式迁移即可：

```text
SchemaVersion
1 → initial schema
2 → add cache duration
3 → add rule raw JSON
```

不需要第一版引入复杂 ORM。可以使用原始 SQL 和小型映射层。
