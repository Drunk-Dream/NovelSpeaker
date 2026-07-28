# 技术栈与目标架构

## 1. 技术栈

- C# / .NET 10
- WPF + Wpf.Ui 4.x
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Microsoft.Data.Sqlite.Core + SQLitePCLRaw.bundle_winsqlite3
- Jint
- NAudio
- xUnit

目标平台为 Windows 10/11 x64，自包含发布。

## 2. 依赖方向

```text
NovelSpeaker.Domain
        ↑
NovelSpeaker.Application
        ↑
NovelSpeaker.Infrastructure
        ↑
NovelSpeaker.App
```

实际项目引用遵循 Clean Architecture 方向：

- Domain：纯业务值、状态和不含技术细节的规则模型。
- Application：用例、端口、DTO、播放/缓存/规则编排。
- Infrastructure：SQLite、文件、HTTP、Jint、NAudio、设置和诊断适配。
- App：WPF Shell、Page/View/ViewModel、平台桥接和组合根。

App 的业务页面不得直接依赖 Infrastructure；所有业务动作通过 Application 合同进入。

## 3. 功能切片

稳定功能切片如下：

| 切片 | Application 负责 | Infrastructure 负责 |
|---|---|---|
| Books | 导入、删除、元数据、章节规则、文本处理 | 正文文件、编码分析、SQLite repository |
| Speech | 规则导入/编辑、请求编译、试听、错误分类 | JSON parser、Jint、HTTP transport、音频探测 |
| Playback | 会话状态机、当前段调度、预取、进度 | NAudio 播放、进度 repository |
| Cache | 缓存键、查询、保护、LRU、主动缓存、导出编排 | 缓存文件、索引、MP3 编码/合并 |
| Settings | 设置更新、规范化、业务约束 | `settings.json` 原子存取 |
| Desktop | 媒体控制、托盘、迷你窗口的 Application port | —（Windows/WPF 平台 adapter 位于 App） |

不得为了“未来可能复用”建立通用事件总线、万能 Manager 或新的 Service Locator。

## 4. Application 用例原则

- 用例名称表达动作或查询，不暴露 SQLite、HTTP、Jint、NAudio、WPF 类型。
- 公共接口只在存在真实边界、替换价值或测试隔离价值时创建。
- 同一状态只能有一个所有者；ViewModel 不复制播放状态机、缓存队列或规则编辑状态。
- DTO 只服务于实际调用边界；重复的 read model、mapper 和兼容 wrapper 应收敛。
- 用户可观察错误使用稳定结果类型或安全异常投影，技术异常不直接泄露到 UI。

## 5. DI 与组合根

目标规则：

- `IServiceProvider` 只允许存在于 App 组合根、框架要求的 Page provider/factory 等桥接层。
- Page、ViewModel、Application service 不得主动从容器解析依赖。
- 各功能通过 `AddNovelSpeaker...()` 注册模块集中声明生命周期。
- Singleton 不捕获页面级对象或短生命周期资源。
- 页面实例由集中导航 provider/factory 创建，不在页面内部 service-locate。
- 构建测试开启可解析、scope/lifetime 和依赖方向验证。

## 6. 状态所有权

| 状态 | 唯一所有者 | 生命周期 |
|---|---|---|
| 当前播放会话 | Playback Application service | Playback session |
| 当前音频输出 | Local audio coordinator | Playback session |
| 页面加载/编辑副本 | 对应 Page/ViewModel | Page activation |
| 主动缓存批次 | Application background cache coordinator | Process/background job |
| 托盘/迷你窗口 | Desktop shell coordinator | Process |
| 当前设置快照 | Settings service | Process |
| 短操作 | 发起用例/控制器 | Operation |

页面离开只能取消页面拥有的工作，不能误取消正在播放或已经启动的主动缓存任务。

## 7. 播放与主动缓存架构

播放、预取和主动缓存共用一条音频获取能力：

```text
Text snapshot
  → AudioCacheKey
  → cache lookup
  → rule-level admission / rate limit
  → TTS execution
  → validated audio
  → atomic cache write
```

优先级固定为：

```text
Current playback > Playback prefetch > Active cache
```

同一规则的所有请求必须经过同一异步并发/速率限制器，不能为主动缓存另建绕过限制的客户端或 semaphore。

主动缓存协调器负责批次快照、章节队列、进度、取消和状态发布；播放器只提交缓存请求，不拥有后台批次。

## 8. 桌面平台边界

以下能力必须通过可测试端口进入平台实现：

- 文件/目录选择。
- 用户显式选择文件后的元数据与文本读写；合同位于 Application，技术适配位于 Infrastructure。
- 剪贴板和打开目录。
- Windows 系统媒体传输控制。
- 耳机/键盘媒体按键。
- 系统托盘。
- 迷你播放器窗口创建、位置和置顶。
- UI Dispatcher 调度。

Application 不引用 Windows/WPF 类型；App adapter 负责平台事件与 Application 命令之间的转换。

## 9. 资源所有权

- HTTP transport 明确拥有 `HttpResponseMessage` 与 response stream，直到结果所有权转交或释放。
- 临时 TTS 文件、缓存 staging 文件和 MP3 导出临时文件均有唯一 owner，并在失败/取消时清理。
- NAudio 播放器、reader/stream 和输出设备必须在会话替换或进程关闭时确定性释放。
- fire-and-forget 任务必须登记到进程/操作所有者，并观察异常。
- `async void` 仅限 WPF 事件入口，且立即转交可等待流程。

## 10. 数据兼容边界

- SQLite 当前 schema 为 6；已发布 migration 只能追加。
- 现有内部正文、阅读进度、缓存键和合法历史记录不得因重构失效。
- 数据格式变化必须有独立迁移和升级测试，不用兼容 wrapper 永久掩盖旧模型。
- 所有持久化路径必须通过应用数据根目录约束的 resolver。

## 11. 架构自动约束

测试持续验证：

- Domain 无产品层反向引用。
- Application 不暴露具体基础设施/UI 技术类型。
- Infrastructure 不引用 App。
- App 非 Bootstrap 代码不直接依赖 Infrastructure。
- ViewModel 公共合同不暴露 Page/Window/Dispatcher/WPF 视觉类型。
- 文件名、主公共类型和命名空间保持一致。
- 非组合根代码不新增 `IServiceProvider` 依赖。

架构收口任务见 `TASK_BACKLOG.md`，本文件不记录迁移过程。
