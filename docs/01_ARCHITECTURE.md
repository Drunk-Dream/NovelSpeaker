# 架构

## 1. 技术栈

- C# / .NET 10
- WPF + Wpf.Ui 4.x
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Microsoft.Data.Sqlite.Core
- Jint
- NAudio
- xUnit

目标平台：Windows 10/11 x64，自包含发布。

## 2. 四层结构

正式产品项目保持：

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
- **Application**：按业务模块组织的用例、端口、DTO/read model、状态 owner 与编排。
- **Infrastructure**：SQLite、文件、HTTP、Jint、NAudio、设置存储和日志适配。
- **App**：WPF Shell、Page/ViewModel、平台桥接、主题与组合根。

不再新增 Books/Playback/Cache 等独立程序集。架构压力通过层内模块、Feature 和职责重构解决，而不是增加 assembly 数量。

## 3. App Feature 结构

目标结构：

```text
Features/
├─ Books/
│  ├─ Library/
│  ├─ Details/
│  └─ Shared/
├─ Playback/
├─ Cache/
├─ Rules/
│  ├─ Tts/
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

- Feature 不得形成双向依赖。
- `Books/Library` 与 `Books/Details` 不直接互相引用。
- Rules 共用的编辑生命周期放 `Rules/Shared`，不提升为全局 Shared。
- 全局 `Shared` 只保存真正跨多个业务域复用的 presentation/lifecycle/platform 基础设施。
- `Shared` 不依赖 Feature。
- Feature-local controller 默认留在 Feature 内。

## 3.1 Application 模块边界

四层结构之内，Application 进一步按稳定业务能力划分模块。目标概念结构为：

```text
Application/
├─ Books/
├─ Speech/
├─ Cache/
│  ├─ Plans/
│  ├─ ActiveCache/
│  └─ Export/
├─ Playback/
├─ Settings/
└─ Desktop/
```

目录可以按实际类型数量做轻量调整，但 namespace、DI registration 和依赖方向必须体现相同边界。

核心规则：

- **Cache 是一级 Application 模块，不是 Playback 的子系统。**
- Books、Speech、Settings 只表达自身状态、查询和变更语义，不依赖 Cache 的 invalidation、Coverage 或物理存储概念。
- Cache 可以消费 Books、Speech、Settings 提供的稳定 query/role/change contract，并把这些变化解释为自己的 Coverage/plan/cache 失效。
- Playback 可以消费 Books、Speech、Settings 和 Cache，但 Cache 不依赖 Playback session state。
- 如果 ActiveCache/Export 需要当前位于 Playback 的非 session 能力，应把该能力移到其真实业务 owner 或提取窄角色合同；不得为了目录迁移形成 Cache ↔ Playback 循环。
- Desktop 只消费需要的稳定 Application role interface，不拥有 Playback/Cache 真值。
- Application 模块不得形成依赖环；跨模块依赖必须可以解释为稳定的单向业务关系。

配置变化与 Cache 的关系采用明确的 typed source change：

```text
Settings change ─────────────┐
TTS rule semantic change ───┼─→ Cache-owned integration → Coverage invalidation
Regex/text-profile change ──┘
```

源模块发布“自身发生了什么变化”，不发布“Cache 应该如何失效”。Cache-owned integration 负责把这些变化映射为 Cache 域内 typed invalidation。禁止为此引入通用 EventBus/Messenger。

## 4. 接口和抽象原则

只在存在真实边界时创建 interface。主要理由：

1. Infrastructure 技术实现边界；
2. process/session/background owner 的稳定角色视图；
3. 跨模块/跨 Feature 合同；
4. 外部副作用与测试隔离。

Feature-local projector、mapper、controller、editor session 默认使用：

```text
internal sealed class
```

不因为只有一个实现就机械保留/删除 interface，也不因为“方便 Mock”自动创建接口。

禁止：

- 通用 EventBus/Messenger；
- Service Locator；
- 万能 `Manager/Helper/Utils`；
- 长期兼容 wrapper；
- 通过 Shared 隐藏 Feature 或 Application 模块循环依赖。

### 4.1 成熟能力优先

NovelSpeaker 默认不重新实现平台、框架或成熟库已经稳定提供的基础设施能力。

优先顺序：

```text
.NET / Windows / WPF / Wpf.Ui 标准能力
        ↓
项目中已有且职责清晰的能力
        ↓
确有必要时采用成熟、维护良好的依赖
        ↓
只有存在已证实能力缺口时才自定义基础设施
```

自定义实现必须回答：

- 标准能力具体缺少什么；
- 该缺口是否属于真实产品/性能要求；
- 为什么组合/适配现有能力不足以解决；
- 新增 mutable state、生命周期和维护成本由谁负责；
- 有哪些结构性/行为测试长期保护它。

“成熟能力优先”不要求为简单领域逻辑或少量纯计算引入第三方依赖；目标是避免重新实现框架级状态机、调度器、虚拟化、容器生命周期、通用消息系统等高维护成本基础设施。

尤其在 WPF 层：

- container generation/recycling 由 WPF ItemsControl/VirtualizingPanel 标准机制负责；
- scroll extent/offset、virtualization lifecycle 不由 Feature 自建并行状态机；
- 应用只维护业务/presentation 所需的逻辑状态和轻量布局投影。

## 5. 状态所有权

核心原则：**同一 mutable state 只有一个 owner**。

| 状态 | Owner | 生命周期 |
|---|---|---|
| 当前播放会话、当前逻辑位置、PlaybackSnapshot | Playback Session Core / `PlaybackCoordinator` facade | Playback session / process |
| ReadingProgress checkpoint | Application progress controller + persistence port | Persistent |
| 当前应用设置 snapshot | Settings process service | Process |
| 当前路由 | Shell navigation owner | Process |
| 物理缓存/index/file | CacheStore | Persistent/rebuildable |
| Cache Coverage/目录投影 | Cache query + active Page projection | Query/Page activation |
| 主动缓存批次 | `ActiveCacheCoordinator` | Background job |
| 章节导出批次 | `ChapterExportCoordinator` | Background job |
| Speech Plan 补建 | `SpeechPlanRepairCoordinator` | Background job |
| 页面 draft/filter/selection | 当前 Page/ViewModel | Page activation |
| 当前规则编辑草稿 | 当前 Rules editor session | Page activation |

ViewModel 不复制 process/session owner 的可变状态。跨页面展示通过 immutable snapshot/read model 投影。

## 6. ViewModel 生命周期

普通 Page/ViewModel 默认 **Transient**。

真正需要跨页面继续存在的状态必须显式提升为 process/session/background owner，而不是依赖 singleton ViewModel。

允许 Singleton 的典型对象：

- Playback owner；
- Settings owner；
- Cache invalidation/repair/active-cache/export coordinator；
- Shell navigation；
- desktop lifecycle。

禁止普通 Feature ViewModel 通过 Singleton 保存页面 UI 状态。

## 7. Playback 目标架构

`PlaybackCoordinator` 保持唯一 Session owner 和对外 facade，但内部拆职责：

```text
PlaybackCoordinator
├─ PlaybackSessionState
├─ PlaybackCommandProcessor
├─ PlaybackAudioController
├─ PlaybackContentResolver
├─ PlaybackPrefetchCoordinator
├─ PlaybackProgressController
└─ PlaybackStopTimer
```

约束：

- 只有 Session Core 可以提交当前 Book/Chapter/Segment/epoch。
- 子组件不得各自持有另一份 current position。
- Audio callback 转为内部命令，不直接修改页面状态。
- Progress controller 负责 checkpoint 和 stale-session 防护。
- `PlaybackSnapshot` 仍由同一个 owner 发布。
- Playback 可以使用 Cache，但不拥有 CacheCatalog、Coverage、Speech Plan repair、ActiveCache 或 Export 的生命周期。
- 对 `PlaybackCoordinator` 的进一步拆分只能抽离不拥有 session truth、且存在独立变化原因的职责；不得为了减小文件而拆出第二套 session state。

## 8. Player Presentation

XAML 继续绑定单一 `PlayerViewModel`，但内部职责使用 Feature-local controller：

```text
PlayerViewModel
├─ PlayerPlaybackProjection
├─ PlayerContentController
├─ PlayerSpeechControlController
├─ PlayerCacheDecorationController
└─ PlayerInteractionController
```

PlayerViewModel 主要负责：

- 可绑定页面状态组合；
- Commands；
- activation/deactivation；
- controller 生命周期。

这些 controller 默认不进入 Application、不建立 interface、不提升为全局 Shared。

## 9. Cache 架构

Cache 是一级 Application 模块。不建立大一统 `CacheManager`；物理缓存事实、当前配置完整度、Speech Plan、后台任务和页面交互状态必须分开。

目标职责：

```text
CacheStore                         物理 cache/index/file 唯一事实与原子操作
CacheCatalog                       物理缓存 overview/book/chapter read model
CacheInvalidationCoordinator       Cache-local typed invalidation + 短窗口合并
CacheConfigurationChangeObserver   把外部配置源变化映射为 Cache Coverage 失效
CacheCoverageQuery                 当前配置下的缓存完整度只读查询
SpeechPlanRepairCoordinator        缺失/过期 plan 的 process 后台补建 owner
ActiveCacheCoordinator             主动缓存 batch owner
ChapterExportCoordinator           导出 batch owner
CacheManagement VM                 transient filter/selection/live projection
```

Cache invalidation 是“数据已失效”的通知，不是另一份统计真值：

- 物理 cache mutation 由实际提交变更的一层报告最窄已知范围：Global、Book 或明确 Chapter 集合；
- 多章清理等已知具体章节的操作不得无必要退化为整本书失效；
- invalidation 区分物理统计、目录结构、当前配置 Coverage 等受影响方面；
- `CacheInvalidationCoordinator` 只在 Cache 域内使用，不演化为通用 EventBus/Messenger；
- 高频连续 mutation 在短窗口内合并为 immutable batch；具体毫秒数属于实现/性能调优，不是产品合同；
- consumer 收到 invalidation 后重新读取最小 read model，事件本身不携带可长期依赖的总大小、百分比等派生统计。

完整度与物理缓存分离：

```text
Physical cache
  → CacheStore / CacheCatalog

Books/Speech/Settings current configuration
  + ChapterSpeechPlan
  + Physical cache
  → CacheCoverageQuery
```

`CacheCoverageQuery` 不负责后台补建。读取发现 `PlanMissing`/`PlanStale` 时，由明确的 use case/controller 向 `SpeechPlanRepairCoordinator` 登记补建；补建提交完成后发布对应 Coverage invalidation。

设置、TTS 规则、正文处理规则等改变时，由 Cache-owned configuration observer 根据源模块的 typed change 判断 Coverage 失效范围。源模块不得直接调用 Cache invalidation API。配置失效不得触发立即遍历全部书籍/章节重算，active 页面只重新计算 current/viewport/明确需要的 decoration。

页面 selection 属于 Page activation state。cache/catalog/coverage 刷新不得无条件重置 selection；目录结构变化只移除已经不存在的选择项。

## 10. Rules 与 Settings

### Rules

共享编辑生命周期，不共享业务模型：

```text
Rules/Shared
├─ EditorSession
├─ RuleSelectionController
├─ RuleReorderController
└─ RuleImportSession
```

validation、default、preview/test、persistence DTO 和规则语义留在各自 Feature/Application 模块。不建立复杂泛型 Rule Framework。

规则修改如果会影响其它模块，只发布规则自身的 typed semantic change；不得嵌入 Cache-specific side effect。

### Settings

```text
Process Settings State
        ↓
Transient Settings Page/ViewModel
```

`AppSettingsService` 是 process settings snapshot 的唯一 owner。即时设置页面投影 process snapshot；需要保存的设置使用 transient draft。

Settings change event 只描述 settings snapshot 变化。Cache、Theme、Playback 等消费者各自在自己的边界解释其影响，Settings 不主动调用这些消费者的内部协调器。

## 11. CQRS-style Query Model

采用轻量读写职责分离，不引入 MediatR、CommandBus 或 QueryBus。

示例：

```text
GetLibrarySummaries
GetBookHeader
GetBookCatalog
GetCurrentReadingPosition
GetChapterContent
GetCacheStatuses(indices/range)
GetCachedBookSummaries
GetCachedChapterCatalog
```

原则：

- query 返回针对场景的 immutable read model；
- command 使用明确 use case/service；
- 不用一个大型详情 DTO 同时承担 header/catalog/status/statistics；
- App 不直接组合多个低层 persistence port 完成页面查询。

## 12. 大列表架构

目标规模至少 10,000 章节；Library 同样按大数据集设计。

核心模式：

```text
Immutable Catalog
        +
Sparse Mutable Decoration
```

稳定基础字段存于轻量 catalog；Current、Selected、CachePercentage、Loading 等动态状态只作用于真正变化/可见/当前项目。

禁止默认使用：

```text
full DTO
→ full complex mutable ItemViewModel
→ Clear + N × Add
→ full status enrichment
```

要求：

- current index O(1) 定位；
- snapshot/current 变化只更新旧/新目标；
- cache status 默认按 current/viewport/明确受影响 index enrichment；
- selection 独立于 WPF container；
- WPF UI virtualization 不作为 data virtualization 的替代；
- UI virtualization 优先使用 WPF 标准容器/虚拟化能力，不自行维护 `ItemContainerGenerator`、realized range 或 `IScrollInfo` 状态机。

### 12.1 Library 响应式虚拟列表

Library 保留响应式卡片布局，但按“逻辑 Row + WPF 标准行虚拟化”实现：

```text
Immutable visible book projection
        ↓
responsive layout metrics
        ↓
LibraryBookRowProjection
        ↓
standard WPF ItemsControl/ListBox
        ↓
VirtualizingStackPanel (row-level)
        ↓
每个 Row 内少量 BookCard
```

原则：

- responsive layout 只计算列数、卡片宽度和行分组；
- 外层 Row 由 WPF `VirtualizingStackPanel` 负责 container generation/recycling；
- Row 内项目数很少，不再实现第二套 item-level virtualization；
- 不自定义 `ItemContainerGenerator.GenerateNext/Recycle` 生命周期；
- 不通过 Feature 自实现 `IScrollInfo`、extent/viewport/offset 状态；
- resize 时重新计算 row projection，保持书籍顺序和逻辑 anchor；
- scroll restoration 保存逻辑 `BookId` anchor，通过 row lookup + 标准 scroll/bring-into-view 能力恢复，不自行模拟整个虚拟画布的像素位置。

## 13. Staged Loading

复杂页面统一使用：

```text
Critical
  → First Interactive Frame
  → Secondary Enrichment
  → Background Enhancement
```

阶段要求：

- Critical 只包含首屏必要数据；
- current locator、cache status、次级统计不得阻塞首个交互帧；
- 每阶段绑定 activation/version/cancellation；
- 页面离开后页面拥有的 enrichment 失效；
- `Task.Yield()` 本身不构成 staged loading；
- 不用固定 `Task.Delay` 猜测渲染完成。

## 14. DI 与组合根

- `IServiceProvider` 只允许出现在 App 组合根、Page provider/factory 和框架要求的桥接层。
- Feature/ViewModel 不从容器主动解析依赖。
- 真实 process owner 使用 Singleton。
- Page/ViewModel/Feature-local controller 默认 Transient。
- Singleton 不捕获 Page、Window 或短生命周期 UI 对象。
- Application/Infrastructure 注册按业务模块集中声明；Cache 与 Playback 使用独立 registration，不由 Playback registration 顺带拥有 Cache 生命周期。
- 注册边界和模块依赖由 Architecture/DI tests 守护。

## 15. 导航架构

- `CurrentRoute` 是完整强类型当前路由。
- 普通页面使用固定 parent route。
- `PlayerRoute` 携带一次性 `ReturnRoute`，不形成历史链。
- Wpf.Ui 内部 history/cache 不作为业务状态。
- 不通过 Page Singleton/NavigationCache 恢复参数或规避加载成本。

详细交互见 `04_UI_NAVIGATION_AND_PERFORMANCE.md`。

## 16. 平台边界

Application 不引用 WPF/Windows 技术类型。以下能力通过 App adapter 或技术 port 实现：

- 文件/目录选择；
- 剪贴板；
- 打开目录；
- Windows 媒体控制；
- 托盘；
- 迷你窗口；
- UI scheduler/Dispatcher；
- HTTP/NAudio/SQLite/文件系统等 Infrastructure 能力。

## 17. 架构自动约束

Architecture Fitness Tests 至少长期验证：

- 四层引用方向；
- App 非 Bootstrap 不直接引用 Infrastructure；
- Shared 不依赖 Feature；
- 禁止 Feature 双向引用；
- ordinary Page/ViewModel 不注册 Singleton；
- ViewModel/Feature controller 不依赖 `IServiceProvider`；
- 不新增通用 EventBus/Messenger；
- Application 不引用 WPF；
- Application 业务模块不形成循环依赖；
- Books/Speech/Settings 不引用 Cache-specific invalidation/Coverage/storage API；
- Playback mutable session state 只由指定 owner 修改；
- 页面不直接写 ReadingProgress；
- 大列表 helper 不重新引入首屏 `Clear + N × Add` 模式；
- Library 不重新引入 Feature-owned `ItemContainerGenerator`/`IScrollInfo` 虚拟化状态机；
- 兼容 wrapper/Obsolete bridge 不长期存在。

不把代码行数、构造参数数量或绝对毫秒性能作为机械架构门槛。

## 18. 迁移与遗留代码清理

模块/namespace/owner 迁移的完成标准不仅是新路径可用，还必须在同一轮清理迁移遗留：

- 旧 namespace 和空目录；
- forwarding/compatibility wrapper、alias interface、Obsolete bridge；
- 重复 DTO/read model/projector/controller；
- orphan DI registration 和仅为旧架构存在的 factory；
- 旧事件、旧 mutation/invalidation 入口；
- 测试中的旧 fake/stub/helper 与临时白名单；
- 一次性 migration instrumentation、trace、dump、脚本。

内部 API 不为迁移便利长期保留兼容层。唯一例外是已发布用户数据、SQLite migration、持久化格式和明确的外部兼容合同；这些兼容需求不得与内部代码兼容混为一谈。
