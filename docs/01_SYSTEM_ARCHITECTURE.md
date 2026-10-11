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

```text
Application/
├─ Books/
├─ Speech/
├─ Cache/
├─ Playback/
├─ Settings/
└─ Desktop/
```

主要依赖方向：

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
- 跨模块变化使用窄 typed snapshot/change source/role port；不引入通用 EventBus/Messenger。

## 3. Books 领域边界

Books 统一围绕下列模型组织：

```text
Book
├─ BookSourceBindings[]
│  ├─ LocalBinding [0..1]
│  └─ OnlineBinding [0..N]（future）
├─ ActiveSourceBindingId?
├─ CurrentCatalog
├─ ReadingState
└─ Content access boundary
```

长期原则：

- 产品身份是规范化 `Title + Author`；BookId 是稳定技术主键。
- Title / Author 入库后不可普通编辑。
- `BookSourceBinding` 表示“这本 Book 与一个具体来源的绑定”，而不是全局 Source Definition 本身。
- Local-only 数据留在 typed Local Binding；不重新放回 Book 主表。
- 未来 Online Source Definition 是独立系统，Books 只通过 Binding 与其连接。
- `ActiveSourceBindingId` 可以为空，不建立自动 fallback。
- **数据库只维护 Book 当前 Active Source 的一份 CurrentCatalog。** Catalog 不再长期挂在每个 Binding 下。
- ReadingState 属于 Book，只通过 ordinal + 章内位置表达，不持久引用跨 Source Chapter Identity。
- Content 获取通过稳定 port 统一，Playback / UI 不直接判断本地路径或未来 HTTP locator。
- Catalog entry 可以保留技术 ChapterId 供 Speech Plan / Audio Cache 引用，但不升级为跨 Source 章节对应体系。
- 精确合同见 `docs/specs/BOOK_DATA_MODEL.md`。

## 4. Content 边界

上层统一消费类似：

```text
Book + CurrentCatalogEntry
        ↓
Content Service
        ├─ Local persistent content
        └─ Online content provider/cache（future）
```

原则：

- Local content 是业务持久数据，不属于 Cache。
- 当前本地实现允许使用应用内规范化文本文件 + chapter ranges。
- Future Online content 可以使用 locator + 本地正文文件缓存，但网络、规则和缓存实现细节不得泄漏到 Playback/App。
- 不为了未来 Online Source 提前创建万能 Source/Content plugin framework。

## 5. 搜索与 Source 刷新边界（future only）

未来全局搜索和刷新绑定源列表可以共享底层 Source Search Engine，但上层生命周期不同：

```text
SourceSearchEngine
   ├─ SearchSession → TemporaryBook aggregation
   └─ RefreshSession → RefreshWorkingSet
```

稳定边界要求：

- 结果按 Source 完成情况流式返回，不要求遍历全部 Source 后才出现 UI 结果；
- 后续可以从串行扩展为受限并发，不让 UI 依赖具体调度方式；
- SearchSession 的 TemporaryBook 不直接写正式 Book 数据；
- RefreshWorkingSet 在内存中增量形成，正式 Binding 在提交前不被半更新；
- 离开承载换源功能的页面时立即停止接收新结果、取消未完成搜索，不等待它们结束，仅提交已经进入 Working Set 的结果；
- 应用退出时取消并丢弃未提交 Working Set，不为了刷新结果延长关闭时间；
- Active Online Binding 在刷新提交时受到保护，但不增加“本轮未发现”等持久状态。

本轮仅保证 Book/Binding/CurrentCatalog 的基础结构不会阻碍上述设计，不实现这些 future runtime。

## 6. Speech Provider 边界

Speech 以 Provider Type + Provider Instance 建模。上层消费 Provider Runtime，不直接依赖具体 HTTP/Edge transport。各 Provider 保持 typed config，不建立万能 ProviderConfig、脚本插件平台或 capability framework。

## 7. App Feature

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
├─ Rules/
├─ Settings/
└─ Diagnostics/

Shell/
Desktop/
Shared/
```

规则：

- Feature 不形成双向依赖。
- Feature-local controller/projector 默认留在 Feature 内。
- 全局 Shared 只保存真实跨业务域复用的 presentation/lifecycle/platform primitive。
- Shared 不依赖 Feature，不通过 Shared 隐藏模块循环。
- 编辑工作台优先组合小型行为 owner，不建立大型泛型工作台或继承体系。

## 8. 状态所有权

核心原则：**同一 mutable state 只有一个 owner。**

| 状态 | Owner | 生命周期 |
|---|---|---|
| Book / Binding / CurrentCatalog 持久事实 | Books persistence/use case | Persistent |
| ActiveSourceBindingId | Books use case + persistence | Persistent |
| ReadingProgress checkpoint | Application progress use case + persistence | Persistent |
| Playback session / logical target / preparation | Playback runtime/session owner | Session / process |
| Speech Provider 列表及配置 | Speech persistence/use case | Persistent |
| CurrentProvider / Settings snapshot | Settings process service | Persistent / process |
| Audio Cache/index/file | Cache store | Persistent / rebuildable |
| Active Cache / Export / repair | 对应 coordinator | Background job |
| 当前路由 | Shell navigation owner | Process |
| 页面 filter/selection/draft | 当前 Page/ViewModel | Page activation |
| 未来 SearchSession / RefreshSession | 对应 page/flow owner | Explicit flow / page |
| Diagnostics session | Diagnostics owner | Explicit session |

ViewModel 不复制 process/session/background owner 的 mutable truth。跨页面展示使用 immutable snapshot/read model。

## 9. Playback 边界

Playback 消费 Books/Content/Cache/Speech 的稳定能力，不拥有 Book、Catalog 或 Source Binding 生命周期。

- 当前章节/段落来自 committed logical target，而不是最近成功加载的音频。
- 用户切段/切章先提交 UI/logical target，再异步准备音频。
- low-level audio result 必须校验 session + target/preparation identity；迟到结果丢弃。
- Source context 真正变化时替换/结束对应 Playback session；普通切段不 replacement 整个 session。
- Source switch 由 Books 领域完成并发布已提交变化，UI 不手工串联多个模块刷新。

## 10. 生命周期与接口

普通 Page/ViewModel 默认 transient。只有真实长期 owner 才使用 process/session/background service。

只在存在真实边界时创建 interface，典型理由：

1. Infrastructure 技术实现边界；
2. process/session/background owner 的稳定角色；
3. 跨模块/跨 Feature 合同；
4. 外部副作用与测试隔离。

Feature-local mapper/projector/controller 默认使用 concrete internal type，不因为方便 Mock 机械创建 interface。

## 11. Query 与命令

采用轻量 read/write 职责分离，不引入 MediatR、CommandBus 或 QueryBus。

- query 返回场景化 immutable read model；
- command/use case 使用明确服务；
- 不用大型 DTO 同时承载 header、catalog、statistics、cache 等所有状态；
- App 不直接组合多个低层 persistence port；
- 大 CurrentCatalog 与动态 decoration 分离；
- Book query 不向 App 暴露 SQLite row 或 Local StoredContentPath。

## 12. 大列表

大型 Library/Chapter/Cache 列表遵循：

```text
Immutable Catalog
+
Sparse Mutable Decoration
```

至少支持 10,000 条连续 Catalog；current item 使用 O(1) 或有界 lookup；WPF virtualization 与 projection 规模控制同时成立。

## 13. 成熟能力优先

默认优先复用 .NET / Windows / WPF / Wpf.Ui 与项目已有职责清晰的能力。避免重复实现通用消息总线、后台任务调度器、并发/重试框架、DI/service locator、WPF virtualization/container lifecycle 等基础设施。

## 14. 禁止项

- 通用 EventBus/Messenger。
- Service Locator。
- 万能 Manager/Helper/Utils。
- 万能 Provider Config 或 Source Config。
- 提前实现 Online Source plugin framework。
- 跨页面全局 SelectionService。
- 通过 Shared 隐藏循环依赖。
- 为内部重构长期保留 Old/New/V2/Compat wrapper。
- 通过 singleton Page/ViewModel 或 Navigation cache 保存长期业务状态。
