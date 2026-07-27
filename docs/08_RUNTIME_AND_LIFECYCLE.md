# 运行时与生命周期

## 1. 生命周期层级

NovelSpeaker 只使用下列明确生命周期：

| 层级 | 示例 | 所有者 |
|---|---|---|
| Process | Shell、设置快照、托盘、媒体控制、后台任务注册 | App lifecycle coordinator |
| Playback session | 当前书籍/章节/段落、音频、预取 | Playback coordinator |
| Page activation | 页面加载、编辑副本、页面导航守卫 | Page/ViewModel activation scope |
| Background job | 主动缓存批次 | Active cache coordinator |
| Operation | 导入、试听、清理、导出、保存 | 发起用例/控制器 |

状态不能跨层级复制所有权。

## 2. 启动顺序

建议启动：

```text
configure logging
  → load/normalize settings
  → build DI container
  → initialize/migrate database
  → recover unfinished operations
  → initialize playback/desktop coordinators
  → create Shell
  → apply theme and close/tray preference
  → show main window or tray according to setting
```

启动失败在 Shell 可用前必须投影为最小安全错误；不能继续运行半初始化数据库或播放器。

## 3. 组合根

- `App.xaml.cs` 只连接 WPF 生命周期和启动协调器。
- 注册按 Domain/Application/Infrastructure/App 功能模块分组。
- 非 Bootstrap 代码不注入或转发 `IServiceProvider`。
- 框架需要按 route 创建页面时，集中 Page provider/factory 是唯一允许的容器解析桥接。
- 测试验证关键服务可解析和 lifetime 合法。

## 4. Page activation

每个可导航页面使用统一 activation scope：

- 进入时创建新的 activation/version 和 CTS。
- 页面加载、刷新和 UI 投影绑定该 scope。
- 离开时取消页面拥有的工作并注销导航守卫/事件订阅。
- 快速离开再进入时，旧结果通过版本检查丢弃。
- activation 取消不得传播为用户错误 Snackbar。

播放会话、主动缓存、托盘和媒体控制不属于页面 scope。

## 5. 页面事件入口

- `async void` 只允许 WPF event handler。
- handler 立即转交 ViewModel/controller 的可等待方法。
- 所有异常必须被统一入口捕获并交给安全错误投影。
- 不允许未登记的 `_ = SomeTaskAsync()`；确需 fire-and-forget 时必须注册 owner、取消和异常观察。

## 6. Operation 生命周期

导入、试听、缓存清理、MP3 导出、设置保存等短操作：

- 各自拥有 CTS。
- 重复启动时按操作定义决定拒绝、替换或排队。
- 调用方取消直接结束，不转成失败。
- 需要临时文件/数据库事务时，finally 完成确定性释放和补偿。

缓存管理页的 MP3 导出从目录选择开始占用一个页面 Operation slot，重复启动直接拒绝。
取消按钮和页面离开取消该 Operation CTS；导出不会转交为 Process 级后台批次，也不会影响已移交给
主动缓存协调器的后台缓存任务。

## 7. Playback session

- 新书籍/章节/规则/语速/关键文本配置产生新 session generation。
- 当前音频、预取和进度保存都绑定 session/version。
- NAudio 事件只投递内部命令。
- 迟到 HTTP、缓存或音频回调不能更新新 session。
- Session 关闭按顺序取消预取、保存必要进度、停止音频、释放资源。

## 8. 主动缓存后台任务

主动缓存是 Process 下的 Background job：

- 全应用最多一个批次。
- 批次拥有独立 CTS 和不可变配置快照。
- 页面切换、播放切章和主窗口隐藏不取消任务。
- Application 暴露只读 progress snapshot；Shell/PlayerPage 可以同时订阅。
- 取消后停止未开始工作；已完成缓存保留。
- 完成/取消/失败后从 active slot 释放，不保留历史任务中心。

后台任务 registry 负责进程关闭时的取消和异常观察；不能只有“Task 列表登记”而没有业务状态 owner。

## 9. 共享 TTS admission

当前播放、预取、主动缓存通过同一规则级异步 limiter 申请执行资格。

优先级：

```text
Playback current > Prefetch > Active cache
```

等待必须可取消；不使用同步 Mutex 等待。优先级调度要避免主动缓存永久占用许可，也要避免在长时间播放时形成无法取消的积压。

## 10. 托盘与主窗口

关闭主窗口时按设置：

- Hide to tray：取消 WPF close，隐藏主窗口，进程继续。
- Exit：经过导航/未保存保护后执行完整进程关闭。
- Ask：显示一次选择，按用户决定执行上述路径。

启动最小化到托盘只改变初始窗口可见性，不改变应用初始化顺序。

托盘“退出”始终走显式 shutdown，不等价于关闭主窗口事件。

## 11. 迷你播放器

- 与主窗口共享 Process/Playback services，不创建第二套播放器或 ViewModel 状态机。
- 打开时隐藏主窗口；恢复时关闭/隐藏迷你窗口并显示主窗口。
- 关闭迷你窗口触发恢复主窗口，不退出进程。
- 窗口位置和置顶状态持久化；迷你模式本身不持久化。

## 12. 系统媒体控制

平台 adapter 在进程生命周期注册/注销媒体事件：

- Play/Pause → Application 播放命令。
- Previous/Next → 上一/下一段。
- 播放快照 → 系统媒体标题/副标题/播放状态。

回调线程不直接操作 WPF 控件，必须经平台调度器/应用命令边界。

## 13. 定时停止

计时器属于 Playback session 的临时控制器：

- 定时模式使用可替换 CTS/TimeProvider。
- “当前段结束/当前章结束”订阅稳定播放边界事件，不轮询 UI。
- 触发后调用 Pause，不取消主动缓存。
- 退出应用时取消；下次启动不恢复。

## 14. 关闭顺序

显式退出：

```text
block new UI operations
  → resolve navigation/edit guards
  → cancel active cache/background jobs
  → stop media/tray callbacks
  → persist playback/settings state
  → stop/release NAudio
  → flush diagnostics
  → dispose host/container
```

关闭流程必须可等待、可重复调用且有上限；不得在 UI Dispatcher 上无界同步等待。

## 15. 测试要求

- Page lifecycle：进入、离开、快速重入、旧结果晚到。
- Playback：session 替换、暂停、媒体命令、定时停止。
- Background cache：跨页面持续、优先级、取消、完成、失败。
- Tray/mini：隐藏/恢复/退出状态转换。
- 所有异步测试基于事件、状态版本或可控 `TimeProvider`，不用固定延时猜测。
