# T008：建立 Playback logical target 与 preparation 身份

## 依赖与阶段性质

依赖 T007。从本任务开始进入 T008–T010 staged breaking migration window。

本切片允许 `PlaybackCoordinator`、runtime transition、部分测试和 App projection 暂时处于迁移中间态；不得为了临时可编译保留旧的“audio-ready 后 commit replacement”和新的 target-driven 双轨。T009 迁移完整播放流水线，T010 收口并恢复标准完整门禁。

## 背景

T004–T007 已解决 Playback 多份 mutable truth、旧字段 rollback、callback 身份和 snapshot 拼装问题，但当前 `ReplaceSessionAsync` 仍把以下动作绑定成一个过大的原子操作：

```text
resolve target
→ prepare/generate audio
→ commit replacement
→ publish snapshot
→ start local audio
```

结果是网络 TTS / cache miss 的耗时直接阻塞章节/段落在 UI 上的切换，并且真正耗时发生在 UI 尚未进入 loading state 时。

下一阶段不回退到旧多状态实现，而是在现有 authoritative runtime 上重新选择 commit boundary。

## 目标

把高层 Playback runtime 明确拆成同一 owner 下的四类事实：

```text
Session Context
+ Logical Target
+ Playback Intent / Preparation
+ Accepted Audio Facts
```

### Session Context

- `SessionId` 表示当前 Book + ActiveSource context 的播放生命周期。
- 同一 Book/Source 内的切段、切章、自动下一段不再创建完整 session。
- 切换 Book、ActiveSource、Source/Catalog context 已失效、Clear/Dispose 等才 replacement/retire session。
- session lifetime 继续拥有 cancellation、audio protection、prefetch 等需要随上下文销毁的资源。

### Logical Target

建立 runtime-owned target identity，至少包含：

- 当前可播放 `PlaybackPosition`；
- 单调递增 `TargetRevision`；
- 与当前 Book/Source context 一致的稳定身份。

Target 是 Player/ReadingProgress 的“用户现在位于哪里”的权威事实。Snapshot 的 ChapterIndex/SegmentIndex 必须从 target 投影，而不是从最后一个成功开始播放的音频推断。

### Playback intent / preparation

不要继续让单个 `PlaybackState` 同时承担用户意图、网络 preparation 和 low-level transport 三种职责。

可以根据现有代码选择最小 records/enums，但长期语义必须能够区分：

- 用户希望当前 target 播放还是保持 paused/stopped；
- 当前 target 是否正在准备音频；
- 是否处于 corrupt-audio recovery；
- 当前 target 是否已经最终失败/可重试；
- low-level audio 是否确实绑定并播放当前 target。

不要为每个组合建立爆炸式状态机；优先用少量正交字段，再由 Snapshot projector 派生用户可见状态。

### Preparation identity

一次异步音频准备必须绑定可校验 identity，至少覆盖：

```text
SessionId
+ TargetRevision
+ 当前有效 synthesis identity
```

synthesis identity 可以复用既有 Provider fingerprint / speed / stable segment identity 等事实，不要求新增持久 revision。

旧 preparation 即使忽略 cancellation 并返回，也只能成为 cache 中可独立成立的结果，不能启动当前播放器或改写 runtime。

## Transition 语义

新增/调整 runtime transition，使 logical target 可以在没有音频结果时合法 commit。

目标 commit 只要求：

- Book/Source context 当前有效；
- 目标章节/段落已经完成必要逻辑解析并可作为播放位置；
- command/session identity 未失效。

**不得要求目标音频已经生成、可解码或已经交给 local player。**

Target commit 后可以产生明确 effects，例如：

- checkpoint logical position；
- retire/stop 当前 target 的旧音频；
- cancel obsolete preparation；
- prepare current target audio；
- refresh prefetch。

这些 effect 失败不回滚已经提交的 target。

## ReadingProgress

- 用户显式 target commit 后即可保存新的逻辑 Chapter/Segment 位置。
- AudioPosition 只接受确实绑定当前 target 的 audio facts。
- 不把“旧段音频位置”拼到新 target。
- Book/Source session replacement 仍需在稳定边界 checkpoint retiring context。

## 强制减法

完成本切片时应开始删除或失效以下假设：

- “每个段落位置变化都必须 PrepareReplacement/CommitReplacement”；
- “准备失败必须保留/恢复旧逻辑位置”；
- “Snapshot 的位置只能在音频 ready 后变化”。

不得新增第二个 TargetService、NavigationState 或 App-owned playback target mirror。

## 核心测试

永久测试只保护稳定行为：

- 同一 session 可以 commit 多个 target revision，SessionId 不随普通切段变化；
- stale target/preparation result 被拒绝；
- target commit 不需要 audio ready；
- Book/Source context replacement 仍会拒绝旧 session 结果；
- checkpoint 的 logical position 与 accepted audio position 不串 target。

允许使用临时内部 transition tests 辅助迁移，T010 前删除不具长期价值的实现断言。

运行 Playback runtime/unit focused tests、format 和能运行的最小 build；完整 solution gate 留给 T010。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。