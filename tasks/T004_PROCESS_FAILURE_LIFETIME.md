# T004：收拢 fatal failure 与 Process 生命周期职责

## 背景

本轮 Provider Popup 崩溃暴露出：UI fatal exception 已被生产日志捕获，但运行期故障复用了 startup failure 通道；全局异常处理器随后执行有序 Shutdown，Diagnostic Session Store 又无条件写入 `normal-exit`，导致“关闭方式”和“退出原因”混为一谈。

## 权威参考

- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- `AGENTS.md`

## 目标架构

只建立一个小型、明确的 Process failure/lifetime owner：平台入口报告一次稳定故障语义，Production Logging、Diagnostic Session 和最终 Process exit reason 消费同一事实。不要建立通用异常框架。

## 必须完成

1. 将 startup-specific diagnostics 与运行期 fatal failure 语义分开；运行期 Dispatcher/AppDomain 等 fatal failure 不再记录成 `app.startup.failure`。
2. 建立一个最小的 Process 退出原因 owner/value contract，能够至少区分正常退出、startup failure、fatal UI failure，以及能够进入有序关闭流程的其它 fatal failure；真正来不及主动结束的后台/进程级崩溃仍由下一进程恢复逻辑推断 unexpected termination。
3. “已完成有序 Shutdown”只描述关闭机制，不得覆盖已经确定的 fatal exit reason。
4. 让 Diagnostic Session 的 Process 结束 API 接收上层已经确定的退出原因；Store 只负责持久化，不再自行把所有有序关闭写为 `normal-exit`。
5. 在活动诊断会话中为能够被当前进程观察并记录的 fatal failure 写入一条稳定、低基数的 Diagnostic Event。完整 exception/stack trace 继续只进入 Production Logging。`TaskScheduler.UnobservedTaskException` 等被明确观察并继续运行的非致命异常不得因为入口名称而自动升级为 fatal。
6. 保留现有 Session 跨进程恢复能力；上一 Process 没有结束记录时，恢复仍能标记 unexpected termination。
7. 保持故障处理 best effort：Logging/Diagnostic Session 自身失败不得替代原错误处理或阻止关闭。

## 复杂度边界

- 不引入 EventBus/Messenger、OpenTelemetry、Crash Database、Minidump、第二套异常持久化、插件式 failure pipeline 或复杂 Process 状态机。
- 不新增长期 UI 或用户设置。
- 优先使用现有 Observability/DiagnosticRegistry/CorrelationContext。
- 本任务按现有 `.nsdiag` schema 实现，不做数据库 schema/data migration。若证据表明必须迁移，停止该部分并按 `AGENTS.md` 向用户单独申请授权。

## 永久测试

只保护稳定高风险行为：

- fatal UI failure + orderly shutdown 最终不是 normal exit；
- fatal failure 在活动 Session 中留下稳定 Diagnostic Event；
- 详细异常仍进入 Production Logging；
- 正常退出仍保持正常语义；
- 未主动留下结束记录的 Process 在恢复时仍能被推断为 unexpected。

不要断言内部类名、精确调用顺序或具体 SQLite SQL。

## 自动验收

运行与 Bootstrap、Process lifecycle、Diagnostics store 相关的 focused tests，并在任务收口时执行完整 Release 质量门禁。
