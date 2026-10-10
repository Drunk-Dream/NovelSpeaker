# Book / Source / Catalog / Content 数据模型规范

## 1. 定位

本规范定义 NovelSpeaker 的统一书籍领域模型，以及 Local Source 与未来 Online Source 共用的稳定边界。

当前实施阶段只要求：

- 重构 Book / Source Binding / Current Catalog / ReadingState 的基础模型；
- 调整 SQLite，使数据库结构与该模型一致；
- 让现有 Local TXT 导入、书库、详情、播放、进度、删除等功能稳定运行在新模型上；
- 只为未来 Online Source 预留清晰边界，不实现在线书源规则、搜索、刷新、目录抓取或正文抓取。

未来 Online Source 的规则系统计划参考 Legado 的成熟经验，但 NovelSpeaker 不追求直接兼容 Legado 规则格式。规则应优先保持结构化、显式、低歧义，使 AI 可以较容易地把 Legado 规则转换为 NovelSpeaker 规则。

## 2. 全局模型

```text
Online Source Definition（未来）
        │
        │ search / resolve
        ▼
Book ───────── BookSourceBinding
 │                    │
 │                    ├─ LocalBinding [0..1]
 │                    └─ OnlineBinding [0..N]（未来）
 │
 ├─ ActiveSourceBindingId
 ├─ CurrentCatalog
 ├─ ReadingState
 └─ Content
      ├─ Local persistent content
      └─ Online text cache（未来）
```

核心原则：

- Book 是用户书架中的最高层实体。
- 产品语义上，`Title + Author` 确定一本唯一书籍；`BookId` 只是数据库技术主键。
- Source Definition 与某一本具体书解耦；Book 与来源之间通过 `BookSourceBinding` 连接。
- 一个 Book 最多一个 Local Binding，可以有多个 Online Binding，但任一时刻最多只有一个 Active Source Binding。
- 数据库只长期保存当前 Active Source 对应的一份 Current Catalog，不保存所有 Binding 的 Catalog。
- ReadingState 属于 Book，不属于 Source。
- 正文获取统一由 Content 边界负责，上层不判断正文来自本地文件还是在线缓存/网络。

## 3. Book Identity

### 3.1 产品身份与技术主键

Book 至少具有：

```text
BookId
Title
Author
NormalizedTitle
NormalizedAuthor
ActiveSourceBindingId?
Description?
ImportedAt
LastPlayedAt?
UpdatedAt
```

语义：

- `BookId` 是内部技术主键，用于数据库关联和运行时引用。
- `NormalizedTitle + NormalizedAuthor` 是产品唯一身份，数据库必须建立唯一约束。
- Title / Author 在 Book 入库时确定，之后不提供普通用户修改入口。
- 不再建立“BookId 才是真正书籍身份，书名作者只是候选匹配”的长期产品模型。
- 不建立模糊“同一本书”判定体系。

### 3.2 规范化

规范化必须保守，目标只是消除明显的表示差异，而不是猜测作品等价性。

建议长期语义：

- Unicode Normalization Form C；
- 去除首尾空白；
- 连续 Unicode 空白折叠为单个普通空格；
- Author 缺失时使用空字符串参与身份计算；
- 保留大小写、标点、括号、副标题等其它字符，不进行模糊清洗。

实现可以把规范化集中在唯一一个领域/应用组件中；查询、导入和迁移不得各自复制不同规则。

### 3.3 唯一性冲突

若历史数据库中存在多个 Book 在新规范化规则下得到相同身份：

- 不自动合并；
- 不猜测应保留哪个 Local Source；
- 不静默删除任何一本书；
- 数据库迁移必须原子失败，并进入明确的“需要重新导入书籍”兼容处理路径。

这类冲突不值得为了旧模型建立长期兼容层。

## 4. Book Metadata

Title / Author 是 Book Identity 的组成部分，入库后不可编辑。

Description、未来 Cover 等不参与 Book 唯一性，可以作为当前书籍详情快照存在于 Book 或对应 read model 中；它们允许随着当前 Active Source 的详情刷新而变化。

长期约束：

- UI 不再提供“修改书名”“修改作者”功能；
- 如果 Local TXT 的自动识别结果不正确，应在入库前确认流程中修正，或者删除后按正确规则重新导入；
- 不建立永久 metadata override 层。

## 5. Source Definition 与 BookSourceBinding

### 5.1 Source Definition

Source Definition 描述“如何访问一个来源”，它不是某一本具体书。

未来 Online Source Definition 预计至少具有：

```text
SourceDefinitionId
Name
Enabled
Rules / configuration（未来设计）
```

`Enabled` 主要影响全局搜索、刷新绑定源等主动发现流程。禁用 Definition 不等于删除已经存在的 BookSourceBinding。

当前阶段不实现 Online Source Definition 的规则 schema、持久化结构、编辑器或运行时。

### 5.2 BookSourceBinding

Binding 表示：

> 某一本 Book 与某一个具体内容来源之间的绑定关系。

通用字段至少包括：

```text
BindingId
BookId
SourceType
CreatedAt
UpdatedAt
```

Source-specific 数据使用 typed storage，不使用一个万能 JSON 字典承载所有来源类型。

### 5.3 Local Binding

每个 Book 最多一个 Local Binding。

Local Binding 至少保存：

```text
BindingId
OriginalFileName
StoredContentPath
SourceHash
Encoding
ImportedAt
LastImportedAt
```

语义：

- Local Source 是导入到 NovelSpeaker 数据目录后的持久快照；
- 用户外部 TXT 只作为导入输入，导入后可以移动、改名或删除；
- NovelSpeaker 永不写回用户外部 TXT；
- Local Source 正文属于业务数据，不属于可清理 Cache。

### 5.4 Online Binding（未来）

未来 Online Binding 预计连接：

```text
Book
+
OnlineSourceDefinition
+
该书在该在线来源中的 locator
```

例如未来可能需要：

- source definition id；
- book URL / remote id / opaque locator；
- 必要的来源级书籍定位信息。

这些字段在真正设计 Online Source 规则系统时再确定。当前阶段不得为了“先留全”而引入万能配置容器。

## 6. Active Source

Book 保存 nullable `ActiveSourceBindingId`。

规则：

- 一个 Book 任意时刻最多只有一个 Active Binding；
- Binding 的新增或更新不自动等于激活；
- 新建 Book 时，第一个成功建立的 Local Binding 可以成为 Active Binding；
- 删除 Active Binding 时不自动选择其它 Binding；
- 删除最后一个 Binding 时删除 Book；
- Source 切换是显式用户动作；
- Source 切换后停止当前播放，不自动续播；
- 不建立自动 Source fallback。

当前版本只实现 Local Source，因此正常可读 Book 实际上会使用其 Local Binding；这些规则主要用于保证下一阶段 Online Source 接入时不需要再次推翻模型。

## 7. Current Catalog

### 7.1 所有权

NovelSpeaker 只持久化当前 Active Source 对应的一份 Current Catalog。

```text
Book
├─ ActiveSourceBindingId
└─ CurrentCatalog
   ├─ SourceBindingId
   └─ Entries[]
```

Catalog 不再长期属于每一个 Binding。

因此以下模型不再成立：

```text
Book
└─ Sources[]
   └─ each Source owns persistent Catalog
```

### 7.2 Catalog Entry

通用 Catalog Entry 至少表达：

```text
ChapterId          // technical id
BookId
ChapterIndex       // 0-based ordinal
SortOrder
Title
```

Source-specific 内容定位使用 typed persistence：

- Local Catalog Entry：`StartOffset + Length`；
- Online Catalog Entry：未来使用 URL / locator 等专属数据。

ChapterId 可以继续服务：

- SQLite 外键；
- Speech Plan；
- Audio Cache；
- 当前 Catalog 内部技术引用。

但它不是跨 Source、跨 Catalog replacement 的产品级 Chapter Identity。

### 7.3 Catalog replacement

任何目录更新或 Source 切换都必须：

```text
prepare complete new catalog snapshot
→ validate
→ atomically replace CurrentCatalog
```

失败时旧 Catalog 保持完整可用。

不得边获取/边解析边把半成品逐条写进正式 CurrentCatalog。

### 7.4 Local Source 切换

未来从 Online Source 切换回 Local Binding 时：

- 不读取一份长期隐藏的 Local Catalog；
- 使用当前章节解析规则重新从 Local Source 的持久正文副本解析完整目录；
- 完整成功后替换 CurrentCatalog。

这意味着 Local Binding 长期持久化的是 Source Content，而不是一份永远存在的 Local Catalog。

## 8. Content System

上层统一使用内容获取边界：

```text
GetContent(Book, CatalogEntry)
```

或者等价的窄接口。

### 8.1 Local Content

Local Source：

```text
CurrentCatalog entry
→ local range metadata
→ stored local content file
→ chapter text
```

要求：

- Content reader 不依赖用户外部 TXT；
- Local Content 不是 Cache；
- Catalog 与正文范围必须属于同一次完整快照；
- 重新导入失败不能破坏旧正文和旧 Catalog。

### 8.2 Future Online Content

未来 Online Source：

```text
CurrentCatalog entry
→ content locator
→ local text cache lookup
   ├─ hit  → read text file
   └─ miss → fetch online → persist file + cache metadata → return
```

正文缓存身份必须能从“当前 Binding + Catalog Entry locator”稳定定位对应正文。

推荐长期唯一性：

```text
BindingId + ContentLocator
```

ChapterIndex 只表示目录顺序，不作为在线正文缓存唯一身份。

## 9. Future Online Text Cache

当前阶段只固定边界，不实现。

长期规则：

- 正文实体保存为独立文本文件；
- SQLite 只保存必要缓存元数据，例如 BindingId、ContentLocator、LocalPath、CreatedAt；
- 不使用 LRU；
- 正常 Source switching 不清理；
- Catalog refresh 不因为章节序号变化而错误复用；
- 删除 Book 或用户主动清理该 Book 正文缓存时清理；
- Binding 暂时从刷新结果中消失不要求立刻删除旧正文缓存；
- Local Source 正文永远不进入这一缓存体系。

Online Text Cache 与 Speech/Audio Cache 是两类独立数据，不强行复用一个 store/index。

## 10. ReadingState

ReadingState 属于 Book。

长期核心坐标：

```text
ChapterIndex
+ PositionInChapter
```

当前实现可以继续细分 PositionInChapter，例如：

- SegmentIndex；
- CharacterOffset；
- AudioPositionMilliseconds。

规则：

- ReadingState 不引用 SourceId / BindingId；
- ReadingState 不把 ChapterId 作为产品级位置身份；
- Source A / Source B 共用同一 Book ReadingState；
- Source switching 不做章节标题、URL、正文、Hash 或比例匹配；
- 新 Catalog 中仍存在原 ChapterIndex 时直接沿用；
- 原 ChapterIndex 超过新 Catalog 最大范围时截到最后一章；
- 章内位置超过新正文范围时截到最后一个合法位置；
- Catalog 为空时保持未定位状态，不伪造章节。

## 11. Search / Discovery（未来）

搜索属于临时会话，不直接创建持久 Book。

```text
SearchSession
→ per-source results
→ aggregation pool
→ TemporaryBook
```

聚合规则：

```text
NormalizedTitle + NormalizedAuthor
```

每一个聚合结果在产品语义上就是一本临时 Book，但不写入数据库。

TemporaryBook 可以拥有：

- Title / Author；
- 已搜索到的多个临时 Online Binding；
- TemporaryActiveSource；
- 临时详情 / Catalog。

用户点击“加入书架”时：

- 把当前已经聚合得到的全部 Source Binding 转为正式 Binding；
- TemporaryActiveSource 成为正式 ActiveSource；
- 若正式 Book 已存在，则全局搜索行为本身不自动修改该 Book 的绑定集合。

当前阶段不实现 SearchSession。

## 12. Refresh Binding List（未来）

刷新某本书的绑定源列表复用底层搜索能力，但拥有独立生命周期。

核心模型：

```text
PersistedBindings
        │
        │ unchanged while refreshing
        ▼
RefreshSession
        ↓
RefreshWorkingSet   ← incremental results
        ↓
UI live projection
```

停止时机包括：

- 所有 Source 搜索完成；
- 用户主动停止；
- 用户离开承载换源能力的页面；
- 刷新过程中用户直接选择换源。

正常停止：

- 立即停止接收新结果；
- 取消未完成 Source 搜索；
- 不等待未完成搜索返回；
- 冻结当前 Working Set；
- 提交目前已经获得的结果。

应用退出：

- 取消未完成搜索；
- 放弃尚未提交的 Working Set；
- 不为了保存刷新结果延长退出时间。

Active Online Binding 在刷新提交时受到保护，即使本轮没有重新搜索到，也保留在正式绑定集合中；不为此增加“本轮未发现”等额外持久状态或 UI 状态。

当前阶段不实现 RefreshSession。

## 13. UI 表达原则

书籍与 Source 相关 UI 遵守：

> 状态优先通过已有操作的文案、可见性和可用性表达，不重复增加同义状态文本。

例如详情页提供“加入书架”或“移出书架”按钮时，不再额外显示“已加入书架 / 未加入书架”。

当前阶段只需要删除与新身份模型冲突的 Title / Author 编辑能力；Online Source 详情、换源 UI、搜索 UI 留到后续阶段。

## 14. 删除语义

### Remove Binding

删除 Binding 时清理该 Binding 自己的 typed 数据。

如果删除的是 Active Binding：

- `ActiveSourceBindingId → null`；
- CurrentCatalog 必须失效/清理；
- 不自动选择其它 Binding。

如果删除最后一个 Binding：

- 删除整个 Book。

Future Online Text Cache 的物理清理按照正文缓存生命周期处理，不把刷新列表变化等同于用户主动 Remove Binding。

### Delete Book

删除 Book 需要协调清理：

- Book row；
- all BookSourceBindings；
- CurrentCatalog；
- Local Source persistent content；
- ReadingProgress；
- Speech Plans；
- Audio Cache index/files；
- Future Online Text Cache；
- 其它 Book-owned 派生状态。

SQLite cascade 不能替代物理文件协调。

## 15. 当前阶段的非目标

本轮明确不实现：

- Legado 规则兼容层；
- Online Source Definition 规则 schema；
- 在线书源编辑器；
- 全局搜索；
- SearchSession；
- RefreshSession；
- Online Catalog 获取；
- Online Content 获取；
- Online Text Cache 实际读写；
- 登录 / Cookie / WebView；
- JS/source variable environment；
- 自动 Source fallback；
- 跨 Source Chapter Identity；
- 模糊章节匹配。

本轮只需要保证未来实现这些能力时，不必再次推翻 Book / Binding / CurrentCatalog / ReadingState / Content 的核心边界。
