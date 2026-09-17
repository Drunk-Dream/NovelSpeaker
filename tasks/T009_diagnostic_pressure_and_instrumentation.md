# T009 — 收敛诊断压力策略与 instrumentation

## 目标

避免诊断系统因为过宽 instrumentation 和瞬时 bounded-queue 压力而自我触发永久故障。

## 必须完成

1. 审计 OperationCatalog 与所有 instrumentation 调用点，重点检查 `UiDispatcherStall`。
2. 普通 Dispatcher 调用不得自动等价于 stall：
   - stall/等待异常应有明确判定语义；
   - 普通 UI work 如确需 telemetry，使用语义正确的稳定 operation 或完全不采集。
3. Diagnostic Session writer 区分：
   - hard cap；
   - transient queue pressure；
   - true storage/write failure。
4. 一次 `TryWrite` 失败不得直接把整个 Session 永久标记为 `storage-failure`。
5. 定义简单、有限的优先级：
   - Session lifecycle、Problem Marker、capture-stopped 等关键控制记录优先；
   - 普通高频 Activity/Event/ResourceSample 压力下允许丢弃或聚合。
6. dropped count/health 状态可观测，但不得递归写日志或高频制造更多诊断事件。
7. 不实现复杂动态采样器、无限重试、第二 writer 或 Emergency Logger。

## 自动验收

- 可控小容量 queue 压力测试：普通样本可丢弃，但 Session 保持 Active。
- 真正 SQLite/file write failure 才进入 storage-failure/degraded。
- Marker/lifecycle 在普通样本压力下具有更强保留保证。
- 高并发 instrumentation 不阻塞业务线程。
- 验证 WpfUiScheduler 正常 Invoke 不全部产生 `ui.dispatcher-stall`。
- 不使用绝对毫秒性能门槛。
