# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **Observability、生产日志、性能遥测与诊断系统建设阶段**。

规划代码基线：`33431ddcb2f998168b989516fefabb0131c675d9`（`dev`，`refactor(architecture): remove migration debt baseline`）。

上一轮已经完成 Application 模块边界收敛，当前稳定事实包括：

- Application 主要模块为 Books / Speech / Cache / Playback / Settings / Desktop；
- Cache 是一级模块；
- Playback 继续唯一拥有 mutable session truth；
- Settings 继续唯一拥有 process snapshot；
- Architecture Fitness Tests 已清除上一轮 migration debt baseline；
- 全量 Release tests 在上一轮收口时通过。

本轮目标是在这些稳定边界上建立：

1. 薄的 Observability 基础合同与稳定 Operation/Registry；
2. 默认开启的本地结构化生产日志；
3. 用户主动开启的本地普通性能遥测；
4. 面向任何可复现问题的诊断会话；
5. 诊断悬浮控制条、Problem Marker、主动窗口截图与导出；
6. 完整隐私约束、失败降级与自动质量门禁。

长期目标见：

- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`

每个任务的详细实施合同位于 `tasks/Txxx_*.md`。Agent 不应只根据本 Backlog 的简述自行补全实现。

## 2. 状态

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短“完成成果”
- `[!]` 阻塞，记录会影响产品/架构/隐私/路线的真实冲突

默认一次执行一个任务；如果调用明确要求连续执行，可按依赖顺序继续。**人工验收永远是可选项，不阻塞任务完成或下一任务执行。**

完成任务后：

1. 满足对应 task spec 的强制自动验收；
2. 更新本文件状态与完成成果；
3. 删除该任务对应 `tasks/Txxx_*.md`；
4. 不等待人工验收。

---

# Phase A：共享 Observability 基础

## [x] T001（P0）：建立薄 Observability API 与稳定诊断合同

依赖：无。

实施规格：`tasks/T001_observability_foundation.md`

目标：建立业务只打一次稳定操作点、性能遥测与诊断会话可分别消费的最小基础，同时建立 Operation/Diagnostic Registry、隐私约束和 Architecture Tests；本任务不实现生产日志文件、遥测落盘或 `.nsdiag`。

完成成果：在 Application 建立强类型 Observability 合同、稳定 Operation/Diagnostic Registry、隐私字段白名单、AsyncLocal correlation scope 与隔离 consumer fan-out；补充 contract/architecture tests，未引入任何持久化 writer。

---

# Phase B：生产日志

## [x] T002（P0）：实现本地结构化生产日志

依赖：T001。

实施规格：`tasks/T002_production_logging.md`

目标：建立默认开启、低开销、JSONL、可轮转的生产日志，覆盖异常、失败、恢复与少量生命周期；日志失败不得影响业务，并可在诊断会话 active 时附加 correlation。

完成成果：收敛为单一默认开启的 JSONL 生产日志路径，建立稳定 LogEvent Registry、结构化异常与隐私脱敏；实现高低优先级 bounded queue、后台批量 writer、日期/大小轮转、retention/总容量保护、flush/degraded 生命周期和诊断 correlation；补充 startup/error-boundary、真实失败 reporter、队列溢出、异常树、轮转与隐私回归测试。

---

# Phase C：普通性能遥测

## [x] T003（P0）：实现本地普通性能遥测与诊断信息导出

依赖：T001、T002。

实施规格：`tasks/T003_performance_telemetry.md`

目标：实现默认关闭的普通性能遥测、长期低开销聚合、本地 retention、设置入口、清除数据和“诊断信息”导出；不保存原始高频事件，不自动上传。

完成成果：实现默认关闭、用户可切换的本地性能遥测，覆盖操作、页面关键加载、调度器、播放、TTS、缓存、存储与进程资源；采用低基数窗口聚合、JSONL retention/轮转与失败隔离；新增诊断页清除/导出入口，生成稳定 ZIP（summary、telemetry、logs、environment）且不保存原始事件或上传。

---

# Phase D：诊断会话

## [x] T004（P0）：实现诊断会话核心、跨进程恢复与 `.nsdiag`

依赖：T001、T002。

实施规格：`tasks/T004_diagnostic_session_core.md`

目标：实现用户显式开始/结束的诊断会话、Activity/Event/Snapshot/Resource Sample、Session 内匿名对象关联、硬容量上限、跨 Process/重启继续和 SQLite `.nsdiag` 权威存储；不实现最终悬浮 UI 和截图。

完成成果：实现显式诊断会话、跨进程/重启恢复、匿名对象关联、持续批量 SQLite writer、硬容量与 WAL 自包含收口、reparse-point 防护、失败隔离及启动/退出生命周期接入。

## [x] T005（P0）：实现诊断悬浮控制条、主动截图与问题诊断导出

依赖：T004、T003。

实施规格：`tasks/T005_diagnostics_ui_export.md`

目标：实现从 Settings 打开诊断工具、开始诊断、标记问题、主动截取 NovelSpeaker 当前窗口、结束、重新开始、立即/以后导出，以及供 AI 和人预览的问题诊断包；不建设 Session 列表页面。

完成成果：新增 Settings 问题诊断入口、独立悬浮控制条、显式当前窗口截图与 Marker；接入 hard-cap 状态刷新、采集中防误关闭和跨进程恢复；统一立即/选择 `.nsdiag` 导出为包含摘要、时间线、日志、环境、Schema 与附件的 ZIP，并验证源文件不被改写。

---

# Phase E：系统收口

## [ ] T006（P0）：完成隐私、失败隔离、集成验收与遗留清理

依赖：T001–T005。

实施规格：`tasks/T006_observability_quality_closure.md`

目标：对 Logging / Telemetry / Diagnostic Session 做跨模块审计，确保隐私边界、失败降级、容量/retention、Architecture Fitness Tests、WPF 隔离、导出和全量 Release 门禁全部稳定；删除本轮兼容/实验/一次性产物，不增加下一阶段功能。

完成成果：待填写。
