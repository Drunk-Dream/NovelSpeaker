# 技术栈与目标架构

## 1. 文档定位

本文定义 NovelSpeaker 长期维护所遵循的目标代码架构。它描述的是重组完成后的稳定形态，不记录迁移批次、临时适配器或任务状态；迁移过程和验收项统一维护在 `TASK_BACKLOG.md`。

架构优化必须保持现有产品行为、SQLite 数据、内部 `content.txt`、音频缓存键和发布形式兼容。不得借重构暗中扩大产品范围，也不得以一次性“大爆炸重写”替代可验证的纵向迁移。

## 2. 技术栈

| 领域 | 选择 |
|---|---|
| 语言与运行时 | C#、.NET 10 |
| 桌面 UI | WPF、Wpf.Ui 4.x |
| MVVM | CommunityToolkit.Mvvm |
| 数据库 | Microsoft.Data.Sqlite.Core、SQLitePCLRaw.bundle_winsqlite3 |
| 规则表达式 | Jint |
| 音频 | NAudio |
| 组合与日志 | Microsoft.Extensions.DependencyInjection、Microsoft.Extensions.Logging |
| JSON | System.Text.Json |
| 测试 | xUnit、Microsoft.NET.Test.Sdk |

不引入 Prism、MediatR、通用插件框架、完整 DDD/CQRS 框架、事件溯源或自建后端。重组目标是让现有四层结构真正承担清晰职责，而不是增加框架和项目数量来制造表面分层。

## 3. 架构原则

1. **按功能内聚，按依赖分层。** Books、Speech、Playback、Settings 等功能在各层拥有对应切片；不再把所有接口、DTO、页面或服务平铺到单一大目录。
2. **Application 拥有用例。** 导入、规则管理、播放会话、缓存管理和设置更新等流程编排属于 Application；Infrastructure 只实现数据库、文件、HTTP、脚本、音频和日志适配。
3. **依赖指向业务内核。** Domain 不依赖任何外部技术；Application 不暴露 SQLite、WPF、Wpf.Ui、Jint、NAudio 或 `HttpClient` 类型。
4. **语义端口优先。** 应用层依赖“读取书籍详情”“保存导入结果”“生成音频”之类业务语义接口，不依赖连接、命令、事务或路径拼接细节。
5. **单一状态所有者。** 播放状态、页面激活状态、编辑副本和后台任务各有唯一所有者；不得在 ViewModel、协调器和适配器中复制同一份可变状态。
6. **取消和迟到结果隔离是架构能力。** 所有可等待流程传递 `CancellationToken`，并在会话或页面切换时通过版本号/会话 ID 拒绝旧结果。
7. **先锁定行为，再移动和拆分。** 任何大类拆分、命名空间移动或项目拆分都先增加特征测试；每个批次应可独立编译、测试和回滚。

## 4. 解决方案与依赖方向

目标解决方案保持四个产品项目：

```text
NovelSpeaker.slnx
├─ src/
│  ├─ NovelSpeaker.Domain
│  ├─ NovelSpeaker.Application
│  ├─ NovelSpeaker.Infrastructure
│  └─ NovelSpeaker.App
└─ tests/
   ├─ NovelSpeaker.Domain.UnitTests
   ├─ NovelSpeaker.Application.UnitTests
   ├─ NovelSpeaker.Infrastructure.IntegrationTests
   ├─ NovelSpeaker.App.PresentationTests
   └─ NovelSpeaker.App.WpfTests
```

产品项目依赖固定为：

```text
NovelSpeaker.App --------------------> NovelSpeaker.Application
       │                                         │
       │ 仅 Bootstrap/Composition Root           v
       └-----------------------------> NovelSpeaker.Infrastructure
                                                 │
NovelSpeaker.Infrastructure --------------------┤
                                                 v
                                      NovelSpeaker.Domain

NovelSpeaker.Application ----------------------> NovelSpeaker.Domain
NovelSpeaker.Domain ----------------------------> 无项目依赖
```

约束：

- App 项目为了组合依赖可以引用 Infrastructure，但只有 `Bootstrap`/组合根可以使用 Infrastructure 命名空间；页面、ViewModel 和 UI 服务只依赖 Application 或 App 内部抽象。
- Application 项目只引用 Domain 和 BCL。完成重组后必须移除 `Microsoft.Data.Sqlite.Core` 包引用。
- Infrastructure 实现 Application 端口，可以引用 Domain。
- Domain 不保存页面投影、HTTP 执行结果、SQLite 映射对象或导入预览 DTO。
- 测试项目按被测层单向引用，不通过引用 App 来测试 Domain/Application。

## 5. 功能切片

四层中的稳定功能边界如下：

| 功能切片 | 主要职责 | 不负责 |
|---|---|---|
| Books.Import | TXT 分析、规范化、去重、章节识别、内部文件与元数据提交 | UI 文件选择、SQLite 命令 |
| Books.Library | 书库摘要、详情、元信息更新、删除 | 播放状态机、卡片视觉 |
| Books.ChapterRules | 章节规则编辑、默认规则、排序与启用 | 正文替换 |
| Books.TextProcessing | 动态分段、正则替换、原始偏移映射 | 章节重新识别、HTTP 请求 |
| Speech.Rules | 规则导入、编辑、验证、当前规则 | 播放调度 |
| Speech.Compilation | 模板解析、受限求值、请求模型编译和脱敏预览 | 直接播放或缓存 |
| Speech.Execution | 限流、HTTP 执行、响应与音频验证 | 页面错误文案 |
| Playback.Session | 会话状态机、跳转、旧结果隔离、快照 | SQL、HTTP 模板语法、WPF |
| Playback.Audio | 当前段缓存命中或在线生成、本地音频协调 | 章节解析和规则编辑 |
| Playback.Cache | 缓存键、索引、原子写入、保护、LRU 和管理查询 | 播放页面状态 |
| Playback.Progress | 进度保存、恢复和字符偏移定位 | UI 滚动 |
| Settings | 设置读取、校验、更新和变更语义 | JSON 文件格式、具体页面控件 |

功能之间通过 Application 中的小型明确接口协作。不得建立一个跨越全部功能的 `CommonService`、`Manager`、事件总线或万能上下文对象。

## 6. 目标目录结构

### 6.1 Domain

```text
src/NovelSpeaker.Domain/
├─ Books/
│  ├─ Book.cs
│  ├─ Chapter.cs
│  ├─ ChapterRule.cs
│  ├─ RegexReplacementRule.cs
│  └─ TextSegmentationOptions.cs
├─ Speech/
│  ├─ HttpTtsRule.cs
│  └─ TtsErrorKind.cs
├─ Playback/
│  └─ 纯值对象或状态枚举（仅在确有领域不变量时存在）
├─ Settings/
│  └─ AppSettings.cs
└─ Common/
   └─ AppInfo.cs
```

Domain 只保留具有稳定业务含义或不变量的实体和值对象。以下内容属于 Application 合同而不是 Domain：

- 导入预览、列表摘要、编辑器模型和 UI 状态。
- `ParsedTtsRequest`、请求体、响应文件、重试信息和 HTTP 状态。
- `PlaybackSnapshot`、缓存查询结果和用例执行结果。
- SQLite 时间戳字符串、行映射或事务对象。

领域对象不应为了持久化方便而承担模板编译、JSON 解析或数据库映射。规范化和映射由对应 Application/Infrastructure 切片负责。

### 6.2 Application

```text
src/NovelSpeaker.Application/
├─ Common/
│  ├─ Time/
│  └─ Results/
├─ Books/
│  ├─ Import/
│  ├─ Library/
│  ├─ ChapterRules/
│  └─ TextProcessing/
├─ Speech/
│  ├─ Rules/
│  ├─ Compilation/
│  └─ Execution/
├─ Playback/
│  ├─ Session/
│  ├─ Audio/
│  ├─ Cache/
│  └─ Progress/
└─ Settings/
```

每个切片就近放置：

- 对外用例接口和实现。
- 命令、查询、结果和只读投影。
- 该用例需要的基础设施端口。
- 纯业务编排和输入校验。

不要机械创建 `Interfaces`、`Models`、`Services` 三层空目录。一个类型只有在存在第二个稳定消费者时才移入上级 `Common`；否则留在所属功能附近。

### 6.3 Infrastructure

```text
src/NovelSpeaker.Infrastructure/
├─ Composition/
│  └─ InfrastructureRegistration.cs
├─ Persistence/
│  ├─ Sqlite/
│  │  ├─ Connection/
│  │  ├─ Migrations/
│  │  └─ Mapping/
│  ├─ Books/
│  ├─ SpeechRules/
│  ├─ Playback/
│  └─ RegexReplacement/
├─ FileSystem/
│  ├─ AppData/
│  ├─ Books/
│  └─ Cache/
├─ Speech/
│  ├─ Http/
│  ├─ Legado/
│  └─ Scripting/
├─ Audio/
│  └─ NAudio/
├─ Settings/
└─ Diagnostics/
```

Infrastructure 允许包含：

- SQLite 连接工厂、SQL、迁移、事务和行映射。
- 文件路径、临时文件、原子替换和目录维护。
- `HttpClient`、HTTP Header/Body 转换、Cookie 策略和响应流。
- Jint 引擎配置与安全沙箱。
- NAudio 设备和解码实现。
- JSON 设置存储和滚动日志。

Infrastructure 不应包含：

- `PlaybackCoordinator` 等业务状态机。
- 规则编辑工作区、导入用例或设置校验用例。
- 面向页面的列表项、编辑器副本和 Snackbar 文案。
- 仅因“实现会访问数据库”而被放入本层的应用服务。

SQLite 连接工厂是 Infrastructure 内部细节，不作为 Application 公共端口。需要跨多表或数据库与文件的一致性时，由 Infrastructure 提供语义化、可测试的原子操作端口，而不是把 `SqliteConnection` 或 `SqliteTransaction` 暴露给 Application。

### 6.4 App

App 的详细约束见 `06_UI_AND_USER_FLOWS.md`，目标结构概览为：

```text
src/NovelSpeaker.App/
├─ Bootstrap/
├─ Shell/
│  ├─ Navigation/
│  ├─ Activation/
│  └─ Input/
├─ Features/
│  ├─ Library/
│  ├─ BookDetails/
│  ├─ Playback/
│  ├─ TtsRules/
│  ├─ ChapterRules/
│  ├─ RegexReplacementRules/
│  ├─ Cache/
│  ├─ PlaybackSettings/
│  ├─ ImportTextSettings/
│  ├─ Appearance/
│  └─ Diagnostics/
└─ Shared/
   ├─ Feedback/
   ├─ Theming/
   ├─ Dialogs/
   ├─ Behaviors/
   └─ Presentation/
```

Page 是导航、激活、取消和未保存保护边界。ViewModel 只暴露语义状态，不暴露 `FontWeight`、`SymbolRegular`、Dispatcher、Page 或 Window。Wpf.Ui 只在 Shell、View 和平台适配器中出现。

## 7. Application 用例与端口

### 7.1 命名

- `*Service`：一个清晰功能用例集合，例如书籍导入或规则库。
- `*Repository`：领域实体或聚合的持久化集合语义。
- `*Store`：设置、进度、文件或其它键值/状态存储语义。
- `*Gateway`/`*Client`：外部系统边界，例如 HTTP TTS。
- `*Coordinator`：确实拥有状态机并协调多个异步协作者的长期对象。
- `*Workspace`：仅用于具有编辑副本、启用/排序即时保存和未保存保护语义的管理用例。

避免新增含糊的 `Manager`、`Helper`、`Utils`、`Processor`。接口不应只为每个实现机械复制；纯内部、无替换需求的 Application 协作者可以使用 `internal sealed` 类。

### 7.2 端口粒度

端口表达完整语义，而不是底层操作。例如：

```csharp
public interface IBookImportStore
{
    Task<bool> ExistsBySourceHashAsync(string sourceHash, CancellationToken cancellationToken);

    Task SaveAsync(
        Book book,
        IReadOnlyList<Chapter> chapters,
        CancellationToken cancellationToken);
}
```

不允许：

```csharp
public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
```

后一接口把具体数据库类型泄露到 Application，导致 Application 必须引用 SQLite 包，并允许任意用例绕过仓储边界。

### 7.3 结果与异常

- 预期业务分支使用明确结果类型或状态枚举，例如重复书籍、需要选择编码、无可用规则。
- 编程错误和不可恢复基础设施失败使用异常；Application 在边界处投影为安全错误类别。
- Infrastructure 异常不得直接成为 UI 文案。
- 用户可见结果不得包含小说正文、完整 URL、Header、Body、Token 或服务端完整错误正文。

## 8. 播放架构

`IPlaybackCoordinator` 是 Application 的稳定门面。它串行接收播放、暂停、跳转、规则/语速变化、正文规则刷新和书籍删除通知，并发布不可变 `PlaybackSnapshot`。

协调器内部按职责拆分，但不把共享可变状态散落到多个服务：

| 协作者 | 职责 |
|---|---|
| PlaybackSessionState | 当前书籍、规则、位置、会话 ID 与取消源的唯一所有者 |
| PlaybackPositionResolver | 纯计算相邻段落/章节、恢复位置和原始偏移映射 |
| PlaybackSegmentRunner | 为当前段获取音频、调用本地播放并分类结果 |
| PlaybackPrefetchController | 维护有限预取窗口、去重、优先级和会话取消 |
| PlaybackProgressService | 保存/恢复进度，统一保存时机 |
| PlaybackSnapshotProjector | 从状态生成不可变 UI 快照 |

拆分顺序应从纯计算和 I/O 边界开始；`PlaybackCoordinator` 始终保留唯一命令串行化入口。不得为了缩短文件而建立一组互相回调、共同修改状态的微服务。

本地音频边界保持独立：Application 的本地音频协调器只处理单文件加载、播放、暂停、停止和定位；Infrastructure 的 NAudio 适配器只访问设备与解码器。

## 9. 数据与文件一致性

- SQLite schema 与迁移由 Infrastructure 独占。
- 导入、删除和缓存写入涉及数据库与文件时，使用“暂存文件 → 数据事务 → 最终清理/补偿”的显式协议。
- Application 只看到语义化提交结果，不直接协调 SQL 事务。
- 迁移文件、兼容读取和数据修复属于不可随意清理的基础设施资产。
- 重构不得改变现有表、数据目录、章节偏移或缓存键；确需改变时必须单独设计迁移并增加升级测试。

具体数据模型见 `05_DATA_AND_PERSISTENCE.md`。

## 10. 导航、激活与桌面生命周期

- App 使用自有强类型路由和参数；Wpf.Ui Page 类型映射集中在 Shell 导航适配器。
- ViewModel 不直接引用具体 Page 类型。
- 每个 Page 激活时创建 activation scope 和取消源，离开时取消页面工作并注销事件/导航守卫。
- 未保存保护是统一的页面离开协议，必须覆盖按钮返回、一级导航、快捷键、正在播放入口和窗口关闭。
- 应用级播放会话可以跨页面存在；页面 activation 状态不得通过 Singleton ViewModel 偶然保存。
- 启动、数据库初始化、主题应用、窗口展示和后台维护由 Bootstrap 中的启动协调器顺序执行。

## 11. 依赖注入与生命周期

生命周期按状态所有权决定：

| 生命周期 | 适用对象 |
|---|---|
| Singleton | 无状态解析器、线程安全仓储/适配器、应用级播放协调器、主题运行时、全局设置状态 |
| Page/activation scope | 页面 ViewModel、编辑会话、页面取消源、页面级投影 |
| Transient | 轻量无状态工厂产物、短生命周期命令对象 |
| Session-owned | PlaybackSession、预取窗口、规则试听和导入操作 |

根注册方法只组合功能注册模块，例如 Books、Speech、Playback、Settings、Shell。每个功能模块负责本切片的实现映射；禁止恢复一个包含全部类型的超长注册清单。

构建服务容器时必须在测试中验证关键服务可解析、生命周期符合预期，并尽可能启用 scope/build 验证。Singleton 不得捕获页面作用域服务。

## 12. 并发、取消与时间

- 所有异步 I/O 和可等待业务流程接收并传递 `CancellationToken`。
- `CancellationToken.None` 只允许出现在应用退出后的尽力清理、不可取消的最终持久化或明确记录原因的进程级后台任务中。
- 页面事件、导航回调、导入、试听和自动保存必须使用对应 activation/operation Token。
- 旧播放会话依赖 `SessionId + CancellationToken` 双重隔离。
- 防抖、限流、重试和超时使用可注入 `TimeProvider` 或可控调度抽象，测试不得依赖任意固定延迟来等待状态变化。
- `async void` 仅限 WPF/播放器事件入口；入口必须捕获异常并尽快转交串行协调器。

## 13. 安全和隐私边界

- Jint 脚本是不可信输入，禁止 CLR、文件、进程、反射、任意网络、环境变量和宿主对象访问。
- 规则编译和执行限制必须集中配置，并覆盖超时、语句数、递归深度和输出长度。
- 日志、异常投影、请求预览、Snackbar 和诊断摘要统一经过脱敏器。
- 安全脱敏应防御 Cookie/LoginInfo 等字段，即使当前版本尚未执行这些兼容能力。
- 当前没有 SecretStore；规则结构化字段未静态加密的限制保持可见。

## 14. 架构自动约束

测试必须阻止以下回归：

- Domain 出现对 Application、Infrastructure、App 或外部技术包的引用。
- Application 引用 SQLite、WPF、Wpf.Ui、Jint、NAudio 或 Infrastructure。
- App 的 Features/Shared 引用 Infrastructure；只有 Bootstrap 可引用。
- Infrastructure 中出现页面/ViewModel 类型或 WPF 引用。
- ViewModel 公共属性暴露 WPF/Wpf.Ui 类型。
- 非 Infrastructure 代码出现 `SqliteConnection`、`SqliteCommand` 或 SQL 文本。
- 测试项目产生反向引用或纯单元测试依赖 WPF STA Host。

这些检查应使用轻量反射、项目文件检查或现有分析器完成；除非收益明确，不为此引入重量级架构框架。

## 15. 重组完成标准

- 四个产品项目符合第 4 节依赖方向，Application 不再引用 SQLite 包。
- 业务用例实现位于 Application；Infrastructure 只保留技术适配器和原子持久化实现。
- Domain 不再容纳 HTTP/页面/导入流程 DTO。
- `PlaybackCoordinator`、`PlayerViewModel`、规则工作台和 Shell 已按单一职责拆分，并保留唯一状态所有者。
- App 采用 feature slice；页面、ViewModel、组件和功能 DI 注册就近组织。
- 所有页面具有统一激活、取消和未保存保护协议。
- 测试按 Domain、Application、Infrastructure、Presentation、WPF 分层，纯测试可并行且不依赖 STA。
- 现有 SQLite 数据、内部书籍文件、阅读进度、音频缓存和 UI/播放行为保持兼容。
- 每个迁移批次都通过 `09_TESTING_AND_QUALITY.md` 规定的质量门禁。
