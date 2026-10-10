# T001 — 收敛 Book / Binding / CurrentCatalog 领域与 Application 边界

## 目标

把当前代码中“BookSource 自带 metadata + Chapter 永久归属 Source”的中间模型切换为本轮最终领域语义，为后续 SQLite 与 Local Import 迁移提供单一目标。

本任务处于 staged breaking migration window，可暂时破坏明确受影响的 Books/Import/Persistence 调用方，但不得用长期兼容层维持旧 API。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/specs/BOOK_IMPORT.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`

## 实施要求

### 1. Book Identity

建立单一 normalization 能力，长期语义严格遵守规范：

- Unicode NFC；
- trim；
- 连续 Unicode whitespace 折叠为一个普通空格；
- null/empty Author 规范为 `""`；
- 不删除标点、括号、副标题，不做模糊等价。

Book Domain/Application model 明确区分：

- `BookId`：技术主键；
- `Title / Author`：产品身份字段；
- `NormalizedTitle / NormalizedAuthor`：持久唯一键材料。

删除“Book identity 可以靠用户后续 metadata edit 改变”的领域假设。

### 2. BookSourceBinding

把通用 Source entity 收敛成 Binding 语义：

```text
BindingId
BookId
SourceType
CreatedAt
UpdatedAt
```

- Title / Author 不再是 Binding 的独立真值。
- 不为 Future Online Source 增加万能 JSON config。
- Local-only 字段继续 typed model。
- 可以保留 `SourceType.Local`，并在领域 enum 中预留清晰的 Online 类型值；不要因此实现 Online runtime。

命名可以根据现有代码最小化迁移成本，但公共语义必须明确。若继续使用 `BookSource` 类名会持续造成“Source Definition 与 Binding”混淆，应在本轮直接重命名，而不是再加 alias。

### 3. CurrentCatalog

Application/Domain 改为：

```text
Book
├─ ActiveSourceBindingId
└─ CurrentCatalog
```

Chapter/Catalog entry 不再表达“永久属于一个 Source 的 Catalog”。至少应能表达：

- technical ChapterId；
- BookId；
- producing SourceBindingId；
- ChapterIndex / SortOrder；
- Title。

Local range 继续使用 typed `LocalChapterContent` 或等价结构。

### 4. Content 边界

保留/收敛稳定的章节正文读取 port，使上层通过 `Book + CurrentCatalogEntry` 获取正文，而不是依赖 StoredContentPath。

不要在本任务实现：

- Online HTTP；
- locator runtime；
- Online cache；
- Search/Refresh。

### 5. ReadingState

Book-level ReadingState / ReadingProgress 的公开语义只依赖 ordinal + 章内位置；不要新增 ChapterId-based 阅读身份。

提供一个单一、可复用的边界截断逻辑，供后续 Source/Catalog replacement 使用。

## 允许暂时失效的调用方

在 T001 结束时，下列区域允许因下一任务尚未切换而暂时编译失败或 focused tests 失败：

- Books persistence repositories；
- DirectBookImportService 及其 import DTO；
- BookDetails/Library query 中直接依赖旧 Source metadata/Catalog shape 的代码；
- Playback content query 中直接依赖 `Chapter.SourceId` 的代码；
- 相应旧模型测试。

不得破坏 Speech/Settings/Diagnostics 等无关模块。

## 做减法

优先删除：

- 只为 Source metadata snapshot 存在的 Domain/Application DTO；
- 旧 Book metadata mutation contract 中与 Title/Author edit 强绑定的部分；
- 仅为 Source-owned Catalog 适配的 mapper/forwarder。

不要新增 `V2`、`LegacyBookSource`、`CompatCatalog` 等过渡 API。

## 验收

- 新领域类型自身可编译，或至少 Domain 项目保持可编译。
- normalization 有少量核心测试：空白/NFC/空 Author/保留标点；不要穷举 Unicode 组合。
- ReadingState clamp 有核心边界测试。
- 静态检查确认 Domain 不依赖 Infrastructure/WPF。
- 记录明确仍待 T002–T004 接回的编译/测试缺口。
