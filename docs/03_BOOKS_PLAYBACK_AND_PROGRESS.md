# Books、Sources、Playback 与 Reading Progress

## 1. 核心链路

```text
Local TXT import
→ Book
→ Local Source
→ Source-owned Catalog + Content
→ text processing / segmentation
→ Speech Plan
→ Speech Provider / audio cache
→ Playback
→ Book-level Reading Progress
```

Books、Speech、Playback、Cache 各自拥有自己的稳定职责，不通过共享 mutable state 绑定。

Book / Source / Catalog / Content / ReadingState 的精确数据合同见 `specs/BOOK_DATA_MODEL.md`。

## 2. Book、Source 与 Catalog

### Book

Book 表示用户认知中的“一本书”。

- `BookId` 是创建后的永久内部身份。
- 书名 + 作者只用于自动发现/匹配候选 Book，不作为持久身份。
- Source 已明确绑定到 Book 后，后续 Source 元数据变化不改变 BookId。
- Book 保存当前展示元数据快照、ActiveSourceId、Book 级 ReadingState 以及书籍级时间状态。
- Book 可以绑定多个 Source，但任一时刻最多一个 ActiveSource。
- Book 允许暂时没有 ActiveSource；此时保留最后一次 ActiveSource 投影到 Book 的显示元数据，但正文不可用，直到用户明确选择 Source。

### Source

Source 表示一本 Book 的一个内容来源。

- Source 自己保存 Title / Author / Description / Cover 等来源元数据快照。
- 当前 Book 展示元数据来自 ActiveSource。
- 非 ActiveSource 更新只更新 Source 自己，不改变 Book 当前展示。
- 用户显式绑定 Source 时允许其书名/作者与 Book 当前元数据不同；显式绑定优先于自动匹配。
- 不允许把同一个 Source 重复绑定到同一 Book。
- 当前版本只实现 Local Source。Online Source 只作为未来边界，不在本轮实现具体规则系统。

### Catalog

Catalog 属于 Source，不属于 Book。

```text
Book
└─ Sources[]
   └─ Source
      ├─ Metadata Snapshot
      ├─ Catalog
      │  └─ Chapter entries[]
      └─ Content
```

- Book 不拥有一套独立主目录。
- 当前可见章节目录始终来自 ActiveSource.Catalog。
- Catalog entry 可以有技术性稳定 ID 供数据库、Speech Plan、音频缓存等内部引用，但产品逻辑不建立跨更新/跨 Source 的“Chapter Identity”推断体系。
- ChapterIndex/目录位置用于排序、定位、播放和 ReadingState，不根据标题、正文、哈希或相似度猜测“是否同一章”。
- 大型 Catalog 只包含稳定轻量字段；Current/Selected/CachePercentage/Loading 等动态状态作为独立 decoration。
- 页面通过场景化 read model 查询 Library、Book Header、Active Source Catalog 与当前阅读位置。

## 3. Local Source

Local Source 是用户导入 TXT 后形成的应用内持久来源快照。

- 每个 Book 最多一个 Local Source。
- 原始外部 TXT 只是导入输入；导入成功后，移动或删除原始 TXT 不影响已导入书籍。
- NovelSpeaker 永不写回用户外部 TXT。
- Local Source 保存解析后的 Catalog 与正文持久数据。
- 实现可以使用一个应用内规范化正文容器加章节范围来承载“各章节正文”；这属于内部存储方式，不等价于保留用户原始 TXT 文件引用或把 Local Source 当缓存。
- Local Source 正文是 Source 的永久数据，不属于可清理缓存。

TXT 导入、元数据识别、章节规则与空行分章的精确语义见 `specs/BOOK_IMPORT.md`。

## 4. Local Source 导入与重新导入

自动发现候选 Book 时使用严格的书名 + 作者匹配：

- 空作者也作为明确值参与匹配。
- 不做模糊标题、正文、文件名或哈希相似度匹配。
- 0 个候选：创建新 Book + Local Source，并把该 Local Source 设为 ActiveSource。
- 1 个候选：把本次 TXT 作为该 Book 的 Local Source 新建或更新。
- 多个候选：不自动选择；用户明确指定目标 Book 或选择新建 Book。

重新导入更新 Local Source 时：

- 保留 BookId。
- Local Source 作为完整新快照替换旧 Source metadata / Catalog / Content。
- Book 的 ReadingState 不因重新导入而建立章节内容匹配。
- 如果 Local Source 是 ActiveSource，则 Book 当前展示元数据同步为新的 Source 元数据。
- 如果未来 Local Source 不是 ActiveSource，则更新它不会自动激活，也不会改写 Book 当前展示元数据。
- 更新失败时旧 Local Source 必须保持完整可用；不得留下半旧半新的持久状态。

## 5. Reading Progress

ReadingState 属于 Book，而不是 Source。

当前持久化仍可以保存实现所需的：

```text
BookId
ChapterIndex
SegmentIndex
CharacterOffset
AudioPosition
UpdatedAt
```

长期语义只有两个核心坐标：

```text
Catalog position
+ position inside chapter
```

规则：

- ReadingState 不引用产品级 Chapter Identity。
- 当前活动书籍的即时 UI 位置使用 matching PlaybackSnapshot。
- 非活动书籍和应用重启使用 SQLite ReadingProgress。
- 显式章节/段落的 logical target 一旦完成必要位置解析并 commit，就及时 checkpoint 新逻辑位置；不以音频获取成功为前提。
- Pause、Stop、Book/Source session replacement、shutdown 等仍是稳定 checkpoint 边界。
- 页面不得直接写 ReadingProgress。
- 不进行逐毫秒高频 SQLite 写入。

### Source 切换或 Catalog 更新

不同 Source 共享同一 Book ReadingState。

- 不进行标题、正文、哈希、相似度或百分比映射。
- ChapterIndex 直接沿用；超过目标 Catalog 范围时截断到最后一个有效章节。
- 章内位置直接沿用；超过目标章节可用范围时截断到最后一个有效位置。
- Catalog 为空时保持可解释的未定位状态，不伪造章节。
- Source 切换后停止当前播放，不自动续播。
- Source/Catalog 更新期间的旧播放、预取或正文获取任务必须停止或失效，迟到结果不能覆盖新 ActiveSource。

## 6. 文本与 Speech Plan

章节消费：

```text
active Source chapter content
→ normalize / segment
→ regex/text profile
→ DisplayText / SpeechText
→ current ChapterSpeechPlan
```

正常朗读分段继续保持“每一个非空行是一个自然段”；导入期“空行分章”只影响 Catalog，不改变运行时 TextSegmenter 的自然段语义。

正则规则的精确执行语义见 `specs/REGEX_REPLACEMENT.md`。

Speech Plan 是当前配置下的派生数据，用于稳定段身份、朗读列表和音频缓存完整度，不成为正文权威来源。Catalog entry 的技术 ID 可以继续作为 Speech Plan/音频缓存的内部引用，只要业务逻辑不把它升级成跨 Source 的章节身份匹配系统。

## 7. Playback Session

Playback session 是当前活动 Book / ActiveSource context 的运行时 owner；logical target 与音频 preparation/transport 是 session 内部彼此解耦但由同一 runtime 统一协调的状态。

`PlaybackSnapshot` 是跨页面 immutable 投影。Page/ViewModel 只消费 snapshot，不复制 session、target 或 audio mutable truth。

内部数据流保持单向，但 commit 边界以用户目标为中心：

```text
user command / automatic advance / committed domain change
→ resolve valid logical target
→ commit target + increment target revision
→ publish PlaybackSnapshot
→ execute audio/prefetch effects
→ validate session + target/preparation identity
→ accept audio result
→ project next PlaybackSnapshot
```

长期规则：

- session identity 代表 Book / Source context 生命周期，不代表单个段落；同一 context 内切段、切章和自动推进只更新 logical target。
- logical target 至少包含可播放位置和单调 target revision。UI 当前章节/段落来自 target，不来自“最近成功加载的音频”。
- 显式导航在目标位置完成必要解析后立即 commit，随后才异步获取缓存/合成音频；网络或缓存时延不得阻塞 UI target 切换。
- 用户显式跳转时，旧 target 音频立即停止；不得在新 UI 位置继续朗读旧段落。
- low-level audio owner 只拥有设备资源、设备位置与播放回调。音频结果必须绑定当前 session + target/preparation identity，迟到结果直接丢弃。
- 音频准备失败、取消或 Provider 请求失败不会回滚 logical target；当前 target 进入可重试失败状态，或按既有自动失败恢复策略推进。
- 只有切换/失效 Book 或 ActiveSource context、关闭播放上下文等真正边界才 replacement/retire 整个 session。
- Books/Regex/Settings 的已提交变化由 Playback 自己订阅并进入同一串行 command/transition 边界，发起变化的页面不调用 Playback refresh API。
- Provider/语速等下一句配置变化不打断已经开始的当前音频；尚未开始的 preparation 必须使用或重新验证最新有效配置。

支持：

- Play/Pause/Stop；
- 上一/下一段；
- 章节跳转；
- 段落跳转；
- 音频完成/错误后的安全推进；
- 页面切换、托盘和迷你播放器之间持续播放。

### 等待与播放状态

“用户已经定位到哪里”和“声音是否已经开始”是两个不同事实：

- logical target commit 后，Player 立即高亮/显示新章节与段落；
- target 需要音频且尚未开始播放时进入 Preparing；
- 损坏音频重生成使用 Recovering；
- low-level audio 真正开始后才进入 Playing；
- Paused/Stopped/Faulted 保持明确用户语义；
- 若不存在真实的流式 buffer 阶段，不保留一个只在本地播放器交接时短暂闪现的高层 Buffering 状态。

加载提示描述“正在准备音频”，而不是“正在缓存”。短到不可感知的 preparation 不应闪烁提示；只有真实可感知等待才延迟显示，音频开始后立即消失，不人为延长最短显示时间。

### 播放失败恢复

播放失败恢复必须限制内容损失，同时避免单个坏段永久阻塞连续播放：

- 网络瞬断、超时、有限服务端错误等可恢复失败可以在拥有该失败语义的稳定边界执行有限重试；不要在 Provider Runtime 与 Playback 同时建立竞争的双重重试循环。
- 明显的无效配置、认证/权限失败或其它已知非瞬时错误不进行机械重复请求；它们可以直接进入“本段最终失败”。
- 一个段在有限重试后仍最终失败时，Playback 自动跳过该段并尝试下一段。
- 任一后续段成功得到可播放音频并正常进入播放后，连续自动跳过计数清零。
- 连续自动跳过达到 3 段后，在已经跳过第 3 个失败段后暂停自动推进；下一段不自动继续，等待用户重新播放、重试或调整 Provider/配置。
- 用户显式恢复播放后重新开始新的连续失败观察窗口。
- 自动跳过复用正常 target transition 与 checkpoint 语义；失败段的音频错误不能复活旧 target。

## 8. Source 激活与更新的长期语义

当前版本不实现 Online Source UI，但通用模型遵守：

- 绑定或更新一个非 ActiveSource 不自动激活。
- 普通“更新/刷新”只作用于 ActiveSource。
- ActiveSource 暂时不可用时不自动 fallback 到其它 Source。
- 删除 ActiveSource 时不自动切换到其它 Source；Book 可以进入 ActiveSource=None。
- 删除最后一个 Source 时同时删除 Book。
- 切换 Source 后停止播放且不自动续播。
- Source Catalog 更新采用完整快照替换；新快照准备失败时保留旧快照。
- 对未来 Online Source，正文缓存只在来源 locator 明确稳定一致时复用；不通过标题/位置/正文猜测。
- Online Source 的具体规则解析、网络获取、登录和 Legado 类能力不在当前实现范围。

## 9. Audio 与 Speech Provider

- Playback 通过稳定 Provider Runtime 请求音频，不自行解析 HTTP Provider 模板或判断具体 Provider Type。
- Provider、HTTP 模板、Edge Provider、试听与导入导出的精确合同见 `specs/HTTP_TTS.md`。
- 本地播放资源只有一个明确 owner；Page 不持有/释放 NAudio 资源。
- Provider 请求失败、取消、空音频等必须按稳定错误分类处理，不能错误推进大量阅读进度。
- 当前播放、prefetch 和 Active Cache 共享稳定 Speech admission 语义，但拥有不同生命周期。
- 全局语速属于 Speech/Playback 统一控制；播放音量属于本地 Audio Playback，不作为 Provider 配置发送。

## 10. Prefetch

Playback Prefetch 属于当前 Playback session，并面向当前/后续 logical target 工作；它不能成为第二个 target owner。

优先级：

```text
Current Playback
> Playback Prefetch
> Active Cache
```

Prefetch 可以复用 Cache/Speech 的稳定能力，但不成为 Cache background owner，也不与 Active Cache 共享 mutable lifecycle。

Speech Provider 或其有效合成配置变化后，尚未开始的新预取使用最新状态；旧配置已经生成的音频可以保留在缓存中，但不得因身份错误继续命中。

Target revision 改变后，与旧 target 强绑定且尚未消费的临时准备结果必须失效；可安全复用的物理缓存仍按 cache identity 保留。Source 切换属于 session context 切换，旧 Source 的未完成预取必须停止或失效。

## 11. Speech Provider 与播放

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

## 12. UI 投影

Library、BookDetails、Player 从稳定 read model/snapshot 构造 presentation：

- Library 只更新受影响卡片，不因播放位置变化重新查询整个书库。
- BookDetails 的 Active Source Catalog 与动态 decoration 分离。
- Player XAML 继续绑定一个页面 ViewModel，但内部可以使用 Feature-local controller 分离目录、正文、音频缓存 decoration 与交互。
- Player 的当前章节/段落跟随 committed logical target 立即更新，不等待音频准备完成。
- 音频等待反馈只反映当前 target 的 preparation/recovery；短等待不闪烁，真实等待显示明确的“正在准备音频”反馈。
- 面向用户的章节名称只使用 Chapter.Title；内部 ChapterIndex 不格式化为额外“第 N 章”。
- Speech Provider 选择器使用管理页同一 SortOrder，只展示名称；CurrentProvider 使用全局 Current 视觉语义，不显示“当前”文字。
- 用户主动定位优先于后台 decoration。
- 批量章节管理遵守 `specs/BATCH_MANAGEMENT.md`；批量管理选择状态不是 Playback session 真值。

## 13. 必须保护的行为

- BookId 在 Source 更新和重新导入后保持稳定。
- ActiveSource Catalog 才是当前章节框架；Book 不复制第二套目录真值。
- 当前活动位置与持久化 checkpoint 不冲突。
- Source 切换/Catalog 替换只做位置边界截断，不做模糊章节映射。
- 已完成必要逻辑解析的用户显式跳转先提交 logical target；后续音频准备失败/取消不回滚该 target。
- 迟到的 Source/Provider/cache/audio 结果不能覆盖更新后的 session context 或 logical target revision。
- 当前句与下一句之间的 Provider/config 切换语义稳定。
- 连续失败只能造成有限自动跳过；达到阈值后必须暂停等待用户。
- 页面生命周期不能销毁 Playback session。
- 主窗口、MiniPlayer、SMTC 共用同一播放状态。
- 超长章节目录仍可连续定位、滚动和播放。
