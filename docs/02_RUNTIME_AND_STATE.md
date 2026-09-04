# 运行时与状态

## 1. 生命周期层级

| 层级 | 示例 | Owner |
|---|---|---|
| Process | Shell、Settings、导航、托盘、后台协调器 | Process service/coordinator |
| Playback session | 当前书/章/段、音频、prefetch、snapshot | Playback Session Core |
| Page activation | 页面加载、draft、filter、selection、locator | Transient Page/ViewModel |
| Background job | 主动缓存、章节导出、speech-plan 补建、维护 | 对应 coordinator |
| Operation | 导入、试听、清理、选择目录、保存 | 发起 use case/controller |

状态不能跨层级复制 owner。

## 2. 启动顺序

```text
configure logging
→ load/normalize settings
→ build DI container
→ initialize/migrate database
→ recover unfinished operations
→ initialize playback/background/desktop coordinators
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

PlaybackAudioController 唯一拥有本地音频输出、reader/stream 和 NAudio 资源。

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

Prefetch 属于 Playback session；Active cache 属于 Process background job。

## 9. 主动缓存后台任务

- 全应用最多一个 active cache batch。
- batch 拥有独立 CTS 与冻结配置快照。
- 页面切换、播放切章和主窗口隐藏不取消任务。
- coordinator 暴露 immutable progress snapshot。
- 取消停止未开始工作，已完成缓存保留。
- 任务完成/取消/失败后释放 active slot。

## 10. 章节 MP3 导出

导出是独立 Process background job：

- 全应用最多一个导出批次；
- 提交时冻结书籍、章节集合和目标目录；
- coordinator 拥有 CTS、Task、进度和终态；
- CacheManagement 只拥有提交前确认/目录选择；
- 页面离开不取消已提交导出；
- 真正退出应用时取消并有界等待。

不建设通用 BackgroundTaskManager。

## 11. Cache/Speech Plan 补建

完整度读取发现符合条件的过期/缺失计划时，由 process owner 异步补建：

- 同章请求合并；
- 有限并发；
- 页面只取消自己的等待/订阅；
- 计划在内存完整构建后短事务提交；
- 普通目录不为从未建立计划的普通章节无条件建立新计划。

## 12. Settings

Settings process service 是当前设置 snapshot owner。

Settings Page/ViewModel transient：

- 即时设置直接投影/更新 process snapshot；
- 需要确认保存的设置持有 transient draft；
- 页面销毁不销毁设置本身。

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
