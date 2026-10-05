# T010A：Playback 架构收口与减法

## 依赖与阶段性质

依赖 T010。

本任务不属于 staged breaking migration window。T008–T010 已完成 target-driven Playback 的产品与架构迁移，本任务只做收口、减法和边界澄清。任务结束时仓库必须保持可构建，并执行标准完整门禁。

## 背景

T008–T010 已建立稳定的：

```text
Playback session context
+ committed logical target / TargetRevision
+ playback intent
+ audio preparation identity
+ accepted low-level audio facts
```

普通同 Book/Source 导航、自动推进和失败跳过已经采用：

```text
commit target
→ publish snapshot
→ stop obsolete audio
→ prepare current target asynchronously
→ reject stale preparation/audio result
```

当前用户体验符合预期，核心模型也已经从重构前的多份 mutable truth、字段 rollback 和 audio-ready session replacement 中脱离。

本任务不重新设计这套模型，只清理迁移后仍存在的重复表达和容易误导后续维护的残余结构。

## 目标

### 1. 收口 Playback effect 表达

先审计 `PlaybackTransition` 及全部 `PlaybackEffect` 的真实消费者，重点包括：

- `PlaybackStopTargetAudioEffect`
- `PlaybackRefreshPrefetchEffect`
- `PlaybackPrepareTargetAudioEffect`
- `PlaybackCancelPreparationEffect`
- `PlaybackCheckpointEffect`
- `PlaybackRetireSessionEffect`

要求：

- Runtime 产生的 effect 必须存在明确且唯一的执行方；
- 不允许同时存在“Runtime 返回 effect”与 Coordinator 另传 `stopAudio` / 直接调用同一副作用的双重表达；
- 对 `StopTargetAudio` 与 `RefreshPrefetch`，根据最终代码选择最简单方案：
  - 真正纳入统一 effect execution；或
  - 若 Runtime 不需要拥有该决策，则删除对应 effect，由现有明确 owner 直接处理。
- 选择标准是减少状态和控制流重复，不追求形式上的“所有东西都必须是 effect”。

不得建立通用 EffectBus、handler registry、visitor framework 或反射分派。

### 2. 明确 Session replacement 只表示 context replacement

审计并重命名容易误导的：

- `PrepareReplacement`
- `CommitReplacement`
- `PlaybackReplacement`
- `PlaybackPreparation`

若这些类型/方法实际只服务 Book / ActiveSource context replacement，应改成直接表达该语义的名称，例如：

```text
PrepareSessionReplacement
CommitSessionReplacement
PlaybackSessionReplacement
```

实际名称可按代码风格调整。

要求：

- 普通切段、切章、自动 next、retry/skip 不得重新走 session replacement；
- target preparation 与 session replacement 在命名上不再混淆；
- 不为兼容旧内部名称保留 forwarding wrapper。

### 3. 删除 target-driven 迁移残留

全仓审计 T008–T010 后的 Playback production/tests，删除：

- 无消费者 effect；
- 旧 audio-ready commit 时序留下的 helper；
- 已无意义的 rollback/compatibility 分支；
- 重复 identity 校验但没有独立安全意义的代码；
- 只锁定旧迁移形状、不再保护核心行为的测试；
- 过期注释、TODO、旧术语和临时 instrumentation。

不要机械删除有独立职责的 identity：

- `SessionId`：Book/Source playback context；
- `TargetRevision`：same-session logical target；
- preparation `AttemptId`：同 target 重启 preparation；
- local `AudioGeneration`：低层 player subscription/event generation。

只有证明确实重复时才合并。

### 4. 审计 PlaybackCoordinator 职责，但不按行数拆类

`PlaybackCoordinator` 当前仍是命令/effect façade，允许它作为 Playback process owner 维持较大体量。

逐块分类：

- public command façade；
- Book/Regex/Settings/Provider committed-change consumption；
- session/target transition orchestration；
- target audio preparation execution；
- prefetch window orchestration；
- local audio callback ingestion；
- ReadingProgress effects；
- stop timer；
- volume persistence；
- content/position resolution。

只有满足以下条件时才提取新的小 owner：

1. 有清楚的单一生命周期；
2. 输入/输出可以用现有稳定数据表达；
3. 提取后能净删除 Coordinator 中的 orchestration/state，而不是只把方法搬文件；
4. 不形成第二份 Playback mutable truth；
5. 至少能让一个职责边界明显更容易理解或测试。

如果不满足，保持在 Coordinator 中。**文件行数不是拆分类的理由。**

禁止建立：

- `PlaybackManager` / `PlaybackHelper` 一类泛化容器；
- 第二个 command processor；
- 第二份 target/session state；
- 通用 background/effect scheduler；
- 仅转发 Coordinator 方法的 façade。

## 明确不处理

### Local audio start 与 serialized command gate

当前 preparation 完成后会重新进入 Playback serialized boundary，再执行 local audio start/load。除非本任务发现明确 correctness bug，否则不改变这一并发边界。

不要因为理论上的 local file load latency 再引入新的并行 command/effect 状态机。

### Checkpoint 与 audio preparation 的先后关系

当前 target 已先 commit/publish，但 target transition 的 checkpoint effect 可能先于实际 preparation scheduling 完成。

本任务不把 checkpoint 与 preparation 改造成并行事务，也不改变 checkpoint failure 的现有产品语义；只有存在已证实 bug 才做最小修复。

这两项可在未来出现真实性能证据时独立优化。

## 用户行为必须保持

本任务不得改变：

- 同 Book/Source 切段切章立即更新 logical target/UI；
- 显式跳转停止旧音频；
- audio preparation failure 不回滚 target；
- Retry 作用于当前 target；
- Pause during preparation 保留 target；
- stale session/target/preparation/audio callback 被拒绝；
- Provider/Speed 当前句不打断、下一句使用最新配置；
- Recovering / Preparing / Playing / Paused / Faulted 的现有用户语义；
- Player 延迟显示“正在准备音频”的现有体验；
- 自动 next、prefetch、连续失败跳过策略。

## 测试策略

优先复用 T008–T010 已有核心测试，不因内部重命名或 helper 清理新增大量测试。

必须保留的核心保护包括：

- target 在 audio ready 前已经 commit；
- audio failure 不回滚 target；
- rapid jumps 只允许最新 preparation/audio 生效；
- Pause/Resume during preparation；
- automatic next 的 cache hit/miss；
- Provider/Speed preparation race；
- Source/Regex invalidation；
- session replacement 拒绝旧 session result；
- preparation feedback 延迟显示且不会跨 target/page activation 泄漏。

如需验证重构过程，可以建立临时测试，但完成前必须删除。

测试若只因为内部类型/方法重命名失败，应更新或删除实现细节断言，不得为测试保留旧 wrapper。

## 减法验收

完成任务前做一次 Playback 专项审计，并在 `TASK_BACKLOG.md` 的完成成果中记录：

- 删除了哪些无消费者 effect / helper / wrapper；
- Session replacement 的最终明确边界；
- `PlaybackCoordinator` 哪些职责保留，哪些被提取以及原因；
- production code 是否实现净删除，或至少没有因抽象产生净复杂度增长；
- 是否发现仍值得未来处理、但本任务明确不做的问题。

如果审计发现现有代码已经是最简单表达，可以只做命名、死代码和测试清理；**不要求为了让任务“看起来有成果”制造新的抽象。**

## 完整门禁

执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
git diff --check
```

并检查：

- LF 换行保持；
- 不存在临时脚本、截图、日志或测试副产物；
- 普通 target flow 不调用 session replacement；
- Runtime 产生的每一种 Playback effect 都有真实、唯一的消费路径，或已删除；
- App/ViewModel 没有新增 Playback mutable truth；
- 没有因本任务引入新的通用框架。

代码或测试失败不得标记 T010A 完成。

完成后更新 `TASK_BACKLOG.md`，记录收口成果并删除本文件。
