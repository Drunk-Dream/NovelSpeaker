# Books、Playback 与 Reading Progress

## 1. 核心链路

```text
TXT source
→ Book / Chapter catalog
→ normalized chapter text
→ text processing / segmentation
→ Speech Plan
→ TTS / cache
→ Playback
→ Reading Progress
```

Books、Speech、Playback、Cache 各自拥有自己的稳定职责，不通过共享 mutable state 绑定。

## 2. Book 与 Chapter

- 外部 TXT 是正文权威来源，应用不写回。
- SQLite 保存书籍、章节和必要定位元数据。
- 章节规则变化后可以重建章节结构。
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

规则的精确执行语义见 `specs/REGEX_REPLACEMENT.md`。

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

## 6. Audio 与 TTS

- Playback 通过稳定 Speech/TTS 能力请求音频，不自行解析 TTS 规则字符串。
- HTTP TTS 的精确规则与执行合同见 `specs/HTTP_TTS.md`。
- 本地播放资源只有一个明确 owner；Page 不持有/释放 NAudio 资源。
- TTS 请求失败、取消、空音频等必须按稳定错误分类处理，不能错误推进大量阅读进度。
- 当前播放、prefetch 和 Active Cache 共享稳定 TTS admission/rate-limit 语义，但拥有不同生命周期。

## 7. Prefetch

Playback Prefetch 属于当前 Playback session。

优先级：

```text
Current Playback
> Playback Prefetch
> Active Cache
```

Prefetch 可以复用 Cache/Speech 的稳定能力，但不成为 Cache background owner，也不与 Active Cache 共享 mutable lifecycle。

## 8. Rules 与播放

- 播放页只在已启用 TTS 规则中选择当前规则。
- 当前规则被禁用或删除后清空选择，不隐式回退到另一规则。
- 新建、导入或重新启用规则不自动成为当前规则。
- 影响 SpeechText 的 Regex/Text 配置变化根据来源位置和新的 Speech Plan 解析最接近可播放段，不简单复用旧数组索引。
- 只影响 DisplayText 的变化尽量不打断音频。

## 9. UI 投影

Library、BookDetails、Player 从稳定 read model/snapshot 构造 presentation：

- Library 只更新受影响卡片，不因播放位置变化重新查询整个书库。
- BookDetails 的 catalog 与动态 decoration 分离。
- Player XAML 继续绑定一个页面 ViewModel，但内部可以使用 Feature-local controller 分离目录、正文、缓存 decoration 与交互。
- 用户主动定位优先于后台 decoration。

## 10. 必须保护的行为

- 当前活动位置与持久化 checkpoint 不冲突。
- 失败/取消的跳转不提交目标位置。
- 迟到的 HTTP/cache/audio 结果不能覆盖新 session。
- 页面生命周期不能销毁 Playback session。
- 主窗口、MiniPlayer、SMTC 共用同一播放状态。
- 超长章节目录仍可连续定位、滚动和播放。
