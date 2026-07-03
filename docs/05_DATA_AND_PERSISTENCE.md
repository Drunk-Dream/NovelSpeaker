# 数据模型与持久化

## 应用数据目录

建议：

```text
%LocalAppData%\NovelSpeaker\
├─ app.db
├─ settings.json
├─ Books\
│  └─ <book-id>\
│     └─ content.txt
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
LastImportedAt TEXT NULL
LastPlayedAt TEXT NULL
```

`StoredFilePath` 指向该书规范化后的 UTF-8 正文文件 `content.txt`。

### Chapters

```text
Id TEXT PRIMARY KEY
BookId TEXT NOT NULL
ChapterIndex INTEGER NOT NULL
SortOrder INTEGER NOT NULL
Title TEXT NOT NULL
StartOffset INTEGER NOT NULL
Length INTEGER NOT NULL
UNIQUE(BookId, ChapterIndex)
```

章节正文不再存入 SQLite。`StartOffset` 和 `Length` 基于 `content.txt` 中规范化后的全文字符偏移。

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
  "logLevel": "Information",
  "closeBehavior": "Exit"
}
```

敏感信息不得放在该文件中。

字段归属：

- `selectedRuleId`：TTS 规则页设置当前规则，播放页快速切换规则也写入该字段。
- `speechSpeed`：播放设置中的默认语速，播放页修改语速也写入该字段。
- `prefetchCount`：播放设置中的预取段落数量。
- `cacheLimitBytes`：缓存与数据页的缓存上限，默认 `2 GB`，最小 `256 MB`。
- `theme`：外观页主题。
- `logLevel`：诊断与关于页日志级别。
- `closeBehavior`：外观页后续规划字段；第一版暂不实现 UI。可选值为 `Exit`、`MinimizeToTray`。

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
9. 将规范化后的全文写入 `Books/<book-id>/content.txt`。
10. 在事务中写入 Books 和 Chapters 元数据。
11. 导入失败时回滚数据库和临时文件。

当前版本不兼容旧的 `SchemaVersion = 1` 本地库。升级到该版本前，需要删除 `%LocalAppData%\NovelSpeaker` 数据目录并重新导入书籍。

## 文本分段

不建议保存每个段落为数据库记录。

打开章节时，先根据 `StoredFilePath + StartOffset + Length` 从 `content.txt` 切出章节正文，再动态生成运行时段落：

```csharp
public sealed record SpeechSegment(
    int SegmentIndex,
    int StartOffset,
    int Length,
    string DisplayText,
    string SpeechText);
```

`StartOffset` 和 `Length` 指向原始规范化正文。后续正则替换启用后，只改变运行时生成的 `DisplayText` 和 `SpeechText`，不改写原始内容和章节偏移。详细设计见 `12_REGEX_REPLACEMENT_PIPELINE.md`。

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


## 正则替换持久化预留

第一版不实现正则替换。后续实现时，可将正则替换规则保存到 SQLite，而不是 `settings.json`，以便支持排序、启用状态、规则校验和未来扩展。

建议字段：

```text
RegexReplacementRules
Id TEXT PRIMARY KEY
Name TEXT NOT NULL
IsEnabled INTEGER NOT NULL
SortOrder INTEGER NOT NULL
Pattern TEXT NOT NULL
Replacement TEXT NOT NULL
Scope TEXT NOT NULL        -- Display / Speech / Both
Options INTEGER NOT NULL
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

要求：

- 规则变更不触发重新导入书籍。
- 规则变更不改写 `content.txt`。
- 规则按 `SortOrder` 稳定执行。
- 语音范围规则影响最终 `SpeechText`，从而影响音频缓存键。
- 缓存键使用最终处理后的 `SpeechText` 哈希，不使用规则配置哈希。

详细设计见 `12_REGEX_REPLACEMENT_PIPELINE.md`。

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
2 → file-backed chapter content metadata schema
```

不需要第一版引入复杂 ORM。可以使用原始 SQL 和小型映射层。
