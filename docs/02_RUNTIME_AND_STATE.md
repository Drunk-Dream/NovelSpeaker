# 运行时与状态

## 1. 生命周期层级

| 层级 | 示例 | Owner |
|---|---|---|
| Process | Shell、Settings、导航、托盘、Cache invalidation 等进程服务 | Process service/coordinator |
| Playback session | 当前书/章/段、音频、prefetch、snapshot | Playback Session Core |
| Page activation | 页面加载、draft、filter、selection、locator | Transient Page/ViewModel |
| Background job | 主动缓存、章节导出、speech-plan 补建、维护 | 对应 Cache/background coordinator |
| Operation | 导入、试听、清理、选择目录、保存 | 发起 use case/controller |

状态不能跨层级复制 owner。模块归属与生命周期是两个不同维度：例如 ActiveCache 属于 Cache 模块，但其运行态是 process background job；Playback Prefetch 属于 Playback session。

## 2. 启动顺序

```text
configure logging
→ load/normalize settings
→ build DI container
→ initialize/migrate database
→ recover unfinished operations
→ initialize cache/playback/background/desktop coordinators
→ create Shell
→ apply theme/close behavior
→ show main window or tray
```

数据库或 Playback 初始化失败时不得进入半初始化正常运行态。

## 3. Page activation

普通 Page/ViewModel transient。每次进入创建新的 activation scope：

- 一个 activation/version；
- 一个 CTS；
- 一个页面 owned-task 集合；
- 页面事件订阅；
- 页面 staged-loading 工作。

离开页面：

- 取消页面 CTS；
- 解除页面事件订阅；
- 丢弃迟到结果；
- 不取消 process/session/background owner 的工作。

页面快速离开再进入时，旧 activation 的结果必须通过 version/cancellation 被拒绝。

## 4. 页面事件入口

- `async void` 只允许 WPF event handler。
- handler 立即转交可等待流程。
- 异常通过统一安全错误投影处理。
- 未登记的 fire-and-forget 禁止存在。
- `OwnedTaskRegistry` 或等价机制必须有明确 owner、取消和异常观察。

## 5. Playback session

Playback session 是当前播放状态唯一 owner。

新书、显式章节/段落跳转、关键规则/文本配置切换等产生新的 session generation/epoch 或受控状态提交。

迟到的：

- HTTP；
- cache；
- audio callback；
- page projection；

不得覆盖更新后的 session。

### 5.1 当前阅读位置

运行时：

```text
PlaybackSnapshot = 当前活动书籍即时真值
```

持久化：

```text
ReadingProgress(SQLite) = checkpoint / 非活动与重启基线
```

显式跳转成功后 checkpoint 新逻辑位置。Pause、Stop、session replacement、shutdown 等仍是稳定 checkpoint 边界。

## 6. Playback command

所有会改变 session 的命令必须进入受控串行化边界：

- Start/Open；
- Play/Pause；
- Stop；
- JumpToChapter；
- JumpToSegment；
- relative move；
- audio completion/error 事件。

失败或取消不能提前提交目标位置。

## 7. Audio 生命周期

PlaybackAudioController 唯一拥有本地播放资源与 callback bridge；底层 NAudio 资源由 Infrastructure audio adapter 持有。

会话替换顺序由 Playback owner 明确控制：

```text
cancel stale/prefetch work
→ checkpoint if needed
→ stop/release old audio
→ resolve/commit new session
→ publish snapshot
→ start/load target audio as needed
```

禁止页面直接持有或释放 NAudio 资源。

## 8. Prefetch 与主动缓存

优先级：

```text
Current playback > Playback prefetch > Active cache
```

同一规则共用 admission/limiter，主动缓存不建立绕过限制的并发路径。

- Playback Prefetch 属于 Playback session。
- Active Cache 属于 Cache 模块和 Process background job。
- 二者可以复用同一窄的音频获取/TTS admission 能力，但不得通过相互依赖的 owner 实现复用。

## 9. 主动缓存后台任务

- 全应用最多一个 active cache batch。
- batch 拥有独立 CTS 与冻结配置快照。
- 页面切换、播放切章和主窗口隐藏不取消任务。
- coordinator 暴露 immutable progress snapshot。
- 取消停止未开始工作，已完成缓存保留。
- 任务完成/取消/失败后释放 active slot。

`ActiveCacheCoordinator` 属于 Cache 模块；不依赖 Playback session mutable state。

## 10. 章节 MP3 导出

导出是 Cache 相关的独立 Process background job：

- 全应用最多一个导出批次；
- 提交时冻结书籍、章节集合和目标目录；
- coordinator 拥有 CTS、Task、进度和终态；
- CacheManagement 只拥有提交前确认/目录选择；
- 页面离开不取消已提交导出；
- 真正退出应用时取消并有界等待。

不建设通用 BackgroundTaskManager。

## 11. Cache live projection 与 Speech Plan 补建

### 11.1 Cache invalidation

物理缓存只有在 file/index mutation 已提交后才发布 invalidation。

变更源报告最窄已知范围：

```text
Global
Book(bookId)
Chapters(bookId, indices)
```

Cache-local process coordinator 将短时间内连续 mutation 合并为一个 immutable invalidation batch，并记录受影响方面，例如：

- physical summary；
- cached catalog structure；
- current-configuration coverage。

active 页面只根据 batch 重新读取自己当前可见/需要的最小 read model。连续 mutation 不得形成 N 次全局 overview、整书 catalog 或完整度全量查询。

同一类刷新保持 single-flight：刷新过程中再次发生 mutation 时只标记 dirty，当前刷新结束后再补一轮，不并发堆积多个相同查询。

页面离开：

- 解除页面 invalidation 订阅；
- 取消尚未提交 UI 的页面刷新；
- 不停止 CacheStore、Playback、ActiveCache 或其它 process/background owner。

重新进入页面先读取当前 snapshot/read model，再开始 live projection。

### 11.2 Coverage invalidation

物理缓存变化会使对应章节 Coverage 可能变化。

Settings、TTS rule、Regex/text profile 等源模块只发布自身的 typed semantic change，不直接调用 Cache invalidation API。由 Cache-owned integration 订阅/消费这些稳定 change source，并判断是否映射为 Coverage 失效：

```text
source module change
→ Cache configuration-change integration
→ CacheInvalidation(Coverage)
→ active consumers re-query
```

配置变化可以使 Coverage 全局失效，但只表示旧 projection 不再可信：

- 不立即重算所有书籍/章节；
- BookDetails/Player/CacheManagement 只重算 current/viewport/明确受影响章节；
- 不因 Coverage 失效重建稳定 catalog。

### 11.3 Speech Plan 补建

`CacheCoverageQuery` 只读取并返回当前状态，不直接启动后台副作用。

完整度读取发现符合条件的过期/缺失计划时，由显式 orchestration 向 process `SpeechPlanRepairCoordinator` 登记异步补建：

- 同章请求合并；
- 有限并发；
- 页面只取消自己的等待/订阅；
- 计划在内存完整构建后短事务提交；
- 普通目录不为从未建立计划的普通章节无条件建立新计划；
- 补建完成后发布最窄章节级 Coverage invalidation；仍处于 active 状态且正在显示该章节的页面自行重读。

## 12. Settings

Settings process service 是当前设置 snapshot owner。

Settings Page/ViewModel transient：

- 即时设置直接投影/更新 process snapshot；
- 需要确认保存的设置持有 transient draft；
- 页面销毁不销毁设置本身。

Settings owner 只发布 previous/current 等设置自身语义，不直接协调 Cache、Theme、Playback 等消费者的内部状态。各消费者在自身边界解释设置变化。

## 13. Staged Loading 生命周期

复杂页面必须把加载工作分阶段：

```text
Critical
→ First Interactive Frame
→ Secondary Enrichment
→ Background Enhancement
```

所有阶段共享当前 activation token/version。页面退出后不允许旧阶段重新写入新页面。

## 14. UI Dispatcher

Dispatcher 只承担小型 UI 提交、WPF interaction 和 layout。

允许：

- 可见状态提交；
- property/collection notification；
- focus/scroll/navigation；
- WPF 控件交互。

避免：

- 大规模 DTO→VM projection；
- 大规模 sort/group/hash；
- SQLite/file materialization 后的重 CPU 处理；
- 随完整数据集 N 线性增长的连续工作。

`Task.Yield()` 只改变 continuation 排队时机，不提供后台线程保证。

## 15. 桌面生命周期

主窗口关闭：

- Hide to tray：隐藏主窗口，Process 继续；
- Exit：执行完整 shutdown；
- Ask：用户选择后执行相应路径。

迷你播放器与主窗口共享 Playback owner。关闭迷你窗口按既定产品语义进入统一退出流程，不创建第二套播放状态。

## 16. 关闭顺序

```text
block new UI operations
→ resolve navigation/edit guards
→ stop media/tray callbacks
→ checkpoint playback/settings
→ stop/release audio
→ cancel active cache/export/plan/maintenance jobs
→ bounded await
→ flush diagnostics
→ dispose host/container
```

关闭必须可等待、可重复且有上限；不得在 Dispatcher 上无界同步等待。
