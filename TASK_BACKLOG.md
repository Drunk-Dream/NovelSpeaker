# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **v0.7.0 后的核心体验增强阶段**。

规划代码基线：`ea07878493a09f080b055cfee62a7e7e5e0e25ec`（`dev`）。上一轮 Speech Provider、v0.7.0 发布与诊断 hardening 已完成，本 Backlog 已清空旧任务，只保留下一阶段尚未实施的工作。

本阶段只做已经确认的高价值增强：

- 统一全项目 Current / Selected / Hover / Focus 视觉语义，并顺带优化播放页 Provider 浮窗与章节标题显示；
- 收拢播放连续失败恢复，有限重试后自动跳段，连续 3 段失败后暂停；
- 重做 TXT 导入元数据识别与“空行分章”，保持直接导入流程；
- 给章节规则、正则规则、元数据规则和 HTTP Provider 增加与批量导出配套的批量导入/多选交换体验；
- 增加第一版本地配置备份/恢复；
- 仅做轻量书库增强，增加“最近导入”排序，不增加阅读状态筛选。

本阶段明确不做：

- 在线书源；
- WebDAV/云同步；
- Local Provider 或通用插件系统；
- 新一轮诊断系统扩张；
- 仅因类文件较大而进行架构重构。

长期规则见：

- `docs/00_PRODUCT_AND_SCOPE.md`
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/specs/BOOK_IMPORT.md`
- `docs/specs/HTTP_TTS.md`
- `docs/specs/REGEX_REPLACEMENT.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短“完成成果”
- `[!]` 阻塞，记录会影响产品/架构/隐私/路线或必须人工授权的真实冲突

默认按 T001 → T006 串行执行。如果调用明确要求连续执行整个 Backlog，可以在每个任务自动验收完成后继续，不等待人工视觉验收。

人工视觉验收永远是可选补充，不阻塞任务完成或下一任务。

每个未完成任务的详细实施合同位于 `tasks/`。完成任务后：

1. 满足 task spec 中的自动验收；
2. 删除所有当前任务临时测试、fixture、脚本、截图和诊断产物；
3. 更新本文件状态与“完成成果”；
4. 删除对应 `tasks/Txxx_*.md`；
5. 不等待人工验收。

### 持久化变更特别约束

T003 预计会涉及 Book Description、元数据规则持久化和新的全局导入设置。`AGENTS.md` 要求数据库结构或持久数据变更必须在实施前逐项取得用户明确授权。

因此 Codex 在 T003 中必须先完成只读审计，列出每一项真实需要的持久化变更、原因、数据影响和回退/保留方案；对尚未获得明确授权的项目停止在迁移实现之前并标记 `[!]`。不得把本 Backlog、长期文档或“执行整个阶段”的授权解释成数据库迁移授权。

---

# Phase A：全局交互语义与播放可靠性

## [x] T001（P0）：统一 Selection/Current 视觉语义并收口相关 UI

目标：把全项目列表状态统一为“Current=左侧 Accent rail、Hover=浅背景、Selected=更深的中性背景、Focus=Focus 边框”，允许 Current+Selected+Focus 自然叠加；同时完成章节标题去自动编号和播放页 Provider Popup 的排版优化。

完成成果：共享 Selection surface 将 Current rail、Selected 中性背景、Hover 与 Focus 分层；统一相关列表和 StyleGallery，章节列表仅显示 Title，播放页 Provider Popup 完成滚动与管理导航行。

## [ ] T002（P0）：收拢播放连续失败恢复

依赖：T001。

目标：对可恢复合成失败保持有限重试；最终失败后跳过当前段，任一成功段重置连续失败计数，连续自动跳过 3 段后暂停等待用户处理。不得建立与 Provider Runtime 竞争的第二套无界重试。

详细规格：`tasks/T002_PLAYBACK_FAILURE_RECOVERY.md`

---

# Phase B：TXT 导入与规则工作台

## [ ] T003（P0）：实现元数据识别与空行分章导入链路

依赖：T002。

目标：按 `BOOK_IMPORT.md` 实现文件名/正文头部元数据规则、Description、全局“空行分章”、章节原始标题展示与设置 IA 调整；移除旧 `BookFileNameTemplate`，保持无编码问题时选择 TXT 后直接导入。

本任务涉及持久化前必须执行上文逐项授权流程。

详细规格：`tasks/T003_BOOK_IMPORT_METADATA_AND_CHAPTERING.md`

## [ ] T004（P1）：增加规则与 Provider 批量交换

依赖：T003。

目标：为章节规则、正则替换规则、文件名元数据规则、正文头部元数据规则和可分享 HTTP Provider 建立一致的 Ctrl/Shift 多选、单文档批量导出和同格式批量导入；修饰键点击不切换右侧 Editor，Microsoft Edge 不参与导出选择。

详细规格：`tasks/T004_BULK_RULE_PROVIDER_EXCHANGE.md`

---

# Phase C：配置迁移与书库轻量增强

## [ ] T005（P1）：实现第一版配置备份与恢复

依赖：T004。

目标：在“缓存与数据”中增加本地私人配置备份/恢复，完整保存设置、Provider（含凭据）和四类规则；恢复采用替换配置快照语义，不包含书籍、阅读进度、缓存或诊断数据，不加入 WebDAV。

详细规格：`tasks/T005_CONFIGURATION_BACKUP_RESTORE.md`

## [ ] T006（P2）：增加书库“最近导入”排序

依赖：T003。

目标：在现有标题/作者搜索与“最近阅读 / 标题”排序基础上增加“最近导入”，继续保持书库轻量，不增加阅读状态筛选、标签、文件夹或收藏系统。

详细规格：`tasks/T006_LIBRARY_RECENT_IMPORT_SORT.md`
