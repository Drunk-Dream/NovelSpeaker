# T006：收敛 Audio effect、异步回调与 Snapshot 投影

## 依赖与阶段性质

依赖 T005。处于 T004–T007 staged breaking migration window。

## 目标

把 low-level audio 明确为 Playback runtime 发出的 effect 和返回的 result，消除 `LocalAudioPlaybackSnapshot`、`PlaybackSessionState.CurrentAudio`、Coordinator `_currentSnapshot` 之间需要手工同步的多份高层真值。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 4 节
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 7、11 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 7、9、13 节

## Audio 边界

- `LocalAudioPlaybackCoordinator` 继续唯一拥有 `IAudioPlayer`/NAudio 资源、设备 position/duration、volume 应用和底层 callback 串行化。
- 每个 start/effect 使用稳定 Playback session identity；snapshot/completed/failed callback 必须携带或可可靠关联该 identity。
- 高层 runtime 只接受当前 committed session 的 result。旧 audio completed/failed/position callback 一律丢弃，不能靠多个互相重叠的 epoch/version 猜测。
- logical position、failure window、selected provider 等不下沉到 low-level audio owner。

## Snapshot

- `PlaybackSnapshotProjector` 从一次完整 runtime state + 当前可接受的 audio facts 纯投影。
- `_currentSnapshot` 若为对外缓存，只能由统一 publish 路径替换，不能成为业务判断或被字段级 patch 当作第二状态源。
- 每次发布要么反映一个完整 transition，要么是当前 session 的有效 audio progress；不得产生 Book/position/provider 跨版本拼接。

## 减法候选

`PlaybackAudioController` 当前主要代理 `ILocalAudioPlaybackCoordinator`、转发事件并包装 dispose。本任务必须基于最终职责判断：

- 若没有独立 policy/ownership，删除该 wrapper，让 runtime owner 直接消费稳定 audio port；
- 若确有必要保留，必须让其拥有清晰且不可由底层 port 替代的职责，并删除纯转发 API。

同样审计并删除只为旧状态同步存在的：`CurrentAudio` 副本、`GetLocalPlaybackSessionId`、重复 `IsCurrent*`、EventEpoch/local session version 等。保留低层资源安全真正需要的 version。

## 强制验证

- 模拟旧 session completed/failed/snapshot 在新 session commit 后迟到，不改变新状态或进度。
- rapid start/stop/start、seek、pause/resume、audio load failure 与 disposal 无死锁、重复完成或错误 checkpoint。
- Snapshot 在同一发布中 Book/Source/position/provider/audio identity 一致。
- volume 与 stop timer 行为保持；dispose 解除事件并有界完成。
- 执行 local audio、Playback event/concurrency focused tests与必要 build；完整门禁留给 T007。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
