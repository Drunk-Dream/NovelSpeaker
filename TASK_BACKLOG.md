# NovelSpeaker 当前开发 Backlog

## 1. 阶段定位

当前进入 **v0.7.0 发布前稳定性收口与诊断职责对齐阶段**。

当前代码基线：`d98783b43c81d959798a42c1ef5d25efaf8aeda9`（`feature/speech-provider-refactor`）。

上一阶段已经完成 Speech Provider 架构重构、HTTP / Microsoft Edge Provider、统一语速、Playback/Cache/Export 接线以及语音服务管理 UI。本阶段不继续扩张 Provider 功能，先处理本轮暴露出的 UI 样式语义、Provider 选择器崩溃和核心回归，然后发布 `v0.7.0`；发布完成后再以独立阶段有限度优化诊断系统的故障/Process 生命周期和问题诊断证据完整性。

本阶段目标：

- 收拢 Button Style，使全局样式按稳定交互语义命名，避免按偶然外观或具体页面不断专用化；
- 修复播放页 Provider 切换 Popup 因缺失 `App.Button.Transparent` 资源导致的应用级崩溃，并补上能够真实实例化 Provider ItemTemplate 的核心 WPF 回归；
- 在以上稳定性问题完成后发布 `v0.7.0`，作为 Speech Provider 重构后的正式稳定基线；
- 发布之后再收拢 fatal failure、Process exit reason、Diagnostic Session 的职责，确保“有序关闭”不再等价于“正常退出”；
- 修正问题诊断包的关联日志与退化可见性，使“确实没有证据”和“证据读取失败/不完整”可以区分；
- 诊断优化保持小型、明确，不引入通用 EventBus、Crash Database、完整 OpenTelemetry、复杂健康监控或第二套日志系统。

长期规则见：

- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/06_UI_AND_VISUAL_SYSTEM.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

## 2. 状态与执行规则

- `[ ]` 未开始
- `[-]` 进行中
- `[x]` 已完成，追加简短“完成成果”
- `[!]` 阻塞，记录会影响产品/架构/隐私/路线的真实冲突

默认按 T001 → T005 串行执行。如果调用明确要求连续执行整个 Backlog，可以在每个任务自动验收完成后继续，不等待人工验收。

人工验收永远是可选补充，不阻塞任务完成或下一任务。

每个未完成任务的详细实施合同位于 `tasks/`。完成任务后：

1. 满足 task spec 中的自动验收；
2. 删除所有当前任务临时测试、fixture、脚本、截图和诊断产物；
3. 更新本文件状态与“完成成果”；
4. 删除对应 `tasks/Txxx_*.md`；
5. 不等待人工验收。

涉及数据库结构或持久数据迁移时继续严格遵守 `AGENTS.md` 的逐项授权要求。本轮诊断优化按现有 `.nsdiag` schema 完成；如果实现证明必须修改 schema，停止该部分并向用户说明后再继续。

---

# Phase A：v0.7.0 发布前稳定性收口

## [ ] T001（P1）：收拢 Button Style 语义与命名

目标：审计现有共享 Button Style 及主要调用方，以稳定交互职责重新收拢语义；复用已有职责相同的样式，消除按“透明”“某页面按钮”等偶然外观或位置建立全局 Style 的倾向。允许在证据充分时重命名或把明确属于 Media/Navigation 的变体迁入对应命名空间，但不得建立兼容 alias。

详细规格：`tasks/T001_BUTTON_STYLE_SEMANTICS.md`

## [ ] T002（P0）：修复 Provider Popup 崩溃并建立核心回归

依赖：T001。

目标：使用 T001 确立的最终 Button 语义修复播放页 Provider 选择器；修正测试上下文仍暴露旧 `Rules` 而未提供真实 `Providers` 的缺口，让自动测试真正实例化 Provider DataTemplate，并保留一个只保护“核心 Provider Popup 可安全打开”的永久 WPF 回归。

详细规格：`tasks/T002_PROVIDER_POPUP_CRASH.md`

## [ ] T003（P0）：发布 NovelSpeaker v0.7.0

依赖：T001、T002。

目标：在发布前稳定性收口完成并通过完整质量门禁后，严格按照仓库 `release-version` Skill 发布指定版本 `v0.7.0`。本任务不夹带后续诊断系统重构；Release 成功后从最终发布主线建立新的 `feature/diagnostics-hardening` 分支继续 T004–T005。

详细规格：`tasks/T003_RELEASE_V0_7_0.md`

---

# Phase B：诊断职责有限收口

## [ ] T004（P0）：收拢 fatal failure 与 Process 生命周期职责

依赖：T003。

目标：用一个小型、单一 owner 的 Process failure/lifetime 边界统一运行期 fatal failure 分类和最终退出原因；让 Production Logging、Diagnostic Session 与 orderly shutdown 消费同一份稳定语义，并保留真正硬崩溃由下一次 Session 恢复推断 unexpected termination 的能力。

详细规格：`tasks/T004_PROCESS_FAILURE_LIFETIME.md`

## [ ] T005（P1）：收口问题诊断证据聚合与退化可见性

依赖：T004。

目标：修正问题诊断导出中关联日志可能被静默漏掉的问题，并把日志证据读取状态明确为 complete / partial / unavailable 或等价稳定语义；保持导出 best effort，但不再把读取失败伪装成“没有相关日志”。只做必要职责拆分，不建立通用 evidence pipeline 或 diagnostics health framework。

详细规格：`tasks/T005_DIAGNOSTIC_EVIDENCE_EXPORT.md`
