# Epic B+C 小说导入与章节拆分设计

## 背景

`docs/11_TASK_BACKLOG.md` 中的 Epic B 覆盖 TXT 小说导入，Epic C 覆盖章节拆分与文本分段的前半部分。本设计将两者合并为一个纵向切片：TXT 文件导入时完成编码识别、文本规范化、重复检测、章节拆分，并将 `Books` 与 `Chapters` 一次性写入数据库。

当前仓库已经具备：

- WPF + MVVM 的最小应用骨架。
- 依赖注入与应用启动流程。
- SQLite 连接工厂与显式迁移执行器。
- 应用数据目录抽象。

当前仓库尚未具备：

- 小说导入应用服务。
- `Books` 与 `Chapters` 业务表。
- 章节规则持久化与默认规则导入。
- 编码检测、文本规范化、重复检测与章节切分。
- 文件选择器、拖放导入与编码预览交互。

本设计只覆盖导入与章节拆分链路，不提前实现文本分段、HTTP TTS、播放协调器、缓存或阅读进度。

## 目标

本次实现完成后，系统应具备以下能力：

- 用户可通过文件选择器或拖放导入本地 TXT。
- 导入默认走自动编码检测：BOM、严格 UTF-8、GB18030 回退。
- 编码检测失败或用户主动切换时，可进入编码预览与重试。
- 导入过程中计算源文件 SHA-256，并阻止重复导入。
- 导入时完成文本规范化与章节拆分。
- 未识别出有效章节时，回退为单章节导入，不创建半成品书籍记录。
- 原始 TXT 复制到应用数据目录。
- `Books` 与 `Chapters` 在单个事务中落库。
- 系统维护一个全局章节规则库，支持新增、修改、删除、启用、禁用、排序。
- 应用首次初始化与用户手动操作时，都可从硬编码默认规则导入到数据库。

## 非目标

本次实现明确不做：

- 按空行或固定长度伪造章节。
- `ITextSegmenter` 与句级分段。
- 阅读进度恢复。
- 书籍元数据编辑界面以外的复杂书库管理。
- 批量导入事务合并。
- EPUB、PDF、MOBI 导入。

## 已确认的产品约束

- 未识别出有效章节标题时，回退为标题 `全文` 的单章节。
- `SHA-256` 相同的文件阻止重复导入，并提示“已存在”。
- 编码预览不是默认步骤；只有自动检测失败或用户主动切换时才使用。
- `Books.Title` 默认取文件名去扩展名。
- `Books.Author` 初始留空，后续允许用户编辑。
- 数据库中的章节规则全部等价，不区分“内置规则”和“用户规则”。
- 默认规则只存在于代码中；首次建表和“导入默认规则”接口调用时写入数据库。
- “导入默认规则”采用保守行为：已存在完全相同的 `(Name, Pattern)` 则跳过，否则新增；不修改已有规则，不恢复启用状态，不删除现有规则。

## 方案选型

评估了三种方案：

### 方案 A：严格同步式单阶段导入

一次调用中串行完成分析与提交。

优点：

- 实现最直接。
- 中间对象最少。

缺点：

- 编码预览与失败原因回显不自然。
- 分析失败与提交失败容易耦合。
- 后续扩展拖放、批量导入、导入确认时更难演进。

### 方案 B：两阶段导入，推荐

先分析，后提交。

优点：

- 将“可预览、可取消、可失败说明”和“真正落库”分离。
- 适合自动导入优先、异常时预览兜底的交互模型。
- 更利于测试分析结果与提交行为。

缺点：

- 需要更多中间模型。

### 方案 C：仓储内聚式导入

由仓储直接接收文件路径并驱动全部流程。

优点：

- 接线较快。

缺点：

- 解析、规则、文件系统、事务和查询职责混杂。
- 不符合当前分层与边界要求。

### 结论

采用方案 B。它最符合现有分层结构，也最容易把失败原因、事务提交和 UI 交互清晰分离。

## 整体架构

导入链路分为两个阶段：

### 阶段一：AnalyzeAsync

输入：

- `BookImportRequest`
- 可选进度回调
- `CancellationToken`

输出：

- `BookImportAnalysis`

职责：

- 读取文件头并检测 BOM。
- 按 UTF-8 BOM、UTF-16 LE BOM、UTF-16 BE BOM、严格 UTF-8、GB18030 的顺序识别编码。
- 生成编码预览所需的文本片段。
- 规范化全文。
- 计算源文件 SHA-256。
- 检查文件是否已导入。
- 读取已启用章节规则并执行章节拆分。
- 在分析过程中回传可取消进度。
- 形成可提交或可提示失败原因的中间结果。

### 阶段二：CommitAsync

输入：

- `BookImportAnalysis`
- 可选进度回调
- `CancellationToken`

输出：

- `BookImportResult`

职责：

- 为书籍与章节分配标识。
- 复制原始 TXT 到应用数据目录。
- 写入最近导入时间，并为最近播放时间预留空值。
- 在单个 SQLite 事务中写入 `Books` 与 `Chapters`。
- 失败时回滚数据库并清理本次导入产生的临时文件。

## 职责边界

建议新增以下应用层与基础设施边界。

### `IBookImportService`

职责：

- 作为 UI 调用的总入口。
- 暴露 `AnalyzeAsync` 与 `CommitAsync`。
- 编排分析与提交阶段。

不负责：

- 直接执行 SQL。
- 在 ViewModel 中承载文件与规则处理细节。

### `ITextFileAnalyzer`

职责：

- 读取文件头与文件内容。
- 执行 BOM、UTF-8、GB18030 探测。
- 生成预览文本。

不负责：

- 章节切分。
- 数据库存取。

### `ITextNormalizer`

职责：

- 统一换行。
- 清理不可见控制字符。
- 保留合理空白。

### `IContentHasher`

职责：

- 计算源文件 SHA-256。

### `IChapterRuleRepository`

职责：

- 读取、管理、排序章节规则。
- 导入默认规则。

### `IChapterSplitter`

职责：

- 接收规范化全文与已启用规则集合。
- 输出按顺序排列的章节结果。

不负责：

- 管理规则持久化。
- 操作播放器或网络。

### `IBookDuplicateDetector`

职责：

- 通过 `SourceHash` 查询是否已存在书籍。

### `IBookFileStore`

职责：

- 分配原始文件的应用数据目录路径。
- 执行复制、临时文件写入、原子重命名与清理。

### `IBookImportRepository`

职责：

- 在事务中写入 `Books` 与 `Chapters`。

不负责：

- 读取原始 TXT。
- 章节识别。

## 数据库结构

现有基础表保留：

- `SchemaVersion`
- `AppMetadata`

本次新增三张业务表。

### `Books`

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

约束建议：

- `SourceHash` 唯一索引。
- 时间字段保存 UTC ISO 8601 文本。

用途：

- 保存导入来源、原文件副本路径、编码与重复检测依据。

### `Chapters`

```text
Id TEXT PRIMARY KEY
BookId TEXT NOT NULL
ChapterIndex INTEGER NOT NULL
Title TEXT NOT NULL
Content TEXT NOT NULL
StartOffset INTEGER NOT NULL
Length INTEGER NOT NULL
FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE
UNIQUE(BookId, ChapterIndex)
```

约束建议：

- `ChapterIndex >= 0`
- `StartOffset >= 0`
- `Length > 0`

用途：

- 保存切分后的章节内容与其在规范化全文中的偏移范围。

### `ChapterRules`

```text
Id TEXT PRIMARY KEY
Name TEXT NOT NULL
Pattern TEXT NOT NULL
SortOrder INTEGER NOT NULL
IsEnabled INTEGER NOT NULL
CreatedAt TEXT NOT NULL
UpdatedAt TEXT NOT NULL
```

约束建议：

- `SortOrder` 支持显式排序。
- `IsEnabled` 使用 `0/1`。

用途：

- 保存全局共享的章节识别规则库。
- 所有规则一视同仁，不区分来源。

## 标识与路径策略

建议 `Books.Id` 与 `Chapters.Id` 由应用层生成 `Guid` 字符串。

原因：

- 无需依赖 SQLite 自增主键。
- 可在写库前分配稳定目录路径。
- 便于后续缓存、进度与 UI 跳转引用。

原始文件路径建议为：

```text
%LocalAppData%\NovelSpeaker\Books\<book-id>\original.txt
```

## 默认章节规则导入

默认规则只存在于代码中，不在数据库中保留来源标记。

行为约定：

- 首次初始化 `ChapterRules` 表后，自动导入硬编码默认规则。
- 用户可通过界面或服务调用“导入默认规则”。
- 导入策略为：
  - 如果数据库中已存在完全相同的 `(Name, Pattern)`，则跳过。
  - 否则插入新规则。
  - 不修改已有规则。
  - 不恢复已有规则的启用状态。
  - 不删除任何现有规则。

这样可以保持模型简单，同时避免覆盖用户已经调整过的规则。

## 章节切分设计

章节切分在 `AnalyzeAsync` 阶段完成。

### 输入

- 规范化后的全文。
- 所有 `IsEnabled = 1` 的规则，按 `SortOrder` 排序。

### 输出

每个章节应包含：

- `ChapterIndex`
- `Title`
- `Content`
- `StartOffset`
- `Length`

### 切分原则

- 使用标题行驱动切分。
- 章节标题行作为章节标题。
- 当前标题后的正文到下一个标题前为该章内容。
- 偏移基于规范化后的全文计算。

### 失败判定

以下任一情况都视为 `NoValidChapters`：

- 未识别出任何章节标题。
- 识别出的章节标题存在，但所有章节内容在去空白后都为空。
- 章节标题或内容无法形成可落库结果。

明确不做的兜底：

- 不把整本书作为单章保存。
- 不按空行自动伪造章节。
- 不导入空的 `Books` 记录。

## 分析结果模型

建议 `AnalyzeAsync` 返回结构化结果，而不是仅靠异常驱动流程。

### `BookImportAnalysis`

建议包含：

- `Status`
- `OriginalFilePath`
- `OriginalFileName`
- `SuggestedTitle`
- `DetectedEncoding`
- `PreviewText`
- `NormalizedText`
- `SourceHash`
- `Chapters`
- `FailureReason`
- `ExistingBookId`

### 状态与失败原因

首版建议将失败原因收敛为有限集合：

- `UnsupportedEncoding`
- `DuplicateBook`
- `NoValidChapters`
- `FileReadFailed`
- `TextNormalizationFailed`

好处：

- UI 可以稳定映射错误提示。
- 测试可直接断言失败分类。
- `CommitAsync` 可明确要求只接收 `Status = ReadyToCommit` 的分析结果。

## 提交流程与事务边界

建议 `CommitAsync` 采用以下顺序：

1. 校验分析结果可提交。
2. 生成 `BookId` 与全部 `ChapterId`。
3. 将原始 TXT 复制到目标目录的临时文件。
4. 开启 SQLite 事务。
5. 插入 `Books`。
6. 批量插入全部 `Chapters`。
7. 提交事务。
8. 将临时文件原子重命名为最终 `original.txt`。

失败处理要求：

- 分析阶段失败：不写数据库，不复制文件。
- 数据库写入失败：回滚事务，删除临时文件。
- 文件复制或重命名失败：视为导入失败，不留下无效数据库记录。

实现目标是保证数据库记录与原始文件副本要么一起成功，要么一起不存在。

## UI 交互流程

### 正常导入

1. 用户通过文件选择器或拖放提供 TXT。
2. ViewModel 调用 `AnalyzeAsync`。
3. 若分析成功，直接调用 `CommitAsync`。
4. 导入成功后刷新书库列表并选中该书。

### 编码异常或人工切换

1. 自动分析失败于编码识别，或用户主动请求切换编码。
2. UI 打开编码预览与编码选择界面。
3. 用户用指定编码重新分析。
4. 成功则继续提交，失败则展示失败原因。

### 重复文件

- 提示“已存在”。
- 不进入提交阶段。

### 无有效章节

- 提示“未识别到有效章节，请检查章节规则”。
- 可提供跳转到规则管理入口。

## 章节规则管理

首版建议提供一个简单页面或对话框，支持：

- 查看全部规则。
- 新增规则。
- 修改规则。
- 删除规则。
- 启用或禁用规则。
- 调整顺序。
- 导入默认规则。

管理界面只操作 `ChapterRules`，不直接耦合书籍导入逻辑。

## 测试策略

本次实现至少覆盖以下测试。

### 编码与文本处理

- BOM 检测。
- 严格 UTF-8 成功与失败路径。
- GB18030 回退。
- 文本规范化行为。

### 去重与分析结果

- 相同文件 `SHA-256` 被识别为重复。
- 无有效章节时返回 `NoValidChapters`。
- 失败结果不会进入提交阶段。

### 章节规则与切分

- 多条规则按 `SortOrder` 顺序匹配。
- 启用与禁用会影响切分结果。
- 导入默认规则时，已存在完全相同 `(Name, Pattern)` 会跳过。

### 提交与事务

- `Books` 与 `Chapters` 在一次事务中落库。
- 任一写入失败时事务回滚。
- 临时文件在失败时被清理。

### 样本文件

建议新增至少以下 TXT 测试样本：

- UTF-8 with BOM
- UTF-8 without BOM
- GB18030
- 正常章节格式样本
- 章节标题边缘样本
- 无章节样本
- 重复导入样本

## 实现顺序

建议按以下顺序推进：

1. 新增数据库迁移：`Books`、`Chapters`、`ChapterRules`。
2. 实现默认章节规则硬编码定义与首次导入。
3. 实现文件分析、编码检测、文本规范化与 SHA-256。
4. 实现章节规则仓储与章节切分器。
5. 实现 `IBookImportService.AnalyzeAsync` 与 `CommitAsync`。
6. 接入文件选择器与拖放导入。
7. 增加编码预览兜底交互。
8. 实现章节规则管理界面。
9. 补齐单元测试与集成测试。

## 风险与取舍

- 首版将章节正文直接存入 SQLite，超大 TXT 可能放大数据库体积；当前以实现简单与事务一致性优先。
- 不做无章节回退会提高失败率，但符合当前产品要求，也能避免后续播放链路建立在不可靠章节结构上。
- 默认规则导入采取只增不改策略，可能导致用户多次导入后规则集合增长；这是当前“所有规则等价”模型下有意接受的简单性取舍。
- 章节规则使用正则表达式，后续需要注意灾难性回溯风险；实现阶段应加入超时或复杂度控制策略。

## 结论

本设计将 Epic B 与 Epic C 的前半部分合并为一个清晰的导入纵向切片：先分析，后提交；导入时直接完成章节切分并落 `Books` 与 `Chapters`；全局共享一个可管理的章节规则库；默认规则仅作为代码内置定义，在首次初始化和手动导入时写入数据库。该方案符合当前仓库分层与产品约束，并为后续分段、播放、进度恢复和规则管理界面提供稳定基础。
