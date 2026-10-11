# 数据与兼容

## 1. 数据权威来源

| 数据 | 权威来源 |
|---|---|
| 用户外部 TXT | 用户文件；只作为导入输入，应用永不写回 |
| Book identity / Description / ActiveSourceBindingId | SQLite |
| BookSourceBinding 与 Local Binding typed data | SQLite |
| 当前 Active Source 的 CurrentCatalog | SQLite |
| Local Source 正文 | 应用数据根中的持久正文文件 |
| 当前活动播放位置 | Playback session / PlaybackSnapshot |
| 可恢复阅读进度 | SQLite ReadingProgress |
| 章节/元数据/正则规则 | 对应正式持久化 store |
| Speech Provider 与设置 | 对应正式持久化 store |
| Speech Plan / Audio Cache | Cache 管理的可重建派生数据 |
| Future Online正文缓存 | 文本文件 + 必要 SQLite metadata；不把正文 body 存入 app.db |
| Active Cache / Export 运行态 | Process coordinator snapshot |
| 普通性能遥测 / 诊断 / 日志 | 对应本地辅助数据 |

Local Source 是导入快照。导入成功后，原始 TXT 的移动、重命名或删除不得使已导入书籍失效。

## 2. 数据根与存储信任边界

正式应用数据位于程序最终解析的数据根。开发与测试使用独立数据根。

- 数据根本身是可信锚点，其祖先或数据根可以由安装器/Scoop 通过 Junction、Symlink 等映射。
- 数据根内部不得继续穿越未经应用管理的 reparse point。
- Books、Cache、数据库、日志、Telemetry、Diagnostics 等路径使用统一信任边界组件。
- Scoop `current\Data -> persist\novelspeaker\Data` 是支持场景。
- 用户显式选择的导出目标不属于应用数据根，但导出实现不得借此扩大内部文件访问范围。

## 3. Book / Binding / CurrentCatalog 持久化边界

长期模型以 `docs/specs/BOOK_DATA_MODEL.md` 为准。

### 3.1 Books

Books 保存：

- `Id`：技术主键；
- `Title` / `Author`；
- `NormalizedTitle` / `NormalizedAuthor`；
- `Description` 等 Book 级当前详情字段；
- nullable `ActiveSourceBindingId`；
- Book 级时间状态。

数据库必须对：

```text
(NormalizedTitle, NormalizedAuthor)
```

建立唯一约束。

Title / Author 入库后不通过普通产品流程修改。

### 3.2 BookSourceBindings

Binding 保存通用关系：

```text
BindingId
BookId
SourceType
CreatedAt
UpdatedAt
```

类型专属数据使用 typed table。

Local Binding 至少保存：

- OriginalFileName；
- StoredContentPath；
- SourceHash；
- Encoding；
- ImportedAt / LastImportedAt。

每个 Book 最多一个 Local Binding。

当前 schema 不提前固化 Online Source Definition 的规则、HTTP、登录、变量或 locator 细节。

### 3.3 CurrentCatalog

SQLite **只保存当前 Active Source 对应的一份 Catalog**。

Catalog entry 至少保存：

```text
ChapterId
BookId
SourceBindingId
ChapterIndex
SortOrder
Title
```

其中 `SourceBindingId` 表示这份当前目录由哪个 Binding 产生，不能用于表示“该 Binding 永久拥有一份 Catalog”。

Local-only `StartOffset / Length` 等定位信息放在 typed LocalChapterContent persistence 中。

长期不保留 inactive Binding 的 Catalog rows。

### 3.4 ReadingProgress

ReadingProgress 继续按 BookId 保存，不持久引用 ChapterId 作为阅读身份。

- 核心语义是 ChapterIndex + 章内位置；
- Source switch / Catalog replacement 只做边界截断；
- 不做标题、内容、URL、Hash 或模糊匹配。

## 4. SQLite migration 总则

- 已发布 migration append-only；不修改、删除、合并或重编号历史 migration。
- schema 变化必须有升级测试。
- migration 成功后运行时代码只读取新 schema，不双读/双写旧结构。
- 内部 namespace/API 重构不得产生无意义 migration。
- 涉及表重建时必须执行 `PRAGMA foreign_key_check` 或等价完整性验证。
- migration 失败必须保持旧数据库事务完整，不允许部分 schema/数据提交。

## 5. v12 → CurrentCatalog 模型迁移

当前 v12 已具有 `Books / BookSources / LocalBookSources / Chapters(SourceId) / LocalChapterContents`，但其语义仍是“Source 拥有自己的 Catalog、Book 展示元数据可随 Source 变化”。下一轮已批准迁移到新的 Book Identity + Binding + CurrentCatalog 结构。

### 5.1 已批准的持久化变更集合

Codex 可以直接实施以下集合，无需再次逐字段请求授权：

1. `Books` 增加并持久化 `NormalizedTitle / NormalizedAuthor`，并建立唯一约束。
2. `Books.ActiveSourceId` 可重命名/重建为语义明确的 `ActiveSourceBindingId`。
3. 现有 `BookSources` 收敛为通用 `BookSourceBindings` 语义；删除 Title / Author / Description 等不再属于 Binding 真值的旧字段。
4. 现有 `LocalBookSources` 收敛为 Local Binding typed persistence；允许随通用表命名同步重建/重命名。
5. 重建 `Chapters`，从 Source-owned Catalog 改为 Book 当前 Catalog：章节至少关联 `BookId + SourceBindingId`，并保证一个 Book 当前只存在一套 ordinal Catalog。
6. 保留 `LocalChapterContents` 或等价 typed table 承载 `StartOffset + Length`。
7. 建立必要的 FK / unique index / trigger，使 Active Binding 必须属于同一 Book、每 Book 最多一个 Local Binding、CurrentCatalog chapter ordinal 唯一。
8. 保留现有 BookId、Local Binding/Source Id、可安全保留的 ChapterId、ReadingProgress、Speech Plan、Audio Cache 及其它仍语义有效的数据关系。
9. 保留现有应用内 normalized content 文件；不为了目录美观做一次性文件搬迁。
10. 删除已经失去语义的旧索引、列、Source-owned Catalog 查询路径和运行时兼容层。
11. 不在本轮新增 Online Source Definition/规则表、Search/Refresh 临时表或 Online 正文缓存表。

实现可以根据 SQLite 限制选择等价表名和迁移步骤，但不得改变上述领域语义。

### 5.2 Title / Author normalization migration

历史数据升级时必须通过与运行时相同的唯一 normalization component 计算身份，而不是在 SQL、导入和查询中复制三套规则。

规范化长期合同：

- Unicode NFC；
- trim；
- 连续 Unicode whitespace → 单个普通空格；
- null/empty Author → `""`；
- 保留大小写、标点、括号、副标题等其它字符。

### 5.3 历史身份冲突

若两个或更多历史 Book 在新 normalization 后产生相同 `(NormalizedTitle, NormalizedAuthor)`：

- 不自动合并 Book；
- 不移动/覆盖任一 Local Source；
- 不静默保留“第一条”；
- migration 原子失败；
- 向上层返回可识别的兼容失败，由产品进入明确的“该书库需要重新导入”处理路径；
- 不为了该罕见历史冲突增加长期 dedup/alias/compatibility 模型。

### 5.4 Current Catalog 迁移

v12 当前只实现 Local Source，因此通常可以把现有 Active Local Source 的 `Chapters` 原位迁为该 Book 的 CurrentCatalog，同时保留 ChapterId。

如果真实数据库出现与 v12 合同不一致、无法无歧义判断当前目录来源的数据：

- 不猜测 inactive/active Catalog；
- 不做模糊章节匹配；
- 迁移失败并进入重新导入路径。

## 6. Local TXT 导入持久化

典型流程：

```text
choose TXT
→ validate path / encoding
→ normalize content
→ extract metadata
→ confirm Title/Author only when rule recognition is incomplete
→ normalize Book identity
→ resolve unique Book
→ build Local Binding content snapshot
→ build CurrentCatalog candidate when Local is/will be Active
→ atomic commit
```

- 导入设置变化不静默重写已有书籍。
- 重新导入失败必须保留旧 Local content 与旧 CurrentCatalog。
- Book Identity 由最终确认后的 Title + Author 决定。
- 入库后不通过 metadata editor 修改 Title / Author。

精确规则见 `specs/BOOK_IMPORT.md`。

## 7. Query 与 read model

持久化层不向 UI 暴露大型万能 DTO。Application 使用场景化 query，例如：

- Library summaries；
- Book details header；
- bound Source summaries；
- Active Source summary；
- CurrentCatalog；
- Book ReadingPosition；
- chapter content；
- Provider summaries/editor model；
- audio cache read models。

避免 N+1；CurrentCatalog 与动态 decoration 分离。普通页面不直接依赖 `StoredContentPath`、SQLite row shape 或 Local typed storage。

## 8. Source Content 与 Cache

### 8.1 Local Source Content

- 是业务持久数据；
- Audio Cache 清理、容量维护不得删除；
- 只有 Local Binding 更新/删除或 Book 删除时替换/删除；
- 外部原 TXT 不再是运行时真值。

### 8.2 Audio Cache

Audio Cache 是可重建派生数据。Provider synthesis fingerprint、SpeechText 与稳定段身份决定缓存可用性；ProviderId/名称/排序不作为合成身份。

### 8.3 Future Online Text Cache

未来允许：

```text
OnlineTextCacheMetadata
- BindingId
- ContentLocator
- LocalPath
- 其它必要获取/校验元数据
```

正文 body 存文本文件，不存 app.db。

生命周期：

- 普通 Source switch 不清理；
- refresh catalog 不清理；
- refresh bindings 不因暂时未发现某 Binding 就即时物理清理；
- 不使用 LRU/自动淘汰；
- 删除 Book 或用户主动清理时清理；
- locator 不一致时禁止错误复用。

本轮不创建这些表或运行时实现。

## 9. 删除与恢复

SQLite 外键级联只能处理记录，不能替代真实文件协调。

删除 Book 需要协调：

- Book / Bindings / CurrentCatalog records；
- Local persistent content；
- ReadingProgress；
- Speech Plan；
- Audio Cache index/files；
- Future Online text cache；
- 必要恢复/补偿状态。

删除 Active Binding 时 ActiveSourceBindingId → null，不自动选择其它 Source；删除最后一个 Binding 时删除 Book。

任何恢复路径不得越过数据根信任边界。

## 10. 配置、Provider 与规则兼容

HTTP Provider、章节规则、正则规则、文件名元数据规则、正文头部元数据规则继续使用各自版本化正式格式。私人配置备份与用于分享的 Provider/规则导出保持不同语义。

Book/Source 本轮重构不得顺手改动这些独立格式，除非新的 Book 导入身份流程确实需要最小必要变化。

## 11. 诊断隐私

结构化日志、普通遥测和诊断会话默认禁止记录小说正文、书名、章节标题、完整本地路径、完整 URL/Query、HTTP Header/Body、Token/API Key、TTS 文本、Regex 正文、SQL 用户参数或缓存音频。

用户主动“截取当前窗口”是允许包含当前界面内容的明确例外，禁止自动截图或录屏。

## 12. 兼容边界

必须长期兼容：

- 已发布 SQLite migration 与可以安全迁移的正式用户数据；
- 用户外部 TXT；
- 正式 Speech Provider / Settings / Rules 数据；
- 正式交换/备份格式；
- 已发布稳定交互语义。

本轮迁移完成后只维护新的 Book / Binding / CurrentCatalog 模型，不为 v12 Source-owned Catalog、可编辑 Book Identity 或 Source metadata snapshot 保留长期 compatibility wrapper。

当前阶段不承诺：

- 未发布 Online Source schema；
- Legado book source rule compatibility；
- Online Search/Refresh runtime；
- 内部 namespace/class/interface；
- 临时 task/spec；
- 为架构迁移存在的 Old/New/V2 adapter。
