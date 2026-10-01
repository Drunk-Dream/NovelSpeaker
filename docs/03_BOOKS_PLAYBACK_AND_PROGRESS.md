# Books、Playback 与 Reading Progress

## 1. 核心链路

```text
TXT source
→ metadata / chapter recognition
→ Book / Chapter catalog
→ normalized chapter text
→ text processing / segmentation
→ Speech Plan
→ Speech Provider / cache
→ Playback
→ Reading Progress
```

Books、Speech、Playback、Cache 各自拥有自己的稳定职责，不通过共享 mutable state 绑定。

## 2. Book 与 Chapter

- 外部 TXT 是正文权威来源，应用不写回。
- 持久化书籍元数据至少包含 Title、Author 和可选 Description；章节保存稳定内部索引与章节 Title。
- ChapterIndex 只用于排序、定位、播放、进度与缓存等内部事实，不作为面向用户的章节编号显示。
- 用户可见章节目录、当前章节和其它章节名称位置直接展示 `Chapter.Title`；只有源内容没有标题而系统必须生成标题时，才展示系统生成的“第 N 节”。
- 元数据与章节识别的精确导入语义见 `specs/BOOK_IMPORT.md`。
- 章节识别配置变化后可以在明确的重新导入/重建流程中重建章节结构；配置变化本身不静默重写已有书籍。
- 大型章节 catalog 只包含稳定轻量字段；Current/Selected/CachePercentage/Loading 等动态状态作为独立 decoration。
- 页面通过场景化 read model 查询 Library、Book Header、Chapter Catalog 与当前阅读位置。

## 3. 文本与 Speech Plan

章节消费：

```text
chapter source
→ normalize / segment
→ regex/text profile
→ DisplayText / SpeechText
→ current ChapterSpeechPlan
```

正常朗读分段继续保持“每一个非空行是一个自然段”；导入期“空行分章”只影响 Chapter catalog，不改变运行时 TextSegmenter 的自然段语义。

正则规则的精确执行语义见 `specs/REGEX_REPLACEMENT.md`。

Speech Plan 是当前配置下的派生数据，用于稳定段身份、朗读列表和缓存完整度，不成为正文权威来源。

## 4. Playback Session

Playback session 是当前活动书籍、章节、段落和播放状态唯一运行时真值。

`PlaybackSnapshot` 是跨页面的 immutable 当前状态投影。Page/ViewModel 只消费 snapshot，不复制 session mutable truth。

支持：

- Play/Pause/Stop；
- 上一/下一段；
- 章节跳转；
- 段落跳转；
- 音频完成/错误后的安全推进；
- 页面切换、托盘和迷你播放器之间持续播放。

所有改变 session 的命令必须在受控边界中提交；失败/取消不能提前改变逻辑位置。

### 播放失败恢复

播放失败恢复必须限制内容损失，同时避免单个坏段永久阻塞连续播放：

- 网络瞬断、超时、有限服务端错误等可恢复失败可以在拥有该失败语义的稳定边界执行有限重试；不要在 Provider Runtime 与 Playback 同时建立竞争的双重重试循环。
- 明显的无效配置、认证/权限失败或其它已知非瞬时错误不进行机械重复请求；它们可以直接进入“本段最终失败”。
- 一个段在有限重试后仍最终失败时，Playback 自动跳过该段并尝试下一段。
- 任一后续段成功得到可播放音频并正常进入播放后，连续自动跳过计数清零。
- 连续自动跳过达到 3 段后，在已经跳过第 3 个失败段后暂停自动推进；下一段不自动继续，等待用户重新播放、重试或调整 Provider/配置。
- 用户显式恢复播放后重新开始新的连续失败观察窗口；若根因仍未解决，最多再自动跳过有限段后再次暂停。
- 自动跳过必须复用正常受控推进与 checkpoint 语义，不允许一次错误直接跳过大量章节或把失败位置错误提交为远端新位置。

## 5. Reading Progress

运行时与持久化语义：

```text
matching PlaybackSnapshot
    > persisted ReadingProgress baseline
```

- 当前活动书籍的即时 UI 位置使用 matching PlaybackSnapshot。
- 非活动书籍和应用重启使用 SQLite ReadingProgress。
- 显式章节/段落跳转成功后及时 checkpoint 新逻辑位置。
- Pause、Stop、session replacement、shutdown 等是稳定 checkpoint 边界。
- 页面不得直接写 ReadingProgress。
- 不进行逐毫秒高频 SQLite 写入。

## 6. Audio 与 Speech Provider

- Playback 通过稳定 Provider Runtime 请求音频，不自行解析 HTTP Provider 模板或判断具体 Provider Type。
- Provider、HTTP 模板、Edge Provider、试听与导入导出的精确合同见 `specs/HTTP_TTS.md`。
- 本地播放资源只有一个明确 owner；Page 不持有/释放 NAudio 资源。
- Provider 请求失败、取消、空音频等必须按稳定错误分类处理，不能错误推进大量阅读进度。
- 当前播放、prefetch 和 Active Cache 共享稳定 Speech admission 语义，但拥有不同生命周期。
- 全局语速属于 Speech/Playback 统一控制；播放音量属于本地 Audio Playback，不作为 Provider 配置发送。

## 7. Prefetch

Playback Prefetch 属于当前 Playback session。

优先级：

```text
Current Playback
> Playback Prefetch
> Active Cache
```

Prefetch 可以复用 Cache/Speech 的稳定能力，但不成为 Cache background owner，也不与 Active Cache 共享 mutable lifecycle。

Provider 或其有效合成配置变化后，尚未开始的新预取使用最新状态；旧配置已经生成的音频可以保留在缓存中，但不得因身份错误继续命中。

## 8. Provider 与播放

- CurrentProvider 是全局设置，值为 ProviderId 或 None。
- 播放页是主动改变 CurrentProvider 的唯一入口。
- 播放页只列出已完成必要配置且当前可见的 Provider，并使用 Provider 的统一排序。
- 不把 None 作为常用切换项展示。
- 删除当前 Provider、隐藏当前实验性 Provider、或使当前 Provider 失去必要配置时，CurrentProvider 清空为 None，不自动回退。
- Provider 恢复可用后不自动恢复 CurrentProvider。
- 切换 Provider、保存当前 Provider 新配置、或 CurrentProvider 变为 None 均不打断已经开始播放的当前语句。
- 下一条尚未开始的语句使用最新 CurrentProvider 与最新已保存 Provider 配置。
- 不建立 Provider fallback 链。
- 影响 SpeechText 的 Regex/Text 配置变化根据来源位置和新的 Speech Plan 解析最接近可播放段，不简单复用旧数组索引。
- 只影响 DisplayText 的变化尽量不打断音频。

## 9. UI 投影

Library、BookDetails、Player 从稳定 read model/snapshot 构造 presentation：

- Library 只更新受影响卡片，不因播放位置变化重新查询整个书库。
- BookDetails 的 catalog 与动态 decoration 分离。
- Player XAML 继续绑定一个页面 ViewModel，但内部可以使用 Feature-local controller 分离目录、正文、缓存 decoration 与交互。
- 面向用户的章节名称只使用 Chapter.Title；内部 ChapterIndex 不格式化为额外“第 N 章”。
- Provider 选择器使用管理页同一 SortOrder，只展示名称；CurrentProvider 使用全局 Current 视觉语义，不显示“当前”文字。
- 用户主动定位优先于后台 decoration。

## 10. 必须保护的行为

- 当前活动位置与持久化 checkpoint 不冲突。
- 失败/取消的跳转不提交目标位置。
- 迟到的 Provider/cache/audio 结果不能覆盖新 session。
- 当前句与下一句之间的 Provider/config 切换语义稳定。
- 连续失败只能造成有限自动跳过；达到阈值后必须暂停等待用户。
- 页面生命周期不能销毁 Playback session。
- 主窗口、MiniPlayer、SMTC 共用同一播放状态。
- 超长章节目录仍可连续定位、滚动和播放。
