# 播放、预取与主动缓存

## 1. 核心原则

- Playback session 是播放状态唯一所有者。
- ViewModel 只发送命令和投影快照，不自行维护核心状态机。
- 旧会话结果通过 SessionId/版本和取消 Token 隔离。
- 当前播放、预取和主动缓存共用同一音频生成与缓存链路。
- 页面生命周期不拥有播放会话或后台主动缓存批次。

## 2. 播放状态

稳定 UI 状态包括：

- Idle
- Buffering / Generating
- Playing
- Paused
- Recovering
- Error

状态变化以 Application `PlaybackSnapshot` 为事实来源。UI 不从按钮文本、音频控件事件或缓存状态反推播放状态。

## 3. 当前段播放流程

```text
Resolve current chapter/segment
  → load/build and commit current chapter speech plan
  → compose optional chapter-title segment without renumbering body identity
  → resolve DisplayText/SpeechText and stable segment identity
  → empty SpeechText? skip audio
  → build SynthesisProfileFingerprint and AudioCacheKey
  → cache hit? validate/open
  → otherwise acquire playback-priority rule permit
  → execute TTS
  → validate audio
  → atomic cache write
  → NAudio playback
  → update progress
```

缓存损坏时删除该条目并允许一次正常重新生成；不能把损坏文件反复重试为成功。

正文段缓存身份不使用运行时 `SegmentIndex`。章节标题是独立合成段，因此开启或关闭“朗读标题”只改变播放序列和标题缓存需求，不使正文缓存失效。

## 4. 会话替换

以下动作建立新播放会话或替换当前 session generation：

- 切换书籍。
- 切换章节。
- 切换 TTS 规则。
- 修改语速。
- 影响 SpeechText 的文本处理配置变化。
- 朗读标题设置变化。

旧 HTTP/缓存/音频结果可以完成清理或形成合法缓存，但不能重新改变当前播放位置或 UI 状态。

## 5. 暂停与停止

暂停：

- 保留当前书籍、章节、段落和可恢复位置。
- 不继续扩展新的播放预取。
- 再次播放从合理位置继续。

停止：

- 结束当前播放会话并回到 Idle。
- 不删除已经完成的缓存。

播放音量属于当前音频输出状态，默认 100%，范围为 0%–100%。播放页和迷你播放器共享同一音量；调整只改变 NovelSpeaker 的播放输出，不改变 Windows 系统音量，也不写入 TTS 或缓存数据。音量作为应用设置持久化，应用重启后恢复上次值。

## 6. 预取

- 默认按设置预取少量后续段落。
- 预取只使用当前 session 的规则/语速/文本快照。
- 跳章、规则变化或 session 被替换后停止继续扩展旧预取。
- 已经完成且缓存键仍合法的结果可以保留。

请求优先级低于当前播放，高于主动缓存。

## 7. 主动缓存批次

主动缓存由独立的 Application coordinator 管理，不属于 PlayerViewModel。

批次创建时冻结：

- BookId 与章节集合。
- TTS 规则快照。
- 语速。
- 当前正文朗读清单、章节标题开关和稳定段身份。

行为：

- 全应用同一时间只有一个批次。
- 章节按书中顺序处理；每章内部按播放段顺序处理。
- 每章开始前按冻结文本配置确保当前朗读清单已经提交；已有当前合成配置、稳定段身份和 `SpeechText` 对应的有效缓存直接跳过。
- 切换播放章节、离开播放页或打开迷你播放器不影响批次。
- 用户可以取消尚未完成的工作；已完成缓存保留。
- 当前播放和预取通过共享 rule limiter 获得更高 admission priority。

## 8. 主动缓存进度

Application 发布只读任务快照，例如：

- BatchId / BookId
- 总章节数、已完成章节数
- 当前章节
- 当前章节已完成段 / 总段数
- 全批次已完成段 / 总段数
- Waiting / Running / Cancelling / Completed / Failed
- 可安全展示的错误摘要

Shell 只订阅快照并显示“缓存中 · 3/8 章 · 42%”；Flyout 可查看章节队列并取消任务。历史完成任务不持久化为“任务中心”。

## 9. 章节选择模式

播放页点击缓存入口后，章节目录进入选择模式：

- 单击：选中单章并清除无修饰键的旧选择。
- `Ctrl+Click`：增减单项。
- `Shift+Click`：按 anchor 做区间选择。
- `Ctrl+A`：选择全部章节。
- `Esc`：退出选择模式。
- 选择模式中单击章节不执行播放跳转。

“开始缓存”在至少选择一章且不存在冲突批次时可用。

## 10. 定时停止

定时停止是播放会话层临时状态，不是持久设置页配置。

支持：

- 15 / 30 / 45 / 60 / 90 分钟。
- 自定义时长。
- 取消定时停止。

触发后只暂停播放。主动缓存继续；预取不再自然扩展新的播放需求。

## 11. Windows 媒体控制

系统媒体与耳机控制映射：

- Play/Pause → 播放/暂停。
- Previous → 上一段。
- Next → 下一段。

Windows 媒体面板展示当前章节标题、书名和播放状态。平台事件必须通过 Desktop/Application port 转换为播放命令，不直接操作 ViewModel。

## 12. 迷你播放器

迷你播放器与主窗口共享同一个 Playback session：

- 只显示书名、当前章节、上一/下一章、上一/下一段、播放/暂停、可拖动的章节段落进度条；悬浮或拖动时显示 `xx/xx` Tooltip。
- 不显示系统标题栏。
- 提供置顶和恢复主窗口；已置顶时使用明确的强调状态。
- 空白区域可拖动窗口，按钮等交互控件区域不触发拖动。
- 主窗口隐藏时播放、主动缓存、托盘和媒体控制继续工作。
- 恢复主窗口只退出迷你模式；关闭迷你播放器退出应用，统一经过桌面生命周期退出流程。

## 13. 正文和目录定位

- 当前段自动居中遵循用户手动滚动抑制规则。
- 用户滚动离开当前段后显示“返回当前段落”入口。
- 播放页章节目录和书籍详情目录在滚离当前章节后显示“定位到当前章节”悬浮入口。
- 定位使用虚拟化安全的索引/容器定位和滚动动画，不假定所有 item 已生成。

## 14. 线程与事件

- NAudio/平台回调只投递短命令，不直接执行长异步流程。
- 核心会话修改串行化。
- 所有 I/O 传递 `CancellationToken`。
- 不用固定 `Task.Delay` 猜测事件完成。
- fire-and-forget 任务必须有 owner、取消和异常观察。
