# NovelSpeaker 当前开发 Backlog

## 1. 当前阶段：Cache Speech Plan / Coverage 架构收口

- **工作基线：** GitHub `dev`，2026-10-14 可见的最新已推送代码。用户已安排 Codex 完成上一轮 **T007**，但相关更改**尚未推送**；新一轮执行时必须以本地实际 HEAD 和工作区为准，保留所有已完成但未提交/未推送的 T007 成果，不能为了应用本文档 reset、checkout 或覆盖用户改动。
- **本轮授权：** 修复目录滚动触发 `ChapterSpeechPlanSegments` 大量写入、数据库膨胀和卡顿的问题；收紧 Plan 创建/保存边界，保证当前配置下缓存百分比准确；简化 Stale repair；治理相关临时内存和孤立 Plan。
- **已存在功能继续保持：** Provider/语速等 synthesis fingerprint 改变时，缓存按新配置重新匹配；**不因为 Provider 改变而重建 Speech Plan**，不另开实现任务。
- **长期合同：** `docs/04_CACHE_AND_BACKGROUND_WORK.md` 为 Cache owner；生命周期参见 `docs/02_RUNTIME_AND_NAVIGATION.md`，规则影响参见 `docs/specs/REGEX_REPLACEMENT.md`；其余遵守 `AGENTS.md`。
- 本轮不开发在线书源，不启动 U1 视觉改版，不重构其它系统。尽量在既有模型、服务和 SQLite 表结构内解决，不为“可能有用”新增通用机制。

## 2. 执行规则

- `[ ]` 未开始；`[-]` 进行中；`[x]` 已完成；`[!]` 遇到超出授权边界、必须由用户与 ChatGPT 决定的阻断问题。
- **默认串行：T001 → T002 → T003 → T004 → T005。** 每一任务结束须可构建、可执行相关测试。本轮不启用 staged breaking migration window。
- 阅读 `AGENTS.md`、当前 task spec 和 task spec 明确引用的少量长期文档；以本地实现为准，不机械照搬任务中提到的类名。先 `git status`/检查相关 diff，保护本地 T007 以及其它用户改动；在已有改动上增量集成，不能当作纯远端工作树。
- 仅在真实需要时调整 Application port、Infrastructure Store、UI binding；不添加多余状态机、通用 Scheduler/EventBus、全局 Plan 内存缓存、无限队列、重复兼容路径或永久的迁移补丁层。
- 已发布 SQLite schema/migration 不擅自更改；本轮计划在现有 `ChapterSpeechPlans`、`ChapterSpeechPlanSegments` 和 `AudioCacheEntries` 中完成。若确需新增持久化结构或改变正式数据语义，先按 `AGENTS.md` 记录具体影响并请求决策。
- 只保留核心行为的正式测试；可使用受控临时规模测试/基准，完成后删除临时 harness、数据文件和截图。人工验收仅作可选补充，不作为阻断下一任务的条件。
- 每任务完成后把状态标记 `[x]`，记录简短成果/自动验证/限制，删除对应 `tasks/Txxx_*.md`；由 Git 记录历史。不得未经授权 push、发 PR、发布或创建 tag。项目文本使用 LF。
- 若旧 `tasks/` 仍有上一轮 `T007_E1_CLOSURE.md` 或其它遗留任务文件，先确认其已执行完毕及成果已被本地保留，再移除旧任务规格；**禁止删改尚在执行中的 T007 工作**。本轮 `tasks/` 最终只保留未完成的新任务规格。

## 3. 当前任务

### Phase A — 目录查询不再放大工作量

- [x] **T001（P0 / Query Boundary）— Cache 只读查询与 PlanMissing 止血**
  完成成果：目录查询不再为 `PlanMissing` 请求 repair；`PlanStale` 仅在同窗口存在物理缓存时请求。10k 章节多窗口回归、SQLite Cache 22 项、Presentation 25 项、格式检查及 Release 构建通过。

### Phase B — Plan 计算与真实缓存生命周期

- [x] **T002（P0 / Persistence）— Plan 计算与持久化解耦**
  完成成果：Plan Build/正文加载纯计算；语音内容 port/DTO 收归 Cache，避免 Books 反向依赖。生成音频时 Plan 与 Ready 索引同事务保存，保留 Header-only 优化；移除 Build 隐式保存。保留并改写核心 Plan/Playback 测试，新增首写回滚、取消、并发及重开库 Coverage/导出键回归；Application 128 项、Infrastructure 111 项、Presentation 78 项（含架构）、Release build、format/diff check 通过。完整门禁留待 T005；未新增 schema，无文档冲突。

### Phase C — Stale 异步修复和真实百分比

- [ ] **T003（P0 / Background & UX）— 简化 Stale repair 与完整度展示**  
  规格：[`tasks/T003_STALE_REPAIR_AND_UI.md`](tasks/T003_STALE_REPAIR_AND_UI.md)。单 worker、轻量去重、最新可视需求、过期/离开取消；修复成功按章更新；所有无有效缓存状态保持空白。

### Phase D — 资源收口

- [ ] **T004（P1 / Resource）— 孤立 Plan 清理与 Active Cache 按章处理**  
  规格：[`tasks/T004_CACHE_RESOURCE_CLOSURE.md`](tasks/T004_CACHE_RESOURCE_CLOSURE.md)。及时清理不再需要的 Plan；避免批量主动缓存同时持有所有章节的完整正文和语音分段；保留既有后台生命周期。

### Phase E — 工程验证

- [ ] **T005（P1 / Closure）— 长目录性能、完整度和完整质量门禁**  
  规格：[`tasks/T005_CACHE_CLOSURE.md`](tasks/T005_CACHE_CLOSURE.md)。验证数据库不随纯滚动增长、当前配置的完整度真实、生命周期正确、无额外永久复杂度及无临时副产物；执行完整门禁。

## 4. 明确不实施

- 不因为 Provider 切换而生成新的 Speech Plan，保留已有 synthesis fingerprint + 音频键匹配设计。
- 不为 `PlanMissing` 批量预计算、不为无音频的章节提前写入 `ChapterSpeechPlanSegments`。
- 不在普通目录显示 0%、“计算中”、过期值或其它替代文案；真实有效百分比计算完成后再显示。
- 不进行同步等待修复、不在 Dispatcher 做正文分段/Regex 计算、不扫描/解码可见章节的所有音频文件。
- 不添加复杂优先级、多 worker 池、持久化修复任务表、跨页面永久缓存或通用 Scheduler。
- 不重写 Book/Source/CurrentCatalog、Provider、TTS 输出身份、缓存文件根信任边界、Playback target-first 或已完成的上一轮 T007 修复。

## 5. 完成标准

1. 仅打开/滚动 10k 章节目录，未缓存章节不会创建新 Plan/Segment 行，数据库写入量和 UI 交互不随扫过章节数异常膨胀。
2. `PlanStale` 只在存在实际缓存且当前需要展示时异步修复；快速滚动/离开页面后不残留大量排队补建；修复失败不触发无限重试。
3. Speech Plan 计算是无持久化副作用的能力；实际 Ready 音频缓存和可用 Plan 在重启后保持正确关联；失效缓存不能计入当前配置的百分比。
4. UI 仅显示有效且非零的真实百分比；无缓存、0%、缺失、过期或失败均空白。切换 Provider 后的当前配置匹配行为不退化。
5. 维持现有可用的 MP3 导出、正常播放及 target-first、主动缓存、退出排空；无长期持有跨大量章节正文/段落的批次内存结构。
6. 无不必要 schema migration、Compat/V2 层、临时测试文件；T005 完整质量门禁结果如实记录。
