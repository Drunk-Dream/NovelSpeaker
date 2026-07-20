# 运行时、启动与生命周期

## 1. 文档定位

本文定义 NovelSpeaker 的进程启动、页面激活、长期会话、后台任务和关闭语义。开发阶段、迁移批次和任务依赖不属于数字编号设计文档，统一记录在 `TASK_BACKLOG.md`。

## 2. 生命周期层级

应用中只允许以下四类状态生命周期：

| 层级 | 示例 | 所有者 |
|---|---|---|
| Process | 应用目录、日志、设置快照、数据库、主题、全局播放协调器 | Bootstrap / ServiceProvider |
| Page activation | 页面加载、编辑副本、筛选、页面异步操作、导航守卫 | 当前 Page activation scope |
| Operation | 导入、规则试听、导出、自动保存、缓存清理 | 发起该操作的服务/控制器 |
| Playback session | 当前书籍、规则、语速、位置、预取和旧结果隔离 | PlaybackCoordinator |

不得用 Singleton ViewModel 代替明确的页面激活状态，也不得让页面取消源终止跨页面继续存在的播放会话。

### 2.1 注册与实际状态所有权

组合模块必须按下表声明生命周期，调用方不得复制同一状态或通过 `IServiceProvider` 另建实例：

| 注册/状态 | 当前生命周期 | 唯一所有者 |
|---|---|---|
| 应用数据目录、SQLite 初始化、Repository、设置 store、日志、主题、导航与反馈服务 | Singleton / Process | 根 `ServiceProvider`；由 Bootstrap 创建和释放 |
| `PlaybackCoordinator`、`PlaybackSessionState`、音频生成去重、预取与缓存保护状态 | Singleton 服务内的 Playback session | `PlaybackCoordinator` 创建和终止 session；音频与预取协作者只持有当前 session 派生状态 |
| `MainWindow`、Shell 导航状态和全局播放投影 | Singleton / Process | Shell 与全局播放协调器 |
| Page 对象及 BookDetails、Cache、Appearance、Diagnostics 等瞬态 ViewModel | Transient / Page activation | 当前导航 Page；离开后取消并释放其操作状态 |
| 导入、规则试听、章节加载、设置防抖保存和缓存清理 | Operation | 发起操作的 ViewModel/对话框或服务；各自拥有 CTS、版本和完成通知 |

当前仍注册为 Singleton 的 `LibraryViewModel`、`PlayerViewModel`、`SettingsViewModel`、`TtsRulesViewModel`、`ChapterRulesViewModel` 和 `RegexReplacementRulesViewModel` 是已登记的页面状态债务：本阶段保持既有行为，但不得将其作为新增页面的范式，也不得向其中继续加入跨 activation 的编辑副本或取消源。后续迁移到 activation scope 时，必须先用特征测试固定导航返回和播放跨页行为。

## 3. 启动顺序

启动由 `Bootstrap` 中的协调器按固定阶段执行：

```text
创建进程级 CancellationTokenSource
  ↓
解析并确保应用数据目录
  ↓
读取、规范化一次 settings snapshot
  ↓
建立脱敏日志
  ↓
构建并校验 DI 容器
  ↓
初始化 SQLite、运行迁移和恢复未完成文件操作
  ↓
载入默认数据
  ↓
应用主题与窗口外观
  ↓
创建 Shell，进入书库
  ↓
登记后台缓存维护
```

要求：

- 设置不能在 DI 前后各创建一套 store 并重复读取。
- 每个阶段可测试、可取消，并只输出脱敏诊断。
- 数据库/恢复失败时不展示主 Shell；使用启动状态窗口和最小安全错误提示。
- 不支持的数据库版本必须在任何业务写入前终止启动。
- 启动成功后才关闭启动状态窗口。

## 4. 组合根

`App.xaml.cs` 只连接 WPF 生命周期和启动协调器，不平铺所有服务注册与启动步骤。

组合顺序：

```csharp
services.AddNovelSpeakerApplication();
services.AddNovelSpeakerInfrastructure();
services.AddNovelSpeakerDesktop();
```

`AddNovelSpeakerInfrastructure()` 依次组合 Persistence、FileStorage、Books、Speech、Audio 和 Settings 适配器模块；顶层组合根不逐项注册适配器。生命周期决策在各功能注册模块中集中声明，并由测试启用 `ValidateOnBuild`/`ValidateScopes` 验证。App 的非 Bootstrap 代码不得解析 `IServiceProvider`。

## 5. 页面激活

每个导航 Page 是 activation 边界：

1. `OnNavigatedTo` 创建新的 activation scope、版本号和 CancellationTokenSource。
2. 加载 ViewModel 所需数据，并忽略旧 activation 的迟到结果。
3. 若页面存在编辑副本，向统一导航守卫注册 `CanLeaveAsync`。
4. `OnNavigatedFrom` 先取消页面操作，再注销事件和守卫，最后释放 scope。

未保存保护必须覆盖：

- 页面自己的返回按钮。
- 一级导航。
- `Alt+Left`、`Esc`、`Ctrl+,` 等快捷键。
- 导航栏“正在播放”入口。
- 由其它功能发起的跳转。
- 窗口关闭。

不得只在某个 ViewModel 的 BackCommand 内实现保护。

## 6. 操作生命周期

导入、试听、缓存清理和自动保存等操作具有独立 CancellationTokenSource：

- 同类新操作可以取消旧操作并递增版本。
- 页面离开时取消只属于该页面的操作。
- 用户取消映射为正常取消结果，不显示错误 Snackbar。
- 不可取消的提交区必须尽量短，并在进入前检查取消。
- 任何 `async void` 事件只负责捕获 WPF/设备事件并转交可等待方法；异常不得逃逸到进程级处理器。

## 7. 播放会话

播放是进程级服务拥有的长期会话，可以在书库和设置页面之间继续存在。以下操作创建新会话或新版本：

- 打开或切换书籍。
- 从停止状态开始播放。
- 跳章、跳到非相邻段或从详情页定位章节。
- 切换规则或语速。
- 正则语音文本变化导致当前段重建。

取消 Token 负责尽快停止工作，SessionId/版本号负责拒绝无法及时取消的迟到结果。只有协调器可以提交当前播放位置和快照。

## 8. 后台任务

后台缓存维护、日志刷新等进程级任务必须登记到生命周期协调器：

- 使用进程级 Token。
- 记录任务引用和安全失败结果。
- 不使用无所有者的 fire-and-forget `Task.Run`。
- 应用退出时发出取消并等待限定时间。
- 后台任务失败不得从未观察 Task 异常路径泄漏敏感内容。

预取不是任意后台任务，它属于当前 `PlaybackSessionState`；停止或换书时必须取消。

## 9. 关闭顺序

```text
阻止新的页面/操作提交
  ↓
请求当前页面未保存保护
  ↓
保存并停止/结束播放会话
  ↓
取消进程级 Token
  ↓
等待已登记后台任务的限定退出
  ↓
刷新设置与日志
  ↓
异步释放播放器、HTTP 与 ServiceProvider
  ↓
关闭进程
```

WPF 的同步退出事件可以保留最小桥接，但不得在 UI Dispatcher 上无界同步等待异步 I/O。超时后记录安全诊断并继续退出。

## 10. 未处理异常

- Dispatcher、AppDomain 和未观察 Task 异常都进入统一安全投影与滚动日志。
- 进程级处理器只用于最后防线，不能替代局部错误处理。
- UI 不显示堆栈、规则原文、完整 URL、Header、正文或服务端完整响应。
- 启动前错误使用独立最小窗口/MessageBox；Shell 建立后使用统一反馈服务。
- 发生致命错误前尽力保存当前阅读进度，但不得因保存失败覆盖原始故障。

## 11. 时间和测试

- 防抖、自动居中恢复、限流、重试和超时使用 `TimeProvider` 或可控调度器。
- 测试通过手动时间推进或明确完成信号等待，不使用任意 `Task.Delay`/`Thread.Sleep` 猜测状态。
- 页面生命周期测试覆盖进入、离开、快速重入、旧结果晚到和守卫取消。
- 进程生命周期测试覆盖启动阶段失败、后台任务失败、关闭取消和资源释放顺序。

## 12. 验收标准

- 启动只读取一份设置快照，DI 容器可验证且关键服务可解析。
- 所有页面具有一致的 activation/cancellation/guard 协议。
- 离开页面后，旧加载、试听、导入或自动保存不会更新新页面状态。
- 播放跨页面继续，但换书/跳转/规则变化不会接受旧会话结果。
- 后台任务均有所有者、取消源和退出等待。
- 关闭不在 UI 线程无界阻塞，并能释放播放器与日志资源。
- 生命周期测试不依赖固定延迟。
