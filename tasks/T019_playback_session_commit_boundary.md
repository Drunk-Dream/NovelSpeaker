# T019：确认并收敛 Playback 替换提交边界

## 目标

用受控音频准备流程确认切章/切段在 checkpoint 保存成功后、音频准备失败或取消时的实际语义，并使实现与 `docs/02_RUNTIME_AND_NAVIGATION.md` 一致。

审计依据：`CODEBASE_AUDIT_REPORT.md` I01。

## 范围与约束

- 沿 `StartNewSessionAsync` 到目标音频准备、snapshot 发布和 ReadingProgress 保存路径追踪可观察行为。
- 使用可控 provider/audio gate 注入 checkpoint 成功后的取消和失败；核对当前 session、UI snapshot、持久进度及后续 Resume。
- 若行为违反现有合同，最小化调整替换/rollback 边界；若合同并未要求恢复旧 session，则记录证据并结束任务，不扩写合同来迎合实现。
- 临时验证代码完成后必须删除。

## 验收

- 有可重复的验证证明 checkpoint 后取消及失败的状态结果。
- 保留已有 session replacement 与 progress 核心覆盖，不添加依赖固定等待的测试。
- 执行 Playback focused tests、format 和 Release build；记录未执行项。

## 交付

若无需代码变更，在 Backlog 记录调查结论；任务关闭后删除本规格。
