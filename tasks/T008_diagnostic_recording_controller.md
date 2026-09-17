# T008 — 重构诊断录制运行态与悬浮控制条

## 目标

当前 DiagnosticToolWindow 是页面式 ToolWindow，且 ViewModel 持有部分易丢失的进程内录制状态。重构为明确的 Recording Controller/owner + 紧凑悬浮控制条，使 Session 生命周期与 WPF Window 生命周期解耦。

## 必须完成

1. 建立单一录制运行态 owner/controller：
   - 从 IDiagnosticSessionService snapshot 投影状态；
   - Start/End/Restart/Recover 都经该 owner；
   - WPF Window/ViewModel 不成为第二份 Session truth。
2. 将当前大窗口改为真正紧凑悬浮控制条：
   - Preparing：开始诊断、容量、关闭；
   - Capturing：录制状态/持续时间、标记问题、截图、结束；
   - Completed：重新开始、导出、完成。
3. Active Session 在应用启动恢复后自动显示控制条，禁止后台隐形录制。
4. 恢复时同步：
   - HardCapBytes；
   - CaptureStopped / reason；
   - StartedAtUtc；
   - 能从 `.nsdiag`/snapshot 得到的持久化统计。
5. MarkerCount / AttachmentCount 如需显示，不得以纯 ViewModel 内存计数作为跨重启真值；可以扩充稳定 snapshot/query，也可以调整 UI 避免显示无法可靠恢复的数字。
6. 容量 Presentation model 使用人类可读显示（16 MB/64 MB/256 MB），内部仍使用 bytes。
7. Start/End/Marker/Capture/Export 等命令使用统一异步错误边界；异常不得直接逃出 Command。
8. 控制条置顶、关闭行为和应用退出生命周期保持可预测，不引入 singleton Page/ViewModel。

## 自动验收

- Presentation tests 覆盖全部状态转移。
- Recover active session 后 controller 和 UI 状态正确。
- WPF 隔离 Desktop 测试验证窗口是紧凑控制条而非页面式布局，可使用结构/尺寸/关键元素约束而不是像素截图作为唯一门禁。
- Active session 恢复后自动显示；ended/no-session 不自动显示。
- 人工视觉验收可选且不阻塞。
