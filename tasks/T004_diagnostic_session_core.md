# T004 — 诊断会话核心与 `.nsdiag`

## 目标

实现可跨 Process/重启持续的诊断会话核心。用户只有在显式“开始诊断”后才创建 Session 和开始采集；会话记录问题现场，不局限于性能问题。

本任务实现核心 API、Registry consumer、持久化和恢复，不实现最终悬浮控制条、主动截图或最终导出 UI。

## 必读

- `AGENTS.md`
- `TASK_BACKLOG.md` 中 T004
- `docs/01_SYSTEM_ARCHITECTURE.md`
- `docs/02_RUNTIME_AND_NAVIGATION.md`
- `docs/05_DATA_AND_COMPATIBILITY.md`
- `docs/07_OBSERVABILITY_AND_DIAGNOSTICS.md`
- `docs/08_QUALITY_AND_TESTING.md`
- T001/T002 完成后的代码

## 必须实现

### 1. Session 生命周期

状态只需要：

- Active；
- Ended。

要求：

- `Open diagnostic tool` 与 `Start diagnostic session` 在 API 上分开；只有 Start 才创建 `.nsdiag`。
- App 正常退出不结束 active Session。
- 下次启动恢复同一 active Session 并创建新的 process instance。
- 应用版本更新后仍可继续同一 Session。
- 只有显式 End 才结束 Session。
- Ended Session 不 reopen；重新复现应创建新 Session。
- 若上一个 Process 没留下正常结束记录，下次恢复时只标记 Unexpected/EndedUnexpectedly，不凭空断言 Crash。

### 2. Active marker

使用一个很小的 active-session marker 只指向当前 `.nsdiag`，不复制完整 Session 状态。

- `.nsdiag` 是权威真值。
- End 时先提交 Ended/flush，再移除 marker。
- marker 损坏/文件缺失/数据库打不开时不能阻塞 NovelSpeaker 启动。
- 第一版不扫描目录猜测“哪个文件可能 active”。

### 3. `.nsdiag` SQLite

建立最小、清晰、可迁移的 SQLite schema，逻辑数据覆盖：

- schema/session；
- process；
- activity；
- event；
- state snapshot；
- resource sample；
- environment/config context；
- 附件元数据预留（真正截图 T005）。

Problem Marker 作为稳定 Diagnostic Event，不需要独立复杂状态机。

数据库 schema version 与 app version 独立。迁移必须事务化；迁移失败时保护原文件并让主应用继续运行。

### 4. Activity / Event / Snapshot

通过 T001 Diagnostic Registry 和 Observability instrumentation 消费：

- Activity：稳定用户/系统操作，有开始/结束/结果/父子关系。
- Event：retry/fallback/state transition/problem marker 等瞬时事实。
- Snapshot：Navigation/Playback/Background 等有限领域状态。
- Resource Sample：低频、轻量资源采样。

禁止：

- 方法级 profiler；
- 任意对象 dump；
- SQL text；
- 用户内容；
- 自由 Dictionary 旁路 Registry。

### 5. 会话内匿名对象关联

提供 Session-local anonymous association：

- 同一真实对象在 Session 内获得稳定匿名 token；
- 允许分析“前后是不是同一本/同一章节”；
- 不持久化真实 BookId/ChapterId/名称/正文；
- 不提供 End 后的反向映射；
- 只为确实需要关联的对象类型使用。

### 6. Problem Marker 核心

提供 API 记录 `diagnostics.problem_marker`。

Marker 语义是“用户认为问题在该时间附近发生”，不是精确故障时刻。

Marker 可触发：

- 一次轻量 Resource Sample；
- 已注册的少量全局 Snapshot Provider；
- 当前 active Activity 摘要。

不要切换到另一套高频采样模式。

### 7. 硬容量上限

Session 创建时接受一个可配置 hard cap。

- 第一版只做 hard cap。
- 不做 soft cap。
- 不做旧时间线聚合/淘汰。
- 不按时间自动结束。
- 达到上限后停止继续采集，但已有 `.nsdiag` 保持可读。
- 保留足够余量完成必要的 capacity/end 元数据。
- 具体默认/preset 由实现集中定义，不写成长期产品合同。

### 8. 持续落盘与失败隔离

- 后台 writer 批量持续提交，不能只在 End 时保存。
- 正常关闭有限 flush；异常退出保留此前已提交的大部分现场。
- SQLite WAL/checkpoint 等具体策略自行选择，但一个结束后的 `.nsdiag` 必须自包含。
- Diagnostics store/writer 失败不得让被诊断的业务失败。
- 日志 writer 与 `.nsdiag` writer 独立。

## 不在本任务范围

- 悬浮控制条；
- Settings 诊断按钮；
- 主动截图；
- 录屏；
- Session 列表页面；
- 问题描述表单；
- Crash Dump；
- timeline.md / ZIP 导出；
- 自动历史聚合降级。

## 自动验收

至少覆盖：

1. Start 前不创建 Session/文件。
2. Active/Ended 唯一状态机。
3. 正常退出后 active session 可由新 process 继续。
4. Unexpected previous process 的事实判断。
5. End 顺序与 stale marker 清理。
6. marker 损坏/SQLite 打不开时应用仍可启动。
7. schema migration 失败不破坏原文件、不阻断应用。
8. Activity/Event/Snapshot 数据来自 Registry。
9. 匿名对象关联同 Session 稳定且不持久化真实 ID。
10. hard cap 可停止后续采集并保持文件可读。
11. diagnostic writer 故障不改变业务结果。
12. no-session 路径低开销且不写 `.nsdiag`。

运行 focused tests 和完整门禁。人工验收仅可选。

## 完成

自动验收通过后更新 T004 完成成果并删除本文件。
