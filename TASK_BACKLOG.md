# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

Observability、生产日志、性能遥测与诊断系统的第一版已经完成，当前进入 **稳定化与职责收敛阶段**。

当前代码基线：`5b9889e3175ee653ed6c4c7eb60050489ac7cde0`（`dev`，`fix(diagnostics): bound reparse checks to data root`）。

本轮不增加新的诊断产品范围，目标是从架构层解决第一版暴露出的稳定性问题：

1. 建立唯一的存储信任边界，正确支持 Scoop 等 Data-root Junction；
2. 收敛诊断会话运行态 owner，并实现真正的紧凑悬浮录制控制条；
3. 修复高频 instrumentation 与诊断 writer 压力策略，避免诊断系统自身制造故障；
4. 统一普通诊断/问题诊断导出基础设施，增加原子输出、自诊断日志和时间戳命名；
5. 补齐真实安装布局、跨重启、并发导出和压力场景的自动回归。

长期目标见：

- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`

每个任务详细实施合同位于 `tasks/Txxx_*.md`。Agent 不应只根据本 Backlog 简述自行补全实现。

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

# 已完成：Observability 第一版

## [x] T001–T006

第一版已完成薄 Observability API、生产日志、普通性能遥测、诊断会话、诊断 UI/导出与首轮质量收口。

当前稳定化工作不回退这些能力，而是修正第一版暴露出的边界不一致和真实运行风险。

---

# Phase F：存储与诊断基础设施稳定化

## [x] T007（P0）：统一应用存储信任边界并修复 Data-root reparse point

依赖：T001–T006。

目标：从架构层统一 reparse-point 与数据根归属规则。最终选定的数据根作为可信锚点，允许 Data root 自身或其祖先为 Junction/Symlink；严格拒绝数据根内部链接逃逸。覆盖真实 Scoop `current\Data -> persist\novelspeaker\Data` 布局，并删除 Diagnostics/Storage 中重复或冲突的路径判断。

完成成果：统一逻辑数据根 containment 与 reparse-point 检查；支持 Data-root Junction 并拒绝根内链接逃逸。Infrastructure 集成测试 382/382、Architecture 测试 55/55、Release build 和格式验证通过。

## [ ] T008（P0）：重构诊断录制运行态与悬浮控制条

依赖：T007。

实施规格：`tasks/T008_diagnostic_recording_controller.md`

目标：将 Session 运行态从窗口/ViewModel 中抽离为明确 owner/controller；把当前页面式 ToolWindow 重构为真正紧凑的悬浮录制控制条。Active Session 跨重启恢复后自动恢复可见控制条，并从持久化 snapshot 恢复容量、停止状态和可恢复统计；容量 UI 使用 MB 等人类可读显示。

## [ ] T009（P0）：收敛诊断采集压力策略与 instrumentation 语义

依赖：T008。

实施规格：`tasks/T009_diagnostic_pressure_and_instrumentation.md`

目标：修正 `UiDispatcherStall` 等过宽采集语义；将诊断 writer 的瞬时 queue pressure 与真实 storage failure 分离。低价值高频记录可丢弃/聚合并计数，关键 Session/Marker 状态优先保留，避免一次 `TryWrite` 失败永久停止整次诊断。

## [ ] T010（P0）：统一诊断导出与可观测失败边界

依赖：T007、T009。

实施规格：`tasks/T010_diagnostics_export_reliability.md`

目标：让普通诊断信息和问题诊断共享 Bundle/atomic-output 基础设施；Windows 保存文件对话框只选择最终 ZIP 目标；文件名使用本地时间戳；日志轮转/损坏文件 best effort；导出、恢复、截图、Marker 等诊断系统自身失败写入脱敏生产日志。当前“性能监测导出失败”若在基础设施重构后仍可复现，再根据新增日志单独建立后续修复任务，不在本任务中基于猜测增加特例。

---

# Phase G：稳定化收口

## [ ] T011（P0）：完成真实环境回归与全量质量门禁

依赖：T007–T010。

实施规格：`tasks/T011_diagnostics_stabilization_closure.md`

目标：对存储信任边界、Scoop Junction、跨重启悬浮控制条、writer 压力、并发日志导出、原子 ZIP、隐私和诊断自记录进行系统回归；清理稳定化过程产生的重复实现和一次性产物，执行完整 Release 质量门禁。
