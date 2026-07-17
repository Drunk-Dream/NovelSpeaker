# 播放链路与状态机

## 核心原则

播放逻辑是项目中最重要的业务逻辑。

第一版实现采用两层协调结构：

- `ILocalAudioPlaybackCoordinator`：只负责单个本地音频文件的加载、播放、暂停、停止、定位和本地解码错误。
- `IPlaybackCoordinator`：面向书籍、章节、段落、规则和会话，负责状态机、导航、自动推进、旧结果隔离和 UI 快照。

在线 TTS 不只是一次 HTTP 请求。必须统一处理：

- 当前播放位置。
- 缓存查询。
- 在线合成。
- 后续段落预取。
- 暂停和继续。
- 切段和切章。
- 会话取消。
- 旧请求结果隔离。
- 错误恢复。
- 进度保存。

## 播放状态

```csharp
public enum PlaybackState
{
    Idle,
    Preparing,
    Buffering,
    Playing,
    Paused,
    Stopped,
    Recovering,
    Faulted
}
```

状态含义：

- `Idle`：未选择可播放内容。
- `Preparing`：正在准备章节、段落或会话。
- `Buffering`：等待当前段音频生成或读取。
- `Playing`：正在播放。
- `Paused`：播放器暂停。
- `Stopped`：用户主动停止。
- `Recovering`：正在重试、删除损坏缓存或重新生成。
- `Faulted`：无法自动继续，需要用户处理。

## 播放会话

每次以下操作都应创建新会话：

- 从停止状态开始播放。
- 切换书籍。
- 跳转章节。
- 跳转到非相邻段落。
- 切换 TTS 规则。
- 修改影响音频的语速或配置。

```csharp
public sealed class PlaybackSession : IAsyncDisposable
{
    public Guid SessionId { get; }
    public CancellationToken CancellationToken { get; }
    public string BookId { get; }
    public int ChapterIndex { get; set; }
    public int SegmentIndex { get; set; }
    public long RuleId { get; set; }
    public int SpeakSpeed { get; set; }
}
```

旧异步任务完成时必须检查 `SessionId`。取消 Token 不足以保证所有第三方请求立即终止。

## 当前段播放流程

```text
Resolve current segment
  ↓
Load chapter text from Books.StoredFilePath using chapter StartOffset/Length
  ↓
Build runtime segment DisplayText/SpeechText
  ↓
Apply enabled regex replacement rules
  ↓
Resolve selected rule and speak speed
  ↓
Build cache key from final SpeechText
  ↓
Cache hit?
  ├─ Yes → validate cached audio
  └─ No  → execute HTTP TTS rule
              ↓
           validate response
              ↓
           atomic cache write
  ↓
Load into audio player
  ↓
Play
  ↓
Start prefetch for following segments
  ↓
On completed: save progress and advance
```

## 预取策略

第一版默认窗口：

```text
当前段：播放或准备
下一段：最高优先级预取
下下段：低优先级预取
```

要求：

- 每条规则默认最多 1～2 个并发网络请求。
- 必须同时遵守 `concurrentRate`。
- 跳章时取消旧预取。
- 已成功写入缓存的旧请求结果可以保留。
- 不允许旧请求改变当前 UI 或播放位置。
- 用户暂停时可继续预取一个有限窗口。
- 用户停止时停止预取。

## 自动推进

段落播放完成：

1. 标记当前段完成。
2. 保存下一段位置。
3. 检查章节内是否还有段落。
4. 若无，移动到下一章。
5. 若无下一章，停止并标记全书完成。
6. 加载缓存或进入缓冲。
7. 开始播放。

## 暂停和停止

### 暂停

- 保留当前音频和播放时间。
- 保存当前位置。
- 可保留有限预取。
- 再次播放时从当前音频时间继续。

### 停止

- 停止播放器。
- 取消当前会话和预取。
- 保存当前位置。
- 清理内存队列。
- 不删除已完成缓存。

## 语速处理

第一版的语速参数由 TTS 规则决定，不建议对已生成音频进行额外变速。

修改语速后：

- 创建新播放会话。
- 生成新缓存键。
- 从当前段起重新生成。
- 旧语速缓存继续保留，交给 LRU 清理。


## 正则替换

正则替换属于当前主线，位于动态段落切分和内容消费之间：

```text
Load chapter text by StartOffset/Length
  ↓
Create raw runtime segments with original offsets
  ↓
Apply enabled global regex replacement rules per segment
  ├─ DisplayText for UI
  └─ SpeechText for TTS
  ↓
Filter empty display/speech results and map progress by original offset
  ↓
Build TTS request and position-related audio cache key from final SpeechText
```

要求：

- 不改写 `content.txt`、`Chapters.StartOffset`、`Chapters.Length` 或原始段落边界。
- 固定使用 `RegexOptions.CultureInvariant` 和每条规则每段 `100 ms` 超时。
- 展示和语音可以使用不同作用范围的结果。
- 空 DisplayText 不显示；空 SpeechText 不请求 TTS；两者都为空时完全跳过。
- 执行字段、启用状态、排序或删除变化后立即重建当前章节，并取消受影响章节的旧预取。
- 当前映射段 SpeechText 变化或被过滤时才停止当前音频并从段首重建会话；仅 DisplayText 变化时当前音频继续。
- 修改前播放中则保持播放，修改前暂停中则保持暂停。
- 音频缓存键保持现有位置相关结构，并使用最终 `SpeechText`；不实现跨位置复用。

详细设计见 `12_REGEX_REPLACEMENT_PIPELINE.md`。

## 错误策略

| 错误 | 行为 |
|---|---|
| DNS、连接重置、超时 | 指数退避，最多重试 2～3 次 |
| 401、403 | 停止当前会话，提示凭据或鉴权错误 |
| 429 | 遵循 Retry-After，暂停该规则请求 |
| 5xx | 有限重试 |
| JSON/Text 错误响应 | 显示截断后的服务端错误 |
| 空音频 | 重试一次 |
| 缓存损坏 | 删除缓存并重新请求一次 |
| 当前段长期失败 | 暂停并提供再次尝试或切换规则；UI 不提供跳过 |
| 连续多段失败 | 自动暂停并显示汇总错误 |

不要自动跳过失败段落，否则用户可能在不知情的情况下漏听内容。协调器内部遗留的 skip API 不属于目标公共交互，应在调用审计和特征测试后收敛。

## 进度保存

保存位置应至少包括：

```csharp
public sealed record BookPosition(
    Guid BookId,
    int ChapterIndex,
    int SegmentIndex,
    long AudioPositionMilliseconds,
    int CharacterOffset);
```

保存时机：

- 每段播放完成。
- 暂停。
- 停止。
- 切换章节。
- 切换书籍。
- 应用正常关闭。
- 应用进入异常恢复前。

`CharacterOffset` 用于文本变化或重新分段后的近似恢复。第一版可主要依赖章节和段落索引。

章节正文来源于导入时保存的规范化 `content.txt`，播放阶段不会从 SQLite 读取整章正文。

## 线程模型

- UI 更新回到 WPF Dispatcher。
- 网络、数据库、文件和脚本执行不得阻塞 UI 线程。
- `PlaybackCoordinator` 的状态变更应串行化。
- 可使用 `SemaphoreSlim` 或内部命令队列保护状态。
- 不在多个事件回调中直接同时推进段落。

## UI 快照

```csharp
public sealed record PlaybackSnapshot(
    PlaybackState State,
    string? BookId,
    string? BookTitle,
    int ChapterIndex,
    string? ChapterTitle,
    int SegmentIndex,
    int SegmentCount,
    long? RuleId,
    string? RuleName,
    int SpeakSpeed,
    long PositionMilliseconds,
    long DurationMilliseconds,
    string? Message,
    bool IsUsingCache,
    bool CanRetry,
    bool CanSkip);
```

ViewModel 订阅快照，而不是直接读取多个可变服务字段。

## 播放代码边界

`PlaybackCoordinator` 是 Application 的稳定门面和唯一命令串行化入口。它保留会话状态所有权，但把可独立验证的职责交给内部协作者：

| 协作者 | 职责 |
|---|---|
| PlaybackSessionState | 当前书籍、章节、段落、规则、语速、SessionId 与取消源 |
| PlaybackPositionResolver | 相邻位置、恢复位置、章节边界和原始字符偏移映射的纯计算 |
| PlaybackSegmentRunner | 当前段音频获取、缓存损坏恢复和本地播放调用 |
| PlaybackPrefetchController | 预取窗口、优先级、去重和会话取消 |
| PlaybackProgressService | 统一保存/恢复进度 |
| PlaybackSnapshotProjector | 从内部状态生成不可变快照 |

Infrastructure 只实现：

- SQLite 阅读进度与缓存索引。
- 文件缓存和原子写入。
- HTTP TTS、Jint 和音频验证。
- NAudio 单文件设备适配。

ViewModel 只消费 Application 快照与命令，不自行加载正文、维护核心状态机或拼装缓存/HTTP 请求。完整分层见 `02_TECH_STACK_AND_ARCHITECTURE.md`。

## 章节加载状态

运行时必须明确区分：

- 章节摘要尚未加载正文。
- 正文已加载且包含可消费段落。
- 正文已加载，但正则规则使其没有可展示/可朗读段落。
- 章节加载失败。取消不提交新状态，旧会话或旧操作的迟到结果不得覆盖当前章节。

不得继续使用 `Segments.Count == 0` 同时表示“未加载”和“已加载为空”，否则空章节会被反复读取、分段并干扰自动推进。

## 当前能力基线

当前产品已经具有：双层本地/书籍播放协调、动态章节分段、正则展示/语音文本、完整下载后缓存、LRU、后续段预取、阅读进度恢复、限流、错误分类、规则/语速切换和旧会话隔离。架构重组必须保持这些行为，不得把它们重新列为待实现功能。
