# T007：清理 Playback 旧状态体系并完成 Phase B 验收

## 依赖与阶段性质

依赖 T006。本任务结束 T004–T007 staged breaking migration window，必须恢复标准完整门禁。

## 目标

确保仓库只剩一套 Playback runtime 数据流，清理迁移残留，并以行为和架构检查验证重构没有把复杂度平移成更多 coordinator/wrapper。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 2、4、5、10 节
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 7、9、11 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md`
- `docs/08_QUALITY_AND_TESTING.md`

## 结构审计

确认并清理：

- Coordinator、Session、Snapshot 各自保存同一 Book/provider/position/audio state 的副本；
- old/new/v2/compat runtime、forwarding wrapper 或只为测试存在的 public interface；
- 多套 SessionId/EventEpoch/generation 同时判断同一个 callback；
- UI/Books/Regex 调用者手工刷新 Playback；
- page/session/background 生命周期相互取消；
- Cache 反向读取 Playback mutable state；
- 仅为降低文件行数而拆出的无 owner 类。

保留 `PlaybackCoordinator` 作为稳定 façade/用例入口是允许的，但它应编排命令与 effects，而不是重新保存 authoritative fields。

## 核心行为验收

- cold start/open paused/start、pause/resume/stop/clear；
- chapter/segment navigation 和失败/取消 commit boundary；
- restart progress restore 与 checkpoint；
- Provider/speed/Regex/metadata/catalog/source changes；
- current playback/prefetch/cache priority；
- corrupt cache recovery、有限 retry、连续 3 段跳过后暂停；
- stale content/synthesis/audio callback；
- MiniPlayer、SMTC、Player 使用同一 snapshot；
- shutdown/dispose 有界且不会丢失稳定 checkpoint。

## 测试收敛

- 保留上述核心行为与并发边界测试。
- 删除直接锁定 private helper、旧字段同步顺序、旧 wrapper 调用次数的测试。
- 合并用不同入口重复验证同一 transition invariant 的低价值测试；不追求每个 command 的机械覆盖。

## 完整门禁

执行 `AGENTS.md` 标准完整门禁。额外运行仓库已有 architecture tests，并用 `rg` 确认旧类型/API 已删除。环境限制必须如实记录；代码或测试失败不得结束窗口。

完成后在 `TASK_BACKLOG.md` 记录新的 state owner、删除的旧状态/代理与测试变化，并删除本文件。
