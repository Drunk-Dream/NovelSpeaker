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
- **Infrastructure**：SQLite、文件、HTTP、Jint、NAudio、诊断持久化等技术实现。
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
- Playback 可以消费 Cache 的稳定 query/role。
- Desktop 只消费稳定角色接口，不拥有 Playback/Cache mutable truth。

跨模块变化使用窄的 typed snapshot/change source/role port；源模块只表达“自身发生了什么变化”，派生消费者在自己的边界解释影响。禁止为此引入通用 EventBus/Messenger。

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

- Feature 不形成双向依赖。
- Feature-local controller/projector 默认留在 Feature 内。
- `Rules/Shared` 只共享规则编辑生命周期，不抽象不同规则业务模型。
- 全局 `Shared` 只保存真实跨多个业务域复用的 presentation/lifecycle/platform primitive。
- `Shared` 不依赖任何 Feature。

## 4. 状态所有权

核心原则：**同一 mutable state 只有一个 owner**。

| 状态 | Owner | 生命周期 |
|---|---|---|
| 当前播放会话与位置 | Playback session owner | Playback session / process |
| ReadingProgress checkpoint | Application progress use case + persistence | Persistent |
| 当前设置 snapshot | Settings process service | Process |
| 当前路由 | Shell navigation owner | Process |
| 物理缓存/index/file | Cache store | Persistent / rebuildable |
| Cache Coverage/read model | Cache query | Query / page projection |
| 主动缓存批次 | Active Cache coordinator | Background job |
| 章节导出批次 | Export coordinator | Background job |
| Speech Plan 补建 | Repair coordinator | Background job |
| 页面 filter/selection/draft | 当前 Page/ViewModel | Page activation |
| 诊断会话 | Diagnostics session owner | Explicit diagnostic session |

ViewModel 不复制 process/session/background owner 的 mutable truth。跨页面展示使用 immutable snapshot/read model。

## 5. 生命周期与接口

普通 Page/ViewModel 默认 transient。

只有存在真实长期 owner 才使用 process/session/background service，例如：

- Playback session owner；
- Settings process owner；
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

原则：

- 第一版不引入完整 OpenTelemetry。
- 内部 API 保持薄且可替换，未来如有真实需求可增加 adapter。
- Telemetry 与 Diagnostic Session 可以共享基础 operation instrumentation，但开关、数据粒度、生命周期和持久化完全独立。
- Logging、Telemetry、Diagnostics 任一失败不得导致业务失败。

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
- 通过 Shared 隐藏 Feature/Application 循环。
- 为内部重构长期保留 Old/New/V2/Compat/forwarding wrapper。
- 通过 singleton Page/ViewModel 或 Navigation cache 保存长期业务状态。
- 为减小文件行数机械拆类并制造第二套 state owner。
