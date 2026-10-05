# T010：收敛 Playback 等待体验并完成 Phase B2 验收

## 依赖与阶段性质

依赖 T009。本任务结束 T008–T010 staged breaking migration window，必须恢复标准完整门禁。

## 目标

完成 target-driven Playback 的用户可见状态、Player 投影、旧路径清理和核心验收，使以下产品语义稳定成立：

> 用户操作先改变播放 target；需要等待音频时给出真实、稳定的反馈；音频失败不会撤销用户导航。

## 用户可见状态

重新审计 `PlaybackState` 与 runtime fields。

长期语义：

- **Preparing**：logical target 已 commit，用户有播放意图，但当前 target 尚未开始出声；
- **Playing**：low-level audio 已确认绑定当前 target 并开始播放；
- **Paused**：保留当前 target，不主动出声；
- **Recovering**：当前 target 的坏音频正在失效/重生成；
- **Faulted**：当前 target 音频最终失败，可 Retry/调整 Provider；
- **Stopped/Idle**：按现有产品语义保留。

如果当前高层 `Buffering` 只表示“音频已经准备好，正在交给本地播放器”的极短内部交接，则删除该高层状态和相关 UI 分支。只有未来存在真实持续的 streaming buffer 且用户需要感知时再引入独立语义。

不要让 `PlaybackSnapshot.State` 再反向成为 authoritative mutable truth；它可以继续作为 runtime 正交事实的稳定用户投影。

## Player 等待反馈

当前段落计数区域的 loading feedback 改为 target preparation 驱动，而不是 audio-ready 后短暂 `Buffering` 驱动。

规则：

- target commit 后章节/段落 UI 立即更新；
- preparation 在短时间内完成时不显示 loading，避免自动下一段/缓存命中时闪烁；
- 当等待达到用户可感知阈值后显示紧凑进度 + `正在准备音频`；
- Recovering 使用明确的恢复/重新生成语义；
- 音频一旦开始或进入 Paused/Faulted/Stopped，loading 立即消失；
- 不设置“最少必须显示 N 毫秒”的强制驻留，避免声音已经开始却仍显示加载；
- 阈值可取约 250–300 ms，但它属于 UI 实现参数，不作为长期精确测试合同；
- 不使用“正在缓存”描述正常播放等待，因为当前工作可能是 cache lookup、HTTP synthesis、validation 或本地文件准备。

延迟显示属于 Presentation lifetime。它只能决定 feedback visibility，不能成为第二份 playback preparation truth，也不能延迟/修改 runtime transition。

## 播放按钮与命令

- Preparing/Recovering 属于“正在尝试播放当前 target”，主按钮应提供符合用户预期的暂停/取消播放意图，而不是因为 low-level 尚未 Playing 就表现成可以重复 Start 的普通 Play。
- Pause during preparation 需要取消/失效当前 preparation，保留 target。
- Faulted 的 Retry 继续针对当前 target。
- 快速显式跳转时 loading timer 与旧 target 一起失效，不得在新 target 上闪出旧提示。

具体图标/动画不建立新的长期像素测试。

## MiniPlayer / SMTC

审计 MiniPlayer 与系统媒体控制：

- 与 Player 读取同一 Snapshot；
- target 已切换但 audio 尚未开始时，标题/章节等可解释信息跟随新 target；
- play/pause command 不因 Preparing 被误判成重复 Start；
- 不创建第二套等待状态。

若系统媒体 API 只能表达有限状态，做最接近的稳定映射，不为平台限制污染核心 runtime。

## 清理

删除本阶段迁移后不再需要的：

- same-segment navigation `PrepareReplacement/CommitReplacement` 路径；
- previous-target rollback helper/state；
- 只服务旧 audio-ready commit 时序的 tests；
- 瞬时 Buffering 的 UI binding/文案（若审计确认无真实语义）；
- target/preparation 的兼容 wrapper、old/new 双轨和临时 instrumentation。

保留真正用于 Book/Source context replacement 的窄 lifecycle primitive，不因名称相似机械删除。

## 长期核心测试

保留少量行为导向测试：

1. 显式 Jump 在 audio gate 未放行时，Snapshot 已指向新 target；
2. audio failure 不回滚 target，Retry 使用同一 target；
3. stale preparation/audio callback 不能覆盖更新后的 target；
4. automatic next 命中 prefetch 时无错误 target，miss 时 target 先切换；
5. Pause/Resume during preparation 语义正确；
6. Provider/speed“当前句不打断、下一句最新配置”保持；
7. Source/Regex context invalidation 与 shutdown 不接受迟到结果。

Presentation 只需要验证 loading feedback 与 target-driven snapshot 的关键行为，不冻结精确 250/300 ms；使用可控 `TimeProvider` 或显式 scheduler/gate，不使用真实等待。

## 完整验收

执行：

```powershell
dotnet restore --locked-mode -r win-x64
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
git diff --check
```

并额外搜索确认：

- ordinary jump/move/completed 不再以 audio-ready `CommitReplacement` 为 commit boundary；
- Player loading 不再直接依赖瞬时 `Buffering`（若 Buffering 已删除则无残留）；
- 不存在 App/ViewModel-owned logical playback target；
- session identity、target revision、audio generation 各自只承担自己的有效性判断。

完成后在 `TASK_BACKLOG.md` 记录 target-driven Playback 的最终边界、净删除路径和测试结果，并删除本文件。