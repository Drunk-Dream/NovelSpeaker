# Book / Source / Catalog / Content 数据模型规范

## 1. 定位

本规范定义 NovelSpeaker 的通用书籍数据模型。目标是让当前 Local TXT 和未来可能出现的 Online Source 共用同一套 Book / Source / Catalog / Content / ReadingState 边界，同时避免为了尚未实现的在线书源提前固化 Legado、站点规则、登录、变量或正文请求细节。

当前实现范围：

- 完成 Local TXT 向该模型的迁移；
- Current version 只实现 Local Source；
- 预留多 Source、Source switching、Online content cache 的稳定边界；
- 不实现 Online Source 的具体能力。

## 2. 总体模型

```text
Book
├─ BookId
├─ DisplayMetadataSnapshot
├─ ActiveSourceId?
├─ ReadingState
└─ Sources[]
   └─ Source
      ├─ SourceId
      ├─ SourceType
      ├─ MetadataSnapshot
      ├─ Catalog
      │  └─ ChapterEntry[]
      └─ Content
```

核心所有权：

- Book 是跨 Source 稳定实体。
- Source 属于 Book。
- Catalog 属于 Source。
- Content 属于 Source / Catalog entry。
- ReadingState 属于 Book。

Book 不拥有第二套独立主目录，ReadingState 不拥有 Chapter Identity。

## 3. Book Identity

### 3.1 永久身份

`BookId` 是 Book 创建后的永久身份。

以下变化不得改变 BookId：

- Source Title / Author / Description 改变；
- ActiveSource 切换；
- Local Source 重新导入；
- Catalog 完整替换；
- 章节正文变化。

### 3.2 自动发现规则

“书名 + 作者”只用于自动发现候选 Book。

规则：

- Title 与 Author 都参与严格匹配。
- 空 Author 也作为一个明确值参与匹配。
- 不通过标题相似度、文件名相似度、正文 hash、章节列表或阅读进度进行模糊匹配。
- 0 个候选：创建新 Book。
- 1 个候选：可以自动绑定/更新该 Book 的 Local Source。
- 多个候选：不得猜测；用户明确选择目标 Book 或新建 Book。

### 3.3 显式绑定

用户显式选择“把这个 Source 绑定到某 Book”时：

- Source Title / Author 可以与 Book 当前显示值不同。
- 显式选择高于自动匹配。
- 不需要为了允许绑定而建立模糊匹配算法。

## 4. Book Display Metadata

Book 保存一份当前展示元数据快照，至少包括：

- Title；
- Author；
- Description；
- Cover（当产品引入正式 Cover 数据时）。

语义：

- 有 ActiveSource 时，Book 当前展示元数据跟随 ActiveSource 的 metadata snapshot。
- 更新非 ActiveSource 不改变 Book 当前展示元数据。
- ActiveSource=None 时保留最后一次已经投影到 Book 的显示元数据，不自动从其它 Source 选择替代来源。
- Book Display Metadata 是 UI/read model 的稳定入口，不代表 Book Identity。

## 5. Source

### 5.1 通用语义

Source 表示一本 Book 的一个内容来源。

Source 至少具有：

- SourceId；
- BookId；
- SourceType；
- Source metadata snapshot；
- created/updated 等必要内部时间状态。

一个 Book 可以有多个 Source，但：

- 同一个 Source 不允许重复绑定；
- 任一时刻最多一个 ActiveSource；
- Book 可以暂时没有 ActiveSource；
- 不建立自动 Source fallback。

SourceType 使用 typed model。不要用一个万能 JSON 字典承载所有类型配置。

### 5.2 Local Source

当前实现只需要 Local Source。

规则：

- 每个 Book 最多一个 Local Source。
- Local Source 是导入快照，不是对外部 TXT 的永久引用。
- 原始 TXT 在导入后可以移动、改名或删除，不影响 NovelSpeaker。
- Local Source 保存自己的 metadata snapshot、Catalog 和正文持久数据。
- Local Source 正文不是 Cache。
- 重新导入同一本书时更新已有 Local Source，而不是产生 `Local Source 2/3/...`。

### 5.3 Future Online Source

当前不定义 Online Source 的具体 schema。

未来实现时可以增加 typed storage 表达：

- Source identity；
- 规则/站点配置；
- book locator；
- catalog locator；
- authentication/session；
- chapter locator；
- request context。

这些字段必须在真正实现 Online Source 时根据实际模型定义，不提前为了“看起来通用”把 Local Source 或 Book 主表变成万能配置容器。

## 6. ActiveSource

- `ActiveSourceId` 属于 Book，允许为空。
- 切换 Source 必须由用户明确触发；绑定/更新一个 Source 不等于激活。
- ActiveSource 暂时不可用时不自动 fallback。
- 删除 ActiveSource 时把 ActiveSource 变为空，不偷偷选择其它 Source。
- 删除最后一个 Source 时同时删除 Book。
- 切换 ActiveSource 后停止当前播放，不自动续播。
- 切换时停止/失效旧 Source 仍在进行的正文获取、预取等上下文工作；已有合法缓存可以保留。

## 7. Catalog

### 7.1 Source ownership

Catalog 始终属于 Source。

```text
Source
└─ Catalog
   ├─ ChapterEntry 0
   ├─ ChapterEntry 1
   └─ ...
```

Book 不复制 Catalog。

### 7.2 ChapterEntry

ChapterEntry 至少表达：

- technical entry/chapter id；
- SourceId；
- ChapterIndex / stable order；
- Title；
- Source type 所需的最小内容定位数据。

Chapter technical ID 可以存在，用于：

- SQLite 外键；
- Speech Plan；
- 音频缓存；
- 稳定内部引用。

但它不是产品级“Chapter Identity”。NovelSpeaker 不尝试推断：

- 两次 Catalog 更新前后哪一章“语义相同”；
- Source A 的某章与 Source B 的哪章“对应”；
- 标题相同是否表示同一章；
- 正文相同是否表示同一章。

### 7.3 Catalog replacement

Source 更新使用完整快照替换：

```text
prepare complete new snapshot
→ validate
→ commit as one Source update
```

失败时旧 Catalog 保持完整可用。

不以“在旧 Catalog 上逐条猜测增删改”作为基本模型。

## 8. Content

### 8.1 Local Source Content

Local Source 保存完整可用正文。

实现可以采用：

```text
one normalized internal content file
+ chapter ranges
```

也可以采用其它等价的持久 Source 表示。关键合同是：

- 正文是 Local Source 业务数据；
- 不是用户外部 TXT 的活引用；
- 不是可清理 cache；
- ChapterEntry 可以一对一读取对应正文。

v0.8.0 已存在 `Books/{BookId}/content.txt` 规范化内部文件。因为每个 Book 最多一个 Local Source，该文件可以在迁移后继续由 Local Source 持有，无需仅为目录结构重排而搬迁。

### 8.2 Future Online Source Content

Online Source 正文通常按需加载。

预留合同：

- Catalog 可以存在而正文不存在。
- 正文缓存不存进 `app.db`。
- 缓存采用 Source-scoped 文件存储。
- Source switching 不清理其它 Source 合法正文缓存。
- 解除 Source 绑定时清理该 Source 正文缓存。
- locator 消失或变化时对应旧正文失效。
- 只有 locator 明确一致时才允许复用，不通过标题/位置/正文猜测。
- 当前不实现容量上限、LRU、自动淘汰或高级逐章缓存管理。
- 当前仅预留整本书在线正文缓存整体清理能力。

## 9. ReadingState

ReadingState 属于 Book。

长期核心坐标：

```text
ChapterIndex
+ PositionInChapter
```

当前实现可以继续把 PositionInChapter 分解为：

- SegmentIndex；
- CharacterOffset；
- AudioPositionMilliseconds。

规则：

- ReadingState 不引用 Chapter technical ID。
- Source A / Source B 共享同一 ReadingState。
- Source switch 不做章节匹配或进度比例换算。
- 目标 Catalog 章节数不足时，把 ChapterIndex 截到最后一个合法章节。
- 目标章节章内位置不足时，把章内位置截到最后一个合法位置。
- Catalog 更新采用相同边界处理。
- Catalog 为空时保持未定位状态，不伪造进度。

## 10. Metadata update semantics

Source metadata snapshot 是来源事实。

- Local Source 重新导入后，Source metadata 使用最新导入结果完整更新。
- 空 Description 也可以覆盖/清空旧 Description；不保留隐藏的“旧值 fallback”。
- ActiveSource metadata 更新后同步 Book Display Metadata。
- Non-active Source metadata 更新只更新 Source。
- 用户未来若直接编辑当前元数据，编辑的是当前模型中的值，不建立永久 override 层；后续 Source 更新仍可以按产品规则覆盖。

## 11. Source removal / Book deletion

### Remove Source

移除 Source 必须同时清理：

- Source row/config；
- Source Catalog；
- Source-owned persistent content；
- Source-owned future online content cache；
- 只依赖该 Source Catalog 的派生数据。

若 Source 是 ActiveSource：

- ActiveSourceId → null；
- 不自动切换其它 Source。

若这是最后一个 Source：

- 同时删除 Book。

### Delete Book

删除 Book 清理：

- all Sources；
- all Catalogs；
- Local Source content；
- future Online Source content cache；
- ReadingProgress；
- Speech Plans；
- audio cache index/files；
- 其它 Book-owned 派生状态。

真实文件删除必须与 SQLite 状态协调，不能仅依赖外键级联。

## 12. v0.8.0 migration principles

v0.8.0 当前模型：

```text
Books
├─ Book metadata
├─ Local TXT fields
└─ StoredFilePath

Chapters
└─ BookId
```

目标：

```text
Books
├─ Display metadata
└─ ActiveSourceId

BookSources
└─ LocalBookSourceData

Chapters / Catalog entries
└─ SourceId
```

迁移必须优先保持：

- existing BookId；
- existing Chapter technical IDs；
- ReadingProgress；
- ChapterSpeechPlans；
- audio cache identity/data when still valid；
- existing normalized local content file。

迁移后不保留旧运行模型双读/双写。

如果自动迁移需要引入复杂长期兼容层、章节模糊匹配、重新定位外部 TXT 或重型一次性 recovery framework，则使用已批准 fallback：不实现复杂迁移，要求用户重新导入本地书籍。

## 13. 非目标

本规范不定义：

- Legado book source rule syntax；
- Online Source 网络协议；
- WebView/login；
- cookie/session lifecycle；
- JS/source variable environment；
- 在线目录增量抓取算法；
- 自动 Source fallback；
- 跨 Source Chapter Identity；
- 模糊章节匹配；
- 高级正文缓存管理。
