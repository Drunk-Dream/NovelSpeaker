# 系统架构

## 1. 顶层结构

NovelSpeaker 保持四层项目结构：

```text
NovelSpeaker.Domain
        ↑
NovelSpeaker.Application
        ↑
NovelSpeaker.Infrastructure
        ↑
NovelSpeaker.App
```

职责：

- **Domain**：纯业务值、规则和不依赖技术实现的模型。
- **Application**：业务用例、稳定模块边界、端口、read model、状态 owner 与编排。
- **Infrastructure**：SQLite、文件、HTTP、WebSocket、Jint、NAudio、诊断持久化等技术实现。
- **App**：WPF Shell、Page/ViewModel、桌面平台桥接、主题与组合根。

不再按 Books/Playback/Cache 等继续拆程序集。模块压力通过层内稳定业务边界和职责收敛处理。

## 2. Application 模块

Application 按稳定业务能力组织：

```text
Application/
├─ Books/
├─ Speech/
├─ Cache/
├─ Playback/
├─ Settings/
└─ Desktop/
```

允许的主要依赖方向：

```text
Cache    → Books / Speech / Settings
Playback → Books / Speech / Cache / Settings
Speech   → Settings
Desktop  → Playback
```

约束：

- Application 模块不得形成循环依赖。
- Cache 是一级模块，不是 Playback 子系统。
- Books、Speech、Settings 不依赖 Cache-specific invalidation、Coverage 或物理存储 API。
- Cache 不依赖 Playback mutable session state。
- Playback 可以消费 Cache 与 Speech 的稳定角色接口。
- Desktop 只消费稳定角色接口，不拥有 Playback/Cache mutable truth。

跨模块变化使用窄的 typed snapshot/change source/role port；源模块只表达“自身已经提交了什么变化”，派生消费者在自己的边界解释影响。mutation 调用者不负责逐个刷新其它模块，UI 也不编排 Playback/Cache/Books 之间的业务后果。变化只在持久提交成功后发布，observer 失败不得回滚已经完成的 mutation。禁止为此引入通用 EventBus/Messenger。

Books 等稳定模块可以分别暴露 MetadataChanged、CatalogChanged、ActiveSourceChanged、Removed 等领域窄事件或等价 typed change；不得把所有模块变化塞入通用消息 envelope。调用者只处理本次操作自身的用户反馈、导航和页面局部结果，长期消费者由对应 process/session owner 自行订阅。

### Book / Source 边界

Books 模块统一使用：

```text
Book
└─ Sources[]
   └─ Source
      ├─ Metadata Snapshot
      ├─ Catalog
      └─ Content
```

长期原则：

- Book 是跨 Source 的稳定实体，BookId 是永久内部身份。
- Source 属于 Book，Catalog 属于 Source，Content 属于 Source。
- Book 不保存第二套 Catalog 真值。
- ReadingProgress 属于 Book，而不是 Source。
- `ActiveSourceId` 可以为空。
- Local Source 是一个 typed Source 实现，不把 StoredFilePath/Encoding/SourceHash 等 Local-only 数据继续放进通用 Book Domain model。
- Source-specific persistence/HTTP/file details 只存在于 Infrastructure；Application 通过 Source/catalog/content port 工作。
- Catalog entry 可以保留技术性 ChapterId 供 Speech Plan/音频缓存引用，但不建立跨 Source 的 Chapter Identity 推断。
- 当前只实现 Local Source。Online Source 的 Legado/站点规则、目录抓取、正文请求、登录和变量体系在真正需要时再建立 typed boundary，不提前做万能 Source plugin framework。
- 精确模型见 `docs/specs/BOOK_DATA_MODEL.md`。

### Speech Provider 边界

Speech 以 Provider Type + Provider Instance 建模。

```text
Playback / Prefetch / Active Cache / Test
                    ↓
             Provider Runtime
             ↙             ↘
      HTTP Provider      Edge Provider
             ↓             ↓
       HTTP transport   Edge transport
```

长期原则：

- 上层消费 Provider Runtime，不直接依赖 `HttpTtsRule`、HTTP transport 或 Edge 协议。
- HTTP、Microsoft Edge、未来 Local Provider 在管理层级上等价，但各自拥有 typed config 和独立 editor/runtime。
- 不建立万能 `ProviderConfig` 字典、通用脚本插件平台或提前设计的 capability framework。
- Provider Type 分派保持简单，直到更多真实类型产生扩展压力。
- Provider Runtime 返回稳定音频结果/错误语义；具体网络协议只存在于 Infrastructure。
- 每个 Provider Type 自己定义会影响音频结果的版本化 synthesis fingerprint。

## 3. App Feature

目标 Feature：

```text
Features/
├─ Books/
│  ├─ Library/
│  ├─ Details/
│  └─ Shared/
├─ Playback/
├─ Cache/
├─ Speech/
│  ├─ Providers/
│  └─ Shared/
├─ Rules/
│  ├─ Chapter/
│  ├─ Regex/
│  └─ Shared/
├─ Settings/
└─ Diagnostics/

Shell/
Desktop/
Shared/
```

规则：

- Feature 不形成双向依赖。
- Feature-local controller/projector 默认留在 Feature 内。
- `Rules/Shared` 只共享真正属于规则编辑的生命周期，不抽象不同规则业务模型。
- Speech Provider 编辑器只共享 Draft/Dirty/Save/Cancel/Test 等生命周期语义，不共享一套万能配置字段。
- 编辑工作台通过组合小型行为 owner 复用 draft、import/export、reorder、management selection 等确实相同的流程；不建立 `GenericWorkbenchViewModel<T>`、大型继承层级或统一业务 DTO。
- 全局 `Shared` 只保存真实跨多个业务域复用的 presentation/lifecycle/platform primitive。
- 页面级 Management Mode 可以共享 stable-key selection/lifecycle primitive，但 Shared 不拥有 Book/Provider/Rule/Chapter 的 batch business action。
- `Shared` 不依赖任何 Feature。

## 4. 状态所有权

核心原则：**同一 mutable state 只有一个 owner**。

| 状态 | Owner | 生命周期 |
|---|---|---|
| Book / Source / Catalog 持久事实 | Books persistence/use case | Persistent |
| 当前 Book ActiveSourceId | Books use case + persistence | Persistent |
| 当前 Playback session context、logical target / target revision、播放意图与准备状态 | Playback runtime/session owner | Playback session / process |
| 当前 Speech Provider Id | Settings process service | Persistent |
| Provider 列表、排序与类型配置 | Speech Provider persistence/use case | Persistent |
| ReadingProgress checkpoint | Application progress use case + persistence | Persistent |
| 当前设置 snapshot | Settings process service | Process |
| 当前路由 | Shell navigation owner | Process |
| Process fatal 状态与最终退出原因 | Process lifetime owner | Process |
| 物理音频缓存/index/file | Cache store | Persistent / rebuildable |
| Cache Coverage/组合 read model/内部失效解释 | Cache application owner | Query / process |
| 主动缓存批次 | Active Cache coordinator | Background job |
| 章节导出批次 | Export coordinator | Background job |
| Speech Plan 补建 | Repair coordinator | Background job |
| 页面 filter/management selection/draft | 当前 Page/ViewModel | Page activation |
| 诊断会话 | Diagnostics session owner | Explicit diagnostic session |

ViewModel 不复制 process/session/background owner 的 mutable truth。跨页面展示使用 immutable snapshot/read model。

Playback session 表示当前 Book / Source context 的生命周期，不把每次切段、切章等同于完整 session replacement。同一 session 内，用户选择首先提交新的 logical target 并增加 target revision；`PlaybackSnapshot` 必须立即反映这个 target，而不是等待网络 TTS、缓存读取或音频解码完成。

音频 preparation 与 low-level transport 是 target 的异步 effect。Playback runtime 仍是唯一高层 mutable owner，负责播放意图、preparation/failure 状态和 accepted audio facts；低层音频播放器只拥有设备资源和 low-level snapshot。低层结果必须至少携带可校验的 session identity + target/preparation identity，并在需要时结合 local audio generation 拒绝迟到结果。

音频准备失败、取消或迟到不得回滚用户已经提交的 logical target。只有 Book/ActiveSource context 真正失效、切换或关闭时才替换/结束 Playback session。面向 UI 的 `PlaybackSnapshot` 始终是 authoritative runtime 的纯投影。

Cache 的 physical facts、Speech Plan、Coverage、repair 和 configuration invalidation 可以由多个内聚组件承担，但一致性解释留在 Cache 模块内。App 只消费场景化组合 read model 与“哪些书/章节展示已变化”的窄通知，不理解 PhysicalSummary/CatalogStructure/Coverage 等内部失效原因。

Process lifetime owner 只维护当前 Process 的稳定退出原因，不承担日志持久化、Diagnostic Session 持久化或通用异常路由。平台异常入口负责把原始 fatal failure 交给该边界；各诊断 sink 消费同一稳定语义，不分别重新判断“这是不是崩溃”。

## 5. 生命周期与接口

普通 Page/ViewModel 默认 transient。

只有存在真实长期 owner 才使用 process/session/background service，例如：

- Playback session owner；
- Settings process owner；
- Process lifetime owner；
- Speech Provider persistence/runtime resolver；
- Cache invalidation/repair/active-cache/export coordinator；
- Shell navigation；
- desktop lifecycle；
- Diagnostics session owner。

只在存在真实边界时创建 interface，典型理由：

1. Infrastructure 技术实现边界；
2. process/session/background owner 的稳定角色视图；
3. 跨模块/跨 Feature 合同；
4. 外部副作用与测试隔离。

Feature-local mapper/projector/controller 默认使用 concrete internal type，不因为“方便 Mock”机械创建 interface。

## 6. Query 与命令

采用轻量 read/write 职责分离，不引入 MediatR、CommandBus 或 QueryBus。

原则：

- query 返回场景化 immutable read model；
- command/use case 使用明确服务；
- 不用大型详情 DTO 同时承载 header、catalog、status、statistics；
- App 不组合多个低层 persistence port 构造页面级 read model；
- 大 catalog 与动态 decoration 分离。
- Book query 默认通过 ActiveSource 投影当前 Catalog/Content，不把 Local Source storage path 暴露到 App。
- ActiveSource=None 是合法 read model 状态，不通过异常或伪造 Catalog 表达。

## 7. 大列表

大型 Library/Chapter/Cache 列表遵循：

```text
Immutable Catalog
+
Sparse Mutable Decoration
```

- 目标至少支持 10,000 条连续 catalog。
- current item 使用 O(1) 或有界 lookup。
- 动态 cache/status 只更新 current/viewport/明确受影响范围。
- WPF virtualization 负责可视 container/layout，不替代 data/projection 规模控制。
- 不在 Feature 中自行实现 WPF item container generator、scroll extent/offset 或 recycling 状态机。
- selection 使用 stable item key，不依赖虚拟化 container 保存业务选择事实。

## 8. Observability 架构

业务模块只描述一次稳定操作语义：

```text
Feature / Application
        ↓
thin Observability API
        ├─ Performance Telemetry consumer
        └─ Diagnostic Session consumer
```

生产日志是独立故障证据基础设施，与上述消费者通过稳定 operation/session/process/activity correlation 关联，但不共用 writer/store。

Process-level fatal failure 采用同样的“事实只分类一次”原则：App 平台边界负责发现 WPF/.NET 未处理异常，一个薄的 process failure/lifetime boundary 形成稳定 failure/exit 语义；Production Logging 保存详细异常，Diagnostic Session 保存低基数故障事实和 Process 退出原因。Diagnostic Session Store 只持久化，不拥有 fatal/normal 分类规则。

原则：

- 第一版不引入完整 OpenTelemetry。
- 内部 API 保持薄且可替换，未来如有真实需求可增加 adapter。
- Telemetry 与 Diagnostic Session 可以共享基础 operation instrumentation，但开关、数据粒度、生命周期和持久化完全独立。
- Logging、Telemetry、Diagnostics 任一失败不得导致业务失败。
- 不为 fatal failure 引入通用 EventBus、第二套日志、Crash Database 或复杂状态机。

## 9. 成熟能力优先

默认决策顺序：

```text
.NET / Windows / WPF / Wpf.Ui
→ 项目已有职责清晰的能力
→ 成熟且维护良好的依赖
→ 只有存在已证实缺口时自定义基础设施
```

尤其避免重复实现：

- WPF virtualization/container lifecycle；
- scroll/viewport/extent 状态机；
- 通用消息总线；
- 通用后台任务调度器；
- DI/service locator；
- 自定义并发/重试框架；
- 已有稳定库可以完成的序列化或网络基础设施。

领域特有的小型纯计算和少量 glue code 不需要为了“复用”引入大型依赖。

## 10. 禁止项

- 通用 EventBus/Messenger。
- Service Locator。
- 万能 Manager/Helper/Utils。
- 万能 Provider Config 或提前设计的插件框架。
- 万能 Book Source Config / 提前实现的 Online Source plugin framework。
- 跨页面全局 SelectionService。
- 通过 Shared 隐藏 Feature/Application 循环。
- 为内部重构长期保留 Old/New/V2/Compat/forwarding wrapper。
- 通过 singleton Page/ViewModel 或 Navigation cache 保存长期业务状态。
- 为减小文件行数机械拆类并制造第二套 state owner。
