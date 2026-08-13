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
| Cache | 稳定段身份、文本/合成配置指纹、当前章节朗读清单、完整度查询、保护、LRU、主动缓存、导出编排 | 朗读清单与缓存 SQLite 适配、缓存文件、MP3 编码/合并 |
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
| 章节 MP3 导出批次 | Application chapter export coordinator | Process/background job |
| 章节朗读清单构建与完整度补建 | 播放、预取、主动缓存、导出用例及缓存工作区的进程级后台 owner；同章 in-flight 任务负责并发合并 | Process/background job or operation |
| 托盘/迷你窗口 | Desktop shell coordinator | Process |
| 当前设置快照 | Settings service | Process |
| 短操作 | 发起用例/控制器 | Operation |

页面离开只能取消页面拥有的工作，不能误取消正在播放、已经启动的主动缓存任务或已经提交给章节导出协调器的 MP3 导出批次。缓存管理页只拥有导出前的确认与目录选择。

## 7. 播放与主动缓存架构

播放、预取和主动缓存共用一条音频获取能力：

```text
Chapter text + TextProfileFingerprint
  → current ChapterSpeechPlan
  → stable segment identity + SpeechTextHash
  → SynthesisProfileFingerprint
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

章节 MP3 导出采用独立的进程级 `IChapterExportCoordinator`：缓存管理页完成可导出性预检、跳过确认和目录选择后提交不可变批次参数，协调器拥有批次 CTS、执行 Task、章节级进度和终态快照。Shell 只投影协调器状态并提供取消/打开目录/关闭完成状态，不拥有导出任务。当前阶段不抽象通用后台任务中心。

章节朗读清单由播放、预取、主动缓存和导出等真正消费章节内容的用例按需建立或更新；完整度读取发现过期计划时由进程级缓存工作区 owner 异步补建，缓存管理页对有缓存但缺失计划的章节同样补建，同章请求合并为一个后台任务。普通目录遇到缺失计划不建立清单；所有页面首轮查询只聚合 SQLite 计划和 `Ready` 索引，不重新处理正文、不逐文件解码。正式缓存写入前必须先提交对应计划。删除某章最后一条缓存索引时同步回收其朗读清单。

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

## 9. UI 主题与样式所有权

Wpf.Ui 是标准 WPF/Wpf.Ui 控件的基础视觉提供者，NovelSpeaker 不复制其默认模板，也不通过主题切换代码重新注入标准控件样式。

资源分层固定为：

1. Wpf.Ui provider dictionaries：默认控件模板、Fluent 交互状态和框架主题资源。
2. NovelSpeaker palette/tokens：语义颜色、稳定间距标尺、圆角、图标尺寸、最小控件尺寸和动效时长。
3. Provider style bridge：将确实需要扩展的 Wpf.Ui 基础样式映射为显式、稳定的具名资源。
4. NovelSpeaker explicit variants：按控件族集中维护的 `App.*` 具名样式，只覆盖必要属性，不以应用级隐式样式接管标准控件。
5. NovelSpeaker shared controls：页面标题、设置行、表单字段和状态视图等跨 Feature 的应用自有控件；控件类位于 `Shared/Presentation/Controls`，默认模板位于 `Shared/Theming/Resources/ControlThemes`。
6. Feature components：书籍卡片、规则列表项、播放视图和缓存章节项等领域视图，由对应 Feature 拥有。
7. Page layout：列宽、页面边距、工作台分栏和页面专用几何由 Shell、页面或组件中的唯一 owner 管理。

约束：

- `Application.Resources` 和全局合并字典不得为标准 WPF/Wpf.Ui 控件定义 NovelSpeaker 隐式样式。
- NovelSpeaker 自有 CustomControl 可以使用默认样式键；局部组件内部可使用受控隐式样式，但作用域不能逃逸。
- Style Gallery fixture 只存在于开发工具，生产控件不得硬编码演示文本、命令或状态。
- Style Gallery 按稳定资源族注册 scene，用于集中展示正式资源和自有控件；scene 身份不依赖 backlog 任务编号。
- 正式页面截图由视觉测试宿主实例化真实 View，按稳定 page/window 身份输出；不得用 Gallery 页面仿制品替代。
- 标准控件完整 `ControlTemplate` 由 Wpf.Ui 所有。确需替换时必须使用局部具名样式或应用自有组件，并有专项 WPF 契约测试。
- 主题切换只更新 Wpf.Ui 主题和 NovelSpeaker palette；样式字典、模板字典和类型资源键保持加载稳定。
- ViewModel 只投影语义状态，不返回 Brush、Style、Thickness、CornerRadius 或其它视觉类型。
- 开发用 Style Gallery 位于独立工具/测试边界，不进入正式导航，也不进入发布包。

## 10. 资源所有权

- HTTP transport 明确拥有 `HttpResponseMessage` 与 response stream，直到结果所有权转交或释放。
- 临时 TTS 文件、缓存 staging 文件和 MP3 导出临时文件均有唯一 owner，并在失败/取消时清理。
- NAudio 播放器、reader/stream 和输出设备必须在会话替换或进程关闭时确定性释放。
- fire-and-forget 任务必须登记到进程/操作所有者，并观察异常。
- `async void` 仅限 WPF 事件入口，且立即转交可等待流程。

## 11. 数据兼容边界

- SQLite 已发布 schema 为 7；已发布 migration 只能追加。
- 内部正文、书籍元数据、规则和阅读进度不得因缓存重构失效。
- 音频缓存是可丢弃数据；缓存键格式重构可以通过追加 migration 明确重置旧索引和应用内部缓存文件，不建立长期兼容读取器。
- 数据格式变化必须有独立迁移和升级测试，不用兼容 wrapper 永久掩盖旧模型。
- 所有持久化路径必须通过应用数据根目录约束的 resolver。

## 12. 架构自动约束

测试持续验证：

- Domain 无产品层反向引用。
- Application 不暴露具体基础设施/UI 技术类型。
- Infrastructure 不引用 App。
- App 非 Bootstrap 代码不直接依赖 Infrastructure。
- ViewModel 公共合同不暴露 Page/Window/Dispatcher/WPF 视觉类型。
- 应用级资源中不存在接管标准 WPF/Wpf.Ui 控件的 NovelSpeaker 隐式样式。
- 主题切换不通过代码重新写入 Style 或 ControlTemplate 类型资源。
- 页面专用宽度、边距和分栏几何不进入全局 Design Token。
- 文件名、主公共类型和命名空间保持一致。
- 非组合根代码不新增 `IServiceProvider` 依赖。

当前实施任务见 `TASK_BACKLOG.md`，本文件只记录稳定架构边界，不记录执行过程。
