# 数据与兼容

## 1. 数据权威来源

| 数据 | 权威来源 |
|---|---|
| 用户外部 TXT | 用户文件；只作为导入输入，应用永不写回 |
| Book 当前展示元数据、ActiveSourceId | SQLite |
| Source 元数据与 Catalog | SQLite |
| Local Source 正文 | 应用数据根中的持久 Source 内容文件 |
| 当前活动播放位置 | Playback session / PlaybackSnapshot |
| 可恢复阅读进度 | SQLite ReadingProgress |
| 章节/元数据/正则规则配置 | 对应正式持久化 store |
| Speech Provider 元数据、排序与类型配置 | 正式持久化 store |
| CurrentProvider 与其它用户设置 | Settings store |
| Speech Plan | Cache 管理的可重建派生数据 |
| 音频缓存 | Cache 管理的可重建文件 + index |
| 未来 Online Source 正文缓存 | Source-scoped 文件缓存；不进入 `app.db` |
| Active Cache / Export 运行态 | Process coordinator snapshot |
| 普通性能遥测 | 本地可删除诊断数据 |
| 诊断会话 | `.nsdiag` 会话文件 |
| 生产日志 | 本地 JSONL |

Local Source 是导入快照。导入成功后，原始 TXT 的移动、重命名或删除不得使已导入书籍失效。

## 2. 数据根与存储信任边界

正式应用数据位于程序最终解析得到的数据根。开发与自动测试使用独立数据根，避免污染真实用户数据。

显式开发/诊断覆盖必须是可识别的开发能力，不建立旧数据根的隐式探测、双读、回退或静默迁移。

应用管理的持久化路径必须经过统一的数据根解析、归属验证和信任边界检查：

- 最终选定的数据根本身是可信锚点。数据根及其祖先可以由安装器、包管理器或用户环境通过 Junction、Symlink 或其他 reparse point 映射，不得仅因为数据根本身或其祖先是 reparse point 就拒绝启动或访问。
- 数据根内部不得继续穿越未经应用管理的 reparse point。Books、Cache、数据库、日志、Telemetry、Diagnostics 等内部路径必须保持在同一逻辑数据根内，并拒绝内部链接逃逸。
- reparse-point 规则必须由统一的存储信任边界组件负责，Diagnostics、Cache、Books 等模块不得维护不同的私有规则。
- Scoop 的典型 `current\Data -> persist\novelspeaker\Data` 布局必须作为受支持场景纳入自动测试。

用户显式选择的导出目标不属于应用数据根。Windows 保存文件对话框只决定最终导出文件的位置和文件名；导出目标可以位于数据根之外，应用内部源数据位置不得要求用户手动选择。导出实现只操作用户选择的目标文件及其同目录临时文件，不借此扩大应用内部存储访问范围。

## 3. Book / Source / Catalog 持久化边界

长期数据模型以 `docs/specs/BOOK_DATA_MODEL.md` 为准。

### Book

Book 保存：

- `BookId`；
- 当前展示 Title / Author / Description / Cover 等元数据快照；
- nullable `ActiveSourceId`；
- Book 级时间状态；
- ReadingProgress 通过独立表按 BookId 关联。

Book 不保存 Local TXT 专属路径、编码、内容 hash 等 Source 私有字段。

### Source

Source 保存：

- `SourceId`；
- 所属 `BookId`；
- Source Type；
- Source 自己的元数据快照；
- 创建/更新时间等必要内部状态。

类型专属持久化使用 typed storage，而不是万能 JSON：

- Local Source 保存原始导入文件名（仅作为来源信息）、内部正文存储 key、导入时编码、内容 hash、导入时间等 Local 专属信息。
- 每个 Book 最多一个 Local Source。
- Online Source 的 identity/config/locator 等具体字段在真正实现 Online Source 时再定义；当前 schema 不提前固化 Legado 或站点规则细节。

### Catalog

Catalog 属于 Source。

- 章节记录通过 `SourceId` 归属 Source，而不是直接归属 Book。
- 章节可以保留技术性 `ChapterId`，用于 Speech Plan、音频缓存和数据库外键。
- 技术性 ChapterId 不构成产品级跨更新/跨 Source Chapter Identity。
- Local Source 的正文范围等 Local 专属内容定位信息使用 Local typed persistence；不把这类字段强加为未来 Online Source 的通用 Catalog 合同。

### ReadingProgress

ReadingProgress 继续按 BookId 保存。

- ReadingProgress 不通过 ChapterId 持久引用章节身份。
- 核心语义是 ChapterIndex + 章内位置。
- Source 切换与 Catalog 更新只做边界截断，不做内容匹配。

## 4. SQLite migration

- 已发布 migration append-only。
- 不修改、合并、删除或重编号已发布 migration。
- schema 变化必须有升级测试。
- 内部 namespace/API/目录重构不得产生无意义 migration。
- 已发布用户数据兼容与内部代码兼容是两个不同问题；项目不为内部 compatibility 长期保留 wrapper。
- migration 成功后，运行时代码只读取新 schema；不双读、不双写旧 Book/Chapter schema。

### v0.8.0 → 通用 Book/Source 模型

v0.8.0 的持久化形态把 Local TXT 专属字段直接放在 `Books`，并让 `Chapters.BookId` 直接归属 Book。本轮已批准把这些数据迁入通用 Book/Source 模型。

允许的持久化变更集合：

1. 新增 Source 基表，表达 `SourceId / BookId / SourceType / Source metadata snapshot`。
2. 新增 Local Source typed persistence，承接 v0.8.0 `Books.OriginalFileName / StoredFilePath / SourceHash / Encoding / LastImportedAt` 等 Local 专属数据。
3. `Books` 增加 nullable `ActiveSourceId`，并把当前展示元数据保留在 Book。
4. 重建/调整 `Books`，移除已经迁入 Local Source 的 Source 专属字段。
5. 重建/调整章节表，使章节 Catalog 归属 `SourceId`；保留现有技术性 ChapterId 和 ChapterIndex。
6. Local Source 专属章节内容范围可以拆入 typed Local persistence；实现应优先选择能保持未来 Source 通用性的结构，而不是继续把 Local-only 字段当全局 Catalog 合同。
7. 迁移每本 v0.8.0 本地书时创建一个 Local Source，并把它设为 ActiveSource。
8. 保留 BookId、ChapterId、ReadingProgress、ChapterSpeechPlans、音频缓存及其它可安全保持的正式数据关系。
9. 删除旧 SourceHash-on-Books 等已经失去语义的索引/约束，按新模型建立必要约束。
10. 不为旧 schema 建立长期 compatibility reader/writer。

当前 v0.8.0 已经把规范化正文保存为应用内 `Books/{BookId}/content.txt`。由于每个 Book 最多一个 Local Source，该文件可以直接成为迁移后 Local Source 的内部正文存储，不要求为了目录美观进行一次性文件搬迁。这样迁移应主要是 SQLite 数据所有权转换，避免新增跨 SQLite/文件系统的重型迁移框架。

如果实现审计证明上述有界迁移无法安全完成，且必须依赖模糊章节匹配、重新寻找用户外部 TXT、长期双模型兼容或新增复杂一次性恢复系统，则停止自动迁移方案，改为新版本要求用户重新导入本地书籍；不要为一次性兼容引入长期复杂度。

### 已有 Provider migrations

- v8 的旧 HTTP TTS Rule → Speech Provider 迁移只转换能按新合同安全表达的配置；成功后不保留旧运行路径。
- v9 增加 `EdgeSpeechProviderConfigs` 并保持 Edge 单实例约束。
- 后续 Provider schema 仍遵守 append-only migration 和 typed storage。

## 5. ReadingProgress

- 当前活动 Book 的即时位置由 Playback session/snapshot 提供。
- SQLite ReadingProgress 是重启和非活动 Book 的恢复基线。
- 页面不得直接写 ReadingProgress。
- 显式跳转成功后及时 checkpoint。
- 不进行逐毫秒高频 SQLite 写入。
- Source 切换、Local Source 更新或 Catalog 替换后，持久进度只做合法边界截断。

## 6. Local TXT 导入

典型流程：

```text
choose TXT
→ validate path
→ detect encoding
→ normalize
→ detect explicit chapter titles
→ extract filename/header metadata
→ apply chapter rules + optional blank-line chaptering
→ resolve Book candidate by strict Title + Author
→ build complete Local Source snapshot
→ atomically create/update Book + Local Source + Catalog
```

Book/Source 元数据长期支持 Title、Author、Description。Description 属于持久元数据，不从章节正文派生显示时临时计算。

文件名元数据规则、正文头部元数据规则、章节规则与“空行分章”的精确执行语义见 `specs/BOOK_IMPORT.md`。

规则/导入设置变化不静默重写已经导入的书籍；只有明确重新导入/更新 Local Source 时才使用最新配置。

重新导入失败必须保留旧 Source snapshot。成功后新 snapshot 一次性成为真值。

## 7. Query 与 read model

持久化层不向 UI 暴露“一切都有”的大型 DTO。Application 使用场景化 query：

- Library summaries；
- Book header；
- bound Source summaries；
- ActiveSource summary；
- Active Source Catalog；
- Book ReadingPosition；
- chapter content；
- Provider summaries / Provider editor model；
- audio cache status/coverage for explicit range；
- cached book/chapter summaries。

避免 N+1；稳定 Catalog 与动态 enrichment 分离。

普通页面不直接依赖 `StoredFilePath`、SQLite row shape 或 Local Source typed storage。正文读取通过 Source/content port。

## 8. Source Content 与 Cache 数据

### Local Source Content

- Local Source 正文是业务持久数据。
- 普通 Cache 清理、音频缓存清理、容量维护不得删除 Local Source 正文。
- 只有更新/移除 Local Source 或删除 Book 时才替换/删除对应持久正文。

### Audio Cache

音频 Cache 是可重建派生数据：

- 物理文件/index 可以被健康维护与清理。
- Speech Plan 只保存当前有意义的派生版本。
- Provider synthesis fingerprint 使用版本化规范序列化。
- ProviderId、名称和排序不作为音频生成身份。
- Provider 配置变化后旧音频文件允许保留；fingerprint 不匹配时只视为当前配置不可用。

### Future Online Source Content Cache

当前不实现 Online Source，但预留以下边界：

- 在线章节正文不存入 `app.db`。
- 正文缓存使用 Source-scoped 文件存储；不另建一套必须与文件双向同步的正文缓存真值数据库。
- 生命周期与 Source 绑定一致；解除 Source 绑定时清理该 Source 正文缓存。
- Source 切换不自动清理非当前 Source 缓存。
- 暂不设容量上限、LRU 或按章节高级管理。
- 第一阶段只需要支持“清理某 Book 的全部在线正文缓存”这一粗粒度能力。
- locator 消失或改变时旧正文立即失效；只有 locator 明确一致时才允许复用。
- Local Source 正文永远不进入这一缓存体系。

## 9. Provider 与规则导入/导出

HTTP Provider 使用 NovelSpeaker 自有版本化交换格式；章节规则、正则替换规则、文件名元数据规则和正文头部元数据规则分别使用各自的版本化交换格式。

共同原则：

- 一个文件/剪贴板文档可以包含一个或多个同类型项。
- 批量导出把当前选择集写入一个文档，而不是为每项弹出独立保存对话框。
- 批量导入逐项解析与校验；单项失败不回滚其它有效项。
- 完全重复项跳过；同名不同内容按对应工作台的唯一命名/追加规则处理，不覆盖既有项。
- 新导入项保持文档中的稳定顺序并追加到对应排序末尾。
- 导入不自动改变 CurrentProvider，也不自动切换正在编辑的规则/Provider。

Provider 特有规则：

- 配置相同但名称不同的 HTTP Provider 仍新增；同名但配置不同则自动生成唯一名称后新增。
- Microsoft Edge 等不可分享的内置 Provider 不参与导入/导出。
- Provider 分享导出包含完整 HTTP 配置，可能包含 API Key、Token、Cookie 等敏感值。
- 不建立自动 Secret 分离或自动脱敏导出；导出前必须明确提醒用户检查敏感凭据。

私人配置备份与用于分享的 Provider/规则导出是两个不同语义。

## 10. 配置备份与恢复

第一版配置备份是本地、私人、版本化的完整配置快照。

### 包含

- App Settings，包括主题、播放/文本相关全局设置、实验功能状态、CurrentProvider 等正式设置；
- 全部 Speech Provider 持久配置及排序；
- HTTP Provider 中的 API Key、Token、Cookie 等敏感凭据；
- 章节规则；
- 正则替换规则；
- 文件名元数据规则；
- 正文头部元数据规则。

### 不包含

- 用户 TXT、内部 Book/Source/Catalog/Content；
- ReadingProgress；
- Speech Plan、音频缓存和 Active Cache 运行态；
- 生产日志、普通性能遥测、诊断会话或问题诊断导出；
- 临时文件和其它可重建派生数据。

### 恢复语义

- 恢复前先完整解析并校验备份文档；明显损坏或不支持的 schema 不进行部分写入。
- 恢复采用“替换当前配置快照”语义，而不是与现有配置静默合并。
- 在开始替换前明确提示当前配置会被覆盖；书籍、阅读进度和缓存不受影响。
- 跨多个持久化 store 的恢复必须有明确协调/补偿边界。
- 恢复结束后由各配置 owner 通过现有 typed change/invalidation 语义刷新运行时。
- 第一版备份文件不要求加密，但创建时必须明确提示其中可能包含敏感凭据。
- 第一版不包含 WebDAV、账号同步或远程上传。

## 11. 删除与恢复

SQLite 外键级联只能处理记录，不能代替真实文件协调。

删除 Book 时必须明确协调：

- Book / Sources / Catalog SQLite records；
- Local Source 持久正文；
- ReadingProgress；
- Speech Plan；
- 音频 cache index/file；
- 必要的恢复/补偿状态。

Source 生命周期：

- 删除非最后一个 Source 只删除该 Source 自己的 metadata / Catalog / content/cache。
- 删除 ActiveSource 时 `ActiveSourceId` 变为 None，不自动选择其它 Source。
- 删除最后一个 Source 时同时删除 Book。
- Source 删除失败不得留下“数据库已解绑但持久正文仍被业务视为有效”或相反的半提交状态。

删除当前 Speech Provider 时必须同时把 CurrentProvider 清空为 None，不自动切到其它 Provider。

任何恢复路径都不能越过数据根安全边界。

## 12. 诊断数据生命周期

生产日志、性能遥测和诊断会话属于辅助诊断数据，不是业务真值。

- 性能遥测默认关闭，历史可由用户清除。
- 普通遥测内部保留时间/容量属于实现策略，不形成用户可配置合同。
- 已结束 `.nsdiag` 不自动删除；用户通过系统文件管理器管理。
- 每个诊断会话具有用户可设的硬容量上限。
- 诊断导出包是派生交换格式，不成为第二份运行时真值。
- 诊断基础设施损坏、满盘或写入失败不得损坏业务数据。

## 13. 隐私边界

结构化日志、普通遥测和 `.nsdiag` 默认禁止记录：

- 小说正文；
- 书名和章节标题；
- 完整本地路径；
- 完整 URL / Query String；
- HTTP Header / Body；
- Token / API Key；
- Provider 试听/合成文本；
- Regex 规则正文；
- SQL 参数中的用户数据；
- 缓存音频内容。

“脱敏诊断摘要”同样不得直接输出完整应用数据目录、日志目录、导出目标路径等绝对路径。

诊断会话允许使用只在当前 Session 内有效、不可反向映射的匿名对象关联，例如 `Book #1`、`Chapter #3`。

用户主动触发“截取当前窗口”时，截图可以包含 NovelSpeaker 当前界面上的用户内容；该行为必须明确由用户主动触发，禁止自动截图或录屏。

配置备份/Provider 导出是用户主动创建的业务文件，不属于诊断脱敏输出。它们可以包含敏感 Provider 凭据，但必须在创建前清楚提示风险。

## 14. 兼容边界

必须长期兼容：

- 已发布 SQLite migration 与正式用户数据；
- 用户外部 TXT；
- 正式发布后的 Speech Provider 配置/设置与规则；
- 明确发布的 Provider/规则交换格式；
- 正式发布的配置备份格式；
- 已发布的稳定交互语义。

本轮 Book/Source 重构要求迁移后只维护新模型，不为 v0.8.0 `Books.StoredFilePath/SourceHash/Encoding` 和 `Chapters.BookId` 旧运行结构保留 compatibility wrapper。自动迁移若保持有界则保留正式用户数据；若无法在不引入复杂一次性系统的前提下安全迁移，则使用“要求用户重新导入本地书籍”的已批准 fallback。

当前开发阶段不继续承诺：

- 旧 NovelSpeaker TTS Rule 导入格式；
- Legado HTTP TTS Rule 导入兼容；
- 旧 `BookFileNameTemplate` 及其模板格式；
- `source`、`java.*` 等仅为 Legado 兼容存在的模板 API；
- 内部 namespace/class/interface；
- 未发布 Online Source 具体 schema；
- 未发布诊断内部实现；
- 临时 task/spec；
- internal JSON payload 的未发布实现细节；
- 为架构迁移存在的临时 adapter/wrapper。
