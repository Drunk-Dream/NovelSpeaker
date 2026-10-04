# T004：建立 Playback authoritative runtime 与 transition 模型

## 依赖与阶段性质

依赖 T003。从本任务开始进入 T004–T007 staged breaking migration window。

允许结束时 `PlaybackCoordinator` 尚未迁移全部命令、部分 Playback tests/App 调用方暂时无法编译。不得保留第二套 runtime 或通过 old/new coordinator 转发维持中间兼容；T005–T006 继续迁移，T007 恢复完整门禁。

## 目标

建立一个唯一高层可变 Playback runtime/session owner，并把状态变化表达为可审查的 transition：

```text
command / domain change / effect result
→ validate identity and preconditions
→ transition authoritative runtime
→ emit effects
→ project immutable snapshot
```

重点不是把 `PlaybackCoordinator.cs` 按行数拆小，而是让“当前在播放什么、逻辑位置是什么、哪个 session 有效”只有一条数据流。

## 必读

- `AGENTS.md`
- `docs/01_SYSTEM_ARCHITECTURE.md` 第 2、4、5 节
- `docs/02_RUNTIME_AND_NAVIGATION.md` 第 1、7、11 节
- `docs/03_BOOKS_PLAYBACK_AND_PROGRESS.md` 第 5、7、9、10、13 节

## Runtime 最小职责

authoritative state 至少统一拥有：

- session identity 与 cancellation lifetime；
- Book/Source context 与已加载内容；
- chapter/segment logical position；
- selected Provider/config identity、speak speed；
- logical playback state、resume/checkpoint position；
- consecutive failure recovery window；
- 当前 audio protection handle；
- 形成 snapshot 所需的 message/cache/retry 等高层事实。

`PlaybackSnapshot` 不得反向写回 runtime，也不得作为独立 mutable truth。volume persistence、stop timer 等 process concern 可以留在明确的专属 owner，不必强塞入 session。

## Transition 边界

- 定义少量明确 transition/decision，而不是为每个字段建立 setter 或通用 state-machine framework。
- transition 对 session identity、合法位置和 commit boundary 做集中校验。
- effects（content resolve、synthesis/cache、audio、progress checkpoint、prefetch）通过现有窄角色执行；runtime 不直接依赖 Infrastructure。
- 失败/取消在 commit 前不能改变当前 session；effect result 必须能证明属于当前 session。

## 本切片范围

1. 建立 runtime state、transition result/effect intent 与纯 snapshot projection 的最小结构。
2. 用纯单元测试覆盖关键 transition invariant；不需要先迁移全部 coordinator 命令。
3. 开始移除 `PlaybackCoordinator` 中与新 runtime 重复的平行字段/alias，但只在调用方已迁入时删除。
4. 记录 T005/T006 尚未迁移的具体命令和 callback，不建立临时 compatibility layer。

## 非目标

- 不建立 Redux、通用 actor、CommandBus 或可配置状态机。
- 不改变用户可见播放语义、失败阈值、Provider 选择、进度格式或持久数据。
- 不拆分 Application assembly，不让 Cache 反向依赖 runtime。

## 强制验证

- 纯 transition tests 覆盖合法/非法位置、session identity、commit 前失败、snapshot 纯投影。
- runtime mutable facts 没有在新类型与 Coordinator 各保存一份；尚未迁移部分需明确列入任务成果。
- 执行 Playback focused unit tests 中仍适用的部分、Application build/静态检查；不要求完整 solution build。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。
