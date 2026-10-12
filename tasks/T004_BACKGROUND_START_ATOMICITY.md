# T004 — Active Cache / Export 启动与通知异常隔离

- **类型/优先级：** 实施 / P2 Reliability。依赖 T003 完成；T005 基于本任务最终协调器语义继续。
- **已验证风险：** 两个真实 coordinator 在建立 `_activeTask`/Waiting snapshot 后，同步调用 `SnapshotChanged?.Invoke`，然后才启动 worker。隔离测试中若 subscriber 抛 `InvalidOperationException`，`StartAsync` 传播异常、实际 worker 未启动、后续 BatchAlreadyActive、取消等不到 completion、`DisposeAsync` 可能持续 pending。生产 WPF observer 抛异常的实际发生率未证实。

## 目标

**观察者异常不能决定后台批次是否开始、结束或释放。** 已接受的 job 有且只有一个 owner，完成/失败/取消一定最终解锁 active slot；快照展示可以尽力通知，通知失败不能破坏协调器的进度与完成责任。

## 范围

1. 仔细追踪 `ActiveCacheCoordinator`、`ChapterExportCoordinator` 的 `StartAsync` / `RunOwnedBatchAsync` / `Publish` / `CancelAsync` / `DisposeAsync` 以及 shell `SnapshotChanged` 订阅链；结合 `docs/04_CACHE_AND_BACKGROUND_WORK.md`、`docs/02_RUNTIME_AND_NAVIGATION.md` 与相应测试核对真实 owner。以当前代码为准，不对照旧位置机械替换。
2. 消除“建立 active slot → 同步调用可能抛异常的 observer → worker 尚未启动”的悬挂窗口。可以通过通知异常隔离、可靠 worker 启动及必要回滚等**局部**方案解决；只要状态机在 observer 各阶段异常下仍保证 job completion。无需引入通用 EventBus、全局订阅容器、新任务调度器或长期兼容层。
3. 考虑初始 Waiting、Running、进度、Cancelling、Completed/Failed/Cancelled 通知：异常 observer 在任一状态抛出，不能阻止其它订阅者对必要快照的接收（若支持多个 subscriber），不得在 catch/finally 再因 publish 抛异常跳过 TCS 完成/slot 清理。业务失败仍按原规则处理；不可吞掉真实 worker 错误而伪报成功。必要时使用现有安全日志接口记录与用户内容无关的信息，不记录正文、Book 名、URL、Token 等。
4. 保持已冻结配置、批次取消/替换、同一时刻最多一个 batch、已完成音频/导出结果保留、页面离开不终止后台批次、Shell 只作为 projection observer。不要把 UI dispatcher 停止视为业务失败，也不延长正常操作路径。

## 验证/验收

- 为两种 coordinator 使用假 worker + 抛异常 observer 复核：Start 后 worker 实际启动或明确安全拒绝且 active slot 无残留；其余 start/stop/retry/Cancel/Dispose 可完成。覆盖 initial notification 和至少一种运行/终态 notification 异常；另验证正常进度订阅者仍收到可用 snapshot，若 observer 有多个订阅者，单个异常不阻止其余者。
- 包含取消与快速重复提交情况，并与 shell 现有异步调度/事件注销语义兼容。使用可控 TCS/取消同步，不以固定延时猜测动作完成。
- 永久核心测试少量、聚焦“观察者不能卡住后台 job”行为，不冻结 coordinator 私有字段或所有 snapshot 次序。必要临时异常 harness 使用后删除。
- 运行相关 Cache/Export focused tests、Release build、format/`git diff --check`。确认两个 coordinator 没有新增第二套完成责任、重复状态或 observer 保护框架；若更新长期生命周期合同确有必要，在 `docs/04_CACHE_AND_BACKGROUND_WORK.md` 补充一条简洁约束。

## 结束要求

记录实际实现的异常隔离/完成策略、自动验收及限制；将 T004 标 `[x]` 并删除本任务规格。严禁顺便把 Active Cache、Export、Repair 合并成万能 background manager。
