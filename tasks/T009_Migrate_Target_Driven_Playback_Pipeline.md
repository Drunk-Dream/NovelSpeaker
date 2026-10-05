# T009：迁移导航、自动推进与音频准备流水线

## 依赖与阶段性质

依赖 T008。处于 T008–T010 staged breaking migration window。

## 目标

把 `Start/Resume/Jump/Move/Completed/Retry` 等实际播放流程迁移到 target-driven 顺序：

```text
resolve logical target
→ commit target
→ publish Snapshot
→ execute audio effects
```

从用户操作到 UI target 更新不能再等待 `IAudioGenerationProvider.GetAudioAsync`、cache miss、HTTP TTS、音频验证或 local player start。

## 显式切段 / 切章

用户点击上一段、下一段、目录章节、正文段落或进度滑块时：

1. 在串行 command 边界解析有效 logical target；
2. 立即 commit target revision；
3. 立即发布 Snapshot，使 Player 高亮、章节标题、段落计数和 ReadingProgress 跟随新 target；
4. 如果旧 target 正在播放，立即停止旧音频；
5. 异步准备当前 target 音频；
6. 只有 identity 仍匹配时才启动 local audio。

若目标内容本身无法解析为合法位置，可以不 commit；但一旦 target 已 commit，后续音频失败/取消不得恢复旧位置。

不要为了减少静音时间继续播放旧段直到新音频 ready，因为这会造成 UI 与声音指向不同段落。

## Start / Resume / Pause

- Start/Resume 以当前 committed target 为基础发起 preparation，不重新建立相同 Book/Source session。
- 正在 Preparing/Recovering 时执行 Pause，应取消/失效当前 preparation，保留 logical target，并进入 Paused。
- Resume 从同一 target 重新准备或复用仍有效结果。
- 快速连续跳转采用 latest target wins；旧 preparation 的 cancellation 是优化，identity 校验才是 correctness boundary。

## 自动下一段

播放完成后：

```text
accept completed audio
→ checkpoint completed target
→ resolve next target
→ commit next target
→ publish Snapshot
→ consume valid prefetched/cache audio or prepare
→ play
```

自动推进与显式导航共用同一 target transition，不维护第二套“next segment”状态机。

如果 prefetch 已完成且身份有效，Preparing 可以极短甚至在 UI 上不可见；如果没有命中，则当前 target 已经切换，等待反馈由 T010 统一呈现。

## Failure / Retry / Skip

- 当前 target 音频最终失败时，Faulted/Retry 仍指向该 target，不回退旧 target。
- 用户 Retry 重新准备当前 target。
- 自动失败恢复需要跳过坏段时，显式 commit 下一 target，然后按同一 pipeline 准备。
- 连续 3 段失败暂停策略保持，不建立 parallel failure position。
- corrupt audio recovery 只失效当前 target 对应缓存/准备，不改变用户位置。

## Provider / speed / Regex

- 已经开始播放的当前句保持现有“当前句不打断”合同。
- 尚未开始的 preparation 必须在真正启动音频前重新验证 CurrentProvider、有效 Provider 配置和 speak speed。
- 配置变化导致已有 preparation obsolete 时，旧结果不得播放；可以重 prepare 同一 target，不需要 bump target revision，除非 logical position 本身变化。
- 影响 SpeechText 的 Regex 变化如果重新映射到不同位置，应通过正常 target transition 提交新 target；Display-only 变化不打断 audio。

## Prefetch

- Prefetch 仍属于 session owner。
- 当前 target revision 改变时，只取消/失效与旧 target 强绑定且不再需要的 in-flight work；不要机械清空可复用物理 cache。
- 后续窗口从 runtime 当前 target 和最新 synthesis configuration 计算。
- Prefetch result 本身不能推进 target。

## 清理方向

迁移完成后，普通段落导航不得继续依赖：

- audio generation 完成后才 `CommitReplacement`；
- `ReplaceSessionAsync` 承担 same-session target change；
- 为 preparation failure 恢复 previous session/position 的字段级 rollback；
- 仅为了 segment change 创建/销毁 session lifetime 和 cache protection owner。

允许保留一个窄的 Book/Source **session context replacement** 路径，它不能再成为普通 navigation 的默认 primitive。

## 核心验证

至少覆盖：

- cache miss / 人工 gate 阻塞音频时，Jump command 已经发布新 Snapshot；
- 新 target 等待期间旧音频已停止；
- 连续快速跳 3 个 target，前两个迟到结果均不能播放；
- target 生成失败后 UI/ReadingProgress 仍停在用户选择位置，Retry 针对该位置；
- 自动 next 在 cache hit 与 cache miss 下使用相同 target transition；
- Pause during preparation、Resume、Stop/Clear、shutdown 没有 orphan audio；
- Provider/speed 在 preparation 等待期间变化时不会播放旧配置结果；
- existing Source/Regex stale-result 防护仍成立。

测试使用 gate/barrier/identity，不使用固定 `Task.Delay` 猜网络时间。运行 Playback Application/Integration focused tests 和必要 Presentation tests；完整门禁留给 T010。

完成后更新 `TASK_BACKLOG.md` 并删除本文件。