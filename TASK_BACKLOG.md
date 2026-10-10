# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：Book / Source / CurrentCatalog / DB 基础重构

- **规划基线：** `dev` @ `4ed4c4750bbdd72d1f7978caad7a1c22d6084219`（`test: close permanent coverage reduction phase`）。执行时以本地实际 HEAD 为准复核，不回退用户后续合法修改。
- **阶段目标：** 把当前已经部分 Source 化但仍使用“Source-owned Catalog / 可编辑 Book metadata”语义的实现，收敛到新的 Book Identity + BookSourceBinding + Active Source + CurrentCatalog + Book-level ReadingState 模型，并让现有 Local TXT 书籍功能稳定运行。
- **本轮实现：** 领域模型、Application 边界、SQLite schema/migration、Local TXT 导入与元数据确认、书库/详情/播放/进度/删除等本地书主链路。
- **本轮不实现：** Online Source Definition 规则 schema、Legado 兼容、在线书源编辑器、全局搜索、SearchSession、RefreshSession、在线目录获取、在线正文获取、Online Text Cache 运行时或换源 UI。
- **长期合同：** `docs/specs/BOOK_DATA_MODEL.md`、`docs/specs/BOOK_IMPORT.md` 为本阶段首要领域事实；数据迁移遵守 `docs/05_DATA_AND_COMPATIBILITY.md`。
- 当前 v12 SQLite 已包含 `BookSources / LocalBookSources / Chapters(SourceId)`，但这是旧语义的中间形态。本阶段允许追加一次新的 schema migration，把它迁到 CurrentCatalog 模型；不得修改已发布 v1–v12 migration。

## 2. 执行规则

- `[ ]` 未开始；`[-]` 进行中；`[x]` 已完成；`[!]` 仅用于发现超出已授权边界且必须由用户决策的问题。
- 按 **T001 → T005** 串行执行。
- **T001–T003 属于一个 staged breaking migration window。** 这些切片允许在 task spec 明确列出的 Book/Source/Catalog/Import 调用方上暂时处于中间破坏态，不得为保持旧调用方编译而新增长期 Compat/V2/forwarder。
- **T004 必须关闭 staged breaking migration window**：恢复本地书核心用户路径、Release build 与对应 focused tests。
- **T005 是最终收口**：清理旧模型残留、复核核心回归、执行标准完整门禁。
- staged window 不等于跳过验证：每个任务仍必须完成 task spec 声明的静态检查、结构验证或 focused tests。
- 不为本轮设新的“测试数量目标”。只按 `docs/08_QUALITY_AND_TESTING.md` 保留/新增高价值核心测试；若临时验证测试不值得永久保留，任务结束前删除。
- 人工验收始终可选，不阻塞 Agent 连续执行。
- 所有文本文件保持 LF。

## 3. 本轮固定架构决策

以下是已批准边界，执行任务时不要重新讨论或另建第二套解释：

- 产品语义上，规范化 `Title + Author` 唯一确定 Book；BookId 是技术主键。
- Title / Author 只在入库前确认，入库后不提供普通编辑能力。
- 一个 Book 最多一个 Local Binding；未来允许多个 Online Binding；最多一个 Active Binding。
- Source Definition 与 BookSourceBinding 是两个概念；当前不实现 Online Source Definition 规则系统。
- SQLite 只保存 Active Source 对应的一份 CurrentCatalog，不保存所有 Binding 的 Catalog。
- Local Source 切回时重新解析目录，而不是读取一份长期持久的 Local Catalog。
- ReadingProgress 属于 Book；Source/Catalog 变化只按 ordinal/position 做边界截断，不做章节匹配。
- Local 正文是持久业务数据，不属于 Cache。
- Future Online 正文缓存与 Audio Cache 分离，本轮只留边界，不实现。
- 不建立 Source 自动 fallback、跨 Source Chapter Identity、模糊 Book/章节匹配或长期旧模型 compatibility layer。

## 4. 任务安排

### Phase A — 领域边界切换

- [x] **T001（P0）— 收敛 Book / Binding / CurrentCatalog 领域与 Application 边界**
  完成成果：建立共享 `BookIdentity`（NFC、Unicode 空白折叠、空 Author、保留大小写和标点），Book 身份字段只读；将旧 Source entity 重命名为 `BookSourceBinding / LocalBookSourceBinding`，删除 Binding metadata 真值；Chapter 显式关联 `BookId + SourceBindingId`，增加完整不可变 `CurrentCatalog` 与 Book-level `ReadingState.Clamp`（空目录未定位）。Application 导入 port 改为唯一身份查询和可空 CurrentCatalog 提交，正文 port 改为 Book 当前目录 entry/context；删除 Title/Author 编辑 contract、use case、SQLite store、注册和旧编辑服务测试，未增加兼容层或 Online runtime。
  自动验收：Domain Release 编译与全部 34 个 Domain 测试通过，新增身份规范化、阅读边界截断及目录快照完整性核心覆盖；Domain/tests 格式验证、改动 Application 文件空白格式检查与 Domain 无外层/WPF 依赖静态检查通过。既有其它核心测试保留。无环境限制；本任务按 staged window 局部门禁验收，未执行全解决方案完整门禁。
  迁移缺口：Application Release 编译当前有 11 个错误，全部位于允许暂时失效的 `DirectBookImportService`（旧候选查询、Source/LocalSource 属性、旧 entity/Chapter 构造及 ActiveSourceId），由 T003 接回。静态确认 T002–T004 仍需接回 Books persistence、SourceContentReader/正文导出、BookDetails/Library/playback metadata query、详情身份编辑 UI 与对应旧模型测试；不得以兼容 API 恢复旧模型。T004 关闭 breaking window，T005 执行完整门禁。未发现长期合同冲突。

### Phase B — 持久化模型切换

- [x] **T002（P0）— SQLite v13 迁移到 Book Identity + Binding + CurrentCatalog**
  完成成果：追加 v13 migration，复用领域 `BookIdentity` 计算唯一规范身份；重建 Book / Binding / CurrentCatalog 关系，保留 BookId、BindingId、ChapterId、正文路径和正式进度、Speech Plan、Audio Cache 数据。身份冲突、缺失 typed data 或歧义目录返回可识别 `IncompatibleBookLibraryException` 并原子回滚；提交前检查外键。数据库约束保护 Active Binding 所属 Book、Local 单例与当前目录 ordinal，删除最后一个 Binding 级联删除 Book。导入 repository 支持唯一身份查询、活动目录原子替换和非活动 Local 仅更新正文；书库/详情/播放查询、正文读取与导出、进度、缓存、删除恢复和路径迁移全部切到新 schema。删除 Binding metadata 真值与旧候选查询、Source-owned Catalog 运行时路径，未留双读双写或新增 Online runtime。
  自动验收：新增 11 个 v13 核心集成用例，覆盖正常升级与重启幂等、正式数据/文件保留、身份冲突/歧义状态回滚、SQL/回调/取消失败后的恢复、约束及原子快照提交；保留 v12 历史迁移测试，把旧读取 smoke 合并到 v13 升级测试，更新通用迁移测试至当前 schema。35 个 persistence focused tests 和全部 34 个 Domain 测试通过。临时验证项目仅排除待 T003 接回的 `DirectBookImportService` 及 Application 两个依赖它的注册入口，使用真实 Domain/Application/Infrastructure 分层项目完成 Release 编译；验证项目及排除配置已删除。格式验证、LF、`git diff --check`、已发布 v4–v12 migration 未变及 Infrastructure 无旧 schema 运行时引用检查通过。
  迁移缺口：常规 Infrastructure build 仍被既有 `DirectBookImportService` 11 个编译错误阻断，由 T003 接回；App 和其余旧模型测试由 T003/T004 集成。T004 仍须关闭 breaking window，并将兼容失败接到用户可理解的重新导入路径；T005 执行全解决方案完整门禁。本任务依 staged window 局部门禁验收，未运行标准完整门禁；无环境限制，未发现长期合同冲突。

### Phase C — Local Import 恢复

- [ ] **T003（P0）— 重接 Local TXT 导入、身份确认与原子 CurrentCatalog 提交**
  规格：`tasks/T003_LOCAL_IMPORT_IDENTITY.md`

### Phase D — 本地书主链路集成

- [ ] **T004（P0）— 恢复书库 / 详情 / 播放 / 进度 / 删除并关闭 breaking window**
  规格：`tasks/T004_LOCAL_BOOK_INTEGRATION.md`

### Phase E — 收口

- [ ] **T005（P0）— 删除旧模型残留、核心回归复核与完整门禁**
  规格：`tasks/T005_BOOK_FOUNDATION_CLOSURE.md`

## 5. 阶段完成标准

T005 完成后至少满足：

- Local TXT 从导入到播放的主链路完全运行在新模型上；
- 数据库不存在运行时 Source-owned Catalog 双模型；
- Book 身份唯一性由数据库与统一 normalization 共同保护；
- Title / Author 旧编辑入口和对应仅为旧模型服务的 use case 已删除；
- v12 正常本地书库可以有界升级并保留关键正式数据；冲突数据不会被静默合并/覆盖；
- 未实现任何本轮明确排除的 Online Source 业务功能；
- 没有为了迁移留下 Old/New/V2/Compat 双读双写；
- 标准完整门禁通过，或对确属环境限制的未执行项如实记录。
