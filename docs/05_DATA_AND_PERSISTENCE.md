# 数据模型与持久化

## 1. 文档定位

本文定义 NovelSpeaker 的数据所有权、当前兼容 schema 和持久化终态约束。架构重组不得改写已发布迁移、破坏现有用户数据，或把 SQLite/文件系统细节重新泄露到 Application。需要新增迁移、恢复日志或路径格式时，实施步骤写入 `TASK_BACKLOG.md`。

## 2. 数据所有权

| 数据 | 真相源 | 说明 |
|---|---|---|
| 书籍、章节元数据 | SQLite | 章节正文不存入数据库 |
| 规范化小说正文 | `Books/<book-id>/content.txt` | UTF-8，章节偏移基于此文件的字符位置 |
| 阅读进度 | SQLite | 同时保存段落索引、原始字符偏移和段内时间 |
| 章节规则、正则规则、TTS 规则 | SQLite | 保存结构化字段，不保存原始导入 JSON |
| 音频文件 | `Cache/Tts` | 数据库只保存索引和统计元数据 |
| 非敏感设置 | `settings.json` | 启动时加载为内存快照，保存采用原子替换 |
| 日志 | `Logs` | 滚动、脱敏，不包含正文或凭据 |

SQLite 与文件系统无法共享真正的原子事务。跨两者的导入、删除和缓存写入必须使用暂存、操作状态和幂等恢复协议，不能仅以“数据库事务成功”宣称整体操作原子。

## 3. 应用数据目录

```text
%LocalAppData%\NovelSpeaker\
├─ app.db
├─ settings.json
├─ Books\
│  └─ <book-id>\
│     └─ content.txt
├─ Cache\
│  └─ Tts\
│     └─ <shard>\<cache-key>.<extension>
├─ Operations\
│  └─ 跨数据库/文件操作恢复记录
└─ Logs\
```

路径要求：

- 新的持久化模型优先保存相对于应用数据根目录的 storage key，而不是任意绝对路径。
- 读取旧绝对路径记录时必须通过集中路径解析器做规范化、根目录包含检查和迁移兼容。
- 删除、移动和覆盖文件前必须确认目标位于应用数据根目录内；解析器拒绝经过现存符号链接/reparse point 的路径。
- 用户最初选择的外部 TXT 只读，导入和删除永远不能修改它。
- 路径格式调整必须新增迁移或兼容读取，不能改写已发布迁移 SQL。

## 4. 当前 SQLite 兼容基线

当前代码的 `CurrentSchemaVersion` 为 `6`，最低支持版本为 `4`。版本 `1–3` 会拒绝启动并要求清理旧数据；高于应用支持版本的数据库也必须拒绝启动，不能按旧代码继续写入。

已发布的迁移 `4`、`5` 和 `6` 是兼容资产，只能追加迁移，不能为整理代码而编辑、合并或重编号。

### 4.1 SchemaVersion 与 AppMetadata

```text
SchemaVersion
Version INTEGER PRIMARY KEY

AppMetadata
Key TEXT PRIMARY KEY
Value TEXT NULL
```

### 4.2 Books

```text
Id TEXT PRIMARY KEY
Title TEXT NOT NULL
Author TEXT NULL
OriginalFileName TEXT NOT NULL
StoredFilePath TEXT NOT NULL
SourceHash TEXT NOT NULL UNIQUE
Encoding TEXT NOT NULL
ImportedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
LastImportedAt TEXT NULL
LastPlayedAt TEXT NULL
```

`StoredFilePath` 当前为兼容字段。终态所有使用都必须经过应用存储路径解析器；后续迁移可增加 storage key，但不得让旧记录失效。

### 4.3 Chapters

```text
Id TEXT PRIMARY KEY
BookId TEXT NOT NULL REFERENCES Books(Id) ON DELETE CASCADE
ChapterIndex INTEGER NOT NULL
SortOrder INTEGER NOT NULL
Title TEXT NOT NULL
StartOffset INTEGER NOT NULL CHECK(StartOffset >= 0)
Length INTEGER NOT NULL CHECK(Length > 0)
UNIQUE(BookId, ChapterIndex)
```

`StartOffset` 和 `Length` 基于规范化 `content.txt` 的字符偏移。正文不存入 SQLite。

### 4.4 ChapterRules

```text
Id TEXT PRIMARY KEY
Name TEXT NOT NULL
Pattern TEXT NOT NULL
SortOrder INTEGER NOT NULL
IsEnabled INTEGER NOT NULL
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

### 4.5 RegexReplacementRules

```text
Id TEXT PRIMARY KEY
Name TEXT NOT NULL
IsEnabled INTEGER NOT NULL
SortOrder INTEGER NOT NULL
Pattern TEXT NOT NULL
Replacement TEXT NOT NULL
Scope TEXT NOT NULL              -- Display / Speech / Both
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

### 4.6 HttpTtsRules

```text
Id INTEGER PRIMARY KEY
Name TEXT NOT NULL
Url TEXT NOT NULL
ContentType TEXT NULL
ConcurrentRate TEXT NULL
Header TEXT NULL
RequestOptionsJson TEXT NULL
LastUpdateTime INTEGER NULL
IsEnabled INTEGER NOT NULL
LastUsedAt TEXT NULL
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

当前 schema 没有 LoginInfo 或 Cookie 持久化字段。Cookie/LoginInfo 当前也不属于已实现兼容能力；若以后实现，必须单独设计结构化字段、迁移、脱敏和生命周期，不能复用不透明原始 JSON 绕过边界。

### 4.7 ReadingProgress

```text
BookId TEXT PRIMARY KEY REFERENCES Books(Id) ON DELETE CASCADE
ChapterIndex INTEGER NOT NULL
SegmentIndex INTEGER NOT NULL
CharacterOffset INTEGER NOT NULL
AudioPositionMilliseconds INTEGER NOT NULL
UpdatedAt TEXT NOT NULL
```

### 4.8 AudioCacheEntries

```text
CacheKey TEXT PRIMARY KEY
BookId TEXT NOT NULL
ChapterIndex INTEGER NOT NULL
SegmentIndex INTEGER NOT NULL
RuleId INTEGER NOT NULL
FilePath TEXT NOT NULL
ContentType TEXT NULL
FileSize INTEGER NOT NULL CHECK(FileSize >= 0)
DurationMilliseconds INTEGER NULL
CreatedAt TEXT NOT NULL
LastAccessedAt TEXT NOT NULL
Status INTEGER NOT NULL
```

索引至少包括：

- `(BookId, ChapterIndex)`，用于按书籍/章节统计和清理。
- `LastAccessedAt`，用于 LRU。

`FilePath` 与 `StoredFilePath` 一样属于兼容字段，所有访问必须通过根目录约束的路径解析器。

### 4.9 BookOperations

```text
OperationId TEXT PRIMARY KEY
Kind TEXT NOT NULL                 -- Import / Delete
Phase TEXT NOT NULL                -- Staged / DatabaseCommitted / Completed
BookId TEXT NOT NULL
PathsJson TEXT NOT NULL            -- 仅包含受约束的相对 storage key
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

完成记录保留为已终止状态，启动恢复只重放未完成记录。新导入正文和音频缓存写入相对 storage key；启动时对位于应用根目录内的旧绝对路径做惰性转换，根外或非法旧值保持原样并由所有消费入口拒绝。

## 5. SQLite 连接与迁移

SQLite 完全属于 Infrastructure：

- 连接工厂不暴露到 Application。
- 每个连接显式启用 `foreign_keys`，配置合理的 busy timeout；是否启用 WAL 由并发测试决定。
- 所有 SQL 参数化。
- 查询按用途映射为明确 read model，不建立万能动态 mapper。
- 写入和批处理使用显式事务。
- migration runner 必须拒绝低于最低支持版本和高于当前版本的数据库。
- 迁移失败回滚，不部分写入版本号。
- 时间在 Application 使用 `TimeProvider`/`DateTimeOffset`，Infrastructure 负责稳定序列化，不在用例中散落 `DateTime.UtcNow.ToString("O")`。

## 6. 小说导入

导入语义顺序：

1. 验证用户选择文件并分析编码。
2. 规范化换行与控制字符。
3. 计算源文件 SHA-256 并检查重复。
4. 按启用章节规则识别章节；无标题时使用整书回退。
5. 在应用数据目录暂存规范化正文。
6. 创建可恢复操作记录。
7. 提交 Books/Chapters 元数据事务并推进为 `DatabaseCommitted`。
8. 幂等切换正式文件并推进为 `Completed`。

任何阶段失败或进程中断后，启动恢复都必须幂等地得到以下一种结果：

- 书籍元数据和最终 `content.txt` 均存在；或
- 两者均不存在且暂存已清理。

不得留下书库记录指向缺失文件，也不得长期留下无数据库记录的正式书籍目录。

## 7. 动态分段与正则替换

段落不持久化为数据库记录。打开章节时：

```text
Stored content
  → 按章节原始偏移读取
  → 动态分段并保留原始偏移
  → 应用正则替换 Display/Speech 链
  → 生成运行时可消费段落
```

要求：

- 分段算法变化不要求迁移章节正文。
- 阅读进度以原始字符偏移作为稳定恢复锚点，段落索引用于快速定位。
- “未加载章节”和“已加载但没有可消费段落”使用显式状态区分，不能都用空集合表示。
- 正则替换不改写 `content.txt`、章节偏移或原始段落边界。

详细语义见 `12_REGEX_REPLACEMENT_PIPELINE.md`。

## 8. 音频缓存

缓存键保持现有位置相关语义：

```text
SHA256(
  bookId
  + chapterIndex
  + segmentIndex
  + ruleId
  + speakSpeed
  + finalSpeechText
)
```

并使用 `AudioCacheKey.CurrentVersion` 作为版本命名空间。不得借架构重构改为跨位置内容寻址、规则配置哈希或凭据哈希。

写入协议：

1. 在缓存目录创建唯一 `.tmp` 文件。
2. 完整复制并刷新文件。
3. 验证文件头和 NAudio 可解码性。
4. 在同卷原子切换为最终文件。
5. 最后写入或更新索引。
6. 失败时清理临时文件；启动维护修复索引/文件不一致。

缓存实现终态分为：

- 音频文件存储与路径策略。
- SQLite 缓存索引和查询。
- LRU/残留文件维护。
- Application 缓存管理用例和 UI read model。

当前播放、正在生成和正在写入的文件继续受保护。LRU 和手动清理不得绕过保护注册表。

## 9. 设置存储

当前 `settings.json` 字段为：

```json
{
  "enableLongParagraphSplitting": true,
  "longParagraphThreshold": 280,
  "defaultSpeakSpeed": 10,
  "prefetchCount": 2,
  "logLevel": "Information",
  "theme": "System",
  "bookFileNameTemplate": "{{name}} 作者：{{author}}",
  "cacheLimitBytes": 2147483648,
  "selectedTtsRuleId": null
}
```

要求：

- 设置只包含非敏感数据。
- 启动时只加载一次规范化快照；日志初始化和 DI 内消费者复用同一快照。
- 同步读取只访问内存，不通过 `GetAwaiter().GetResult()` 阻塞异步文件 I/O。
- 更新串行化并发布变更通知；旧自动保存不得覆盖新值。
- 保存使用同目录临时文件、flush 和原子替换；损坏文件保留安全恢复策略。
- JSON 无法解析时，将原文件按 UTC 时间戳改名为唯一的 `settings.json.<timestamp>[.<n>].corrupt` 备份，当前进程使用规范化默认快照；首次成功更新再原子创建新的 `settings.json`。
- `settings.json` 不保存凭据、Cookie、LoginInfo 或规则正文。

## 10. 删除与恢复

删除书籍必须协调：书籍/章节/进度记录、内部正文、可选音频缓存和当前播放保护。流程使用可恢复 journal，而不是只依赖进程内补偿列表。

要求：

- 删除前确认路径位于应用数据根目录。
- 书籍正文只允许位于对应的 `Books/<book-id>`，缓存只允许位于 `Cache`；所有删除暂存只允许位于对应的 `Operations/<operation-id>`。
- 正在播放的书籍先由 Application 停止/结束会话。
- 数据库事务只处理数据库记录；文件 staging 和恢复状态显式记录。
- 启动时扫描未完成操作并幂等完成或回滚。
- 永不删除用户外部源 TXT。
- 部分失败不得向 UI 报告完全成功。

## 11. 敏感信息

当前没有 SecretStore，TTS 规则 URL、Header、Body 等结构化字段可能以明文保存在本地 SQLite。这是已知限制。

- 普通日志、异常摘要、请求预览、Snackbar 和诊断摘要必须统一脱敏。
- 数据库、规则和小说正文不得附加到崩溃报告。
- 安全脱敏器继续识别 Cookie/LoginInfo 等敏感键，即使当前版本不执行这些功能。
- SecretStore、DPAPI 或 Windows Credential Manager 必须作为独立安全设计和迁移实现，不能在架构移动中顺带加入。

## 12. 持久化验收

- schema 4/5 升级到 6 和新库创建均通过；未来版本数据库被安全拒绝。
- foreign keys 在每个连接上生效，级联/约束有集成测试。
- 导入、删除、设置保存和缓存写入均覆盖故障注入与启动恢复。
- 被篡改的数据库路径不能导致应用数据根目录外文件被读取、移动或删除。
- Application 不引用 SQLite 类型或包。
- 所有存储操作传递取消令牌，取消不被通用 `catch` 吞掉。
- 音频缓存键、现有用户数据和阅读进度在重组后保持兼容。
