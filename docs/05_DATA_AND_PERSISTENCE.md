# 数据与持久化

## 1. 数据所有权

| 数据 | 位置 | 说明 |
|---|---|---|
| 书籍、章节、规则、进度、当前章节朗读清单、缓存索引、操作 journal | SQLite | 已发布 schema version 7；缓存重构通过追加 migration 落地 |
| 规范化正文 | `Books/<book-id>/content.txt` | 应用内部副本 |
| TTS 音频 | `Cache/Tts/...` | 数据库保存索引/统计 |
| 非敏感设置 | `settings.json` | 原子保存 |
| 日志 | `Logs/` | 脱敏 |
| 操作暂存 | `Operations/<operation-id>/` | 导入/删除/恢复使用 |

外部用户 TXT 永远不是应用可写数据。

## 2. 数据目录

```text
%LocalAppData%/NovelSpeaker/
├─ novelspeaker.db
├─ settings.json
├─ Books/
├─ Cache/Tts/
├─ Operations/
└─ Logs/
```

所有数据库记录的内部路径都必须通过集中 resolver 规范化和验证。删除、移动、覆盖前必须确认目标仍位于应用数据根目录，并拒绝可逃逸根目录的路径/reparse-point 情况。

## 3. SQLite 兼容

当前已发布数据库版本为 7。缓存重构只能通过追加 migration 落地，不能修改 version 4–6。目标核心表包括：

- `SchemaVersion` / `AppMetadata`
- `Books`
- `Chapters`
- `ChapterRules`
- `RegexReplacementRules`
- `HttpTtsRules`
- `ReadingProgress`
- `ChapterSpeechPlans`
- `ChapterSpeechPlanSegments`
- `SynthesisProfiles`
- `AudioCacheEntries`
- `BookOperations`

规则：

- 已发布 migration 只追加，不修改、合并或重新编号。
- 新库和受支持旧版本升级都必须走同一 migration runner。
- 高于当前版本的数据库安全拒绝。
- SQLite row/connection/transaction 类型不得泄露到 Application API。
- 音频缓存是可丢弃数据。新版缓存 schema 不迁移旧缓存键，不保留双读/双写或懒迁移；升级时重置旧缓存索引并通过受根目录约束的维护流程清理旧内部音频文件。

## 4. 书籍导入

导入使用可恢复流程：

```text
analyze source
  → normalize text
  → stage internal file
  → write DB metadata/chapters
  → finalize internal file
  → complete operation journal
```

失败或中断后只清理应用自己创建的暂存/内部文件，不能修改外部源文件。重复检测基于规范化输入所定义的稳定哈希策略。

## 5. 正文、动态分段与当前朗读清单

- SQLite 保存章节范围/元数据，规范化正文保存在内部文件。
- 播放或清单缺失时读取章节文本，再按当前文本设置动态分段。
- 正则替换只改变 DisplayText/SpeechText，不重写原始章节范围。
- “未加载”“已加载但无可播放段落”必须是不同状态。
- 每章只持久化一份当前有效的正文朗读清单，不保存 `TextProfile` 历史版本。
- `ChapterSpeechPlans` 保存章节正文版本、当前文本配置指纹、计划输出指纹、状态和正文段数。
- `ChapterSpeechPlanSegments` 只保存播放顺序、稳定来源身份、段类型和最终 `SpeechText` 哈希，不保存完整小说文本。
- 章节标题属于可选合成段，不写入正文朗读清单；开启或关闭“朗读标题”不得改变正文段身份。

文本配置变化时，先在内存中生成新计划：

- `PlanOutputHash` 未变化：只更新计划头中的当前文本配置指纹，不重写段表。
- `PlanOutputHash` 变化：在短事务中替换该章当前段记录。
- 任何情况下都不保留旧配置对应的整套段记录，数据库占用只随书库当前正文段总数线性增长。

## 6. 缓存身份与配置指纹

缓存身份分为三个独立层次。

### 6.1 稳定段身份

- 正文段身份至少由段类型、章节内来源起点和来源长度组成，不使用运行时 `SegmentIndex`。
- `OrderIndex` 只表示当前播放顺序，不参与正文音频复用身份。
- 章节标题使用固定的合成段类型，并以当前标题 `SpeechText` 哈希区分内容。

因此，在正文前插入或移除章节标题只改变运行时顺序，不改变任何正文段缓存身份。

### 6.2 文本配置指纹

`TextProfileFingerprint` 使用版本化规范序列化，包含：

- 分段算法合同版本。
- 长段落切分开关和阈值等会影响正文段落的设置。
- 按稳定顺序排列、会影响 `SpeechText` 的正则规则有效字段。

它只用于判断章节朗读清单是否需要重算，不进入音频缓存键。修改正则但最终段身份和 `SpeechText` 均未变化时，已有音频继续复用。

### 6.3 音频生成配置指纹

`TtsRuleFingerprint` 由规范化后的实际请求语义计算，包括 URL、请求方法、按名称稳定排序的 Header、Body、JSON 结构标记、声明 Content-Type 和 TTS 执行合同版本。规则名称、启用状态、并发限制和时间戳不影响生成结果，不进入指纹。

`SynthesisProfileFingerprint` 包含：

- 指纹 schema version。
- `TtsRuleFingerprint`。
- 语速。
- 未来会改变音频结果的音色、音调、语言、格式等扩展配置。

最终 `AudioCacheKey` 使用版本化结构，并至少包含：

- ChapterId。
- 稳定段身份。
- 最终 `SpeechText` 哈希。
- `SynthesisProfileFingerprint`。

缓存键不包含 `SegmentIndex`、`TextProfileFingerprint` 或“朗读标题”开关。规则请求语义变化后指纹变化，旧音频不会被错误复用。

### 6.4 目标 SQLite 表形状

以下结构描述缓存重构后的稳定数据职责；实际 migration 只能通过追加版本实现：

```sql
CREATE TABLE ChapterSpeechPlans (
    ChapterId TEXT NOT NULL PRIMARY KEY,
    ChapterRevisionHash BLOB NOT NULL,
    TextProfileFingerprint BLOB NOT NULL,
    PlanOutputHash BLOB NOT NULL,
    State INTEGER NOT NULL,
    BodySegmentCount INTEGER NOT NULL CHECK(BodySegmentCount >= 0),
    UpdatedAt INTEGER NOT NULL,
    FOREIGN KEY(ChapterId) REFERENCES Chapters(Id) ON DELETE CASCADE
);

CREATE TABLE ChapterSpeechPlanSegments (
    ChapterId TEXT NOT NULL,
    OrderIndex INTEGER NOT NULL,
    SegmentKind INTEGER NOT NULL,
    SourceStartOffset INTEGER NOT NULL,
    SourceLength INTEGER NOT NULL CHECK(SourceLength > 0),
    SpeechTextHash BLOB NOT NULL,
    PRIMARY KEY(ChapterId, OrderIndex),
    UNIQUE(ChapterId, SegmentKind, SourceStartOffset, SourceLength),
    FOREIGN KEY(ChapterId) REFERENCES ChapterSpeechPlans(ChapterId) ON DELETE CASCADE
) WITHOUT ROWID;

CREATE TABLE SynthesisProfiles (
    Fingerprint BLOB NOT NULL PRIMARY KEY,
    SchemaVersion INTEGER NOT NULL,
    RuleId INTEGER NOT NULL,
    RuleFingerprint BLOB NOT NULL,
    SpeakSpeed INTEGER NOT NULL,
    OptionsJson TEXT NULL,
    CreatedAt INTEGER NOT NULL
);

CREATE TABLE AudioCacheEntries (
    CacheKey BLOB NOT NULL PRIMARY KEY,
    KeyVersion INTEGER NOT NULL,
    BookId TEXT NOT NULL,
    ChapterId TEXT NOT NULL,
    SegmentKind INTEGER NOT NULL,
    SourceStartOffset INTEGER NOT NULL,
    SourceLength INTEGER NOT NULL,
    SpeechTextHash BLOB NOT NULL,
    SynthesisProfileFingerprint BLOB NOT NULL,
    FilePath TEXT NOT NULL,
    ContentType TEXT NULL,
    FileSize INTEGER NOT NULL CHECK(FileSize >= 0),
    DurationMilliseconds INTEGER NULL,
    HealthState INTEGER NOT NULL,
    ValidatedAt INTEGER NOT NULL,
    CreatedAt INTEGER NOT NULL,
    LastAccessedAt INTEGER NOT NULL,
    FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE,
    FOREIGN KEY(ChapterId) REFERENCES Chapters(Id) ON DELETE CASCADE,
    FOREIGN KEY(SynthesisProfileFingerprint) REFERENCES SynthesisProfiles(Fingerprint)
);
```

章节标题不进入 `ChapterSpeechPlanSegments`。标题缓存条目使用 `SegmentKind = ChapterTitle`、固定来源占位值和当前标题 `SpeechTextHash`；正文使用实际来源范围。新增其它合成段时分配新的 `SegmentKind`，不改变现有正文身份。

至少建立以下查询索引：

- `AudioCacheEntries(BookId, ChapterId)`：按书/章统计、清理和删除。
- `AudioCacheEntries(ChapterId, SynthesisProfileFingerprint, SegmentKind, SourceStartOffset, SourceLength, SpeechTextHash, HealthState)`：当前配置完整度聚合。
- `AudioCacheEntries(LastAccessedAt)`：LRU。

外键必须在每个 SQLite connection 上启用。`SynthesisProfiles` 只保存配置身份元数据；没有任何缓存条目引用的 profile 可由维护任务删除。缓存文件仍按书籍/章节独立保存，不做全局内容去重或引用计数。

## 7. 缓存写入与保护

缓存写入：

```text
generate validated audio
  → write staging file
  → flush/close
  → atomic move/replace
  → persist index
```

- 临时文件 owner 必须明确，失败和取消均清理。
- 正在播放、生成、预取、主动缓存写入或导出的缓存条目受保护。
- LRU 和用户清理都必须经过同一保护 registry。
- 缓存文件丢失/损坏时索引可修复，不能导致播放状态机永久失效。
- 音频在写入 `Ready` 索引前必须完成格式与可解码验证。
- 普通完整度查询信任数据库健康状态，不逐个检查文件或解码；播放命中和导出开始时执行严格验证。
- 渐进式缓存健康维护负责发现外部删除、长期未验证或损坏条目，修正索引后发布章节变化通知。

## 8. 主动缓存状态

主动缓存批次是运行时任务，不要求持久化历史任务中心。

批次的规则、语速、文本配置和稳定段清单在创建时冻结；生成出来的音频按正常新版 `AudioCacheKey` 落盘，因此即使批次完成后用户更改设置，旧缓存仍是合法的独立缓存版本，由 LRU 决定是否回收。

## 9. 缓存管理查询

Application 提供：

- 全局总占用与上限。
- 按书籍的大小、已缓存章节数。
- 按章节的大小、条目数和当前配置下的完整度。
- 按指定章节集合批量查询当前配置下的完整度，供播放页和详情页目录使用。
- 受保护条目和不可清理原因。

章节完整度基于当前章节朗读清单、当前 `SynthesisProfileFingerprint` 和 `Ready` 缓存索引计算。正文段通过稳定来源身份和最终 `SpeechText` 哈希匹配；开启朗读标题时再单独叠加标题合成段。

完整度查询不读取章节正文、不重新切段、不执行正则、不逐个检查文件存在性、不调用音频解码器，也不更新 `LastAccessedAt`。旧规则、旧语速和旧文本缓存仍计入物理占用，但不计入当前配置完整度。

当前文本配置对应的朗读清单缺失时返回“计算中”，由有所有者、可取消、有限并发的计划构建协调器补建；当前规则不可用或章节无可播放内容时返回明确状态。

UI 不直接查询 SQLite 或扫描文件系统。

章节完整度查询一次冻结当前规则和语速对应的合成配置，使用少量批量 SQL 将当前计划段与 `Ready` 缓存索引关联并按章节聚合；不得按章节或段落重复打开 SQLite 连接。正常查询路径不得触发任何文件或解码 I/O。

缓存文件和索引成功写入、失效、清理或维护淘汰后发布按书籍/章节定位的缓存变化通知。播放页和详情页
以该通知刷新当前配置完整度；通知只能在写入或清理已经提交后发布。页面对连续通知合并刷新，离开时
取消查询并注销订阅，不通过轮询推测缓存变化。

## 10. MP3 导出

导出只读取当前 `SynthesisProfileFingerprint` + 当前章节朗读清单对应的完整章节缓存。

每章输出一个 MP3：

```text
<选择的根目录>/
└─ <安全书名>/
   ├─ 001_<安全章节名>.mp3
   ├─ 002_<安全章节名>.mp3
   └─ ...
```

规则：

- 同章节多个音频段按播放顺序解码/合并并统一输出 MP3。
- 输入先统一为 44.1 kHz、双声道 float PCM，再通过 NAudio 的 Windows Media Foundation
  适配器编码为 128 kbps MP3；不对来源 MP3 做字节级拼接。
- 不同章节不合并成整书文件。
- 不完整章节不能导出；导出动作不得自动生成缺失缓存。
- 用户同时选择完整与不可导出章节时，UI 在选择目录前明确数量并请求确认；确认后只把当前判断可导出
  的章节提交给导出用例，取消则不开始导出。没有可导出章节时不打开目录选择器。
- 文件名清理 `<>:"/\\|?*`、控制字符、Windows 保留设备名、尾部空格/点，并保证最终路径长度可用。
- 已存在同名文件时生成 `name (2).mp3`、`name (3).mp3`，绝不静默覆盖。
- 导出临时文件在取消/失败时清理；用户已存在文件不纳入回滚。
- 导出开始时一次性冻结所选 TTS 规则、默认语速、当前正文朗读清单和朗读标题设置；导出期间的设置变化不改变当前操作。
- 编码前一次性验证并保护本批次全部缓存输入；经 UI 过滤后提交的任一章节条目缺失或损坏时仍不创建
  部分输出，不能把管理页先前计算的完整度当作导出时的有效性证明。

## 11. 设置存储

`settings.json` 保存非敏感桌面设置，例如：

- 默认语速、预取数量、朗读标题开关、文本分段参数。
- 主题、日志等级、书名解析模板。
- 缓存上限、当前 TTS 规则选择。
- 关闭窗口行为、启动最小化到托盘。
- 迷你播放器位置和置顶状态。

设置保存使用临时文件 + 原子替换。损坏 JSON 备份为带 UTC 时间戳的 `.corrupt` 文件并回退到规范化默认值。

“当前处于迷你模式”、定时停止剩余时间、主动缓存任务进度不持久化。

## 12. 删除与恢复

删除书籍和跨数据库/文件操作使用 operation journal 或等价可恢复协议。

- 删除书籍不影响应用数据根目录外文件。
- 缓存清理不删除书籍、章节、阅读进度或规则。
- 普通音频缓存清理不删除当前章节朗读清单，避免下次目录查询重新处理整本书。
- 删除书籍前，operation journal 保存需要删除的内部正文和音频路径；数据库删除通过外键级联移除章节、阅读进度、当前朗读清单、清单段和音频缓存索引。
- 删除操作完成后，不得存在该书对应的数据库记录或内部音频文件；中断时由启动恢复继续执行，孤立文件由安全维护流程清理。
- 进程启动只恢复未完成记录；已完成记录不重复执行。
- 恢复失败输出脱敏诊断并保持可重试状态。

## 13. 隐私

- 设置、日志和操作 journal 不保存小说正文副本之外的敏感请求内容。
- 日志/异常/诊断不得写出完整 URL、Header、Body、Token 或其它凭据。
- TTS 规则中的敏感字段按规则模型正常持久化时，必须在所有展示和诊断出口脱敏。
