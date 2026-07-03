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
Apply regex replacement pipeline when enabled in a later version
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


## 正则替换预留

第一版不实现正则替换，但播放链路需要为后续扩展保留边界。

正则替换位于章节正文读取和内容消费之间：

```text
Load chapter text by StartOffset/Length
  ↓
Create raw runtime segments with original offsets
  ↓
Apply enabled regex replacement rules
  ├─ DisplayText for UI
  └─ SpeechText for TTS
  ↓
Build TTS request and audio cache key from final SpeechText
```

要求：

- 正则替换不改写 `content.txt`。
- 正则替换不改写 `Chapters.StartOffset` 和 `Chapters.Length`。
- 展示和语音可以使用不同作用范围的替换结果。
- 语音文本变化必须形成新的音频缓存键。
- 缓存键使用最终处理后的 `SpeechText` 哈希，不使用正则规则配置哈希。
- 不同正则规则组合如果产生完全相同的最终语音文本，可以复用同一音频缓存。

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
| 当前段长期失败 | 允许用户跳过、重试或停止 |
| 连续多段失败 | 自动暂停并显示汇总错误 |

不要自动无限跳过，否则用户可能在不知情的情况下漏听大量内容。

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

## 当前实现边界

Epic H 当前已落地：

- 上层 `PlaybackCoordinator`。
- 下层 `ILocalAudioPlaybackCoordinator`。
- 运行时章节分段读取。
- 当前规则解析。
- 在线 TTS 播放闭环。
- 重试、跳过、跨章和语速/规则切换。

仍保留为后续 Epic 的能力：

- 持久化缓存及 LRU。
- 真正的预取调度和去重。
- 阅读进度恢复。
- 限流与 `Retry-After` 协同。
