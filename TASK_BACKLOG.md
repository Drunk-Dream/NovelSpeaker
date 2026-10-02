# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **v0.8.0 后的统一批量管理 + 通用 Book/Source 数据模型阶段**。

规划代码基线：`7b17e4c51d566b0640b49a33c426a61c4a6eaafa`（`main`，v0.8.0）。

上一轮 Backlog 已全部完成，本文件清空旧任务后重新开始编号。当前阶段只实现已经确认的两组长期方向：

1. 把 Library、Playback 章节、Speech Provider、Rules 的批量操作收敛为统一的页面级 Management Mode；CacheManagement 保持文件管理器式选择例外。
2. 把当前“Book 直接拥有 Local TXT/Chapters”的模型迁移为 `Book → Sources → Source-owned Catalog/Content`，当前只实现 Local Source，不实现 Online Source 具体能力。

本阶段明确不做：

- Legado/在线书源规则、目录抓取、正文请求、登录、变量系统；
- 自动 Source fallback；
- 跨 Source Chapter Identity 或模糊章节匹配；
- 高级在线正文缓存管理；
- WebDAV/云同步；
- 新一轮 Diagnostics 扩张；
- 与本阶段无关的大规模 UI/架构重写。

长期合同：

- `docs/00_PRODUCT_AND_SCOPE.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/04_CACHE_AND_BACKGROUND_WORK.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/specs/BOOK_DATA_MODEL.md`
- `docs/specs/BOOK_IMPORT.md`
- `docs/specs/BATCH_MANAGEMENT.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短“完成成果”
- `[!]` 阻塞，只记录真正需要用户决定的新产品/架构/隐私冲突；普通实现细节由 Codex 自行决定

默认按 T001 → T008 串行执行。如果调用明确要求连续执行整个 Backlog，可以按依赖顺序自动继续，不等待人工验收。

人工视觉/交互验收始终是可选补充，不阻塞任务完成或下一任务。

每个未完成任务的详细实施合同位于 `tasks/`。完成任务后：

1. 满足 task spec 中的强制自动验收；
2. 删除所有当前任务临时测试、fixture、脚本、截图和诊断产物；
3. 更新本文件状态并追加简短“完成成果”；
4. 删除对应 `tasks/Txxx_*.md`；
5. 按调用要求停止或继续下一任务，不等待人工验收。

## 3. Staged breaking migration window

T004 → T007 属于一次明确授权的 **staged breaking migration window**。

在这个窗口中：

- 不要求每个任务结束时整个应用都可运行；
- 不要求每个任务都通过完整 solution build/test；
- task spec 必须明确当前切片允许暂时失效的调用方或测试；
- 当前任务仍必须完成自己的 focused verification；
- 不为维持中间态可运行而建立旧/新 Book 模型双读、双写或 compatibility wrapper；
- T008 必须恢复完整标准门禁，阶段才算完成。

T001 → T003 不属于 breaking migration，原则上应保持仓库正常可构建。

## 4. 已批准的 Book schema 变更边界

本轮用户已经明确批准为通用 Book/Source 模型调整数据库表、删除/调整旧表字段，并要求不长期兼容旧模型。T004 可以直接实施 `docs/05_DATA_AND_COMPATIBILITY.md` 列出的 v0.8.0 → Book/Source 持久化变更集合，不需要逐字段重复请示。

如果实现需要超出该文档已列集合的新持久化概念，才按 `AGENTS.md` 停止并请求授权。

自动迁移应保持简单。当前 v0.8.0 的应用内规范化 `Books/{BookId}/content.txt` 可以继续作为 Local Source 持久正文，不为目录美观搬迁。若真实实现证明自动迁移仍必须引入模糊章节匹配、寻找外部 TXT、长期双模型兼容或新的重型一次性恢复系统，则使用已批准 fallback：要求用户重新导入本地书籍，不实现复杂迁移。

---

# Phase A：统一页面级批量管理

## [x] T001（P0）：建立统一 Management Mode 选择基础设施

目标：在现有 `DesktopSelectionController` 稳定 key 选择能力之上建立页面级 Management Mode 生命周期、visible-set reconciliation、Select All、右键语义与 Normal/Management 行为隔离的共享 primitive；不把业务动作塞进 Shared。

完成成果：新增页面级 `ManagementSelectionController<TKey>`，组合现有 stable-key 选择引擎，提供 Enter/Exit/Reset、Normal/Management 点击分流、toggle/range、Select All、visible/manageable set reconciliation 与右键选择语义；支持零选择保持模式和增量选择装饰通知。Shared 仅拥有交互状态，业务动作与 Dirty Draft 保护由 Feature 承担；CacheManagement 保持原行为。新增 10 项长期行为测试，保留既有选择与页面测试；本任务没有需要删除的旧实现或兼容层，临时实施规格已删除。locked restore、format、Release build（零警告/错误）及全量 914 项测试全部通过（零跳过），无环境受限检查或长期文档冲突；业务页面接线由 T002/T003 完成。

## [ ] T002（P1）：迁移 Library 与 Playback 章节批量管理

依赖：T001。

目标：Library 增加显式批量管理、Select All、批量导出/删除；Playback 把现有“主动缓存选择模式”改造成通用章节 Management Mode，当前批量动作仍只有 Cache，并保持 Active Cache coordinator 只负责任务执行。

详细规格：`tasks/T002_LIBRARY_AND_CHAPTER_BATCH_MANAGEMENT.md`

## [ ] T003（P1）：迁移 Speech Provider 与 Rules 批量管理

依赖：T001。

目标：把 Provider 与四类 Rule workspace 从“普通选择直接兼任多选”的现状迁移到 Normal Mode + Management Mode；保留 Dirty Draft 保护、批量导出/删除、部分跳过和 CurrentProvider 删除后变 None 的既有产品语义。

详细规格：`tasks/T003_PROVIDER_AND_RULE_BATCH_MANAGEMENT.md`

---

# Phase B：通用 Book/Source 模型

## [ ] T004（P0）：建立 Book/Source 持久化模型并完成 v0.8.0 schema migration

依赖：T003。

目标：建立 Book、Source、Local Source typed persistence、Source-owned Catalog 的最终数据结构；追加新的 SQLite migration，把现有每本本地书转换为一个 Local Source，并保留 BookId、Chapter 技术 ID、ReadingProgress、Speech Plan 与可安全保留的音频缓存关系。删除旧 Book 上已经失去语义的 Local TXT 字段/约束。

详细规格：`tasks/T004_BOOK_SOURCE_SCHEMA_MIGRATION.md`

## [ ] T005（P0）：重构 Local TXT 导入与重新导入更新

依赖：T004。

目标：直接导入改为创建 `Book + LocalSource + Catalog`；使用严格 Title+Author 自动匹配，唯一候选时更新该 Book 的唯一 Local Source，多候选时要求用户选择目标 Book 或新建；更新采用完整 snapshot 原子替换，失败保留旧 Source。

详细规格：`tasks/T005_LOCAL_SOURCE_IMPORT_UPDATE.md`

## [ ] T006（P0）：把 Query、正文读取、Playback 与 ReadingProgress 接到 ActiveSource Catalog

依赖：T004、T005。

目标：移除运行时“Book 直接拥有 StoredFilePath/Chapters”的假设；Library/BookDetails/Player/Cache/Speech Plan 通过 ActiveSource Catalog 和 Source content port 工作；ReadingProgress 保持 Book 级并在 Catalog 更新时只做边界截断。

详细规格：`tasks/T006_ACTIVE_SOURCE_RUNTIME_INTEGRATION.md`

## [ ] T007（P1）：收口 Source/Book 生命周期并删除旧模型残留

依赖：T006。

目标：完成 Local Source 更新/删除、Book 删除、ActiveSource=None 安全处理、文件/SQLite/音频缓存/ReadingProgress/Speech Plan 协调；删除旧 SourceHash-based duplicate path、Book-owned content contract 和仅为旧 schema 存在的代码/测试。

详细规格：`tasks/T007_SOURCE_LIFECYCLE_AND_LEGACY_CLEANUP.md`

---

# Phase C：最终集成收口

## [ ] T008（P0）：恢复完整可运行状态并执行阶段级验收

依赖：T001–T007。

目标：完成跨模块接线、测试收敛、架构审计和文档一致性检查；确保没有旧/新 Book 模型双路径、没有遗漏的 active-cache selection 专名、没有隐藏 selected item 参与批量操作，最终执行完整标准门禁。

详细规格：`tasks/T008_INTEGRATION_CLOSURE.md`
